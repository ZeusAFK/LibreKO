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
    IKingSystemRuntimeService kingSystemRuntimeService,
    IKingElectionPacketService kingElectionPacketService,
    IKingGovernancePacketService kingGovernancePacketService,
    IBifrostEventService bifrostEventService,
    ILogger<NationSystemsPacketCoordinator> logger) : INationSystemsPacketCoordinator
{

    private const byte SiegeBaseCreate = 1;
    private const byte SiegeCastleFlag = 2;
    private const byte SiegeMoradonNpc = 3;
    private const byte SiegeDelosNpc = 4;
    private const byte SiegeRank = 5;
    private const ushort SiegeTariffMax = 20;
    private const byte ZoneMoradon = (byte)ZoneId.Moradon;
    private const byte ZoneDelos = (byte)ZoneId.Delos;

    public async Task HandleBifrostAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 1)
            return;

        //   2 = BIFROST_EVENT    — remaining time query (the only one with logic)
        var sub = packet.ReadByte();
        if (sub != (byte)TempleSubOpcode.BifrostRemaining)
            return;

        // Remaining-secs comes from the live Bifrost lifecycle service; clients
        // read 0 as "event inactive" and show the right UI state.
        var remaining = bifrostEventService.RemainingSecs;
        var response = BifrostPacketWriter.Remaining(
            TempleSubOpcode.BifrostRemaining, (int)Math.Min(remaining, int.MaxValue));
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
        var subType = packet.RemainingBytes >= 1 ? packet.ReadByte() : (byte)0;
        var tariff = packet.RemainingBytes >= 2 ? packet.ReadUShort() : (ushort)0;

        switch (opcode)
        {
            case SiegeBaseCreate: await HandleSiegeBaseCreateAsync(session, subType); break;
            case SiegeCastleFlag: await HandleSiegeCastleFlagAsync(session); break;
            case SiegeMoradonNpc: await HandleSiegeMoradonNpcAsync(session, subType); break;
            case SiegeDelosNpc: await HandleSiegeDelosNpcAsync(session, subType, tariff); break;
            case SiegeRank: await HandleSiegeRankAsync(session, subType); break;
        }
    }

    private static async Task HandleSiegeBaseCreateAsync(UserSession session, byte subType)
    {
        var resp = SiegePacketWriter.Result(SiegeBaseCreate, subType);
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

        await session.Client.SendPacket(SiegePacketWriter.CastleFlag(SiegeCastleFlag, banner));
    }

    private async Task HandleSiegeMoradonNpcAsync(UserSession session, byte subType)
    {
        var siege = gameDataService.SiegeWarfare;
        if (siege == null) return;

        switch (subType)
        {
            case 2:
            {
                var resp = SiegePacketWriter.CastleSchedule(
                    SiegeMoradonNpc, 2, (ushort)siege.CastleIndex, siege.SiegeType,
                    new SiegePacketWriter.WarSchedule(siege.WarDay, siege.WarTime, siege.WarMinute));
                await session.Client.SendPacket(resp);
                break;
            }
            case 4:
            {
                if (siege.MasterKnights == 0) return;
                var clan = sessionManager.Knights.GetClan(siege.MasterKnights);
                if (clan == null) return;
                var resp = SiegePacketWriter.CastleApplicants(
                    SiegeMoradonNpc, 4, (ushort)siege.CastleIndex, clan.Name, clan.Nation,
                    (ushort)clan.Members,
                    new SiegePacketWriter.WarSchedule(
                        siege.WarRequestDay, siege.WarRequestTime, siege.WarRequestMinute));
                await session.Client.SendPacket(resp);
                break;
            }
            case 5:
            {
                if (siege.MasterKnights == 0) return;
                var clan = sessionManager.Knights.GetClan(siege.MasterKnights);
                if (clan == null) return;
                var resp = SiegePacketWriter.CastleOwner(
                    SiegeMoradonNpc, 5, (ushort)siege.CastleIndex, siege.SiegeType,
                    clan.Name, clan.Nation, (ushort)clan.Members);
                await session.Client.SendPacket(resp);
                break;
            }
        }
    }

    private async Task HandleSiegeDelosNpcAsync(UserSession session, byte subType, ushort tariff)
    {
        var siege = gameDataService.SiegeWarfare;
        if (siege == null) return;

        bool isKing = IsCallerKing(session);

        switch (subType)
        {
            case 2: // Collect funds — king gets Moradon+Delos tax, castellan gets dungeon charge.
            {
                if (isKing)
                {
                    var gold = (long)siege.MoradonTax + siege.DellosTax;
                    if (session.Money + gold > 2_100_000_000L) return;
                    session.Money += (int)gold;
                    siege.MoradonTax = 0;
                    siege.DellosTax = 0;
                }
                else
                {
                    var charge = siege.DungeonCharge;
                    if (session.Money + (long)charge > 2_100_000_000L) return;
                    session.Money += charge;
                    siege.DungeonCharge = 0;
                }
                break;
            }
            case 3: // View tariffs (non-king only)
            {
                if (isKing) return;
                var resp = SiegePacketWriter.Tariffs(
                    SiegeDelosNpc, 3, (ushort)siege.CastleIndex, (ushort)siege.MoradonTariff,
                    (ushort)siege.DellosTariff, siege.DungeonCharge);
                await session.Client.SendPacket(resp);
                break;
            }
            case 4: // Set Moradon tariff (castellan leader only, cap 20)
            {
                if (tariff > SiegeTariffMax || isKing) return;
                siege.MoradonTariff = (short)tariff;
                var resp = SiegePacketWriter.TariffChanged(SiegeDelosNpc, 4, tariff, ZoneMoradon);
                await sessionManager.BroadcastToAll(resp);
                logger.LogInformation("{Name} set Moradon tariff to {Tariff}", session.Name, tariff);
                break;
            }
            case 5: // Set Delos tariff
            {
                if (tariff > SiegeTariffMax || isKing) return;
                siege.DellosTariff = (short)tariff;
                var resp = SiegePacketWriter.TariffChanged(SiegeDelosNpc, 5, tariff, ZoneDelos);
                await sessionManager.BroadcastToAll(resp);
                logger.LogInformation("{Name} set Delos tariff to {Tariff}", session.Name, tariff);
                break;
            }
        }
    }

    private async Task HandleSiegeRankAsync(UserSession session, byte subType)
    {
        var resp = SiegePacketWriter.RankList(SiegeRank, subType, 0);
        await session.Client.SendPacket(resp);
    }

    private bool IsCallerKing(UserSession session)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        return kingData != null
               && !string.IsNullOrEmpty(kingData.KingName)
               && string.Equals(kingData.KingName, session.Name, StringComparison.OrdinalIgnoreCase);
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

            case KingPacketConstants.Npc:
                await kingGovernancePacketService.HandleKingNpcAsync(session);
                break;

            case KingPacketConstants.NationIntro:
                await kingGovernancePacketService.HandleNationIntroAsync(session);
                break;
        }
    }
}
