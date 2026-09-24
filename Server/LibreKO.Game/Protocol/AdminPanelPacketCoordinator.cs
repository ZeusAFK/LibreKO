using LibreKO.Common.Domain.Entities.GameData;
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
    ICollectionRaceService collectionRaceService,
    IPlayerProgressionService playerProgressionService,
    ILoyaltyService loyaltyService,
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
    private const byte ReqCollectionRaces = 8;
    private const byte ReqCollectionRaceStart = 9;
    private const byte ReqCollectionRaceClose = 10;
    private const byte ReqSetLevel = 11;
    private const byte ReqSetSkill = 12;
    private const byte ReqSetLook = 13;

    private const byte KeepProgress = 0;
    private const byte ResetProgress = 1;
    private const int SkillEditBodySize = 2 + ProgressionTable.MasteryClassSlotCount;

    private const byte AckState = 0x10;
    private const byte AckResult = 0x11;
    private const byte AckGrant = 0x12;
    private const byte AckCollectionRaces = 0x14;

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

            case ReqSetLevel:
                await HandleSetLevelAsync(session, packet);
                break;

            case ReqSetSkill:
                await HandleSetSkillAsync(session, packet);
                break;

            case ReqSetLook:
                await HandleSetLookAsync(session, packet);
                break;

            case ReqZone:
                await HandleZoneAsync(session, packet);
                break;

            case ReqCollectionRaces:
                await SendCollectionRacesAsync(session);
                break;

            case ReqCollectionRaceStart when packet.RemainingBytes >= 4:
                await collectionRaceService.StartRaceAsync(packet.ReadInt(), session);
                await SendCollectionRacesAsync(session);
                break;

            case ReqCollectionRaceClose when packet.RemainingBytes >= 4:
                await collectionRaceService.EndRaceAsync(packet.ReadInt(), forced: true, session);
                await SendCollectionRacesAsync(session);
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
        if (packet.RemainingBytes >= sizeof(int))
            await loyaltyService.SetAsync(session, packet.ReadInt());

        Recalculate(session);
        await RefillVitalsAsync(session);

        PersistInBackground(session);
        await userNotificationService.SendStatUpdateAsync(session);
        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true,
            $"Stats set — STR {session.Strength} STA {session.Stamina} DEX {session.Dexterity} " +
            $"INT {session.Intelligence} MP {session.Magic}, {session.StatPoints} free, NP {session.Loyalty:n0}.");
        logger.LogInformation(
            "GM {Name} set own stats: str={Str} sta={Sta} dex={Dex} int={Int} mag={Mag} points={Points} np={Np}",
            session.Name, session.Strength, session.Stamina, session.Dexterity,
            session.Intelligence, session.Magic, session.StatPoints, session.Loyalty);
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

        if (itemData.Countable == 0)
            count = 1;

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
        if (gameDataService.GetCoefficient(target) == null)
        {
            await SendResultAsync(session, false, $"Class {target} has no coefficient on this server.");
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

    private async Task HandleSetLevelAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;

        var level = packet.ReadByte();
        var reset = packet.ReadByte() == ResetProgress;

        if (!ProgressionTable.IsValidLevel(level))
        {
            await SendResultAsync(session, false,
                $"Level must be {ProgressionTable.MinLevel}-{ProgressionTable.MaxLevel}.");
            return;
        }

        if (reset)
            await playerProgressionService.ResetToLevelAsync(session, level);
        else
            await playerProgressionService.SetLevelAsync(session, level);

        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true, reset
            ? $"Level {level} — stats, mastery and skill bar reset."
            : $"Level set to {level}.");
        logger.LogInformation("GM {Name} set own level to {Level} (reset={Reset})",
            session.Name, level, reset);
    }

    private async Task HandleSetSkillAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < SkillEditBodySize)
            return;

        var pool = packet.ReadByte();
        var trees = new byte[ProgressionTable.MasteryClassSlotCount];
        for (var tree = 0; tree < trees.Length; tree++)
            trees[tree] = packet.ReadByte();
        var reset = packet.ReadByte() == ResetProgress;

        if (reset)
        {
            session.SkillData = [];
            session.ResetMasteryPoints();
        }
        else
        {
            session.SkillPoints[ProgressionTable.MasteryPoolSlot] = pool;
            for (var tree = 0; tree < trees.Length; tree++)
                session.SkillPoints[ProgressionTable.MasteryClassFirstSlot + tree] = trees[tree];
        }

        Recalculate(session);
        await RefillVitalsAsync(session);
        PersistInBackground(session);

        if (reset)
        {
            await session.Client.SendPacket(CharacterDevelopmentPacketMapper.CreateSkillResetSuccess(session));
            await session.Client.SendPacket(SkillDataPacketWriter.Cleared());
        }

        await userNotificationService.SendStatUpdateAsync(session);
        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true, reset
            ? $"Skills reset — {session.SkillPoints[ProgressionTable.MasteryPoolSlot]} mastery points in the pool."
            : $"Skill points updated (pool {pool}, trees {MasteryTrees(session)}).");
        logger.LogInformation(
            "GM {Name} edited skill points (reset={Reset}): pool={Pool} trees={Trees}",
            session.Name, reset, session.SkillPoints[ProgressionTable.MasteryPoolSlot], MasteryTrees(session));
    }

    private static string MasteryTrees(UserSession session) =>
        string.Join('/', session.SkillPoints
            .Skip(ProgressionTable.MasteryClassFirstSlot)
            .Take(ProgressionTable.MasteryClassSlotCount));

    private async Task HandleSetLookAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;

        var nationByte = packet.ReadByte();
        var race = packet.ReadByte();

        if (nationByte != (byte)AccountNation.Karus && nationByte != (byte)AccountNation.ElMorad)
        {
            await SendResultAsync(session, false, "Nation must be Karus or El Morad.");
            return;
        }

        var nation = (AccountNation)nationByte;
        if (!CharacterRaceNations.BelongsTo(race, nation))
        {
            await SendResultAsync(session, false, $"Appearance {race} is not a {nation} body.");
            return;
        }

        session.Nation = nation;
        session.Race = race;
        await PersistLookAsync(session, nation, race);

        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true,
            $"Nation → {nation}, appearance {race}. Log out to the character screen and back in to load the new body model.");
        logger.LogInformation("GM {Name} set nation={Nation} race={Race}", session.Name, nation, race);
    }

    private async Task PersistLookAsync(UserSession session, AccountNation nation, byte race)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            var characters = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

            var account = await accounts.GetById(session.AccountId);
            if (account != null)
            {
                account.Nation = nation;
                await accounts.UpdateAsync(account);
            }

            var character = await characters.GetById(session.CharacterId);
            if (character != null)
            {
                character.Race = race;
                await characters.UpdateAsync(character);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin-panel look persist failed for {Name}", session.Name);
        }
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

    private async Task SendCollectionRacesAsync(UserSession session)
    {
        var active = collectionRaceService.ActiveRaces.ToDictionary(a => a.Race.Id);
        var rows = new List<AdminPanelPacketWriter.CollectionRaceRow>();
        foreach (var race in gameDataService.CollectionRaceTable.Values.OrderBy(r => r.ZoneId).ThenBy(r => r.Id))
        {
            active.TryGetValue(race.Id, out var running);
            rows.Add(new AdminPanelPacketWriter.CollectionRaceRow(
                race.Id,
                race.Name,
                race.ZoneId,
                race.MinLevel,
                race.MaxLevel,
                race.DurationMinutes,
                race.AutoStart,
                running != null,
                running?.RemainingSeconds ?? 0,
                running?.Progress.Values.Count(p => p.IsCompleted) ?? 0,
                DescribeSchedule(gameDataService.CollectionRaceSchedulesByRace[race.Id]),
                DescribeObjectives(gameDataService.CollectionRaceObjectivesByRace[race.Id])));
        }

        await session.Client.SendPacket(AdminPanelPacketWriter.CollectionRaces(AckCollectionRaces, rows));
    }

    private static string DescribeSchedule(IEnumerable<CollectionRaceScheduleData> schedules)
    {
        var parts = schedules
            .OrderBy(s => s.Day.HasValue ? (int)s.Day.Value : -1)
            .ThenBy(s => s.Hour)
            .ThenBy(s => s.Minute)
            .Select(s => $"{(s.Day.HasValue ? s.Day.Value.ToString()[..3] : "Daily")} {s.Hour:D2}:{s.Minute:D2}")
            .ToList();
        return parts.Count == 0 ? "manual" : string.Join(", ", parts);
    }

    private string DescribeObjectives(IEnumerable<CollectionRaceObjectiveData> objectives)
    {
        var parts = objectives.OrderBy(o => o.Ordinal).Select(o => o.Kind switch
        {
            CollectionRaceObjectiveKind.EnemyPlayer => $"{o.Count} enemy players",
            CollectionRaceObjectiveKind.Item => $"{o.Count} x {gameDataService.GetItem(o.TargetId)?.Name ?? $"item {o.TargetId}"}",
            _ => $"{o.Count} x {(gameDataService.NpcTable.TryGetValue(o.TargetId, out var npc) ? npc.Name : $"monster {o.TargetId}")}",
        });
        return string.Join(", ", parts);
    }

    private List<short> ClassOptionsFor(UserSession session)
    {
        var options = new List<short>();
        var nationBase = (short)(session.Class / 100 * 100);
        if (nationBase <= 0)
            return options;

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
            await session.Client.SendPacket(AdminPanelPacketWriter.GmFx(session.CharacterId, session.GmModeEnabled));

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
            session.Money, session.SkillPoints, ClassOptionsFor(session),
            (byte)session.Nation, session.Race, session.Loyalty);

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
            character.Loyalty = session.Loyalty;
            character.SkillPointData = session.SkillPoints;
            await characters.UpdateAsync(character);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin-panel persist failed for {Name}", session.Name);
        }
    }
}
