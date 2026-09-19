using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IAdminPanelPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Task SendGrantAsync(UserSession session);
}

public class AdminPanelPacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    ICombatNotificationService combatNotificationService,
    IZoneTransitionService zoneTransitionService,
    IServiceScopeFactory scopeFactory,
    IOptions<GameServerSettings> settings,
    ILogger<AdminPanelPacketCoordinator> logger) : IAdminPanelPacketCoordinator
{
    private const byte ReqState = 1;
    private const byte ReqCoins = 2;
    private const byte ReqStats = 3;
    private const byte ReqGiveItem = 4;
    private const byte ReqSetClass = 5;
    private const byte ReqZone = 6;
    private const byte ReqItemSearch = 7;

    private const byte AckState = 0x10;
    private const byte AckResult = 0x11;
    private const byte AckGrant = 0x12;

    private const byte StatFloor = 1;
    private const byte StatCeiling = 255;
    private const short StatPointsCeiling = 10_000;
    private const int GiveCountCeiling = 9_999;

    private static readonly short[][] JobFamilies =
    [
        [1, 5, 6],
        [2, 7, 8],
        [3, 9, 10],
        [4, 11, 12],
        [13, 14, 15],
    ];

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();

        if (GrantFor(session) == AdminPanelGrant.None)
        {
            logger.LogWarning(
                "Admin-panel sub {Sub} refused for non-GM {Name} (character {CharacterId})",
                sub, session.Name, session.CharacterId);
            await SendStateAsync(session, granted: false);
            return;
        }

        switch (sub)
        {
            case ReqState:
                await SendStateAsync(session, granted: true);
                break;

            case ReqCoins:
                await HandleCoinsAsync(session, packet);
                break;

            case ReqStats:
                await HandleStatsAsync(session, packet);
                break;

            case ReqGiveItem:
                await HandleGiveItemAsync(session, packet);
                break;

            case ReqSetClass:
                await HandleSetClassAsync(session, packet);
                break;

            case ReqZone:
                await HandleZoneAsync(session, packet);
                break;

            default:
                logger.LogDebug("Unhandled admin-panel sub {Sub} from {Name}", sub, session.Name);
                break;
        }
    }

    private async Task HandleCoinsAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4)
            return;

        var amount = packet.ReadInt();
        if (amount == 0)
            return;

        var total = (int)Math.Clamp((long)session.Money + amount, 0L, int.MaxValue);
        var delta = total - session.Money;
        session.Money = total;

        if (delta >= 0)
            await userNotificationService.SendGoldGainAsync(session, delta);
        else
            await userNotificationService.SendGoldLossAsync(session, -delta);

        PersistInBackground(session);
        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true, $"Coins {(delta >= 0 ? "+" : "")}{delta:n0} — now {total:n0}.");
        logger.LogInformation("GM {Name} adjusted own coins by {Delta} (now {Total})", session.Name, delta, total);
    }

    private async Task HandleStatsAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 7)
            return;

        session.Strength = ClampStat(packet.ReadByte());
        session.Stamina = ClampStat(packet.ReadByte());
        session.Dexterity = ClampStat(packet.ReadByte());
        session.Intelligence = ClampStat(packet.ReadByte());
        session.Magic = ClampStat(packet.ReadByte());
        session.StatPoints = Math.Clamp(packet.ReadShort(), (short)0, StatPointsCeiling);

        Recalculate(session);
        await RefillVitalsAsync(session);

        PersistInBackground(session);
        await userNotificationService.SendStatUpdateAsync(session);
        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true,
            $"Stats set — STR {session.Strength} STA {session.Stamina} DEX {session.Dexterity} " +
            $"INT {session.Intelligence} MP {session.Magic}, {session.StatPoints} free.");
        logger.LogInformation(
            "GM {Name} set own stats: str={Str} sta={Sta} dex={Dex} int={Int} mag={Mag} points={Points}",
            session.Name, session.Strength, session.Stamina, session.Dexterity,
            session.Intelligence, session.Magic, session.StatPoints);
    }

    private async Task HandleGiveItemAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 6)
            return;

        var itemId = packet.ReadInt();
        var count = Math.Clamp((int)packet.ReadShort(), 1, GiveCountCeiling);

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
        {
            await SendResultAsync(session, false, $"Item {itemId} is not in the server's item table.");
            return;
        }

        var outcome = session.WithLock(s =>
        {
            var slotIndex = s.FindSlotForItem(itemId, gameDataService, (ushort)count);
            if (slotIndex < 0)
                return (Placed: false, SlotIndex: 0, ItemId: 0, Count: (ushort)0, Durability: (short)0, IsNew: false);

            var slot = s.Inventory[slotIndex];
            var isNew = slot.IsEmpty;
            if (isNew)
            {
                slot.ItemId = itemId;
                slot.Count = 0;
                slot.Durability = itemData.Duration;
            }
            slot.Count = (ushort)Math.Min(GiveCountCeiling, slot.Count + count);

            s.RecalculateStatsWithBuffs(gameDataService);
            return (Placed: true, SlotIndex: slotIndex, ItemId: slot.ItemId, Count: slot.Count,
                Durability: slot.Durability, IsNew: isNew);
        });

        if (!outcome.Placed)
        {
            await SendResultAsync(session, false, "Inventory full.");
            return;
        }

        await userNotificationService.SendStackChangeAsync(
            session, (byte)outcome.SlotIndex, outcome.ItemId, outcome.Count, outcome.Durability, outcome.IsNew);
        await userNotificationService.SendWeightChangeAsync(session);
        await SendResultAsync(session, true, $"Received {itemData.Name} x{count}.");
        logger.LogInformation("GM {Name} granted self item {ItemId} x{Count}", session.Name, itemId, count);
    }

    private async Task HandleSetClassAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;

        var target = packet.ReadShort();
        if (!ClassOptionsFor(session).Contains(target))
        {
            await SendResultAsync(session, false, $"Class {target} is not a valid change for class {session.Class}.");
            return;
        }

        var previous = session.Class;
        session.Class = target;

        session.ResetMasteryPoints();

        Recalculate(session);
        await RefillVitalsAsync(session);

        PersistInBackground(session);
        await combatNotificationService.SendPartyClassUpdateAsync(session);
        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true,
            $"Class {previous} → {target}. Mastery points refunded and the skill bar cleared.");
        logger.LogInformation("GM {Name} changed own class {Previous} → {Target}", session.Name, previous, target);
    }

    private async Task HandleZoneAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;

        var target = packet.ReadShort();
        if (target is <= 0 or > byte.MaxValue
            || !gameDataService.ZoneInfoTable.TryGetValue(target, out var zoneInfo))
        {
            await SendResultAsync(session, false, $"Zone {target} is not on this server.");
            return;
        }

        var name = string.IsNullOrWhiteSpace(zoneInfo.MapName) ? $"zone {target}" : zoneInfo.MapName;

        if (target == session.ZoneId)
        {
            await SendResultAsync(session, false, $"You are already in {name}.");
            return;
        }

        if (session.IsWarping)
        {
            await SendResultAsync(session, false, "A zone change is already under way.");
            return;
        }

        var from = session.ZoneId;
        await SendResultAsync(session, true, $"Moving to {name}.");
        await zoneTransitionService.ChangeZoneAsync(session, (byte)target, 0f, 0f);
        logger.LogInformation(
            "GM {Name} used the panel to change zone {From} -> {To}", session.Name, from, target);
    }

    private List<short> ClassOptionsFor(UserSession session)
    {
        var options = new List<short>();
        var nationBase = (short)(session.Class / 100 * 100);
        if (nationBase <= 0)
            return options;

        if (session.IsGM)
        {
            foreach (var family in JobFamilies)
            {
                foreach (var member in family)
                {
                    var candidate = (short)(nationBase + member);
                    if (candidate == session.Class)
                        continue;
                    if (gameDataService.GetCoefficient(candidate) == null)
                        continue;
                    options.Add(candidate);
                }
            }
            return options;
        }

        var subtype = (short)ClassIdHelper.GetSubtype(session.Class);
        foreach (var family in JobFamilies)
        {
            if (Array.IndexOf(family, subtype) < 0)
                continue;

            foreach (var member in family)
            {
                var candidate = (short)(nationBase + member);
                if (candidate == session.Class)
                    continue;
                if (gameDataService.GetCoefficient(candidate) == null)
                    continue;
                options.Add(candidate);
            }
            break;
        }
        return options;
    }

    public async Task SendGrantAsync(UserSession session)
    {
        var grant = GrantFor(session);
        var speedGranted = SpeedGrantedFor(session);
        if (grant == AdminPanelGrant.None && !speedGranted)
            return;

        await session.Client.SendPacket(
            AdminPanelPacketWriter.Grant(AckGrant, (byte)grant, speedGranted));
        if (session.IsGM)
            await session.Client.SendPacket(AdminPanelPacketWriter.GmFx(session.CharacterId, session.GmFxEnabled));

        if (!session.IsGM)
            logger.LogInformation(
                "Public-demo grant to {Name} (character {CharacterId}): panel={Panel} speed={Speed}",
                session.Name, session.CharacterId, grant == AdminPanelGrant.PublicDemo, speedGranted);
    }

    private AdminPanelGrant GrantFor(UserSession session)
    {
        if (session.IsGM)
            return AdminPanelGrant.GameMaster;
        return settings.Value.PublicDemo.GrantGameMasterPanelToEveryone
            ? AdminPanelGrant.PublicDemo
            : AdminPanelGrant.None;
    }

    private bool SpeedGrantedFor(UserSession session) =>
        session.IsGM || settings.Value.PublicDemo.GrantGameMasterSpeedToEveryone;

    private async Task SendStateAsync(UserSession session, bool granted)
    {
        if (!granted)
        {
            await session.Client.SendPacket(AdminPanelPacketWriter.StateDenied(AckState));
            return;
        }

        var state = new AdminPanelPacketWriter.State(
            session.Class, session.Level,
            session.Strength, session.Stamina, session.Dexterity,
            session.Intelligence, session.Magic,
            session.StatPoints, session.MaxHp, session.MaxMp,
            (short)session.Stats.TotalHit, session.Stats.TotalAc,
            session.Money, session.SkillPoints, ClassOptionsFor(session));

        await session.Client.SendPacket(AdminPanelPacketWriter.StateGranted(AckState, state));
    }

    private static async Task SendResultAsync(UserSession session, bool ok, string message)
    {
        await session.Client.SendPacket(AdminPanelPacketWriter.Result(AckResult, ok, message));
    }

    private static byte ClampStat(byte value) => Math.Clamp(value, StatFloor, StatCeiling);

    private void Recalculate(UserSession session)
    {
        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);
    }

    private async Task RefillVitalsAsync(UserSession session)
    {
        if (session.Hp > session.MaxHp) session.Hp = session.MaxHp;
        if (session.Mp > session.MaxMp) session.Mp = session.MaxMp;
        await combatNotificationService.SendHpChangeAsync(session);
        await combatNotificationService.SendMspChangeAsync(session);
    }

    private void PersistInBackground(UserSession session) => _ = PersistAsync(session);

    private async Task PersistAsync(UserSession session)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var characters = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
            var character = await characters.GetById(session.CharacterId);
            if (character == null)
                return;

            character.Class = session.Class;
            character.Strength = session.Strength;
            character.Stamina = session.Stamina;
            character.Dexterity = session.Dexterity;
            character.Intelligence = session.Intelligence;
            character.Magic = session.Magic;
            character.StatPoints = session.StatPoints;
            character.Money = session.Money;
            character.SkillPointData = session.SkillPoints;
            await characters.UpdateAsync(character);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin-panel persist failed for {Name}", session.Name);
        }
    }
}
