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

public interface INationSystemsPacketCoordinator
{
    Task HandleBifrostAsync(IClient client, Packet packet);
    Task HandleRankAsync(IClient client, Packet packet);
    Task HandleSiegeAsync(IClient client, Packet packet);
    Task HandleKingAsync(IClient client, Packet packet);
}

public class NationSystemsPacketCoordinator(
    IServiceProvider serviceProvider,
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IKingElectionPacketService kingElectionPacketService,
    IKingGovernancePacketService kingGovernancePacketService,
    IBifrostEventService bifrostEventService,
    EventSchedulerService eventSchedulerService,
    ILogger<NationSystemsPacketCoordinator> logger) : INationSystemsPacketCoordinator
{
    public async Task HandleBifrostAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 1)
            return;

        //   2 = BIFROST_EVENT    — remaining time query (the only one with logic)
        var sub = packet.ReadByte();
        if (sub != (byte)TempleSubOpcode.BifrostRemaining)
            return;

        int remaining = 0;
        byte eventType = 0;
        if (eventSchedulerService.IsTempleEventJoinOpen)
        {
            remaining = eventSchedulerService.TempleRemainingJoinSeconds;
            eventType = (byte)eventSchedulerService.CurrentTempleEvent;
        }
        else
        {
            remaining = (int)Math.Min(bifrostEventService.RemainingSecs, int.MaxValue);
        }

        var response = BifrostPacketWriter.Remaining(
            TempleSubOpcode.BifrostRemaining, remaining, eventType);
        await session.Client.SendPacket(response);
    }

    private const byte RankTypePkZone = 1;
    private const byte RankTypeBorderDefenseWar = 2;
    private const byte RankTypeChaosDungeon = 3;
    private const int PkZoneTopCount = 10;

    public async Task HandleRankAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var rankType = packet.ReadByte();

        var response = rankType switch
        {
            RankTypePkZone => await BuildPkZoneRankAsync(session, rankType),
            RankTypeBorderDefenseWar => RankPacketWriter.BorderDefenseWar(rankType),
            RankTypeChaosDungeon => RankPacketWriter.ChaosDungeon(rankType),
            _ => RankPacketWriter.Unsupported(rankType),
        };
        await session.Client.SendPacket(response);
    }

    private async Task<Packet> BuildPkZoneRankAsync(UserSession session, byte rankType)
    {
        using var scope = serviceProvider.CreateScope();
        var characterRepo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

        var karusTop = await characterRepo.GetTopByLoyalty(AccountNation.Karus, PkZoneTopCount);
        var elmoradTop = await characterRepo.GetTopByLoyalty(AccountNation.ElMorad, PkZoneTopCount);
        var myRank = await characterRepo.GetLoyaltyRank(session.Nation, session.DailyLoyalty);

        return RankPacketWriter.PkZone(
            rankType,
            ToRankEntries(karusTop),
            ToRankEntries(elmoradTop),
            (ushort)Math.Min(myRank, ushort.MaxValue),
            session.DailyLoyalty);
    }

    private List<RankPacketWriter.RankEntry> ToRankEntries(IReadOnlyList<CharacterRankRow> entries)
    {
        var result = new List<RankPacketWriter.RankEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var clan = entry.KnightsId > 0 ? sessionManager.Knights.GetClan(entry.KnightsId) : null;
            result.Add(new RankPacketWriter.RankEntry(
                entry.Name,
                (byte)entry.Nation,
                (ushort)entry.KnightsId,
                clan?.MarkVersion < 0 ? (ushort)0 : (ushort)(clan?.MarkVersion ?? 0),
                clan?.Name ?? string.Empty,
                entry.LoyaltyDaily));
        }
        return result;
    }

    public async Task HandleSiegeAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 1)
            return;

        var opcode = packet.ReadByte();
        switch (opcode)
        {
            case SiegePacketConstants.BaseCreate: await HandleSiegeBaseCreateAsync(session, ReadSubType(packet)); break;
            case SiegePacketConstants.CastleFlag: await HandleSiegeCastleFlagAsync(session); break;
            case SiegePacketConstants.WarfareNpc: await HandleWarfareNpcAsync(session, packet); break;
            case SiegePacketConstants.CastleManager: await HandleCastleManagerAsync(session, packet); break;
            case SiegePacketConstants.Rank: await HandleSiegeRankAsync(session, ReadSubType(packet)); break;
        }
    }

    private static byte ReadSubType(Packet packet) => packet.RemainingBytes >= 1 ? packet.ReadByte() : (byte)0;

    private static async Task HandleSiegeBaseCreateAsync(UserSession session, byte subType)
    {
        var resp = SiegePacketWriter.Result(SiegePacketConstants.BaseCreate, subType);
        await session.Client.SendPacket(resp);
    }

    private async Task HandleSiegeCastleFlagAsync(UserSession session)
    {
        var siege = gameDataService.SiegeWarfare;
        var masterId = siege?.MasterKnights ?? 0;
        SiegePacketWriter.ClanBanner? banner = null;
        if (masterId > 0 && sessionManager.Knights.GetClan(masterId) is { } clan)
        {
            banner = new SiegePacketWriter.ClanBanner(
                (ushort)clan.Id,
                clan.MarkVersion < 0 ? (ushort)0 : (ushort)clan.MarkVersion,
                clan.Flag,
                clan.Grade);
        }

        await session.Client.SendPacket(SiegePacketWriter.CastleFlag(SiegePacketConstants.CastleFlag, banner));
    }

    private async Task HandleWarfareNpcAsync(UserSession session, Packet packet)
    {
        var siege = gameDataService.SiegeWarfare;
        if (siege == null || packet.RemainingBytes < 1)
            return;

        var subType = packet.ReadByte();
        switch (subType)
        {
            case SiegePacketConstants.WarfareApply:
                await session.Client.SendPacket(SiegePacketWriter.Result(
                    SiegePacketConstants.WarfareNpc, SiegePacketConstants.WarfareApply, SiegePacketConstants.SignUpClosed));
                break;

            case SiegePacketConstants.WarfareSchedule:
                await session.Client.SendPacket(SiegePacketWriter.Schedule(ScheduledWars(siege)));
                break;

            case SiegePacketConstants.WarfareAssault:
                logger.LogInformation("{Name} asked the castle guard for an assault; no reply is defined", session.Name);
                break;

            case SiegePacketConstants.WarfareChallengers:
                await SendChallengersAsync(session, siege);
                break;

            case SiegePacketConstants.WarfareDefendingUnion:
                await session.Client.SendPacket(SiegePacketWriter.DefendingUnion(DefendingClans(siege)));
                break;
        }
    }

    private static List<SiegePacketWriter.WarSchedule> ScheduledWars(SiegeWarfareData siege) =>
        SiegeRules.IsWeekday(siege.WarDay)
            ? [new SiegePacketWriter.WarSchedule(siege.SiegeType, siege.WarDay, siege.WarTime, siege.WarMinute)]
            : [];

    private async Task SendChallengersAsync(UserSession session, SiegeWarfareData siege)
    {
        if (!SiegeRules.IsWeekday(siege.WarRequestDay))
        {
            await session.Client.SendPacket(SiegePacketWriter.Result(
                SiegePacketConstants.WarfareNpc, SiegePacketConstants.WarfareChallengers, SiegePacketConstants.SignUpClosed));
            return;
        }

        short[] requests =
        [
            siege.RequestList1, siege.RequestList2, siege.RequestList3, siege.RequestList4, siege.RequestList5,
            siege.RequestList6, siege.RequestList7, siege.RequestList8, siege.RequestList9, siege.RequestList10,
        ];

        var period = new SiegePacketWriter.RegistrationPeriod(
            siege.WarRequestDay, siege.WarRequestTime, siege.WarRequestMinute,
            SiegeRules.IsWeekday(siege.WarDay) ? SiegeRules.DayBefore(siege.WarDay) : siege.WarRequestDay);

        await session.Client.SendPacket(SiegePacketWriter.Challengers(
            ClanRows(requests), SiegeRules.ChallengerSlots, SiegeRules.ChallengerSignUpFee, period));
    }

    private List<SiegePacketWriter.ClanRow> DefendingClans(SiegeWarfareData siege)
    {
        if (siege.MasterKnights <= 0)
            return [];

        return ClanRows([siege.MasterKnights, .. sessionManager.Knights.GetAllianceClanIds(siege.MasterKnights)]);
    }

    private List<SiegePacketWriter.ClanRow> ClanRows(IEnumerable<short> clanIds) =>
        clanIds
            .Where(clanId => clanId > 0)
            .Distinct()
            .Select(clanId => sessionManager.Knights.GetClan(clanId))
            .OfType<KnightsEntity>()
            .Select(clan => new SiegePacketWriter.ClanRow(
                clan.Name, clan.Nation, (byte)Math.Clamp((int)clan.Members, 0, byte.MaxValue)))
            .ToList();

    private async Task HandleCastleManagerAsync(UserSession session, Packet packet)
    {
        var siege = gameDataService.SiegeWarfare;
        if (siege == null || packet.RemainingBytes < 1)
            return;

        var subType = packet.ReadByte();
        if (!SiegeRules.IsCastleLord(session, siege))
        {
            await session.Client.SendPacket(SiegePacketWriter.Result(
                SiegePacketConstants.CastleManager, subType, SiegePacketConstants.NoAuthority));
            return;
        }

        switch (subType)
        {
            case SiegePacketConstants.ManagerCollect:
                await CollectDungeonChargeAsync(session, siege);
                break;

            case SiegePacketConstants.ManagerTariffs:
                await session.Client.SendPacket(SiegePacketWriter.Tariffs(
                    siege.MoradonTariff, siege.DellosTariff, siege.DungeonEntranceFee));
                break;

            case SiegePacketConstants.ManagerMoradonTariff:
                await ChangeTariffAsync(session, packet, subType, ZoneId.Moradon, rate => siege.MoradonTariff = rate);
                break;

            case SiegePacketConstants.ManagerDelosTariff:
                await ChangeTariffAsync(session, packet, subType, ZoneId.Delos, rate => siege.DellosTariff = rate);
                break;

            case SiegePacketConstants.ManagerDungeonFee:
                await ChangeDungeonFeeAsync(session, packet, siege);
                break;
        }
    }

    private async Task CollectDungeonChargeAsync(UserSession session, SiegeWarfareData siege)
    {
        var charge = Math.Max(siege.DungeonCharge, 0);
        if ((long)session.Money + charge > ExchangePacketConstants.CoinMax)
        {
            await session.Client.SendPacket(SiegePacketWriter.Result(
                SiegePacketConstants.CastleManager, SiegePacketConstants.ManagerCollect, SiegePacketConstants.CoinsFull));
            return;
        }

        session.Money += charge;
        siege.DungeonCharge = 0;
        logger.LogInformation("{Name} collected {Charge} coins of Delos dungeon charge", session.Name, charge);

        await session.Client.SendPacket(SiegePacketWriter.TaxCollected(session.Money, charge));
    }

    private async Task ChangeTariffAsync(UserSession session, Packet packet, byte subType, ZoneId zone, Action<short> apply)
    {
        if (packet.RemainingBytes < sizeof(ushort))
            return;

        var rate = packet.ReadUShort();
        if (rate > SiegeRules.MaxTariff)
        {
            await session.Client.SendPacket(SiegePacketWriter.Result(
                SiegePacketConstants.CastleManager, subType, SiegePacketConstants.RateNotAllowed));
            return;
        }

        apply((short)rate);
        await sessionManager.BroadcastToAll(SiegePacketWriter.TariffChanged(subType, (short)rate, (short)zone));
        logger.LogInformation("{Name} set the {Zone} tariff to {Tariff}", session.Name, zone, rate);
    }

    private async Task ChangeDungeonFeeAsync(UserSession session, Packet packet, SiegeWarfareData siege)
    {
        if (packet.RemainingBytes < sizeof(uint))
            return;

        var fee = packet.ReadUInt();
        if (fee > ExchangePacketConstants.CoinMax)
        {
            await session.Client.SendPacket(SiegePacketWriter.Result(
                SiegePacketConstants.CastleManager, SiegePacketConstants.ManagerDungeonFee, SiegePacketConstants.RateNotAllowed));
            return;
        }

        siege.DungeonEntranceFee = (int)fee;
        logger.LogInformation("{Name} set the Delos dungeon entrance fee to {Fee}", session.Name, fee);
        await session.Client.SendPacket(SiegePacketWriter.DungeonFeeChanged(siege.DungeonEntranceFee));
    }

    private async Task HandleSiegeRankAsync(UserSession session, byte subType)
    {
        var resp = SiegePacketWriter.RankList(SiegePacketConstants.Rank, subType, 0);
        await session.Client.SendPacket(resp);
    }

    public async Task HandleKingAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var subOpcode = packet.ReadByte();
        switch (subOpcode)
        {
            case KingPacketConstants.Election:
                await kingElectionPacketService.HandleElectionAsync(session, packet);
                break;

            case KingPacketConstants.Impeachment:
                await kingElectionPacketService.HandleImpeachmentAsync(session, packet);
                break;

            case KingPacketConstants.Tax:
                await kingGovernancePacketService.HandleTaxAsync(session, packet);
                break;

            case KingPacketConstants.Event:
                await kingGovernancePacketService.HandleKingEventAsync(session, packet);
                break;

            case KingPacketConstants.NationIntro:
                await kingGovernancePacketService.HandleNationIntroAsync(session, packet);
                break;
        }
    }
}
