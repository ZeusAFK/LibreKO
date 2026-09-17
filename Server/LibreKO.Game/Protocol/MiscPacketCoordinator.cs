using System.Linq;
using System.Collections.Generic;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IMiscPacketCoordinator
{
    Task HandlePremiumAsync(IClient client);
    Task SendPremiumInfoAsync(UserSession session);
    Task HandleAuthorityChangeAsync(IClient client, Packet packet);
    Task HandleCorpseAsync(IClient client, Packet packet);
    Task HandleMarketBbsAsync(IClient client, Packet packet);
    Task HandleNameChangeAsync(IClient client, Packet packet);
    Task HandleSantaAsync(IClient client);
    Task SetSantaOrAngelStateAsync(byte state);
    Task HandleRentalAsync(IClient client, Packet packet);
    Task HandleConcurrentUserAsync(IClient client, Packet packet);
    Task HandleZoneConcurrentAsync(IClient client, Packet packet);
    Task HandleLogosShoutAsync(IClient client, Packet packet);
    Task HandleReportAsync(IClient client, Packet packet);
}

public class MiscPacketCoordinator(
    IServiceProvider serviceProvider,
    SessionManager sessionManager,
    IWorldPacketCoordinator worldPacketCoordinator,
    IKnightsRuntimeService knightsRuntimeService,
    ILogger<MiscPacketCoordinator> logger) : IMiscPacketCoordinator
{
    private const byte RentalNpc = 3;
    private const short NoSummonedFamiliars = 0;
    private const byte CommandAuthority = 0x01;

    private const byte MarketBbsRegister = 0x01;
    private const byte MarketBbsDelete = 0x02;
    private const byte MarketBbsReport = 0x03;
    private const byte MarketBbsOpen = 0x04;

    private const byte NameChangeRequest = 0;
    private const byte ClanNameChangeRequest = 16;
    private const byte NameChangeShowDialog = 1;
    private const byte NameChangeInvalid = 2;
    private const byte NameChangeSuccess = 3;
    private const byte ClanNameNotClan = 4;
    private const byte NameChangeInClan = 4;
    private const byte ClanNameSuccess = 16;
    private const int ScrollOfIdentity = 379090000;
    private const int ScrollOfIdentityAlt = 800032000;
    private const int ClanNameScroll = 800086000;

    private byte santaOrAngelState;

    // In-memory trade-ad board (GS_MARKET_BBS). Persisted for the server lifetime; ads expire after a week.
    private static readonly List<MarketAd> marketAds = new();
    private static int nextMarketAdId = 1;
    private const int MarketBbsMaxAds = 200;
    private const int MarketBbsAdDays = 7;

    public async Task HandlePremiumAsync(IClient client)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        await SendPremiumInfoAsync(session);
    }

    public async Task SendPremiumInfoAsync(UserSession session)
    {
        var packet = MiscPacketWriter.Premium(
            session.AccountStatus, session.PremiumType, session.PremiumTime);
        await session.Client.SendPacket(packet);
    }

    public async Task HandleAuthorityChangeAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var subOpcode = packet.ReadByte();
        if (subOpcode != CommandAuthority)
            return;

        // [u8 sub=COMMAND_AUTHORITY][u32 charId][u8 fame]
        // Previously we wrote a 16-bit charId here, which mis-aligned the trailing
        // fame byte on the client.
        var result = MiscPacketWriter.AuthorityChange(
            CommandAuthority, session.CharacterId, session.Fame);
        await sessionManager.Regions.SendToRegion(session, result, excludeSender: false);
    }

    public async Task HandleCorpseAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 2)
            return;

        var targetId = packet.ReadShort();
        var target = sessionManager.GetByCharacterId(targetId);
        if (target == null)
            return;

        var result = MiscPacketWriter.CorpseLocation(
            targetId, target.GetPosX, target.GetPosZ, target.GetPosY);
        await session.Client.SendPacket(result);
    }

    public async Task HandleMarketBbsAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var subOpcode = packet.ReadByte();
        switch (subOpcode)
        {
            case MarketBbsOpen:
                await SendMarketBbsListAsync(session, packet);
                break;

            case MarketBbsRegister:
                await HandleMarketBbsRegisterAsync(session, packet);
                break;

            case MarketBbsDelete:
                await HandleMarketBbsDeleteAsync(session, packet);
                break;

            case MarketBbsReport:
            {
                // Acknowledge a report (no moderation backend yet).
                var result = MarketBbsPacketWriter.Result(
                    MarketBbsReport, MarketBbsPacketWriter.Succeeded);
                await session.Client.SendPacket(result);
                break;
            }
        }
    }

    private async Task SendMarketBbsListAsync(UserSession session, Packet packet)
    {
        byte filter = packet.RemainingBytes >= 1 ? packet.ReadByte() : (byte)0;
        PruneMarketAds();
        var ads = marketAds.Where(a => filter == 0 || a.BuyType == filter).ToList();

        var result = MarketBbsPacketWriter.AdvertList(
            MarketBbsOpen,
            ads.Select(ad => new MarketBbsPacketWriter.Advert(
                ad.AdId, ad.SellerId, ad.Seller, ad.ItemId, ad.Price,
                (ushort)ad.Count, ad.BuyType, MarketBbsAdDays)).ToList());
        await session.Client.SendPacket(result);
    }

    private async Task HandleMarketBbsRegisterAsync(UserSession session, Packet packet)
    {
        PruneMarketAds();
        if (packet.RemainingBytes < 11 || marketAds.Count >= MarketBbsMaxAds)
        {
            await session.Client.SendPacket(MarketBbsPacketWriter.Registered(
                MarketBbsRegister, MarketBbsPacketWriter.Failed, 0));
            return;
        }

        int itemId = packet.ReadInt();
        int price = packet.ReadInt();
        int count = packet.ReadUShort();
        byte buyType = packet.RemainingBytes >= 1 ? packet.ReadByte() : (byte)1;
        string memo = packet.RemainingBytes >= 1 ? packet.ReadSByteString() : string.Empty;

        var ad = new MarketAd
        {
            AdId = nextMarketAdId++,
            SellerId = session.CharacterId,
            Seller = session.Name,
            ItemId = itemId,
            Price = price,
            Count = count,
            BuyType = buyType == 2 ? (byte)2 : (byte)1,
            Memo = memo,
        };
        marketAds.Add(ad);
        logger.LogDebug("Market ad #{Id} by {Name}: item {Item} x{Count} @ {Price}", ad.AdId, session.Name, itemId, count, price);

        await session.Client.SendPacket(MarketBbsPacketWriter.Registered(
            MarketBbsRegister, MarketBbsPacketWriter.Succeeded, ad.AdId));
    }

    private async Task HandleMarketBbsDeleteAsync(UserSession session, Packet packet)
    {
        var result = new Packet(GameOpcodes.GS_MARKET_BBS);
        int adId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        int removed = marketAds.RemoveAll(a => a.AdId == adId && a.SellerId == session.CharacterId);
        await session.Client.SendPacket(MarketBbsPacketWriter.Result(
            MarketBbsDelete,
            removed > 0 ? MarketBbsPacketWriter.Succeeded : MarketBbsPacketWriter.Failed));
    }

    private static void PruneMarketAds()
    {
        // Drop ads whose seller's character no longer exists is out of scope without a clock; cap the board size.
        if (marketAds.Count > MarketBbsMaxAds)
            marketAds.RemoveRange(0, marketAds.Count - MarketBbsMaxAds);
    }

    private sealed class MarketAd
    {
        public int AdId;
        public int SellerId;
        public string Seller = string.Empty;
        public int ItemId;
        public int Price;
        public int Count;
        public byte BuyType;   // 1 = selling, 2 = want-to-buy
        public string Memo = string.Empty;
    }

    public async Task HandleNameChangeAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var subOpcode = packet.ReadByte();
        switch (subOpcode)
        {
            case NameChangeRequest:
                await HandleCharNameChangeAsync(session, packet);
                break;
            case ClanNameChangeRequest:
                await HandleClanNameChangeAsync(session, packet);
                break;
            default:
                logger.LogDebug("Unhandled WIZ_NAME_CHANGE sub-opcode {Sub} from {Name}", subOpcode, session.Name);
                break;
        }
    }

    private async Task HandleCharNameChangeAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;

        var newName = packet.ReadString();
        if (string.IsNullOrWhiteSpace(newName) || newName.Length is < 3 or > 20)
        {
            await SendNameChangeResultAsync(session, NameChangeInvalid);
            return;
        }

        if (session.KnightsId > 0)
        {
            await SendNameChangeResultAsync(session, NameChangeInClan);
            return;
        }

        var scrollSlot = FindNameChangeScrollSlot(session);
        if (scrollSlot < 0)
        {
            await SendNameChangeResultAsync(session, NameChangeShowDialog);
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        if (await characterRepository.IsNameTaken(newName))
        {
            await SendNameChangeResultAsync(session, NameChangeInvalid);
            return;
        }

        var character = await characterRepository.GetById(session.CharacterId);
        if (character == null)
        {
            await SendNameChangeResultAsync(session, NameChangeInvalid);
            return;
        }

        var oldName = session.Name;
        character.Name = newName;
        await characterRepository.UpdateAsync(character);

        session.Name = newName;
        session.Inventory[scrollSlot].Count--;
        if (session.Inventory[scrollSlot].Count <= 0)
            session.Inventory[scrollSlot].Clear();

        logger.LogInformation("Player {OldName} changed name to {NewName}", oldName, newName);

        await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.Out);
        await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.In);
        await SendNameChangeResultAsync(session, NameChangeSuccess);
    }

    private async Task HandleClanNameChangeAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;

        var newName = packet.ReadString();
        if (string.IsNullOrWhiteSpace(newName) || newName.Length is < 3 or > 20)
        {
            await SendClanNameChangeResultAsync(session, NameChangeInvalid);
            return;
        }

        // Must be in a clan and be the chief.
        if (session.KnightsId <= 0 || session.KnightsFame != 1)
        {
            await SendClanNameChangeResultAsync(session, ClanNameNotClan);
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await SendClanNameChangeResultAsync(session, ClanNameNotClan);
            return;
        }

        // Need a clan-name scroll in the bag.
        var scrollSlot = FindItemInBag(session, ClanNameScroll);
        if (scrollSlot < 0)
        {
            await SendClanNameChangeResultAsync(session, NameChangeShowDialog);
            return;
        }

        // Case-insensitive uniqueness check across all clans.
        var clanNameTaken = sessionManager.Knights.GetAll()
            .Any(k => k.Id != clan.Id && string.Equals(k.Name, newName, StringComparison.OrdinalIgnoreCase));
        if (clanNameTaken)
        {
            await SendClanNameChangeResultAsync(session, NameChangeInvalid);
            return;
        }

        var oldName = clan.Name;
        clan.Name = newName;

        using var scope = serviceProvider.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        await knightsRepo.UpdateAsync(clan);

        // Consume scroll
        session.Inventory[scrollSlot].Count--;
        if (session.Inventory[scrollSlot].Count <= 0)
            session.Inventory[scrollSlot].Clear();

        // Update the in-memory KnightsName for online clan members so their
        // user-info packets reflect the new clan name on next refresh.
        foreach (var member in sessionManager.GetAll())
        {
            if (member.KnightsId == clan.Id)
                member.KnightsName = newName;
        }

        logger.LogInformation("Clan {OldName} (id={ClanId}) renamed to {NewName} by {Player}",
            oldName, clan.Id, newName, session.Name);

        // Re-broadcast the caller's UserInOut so the new clan name appears in tag.
        await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.Out);
        await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.In);

        // Build the success packet ONCE and send to caller + every online clan member
        // so their UIs flip in one shot.
        var success = MiscPacketWriter.ClanRenamed(ClanNameSuccess, newName);
        await knightsRuntimeService.NotifyOnlineClanMembersAsync(clan.Id, success);
    }

    private static int FindItemInBag(UserSession session, int itemId)
    {
        for (var i = InventoryConstants.SlotMax; i < InventoryConstants.SlotMax + InventoryConstants.HaveMax; i++)
        {
            if (session.Inventory[i].ItemId == itemId && session.Inventory[i].Count > 0)
                return i;
        }
        return -1;
    }

    public async Task HandleSantaAsync(IClient client)
    {
        var packet = MiscPacketWriter.SantaState(santaOrAngelState);
        await client.SendPacket(packet);
    }

    public async Task SetSantaOrAngelStateAsync(byte state)
    {
        santaOrAngelState = state;
        var packet = MiscPacketWriter.SantaState(state);
        foreach (var session in sessionManager.GetAll())
            await session.Client.SendPacket(packet);
    }

    public async Task HandleRentalAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var subOpcode = packet.ReadByte();
        switch (subOpcode)
        {
            case RentalList:
            {
                // [1][ushort count]{ int itemId, int days, int cost }
                var result = RentalPacketWriter.Catalog(
                    RentalList,
                    RentalCatalog
                        .Select(entry => new RentalPacketWriter.CatalogEntry(
                            entry.ItemId, entry.Days, entry.Cost))
                        .ToList());
                await client.SendPacket(result);
                break;
            }

            case RentalRent:
            {
                // [2][int itemId] -> [2][byte result][int itemId]
                int itemId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
                var entry = System.Array.Find(RentalCatalog, r => r.ItemId == itemId);
                var rented = entry.ItemId != 0 && session.Money >= entry.Cost;
                if (rented)
                {
                    session.Money -= entry.Cost;
                    await serviceProvider.GetRequiredService<IUserNotificationService>().SendGoldLossAsync(session, entry.Cost);
                    logger.LogDebug("{Name} rented item {Item} for {Days}d ({Cost} gold)", session.Name, itemId, entry.Days, entry.Cost);
                }

                await client.SendPacket(RentalPacketWriter.RentResult(
                    RentalRent,
                    rented ? RentalPacketWriter.Succeeded : RentalPacketWriter.Failed,
                    itemId));
                break;
            }

            case RentalNpc:
            {
                await client.SendPacket(RentalPacketWriter.NpcOpened(RentalNpc));
                break;
            }

            default:
                logger.LogDebug("Unhandled rental sub-opcode {SubOpcode} from {CharId}", subOpcode, session.CharacterId);
                break;
        }
    }

    private const byte RentalList = 1;
    private const byte RentalRent = 2;

    // In-memory rental catalog: (itemId, rental days, gold cost). Item delivery is the follow-up.
    private static readonly (int ItemId, int Days, int Cost)[] RentalCatalog =
    {
        (810024000, 7, 200000),    // 7-day mount
        (900020000, 3, 50000),     // 3-day premium buff scroll
        (810025000, 30, 700000),   // 30-day mount
    };

    private static int FindNameChangeScrollSlot(UserSession session)
    {
        for (var index = InventoryConstants.SlotMax; index < InventoryConstants.SlotMax + InventoryConstants.HaveMax; index++)
        {
            if (session.Inventory[index].ItemId == ScrollOfIdentity && session.Inventory[index].Count > 0)
                return index;
        }

        return -1;
    }

    private static async Task SendNameChangeResultAsync(UserSession session, byte resultCode) =>
        await session.Client.SendPacket(MiscPacketWriter.NameChangeResult(resultCode));

    private static async Task SendClanNameChangeResultAsync(UserSession session, byte resultCode) =>
        await session.Client.SendPacket(MiscPacketWriter.ClanNameChangeResult(resultCode));

    public async Task HandleConcurrentUserAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || !session.IsGM)
            return;

        var players = (short)Math.Min(sessionManager.OnlineCount, short.MaxValue);
        var result = MiscPacketWriter.ConcurrentUsers(players, NoSummonedFamiliars);
        await client.SendPacket(result);
    }

    private static readonly short[] BattleZonesForConcurrent =
    [
        61, 62, 63, 64, 65, 66, // ZONE_BATTLE..BATTLE6
        71, 72, 73,             // ZONE_RONARK_LAND, ZONE_ARDREAM, ZONE_RONARK_LAND_BASE
    ];

    public async Task HandleZoneConcurrentAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var counts = new ushort[BattleZonesForConcurrent.Length];
        foreach (var s in sessionManager.GetAll())
        {
            for (int i = 0; i < BattleZonesForConcurrent.Length; i++)
            {
                if (s.ZoneId == BattleZonesForConcurrent[i])
                {
                    if (counts[i] < ushort.MaxValue) counts[i]++;
                    break;
                }
            }
        }

        var zones = new List<MiscPacketWriter.ZonePopulation>(BattleZonesForConcurrent.Length);
        for (int i = 0; i < BattleZonesForConcurrent.Length; i++)
            zones.Add(new MiscPacketWriter.ZonePopulation((ushort)BattleZonesForConcurrent[i], counts[i]));

        var result = MiscPacketWriter.ZoneConcurrentUsers(zones);
        await client.SendPacket(result);
    }


    private static int logosShoutNumber;

    private const int LogosShoutItem = 800075000;
    private const int LogosShoutMaxMessageLen = 128;

    public async Task HandleLogosShoutAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 5)
            return;

        _ = packet.ReadByte(); // sub_opcode
        var r = packet.ReadByte();
        var g = packet.ReadByte();
        var b = packet.ReadByte();
        var c = packet.ReadByte();
        var message = packet.ReadSByteString();

        bool hasItem = false;
        int itemSlotIndex = -1;
        for (int i = InventoryConstants.InventoryStart;
             i < InventoryConstants.InventoryStart + InventoryConstants.HaveMax; i++)
        {
            if (session.Inventory[i].ItemId == LogosShoutItem && session.Inventory[i].Count > 0)
            {
                hasItem = true;
                itemSlotIndex = i;
                break;
            }
        }

        if (string.IsNullOrEmpty(message) || message.Length > LogosShoutMaxMessageLen)
        {
            await client.SendPacket(LogosShoutPacketWriter.RegisterRejected(
                LogosShoutPacketWriter.RegisterRetryLater));
            return;
        }

        if (!hasItem)
        {
            await client.SendPacket(LogosShoutPacketWriter.RegisterRejected(
                LogosShoutPacketWriter.RegisterNoItem));
            return;
        }

        var slot = session.Inventory[itemSlotIndex];
        slot.Count -= 1;
        if (slot.Count == 0) slot.Clear();

        await client.SendPacket(LogosShoutPacketWriter.RegisterAcceptedAs(
            unchecked((byte)++logosShoutNumber)));

        var fullMessage = $"{session.Name}: {message}";
        var broadcast = LogosShoutPacketWriter.Broadcast(r, g, b, c, fullMessage);

        await sessionManager.BroadcastToAll(broadcast);
        logger.LogInformation("LogoShout from {Name}: {Message}", session.Name, message);
    }

    private const byte ReportFile = 9;
    private const byte ReportYes = 12;
    private const byte ReportNo = 13;
    private const byte ReportListOpen = 14;
    private const byte ReportInspector = 18;

    private const byte VoteYesThreshold = 3;
    private const byte VoteNoThreshold = 2;
    private const int ReportPageSize = 8;
    private const int ReportReasonMax = 512;
    private const byte SheriffPrisonZone = (byte)ZoneId.Prison;
    private const float SheriffPrisonX = 215f;
    private const float SheriffPrisonZ = 158f;

    public async Task HandleReportAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();

        if (!CanUseSheriff(session))
        {
            await SendReportResultAsync(client, sub, success: false);
            return;
        }

        switch (sub)
        {
            case ReportFile:
                await HandleFileReportAsync(client, session, packet);
                break;
            case ReportYes:
                await HandleVoteAsync(client, session, packet, voteYes: true);
                break;
            case ReportNo:
                await HandleVoteAsync(client, session, packet, voteYes: false);
                break;
            case ReportListOpen:
                await HandleListOpenAsync(client, packet);
                break;
            case ReportInspector:
                await HandleInspectorAsync(client);
                break;
            default:
                await SendReportResultAsync(client, sub, success: false);
                break;
        }
    }

    private static bool CanUseSheriff(UserSession session) =>
        session.IsGM || session.Fame == 100;

    private async Task HandleFileReportAsync(IClient client, UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
        {
            await SendReportResultAsync(client, ReportFile, success: false);
            return;
        }

        var targetName = packet.ReadSByteString();
        var reason = packet.RemainingBytes > 0 ? packet.ReadString() : string.Empty;

        if (string.IsNullOrWhiteSpace(targetName) || string.IsNullOrWhiteSpace(reason)
            || reason.Length > ReportReasonMax)
        {
            await SendReportResultAsync(client, ReportFile, success: false);
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var characterRepo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var sheriffRepo = scope.ServiceProvider.GetRequiredService<ISheriffReportRepository>();

        var targetChar = await characterRepo.GetByName(targetName);
        if (targetChar == null)
        {
            await SendReportResultAsync(client, ReportFile, success: false);
            return;
        }

        await sheriffRepo.CreateAsync(session.CharacterId, targetChar.Id, targetChar.Name, reason);
        await SendReportResultAsync(client, ReportFile, success: true);
        logger.LogInformation("Sheriff report filed by {Reporter} against {Target}: {Reason}",
            session.Name, targetChar.Name, reason);
    }

    private async Task HandleVoteAsync(IClient client, UserSession session, Packet packet, bool voteYes)
    {
        if (packet.RemainingBytes < 4)
        {
            await SendReportResultAsync(client, voteYes ? ReportYes : ReportNo, success: false);
            return;
        }

        var reportId = packet.ReadInt();

        using var scope = serviceProvider.CreateScope();
        var sheriffRepo = scope.ServiceProvider.GetRequiredService<ISheriffReportRepository>();

        var report = await sheriffRepo.GetById(reportId);
        if (report == null || report.Status != SheriffReportStatus.Open)
        {
            await SendReportResultAsync(client, voteYes ? ReportYes : ReportNo, success: false);
            return;
        }

        var added = await sheriffRepo.AddVoteAsync(reportId, session.CharacterId, voteYes);
        if (!added)
        {
            // Already voted — silent reject.
            await SendReportResultAsync(client, voteYes ? ReportYes : ReportNo, success: false);
            return;
        }

        if (voteYes) report.VoteYesCount++;
        else report.VoteNoCount++;

        if (report.VoteYesCount >= VoteYesThreshold)
        {
            report.Status = SheriffReportStatus.Approved;
            await sheriffRepo.UpdateAsync(report);
            await ApplyAutoPrisonAsync(report);
            logger.LogInformation("Sheriff: report {Id} approved (yes={Yes}). Target {Target} imprisoned.",
                report.Id, report.VoteYesCount, report.TargetName);
        }
        else if (report.VoteNoCount >= VoteNoThreshold)
        {
            report.Status = SheriffReportStatus.Dismissed;
            await sheriffRepo.UpdateAsync(report);
            logger.LogInformation("Sheriff: report {Id} dismissed (no={No})", report.Id, report.VoteNoCount);
        }
        else
        {
            await sheriffRepo.UpdateAsync(report);
        }

        await SendReportResultAsync(client, voteYes ? ReportYes : ReportNo, success: true);
    }

    private async Task ApplyAutoPrisonAsync(SheriffReportEntity report)
    {
        // If target is online, teleport them to prison zone immediately.
        var target = sessionManager.GetByCharacterId(report.TargetCharId);
        if (target == null) return;

        using var scope = serviceProvider.CreateScope();
        var zoneTransition = scope.ServiceProvider.GetRequiredService<IZoneTransitionService>();
        await zoneTransition.ChangeZoneAsync(target, SheriffPrisonZone, SheriffPrisonX, SheriffPrisonZ);
    }

    private async Task HandleListOpenAsync(IClient client, Packet packet)
    {
        var page = packet.RemainingBytes > 0 ? packet.ReadByte() : (byte)0;

        using var scope = serviceProvider.CreateScope();
        var sheriffRepo = scope.ServiceProvider.GetRequiredService<ISheriffReportRepository>();

        var openCount = await sheriffRepo.CountOpen();
        var totalPages = (byte)Math.Min(byte.MaxValue, (openCount + ReportPageSize - 1) / ReportPageSize);
        var entries = await sheriffRepo.GetOpenPaged(page * ReportPageSize, ReportPageSize);

        var response = ReportPacketWriter.OpenList(
            ReportListOpen, totalPages,
            entries.Select(report => new ReportPacketWriter.OpenReport(
                report.Id, report.TargetName, report.VoteYesCount, report.VoteNoCount,
                report.Reason)).ToList());
        await client.SendPacket(response);
    }

    private async Task HandleInspectorAsync(IClient client)
    {
        using var scope = serviceProvider.CreateScope();
        var sheriffRepo = scope.ServiceProvider.GetRequiredService<ISheriffReportRepository>();

        var openCount = await sheriffRepo.CountOpen();
        var response = ReportPacketWriter.InspectorState(
            ReportInspector, (ushort)Math.Min(openCount, ushort.MaxValue));
        await client.SendPacket(response);
    }

    private static async Task SendReportResultAsync(IClient client, byte sub, bool success)
    {
        var response = ReportPacketWriter.Result(sub, success);
        await client.SendPacket(response);
    }
}
