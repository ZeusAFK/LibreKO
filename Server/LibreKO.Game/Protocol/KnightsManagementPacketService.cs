using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IKnightsManagementPacketService
{
    Task HandlePromoteAsync(UserSession session, Packet packet, KnightsSubOpcode rank);
    Task HandleMemberRequestAsync(UserSession session);
    Task HandleAllListRequestAsync(UserSession session, Packet packet);
    Task HandlePointRequestAsync(UserSession session);
    Task HandlePointMethodAsync(UserSession session, Packet packet);
    Task HandleDonateAsync(UserSession session, Packet packet);
    Task HandleDonationListAsync(UserSession session);
    Task HandleLeaderPointsAsync(UserSession session);
    Task HandleUpdateNoticeAsync(UserSession session, Packet packet);
    Task HandleUpdateMemoAsync(UserSession session, Packet packet);
    Task HandleHandoverListAsync(UserSession session);
    Task HandleHandoverRequestAsync(UserSession session, Packet packet);
    Task HandleMarkVersionReqAsync(UserSession session);
    Task HandleMarkRegisterAsync(UserSession session, Packet packet);
    Task HandleMarkReqAsync(UserSession session, Packet packet);
    Task HandleAllyCreateAsync(UserSession session, Packet packet);
    Task HandleAllyReqAsync(UserSession session, Packet packet);
    Task HandleAllyInsertAsync(UserSession session, Packet packet);
    Task HandleAllyPunishAsync(UserSession session, Packet packet);
    Task HandleAllyRemoveAsync(UserSession session);
    Task HandleAllyListAsync(UserSession session);
}

public class KnightsManagementPacketService(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IKnightsRuntimeService knightsRuntimeService,
    ILoyaltyService loyaltyService,
    ILogger<KnightsManagementPacketService> logger) : IKnightsManagementPacketService
{
    private const short MarkVersionOk = 1;

    public async Task HandlePromoteAsync(UserSession session, Packet packet, KnightsSubOpcode rank)
    {
        if (!knightsRuntimeService.IsClanLeader(session))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(rank, KnightsResult.NoAuthority));
            return;
        }

        var targetName = packet.ReadString();
        if (string.Equals(targetName, session.Name, StringComparison.OrdinalIgnoreCase))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(rank, KnightsResult.CannotChooseYourself));
            return;
        }

        var target = sessionManager.GetByName(targetName);
        if (target == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(rank, KnightsResult.NoSuchUser));
            return;
        }

        if (target.Nation != session.Nation)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(rank, KnightsResult.DifferentNation));
            return;
        }

        if (target.KnightsId != session.KnightsId)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(rank, KnightsResult.NotInClan));
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(rank, KnightsResult.ClanNotValid));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        if (rank == KnightsSubOpcode.Chief)
        {
            session.KnightsFame = ClanRules.FameTrainee;
            target.KnightsFame = ClanRules.FameChief;
            clan.Chief = target.Name;
        }
        else
        {
            if (rank == KnightsSubOpcode.Vicechief
                && !await knightsRuntimeService.CanPromoteToViceChiefAsync(repo, clan.Id, target.Name))
            {
                await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(rank, KnightsResult.NoAuthority));
                return;
            }

            target.KnightsFame = rank switch
            {
                KnightsSubOpcode.Vicechief => ClanRules.FameViceChief,
                KnightsSubOpcode.Officer => ClanRules.FameOfficer,
                _ => ClanRules.FameTrainee,
            };
        }

        await repo.UpdateAsync(clan);
        await knightsRuntimeService.SyncCharacterAsync(repo, session);
        await knightsRuntimeService.SyncCharacterAsync(repo, target);

        await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(rank, KnightsResult.Succeeded));
        await knightsRuntimeService.BroadcastFameChangeAsync(target, clan.Id, target.KnightsFame);
        if (rank == KnightsSubOpcode.Chief)
            await knightsRuntimeService.BroadcastFameChangeAsync(session, clan.Id, session.KnightsFame);

        var title = rank switch
        {
            KnightsSubOpcode.Chief => "chief",
            KnightsSubOpcode.Vicechief => "vice-chief",
            _ => "officer",
        };
        await knightsRuntimeService.SendClanChatAsync(clan.Id, $"{target.Name} has been appointed {title}.");
        logger.LogInformation("{Name} appointed {Target} {Title} in clan {Clan}", session.Name, target.Name, title, clan.Name);
    }

    public async Task HandleMemberRequestAsync(UserSession session)
    {
        if (session.KnightsId <= 0)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MemberListRefused(KnightsResult.NoSuchUser));
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MemberListRefused(KnightsResult.ClanNotValid));
            return;
        }

        await SendMemberListAsync(session, clan);
    }

    private async Task SendMemberListAsync(UserSession session, KnightsEntity clan)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        var members = await repo.GetMembersAsync(clan.Id);
        var now = DateTime.UtcNow;

        var entries = new List<KnightsPacketWriter.Member>(members.Count);
        short online = 0;
        foreach (var member in members)
        {
            var live = sessionManager.GetByName(member.Name);
            if (live != null)
                online++;

            var hours = member.LastOnlineTime is { } last
                ? (int)Math.Max(0, (now - last).TotalHours)
                : 0;
            entries.Add(new KnightsPacketWriter.Member(
                member.Name,
                live?.KnightsFame ?? member.Fame,
                live?.Level ?? member.Level,
                live?.Class ?? member.Class,
                live != null,
                string.Empty,
                hours));
        }

        await session.Client.SendPacket(KnightsPacketWriter.MemberList(
            online, (short)ClanRules.MaxMembers, clan.Notice, entries));
    }

    public async Task HandleAllListRequestAsync(UserSession session, Packet packet)
    {
        var page = packet.RemainingBytes >= 2 ? packet.ReadUShort() : 0;
        var clans = sessionManager.Knights.GetAll()
            .Where(clan => clan.Nation == (byte)session.Nation && clan.Flag >= (byte)ClanType.Promoted)
            .OrderByDescending(clan => clan.Points)
            .ThenBy(clan => clan.Id)
            .Skip(page * KnightsPacketConstants.ClanListPageSize)
            .Take(KnightsPacketConstants.ClanListPageSize)
            .Select(clan => new KnightsPacketWriter.BrowseEntry(clan.Id, clan.Name))
            .ToList();

        await session.Client.SendPacket(KnightsPacketWriter.ClanBrowseList(clans));
    }

    public async Task HandlePointRequestAsync(UserSession session)
    {
        var clan = session.KnightsId > 0 ? sessionManager.Knights.GetClan(session.KnightsId) : null;
        if (clan == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.PointStatusRefused());
            return;
        }

        await session.Client.SendPacket(KnightsPacketWriter.PointStatus(session.Loyalty, clan.ClanPointFund));
    }

    public async Task HandlePointMethodAsync(UserSession session, Packet packet)
    {
        var clan = session.KnightsId > 0 ? sessionManager.Knights.GetClan(session.KnightsId) : null;
        if (clan == null || !knightsRuntimeService.IsClanLeader(session))
            return;

        var choice = packet.RemainingBytes >= 1 ? packet.ReadByte() : (byte)0;
        if (!ClanRules.AcceptsDonations((ClanType)clan.Flag))
        {
            await session.Client.SendPacket(KnightsPacketWriter.PointMethod(
                KnightsPointMethodResult.ClanNotAccredited, clan.ClanPointMethod));
            return;
        }

        if (choice == 0)
        {
            await session.Client.SendPacket(KnightsPacketWriter.PointMethod(
                KnightsPointMethodResult.NotSet, clan.ClanPointMethod));
            return;
        }

        clan.ClanPointMethod = (byte)(choice - 1);
        using (var scope = scopeFactory.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IKnightsRepository>().UpdateAsync(clan);

        await session.Client.SendPacket(KnightsPacketWriter.PointMethod(
            KnightsPointMethodResult.Succeeded, clan.ClanPointMethod));
    }

    public async Task HandleDonateAsync(UserSession session, Packet packet)
    {
        if (session.KnightsId <= 0)
        {
            await session.Client.SendPacket(KnightsPacketWriter.DonateRefused(KnightsDonateResult.Failed));
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.DonateRefused(KnightsDonateResult.ClanNotValid));
            return;
        }

        if (!ClanRules.AcceptsDonations((ClanType)clan.Flag))
        {
            await session.Client.SendPacket(KnightsPacketWriter.DonateRefused(KnightsDonateResult.ClanNotAccredited));
            return;
        }

        var amount = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        if (amount <= 0
            || amount > session.Loyalty
            || session.Loyalty - amount < ClanRules.DonorKeepsNationalPoints)
        {
            await session.Client.SendPacket(KnightsPacketWriter.DonateRefused(KnightsDonateResult.NotEnoughPoints));
            return;
        }

        clan.ClanPointFund = (int)Math.Min((long)clan.ClanPointFund + amount, int.MaxValue);
        await loyaltyService.DonateToKnightsAsync(session, amount);

        using (var scope = scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
            await repo.UpdateAsync(clan);
            await knightsRuntimeService.SyncCharacterAsync(repo, session, includeLoyalty: true);
        }

        await session.Client.SendPacket(KnightsPacketWriter.DonateAccepted(session.Loyalty, clan.ClanPointFund, amount));
        await session.Client.SendPacket(LoyaltyChangePacketWriter.Totals(session.Loyalty, session.MonthlyLoyalty));
        await knightsRuntimeService.SendClanUpdateAsync(clan);

        logger.LogInformation("{Name} saved {Amount} national points for clan {Clan}", session.Name, amount, clan.Name);
    }

    public async Task HandleDonationListAsync(UserSession session)
    {
        var clan = session.KnightsId > 0 ? sessionManager.Knights.GetClan(session.KnightsId) : null;
        if (clan == null || !ClanRules.AcceptsDonations((ClanType)clan.Flag))
            return;

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        var members = await repo.GetMembersAsync(clan.Id);

        var donators = members
            .Select(member => new KnightsPacketWriter.Donator(
                member.Name, sessionManager.GetByName(member.Name)?.KnightsPoints ?? member.DonatedPoints))
            .OrderByDescending(donator => donator.Points)
            .ThenBy(donator => donator.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await session.Client.SendPacket(KnightsPacketWriter.DonationList(donators));
    }

    public async Task HandleLeaderPointsAsync(UserSession session)
    {
        var clan = session.KnightsId > 0 ? sessionManager.Knights.GetClan(session.KnightsId) : null;
        if (clan == null)
            return;

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        var members = await repo.GetMembersAsync(clan.Id);

        var entries = members
            .Select(member => new KnightsPacketWriter.Donator(
                member.Name, sessionManager.GetByName(member.Name)?.Loyalty ?? member.Loyalty))
            .OrderByDescending(entry => entry.Points)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await session.Client.SendPacket(KnightsPacketWriter.LeaderPoints(entries));
    }

    public async Task HandleUpdateNoticeAsync(UserSession session, Packet packet)
    {
        if (session.KnightsId <= 0 || session.KnightsFame != ClanRules.FameChief)
        {
            await session.Client.SendPacket(
                KnightsPacketWriter.NoticeRefused(KnightsNoticeResult.NoAuthority));
            return;
        }

        var notice = packet.ReadString();
        if (notice.Length > KnightsPacketConstants.MaxNoticeLength)
            notice = notice[..KnightsPacketConstants.MaxNoticeLength];

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await session.Client.SendPacket(
                KnightsPacketWriter.NoticeRefused(KnightsNoticeResult.CommandUnavailable));
            return;
        }

        clan.Notice = notice;

        using (var scope = scopeFactory.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IKnightsRepository>().UpdateAsync(clan);

        await knightsRuntimeService.SendClanChatAsync(clan.Id, $"### {session.Name} updated the clan notice. ###");
        foreach (var member in sessionManager.GetAll())
        {
            if (member.KnightsId == clan.Id)
                await SendMemberListAsync(member, clan);
        }

        logger.LogInformation("Clan {Clan} notice updated by {Name}", clan.Name, session.Name);
    }

    public async Task HandleUpdateMemoAsync(UserSession session, Packet packet)
    {
        if (session.KnightsId <= 0 || packet.RemainingBytes < 1)
            return;

        var memoType = packet.ReadByte();
        switch (memoType)
        {
            case 2:
                await session.Client.SendPacket(KnightsPacketWriter.MemoUpdate(
                    KnightsSubOpcode.UpdateMemo, 2, KnightsPacketWriter.Failed, string.Empty));
                return;

            case 3:
                {
                    var memo = packet.ReadString();
                    if (memo.Length > KnightsPacketConstants.MaxMemoLength)
                    {
                        await session.Client.SendPacket(KnightsPacketWriter.MemoUpdate(
                            KnightsSubOpcode.UpdateMemo, 3, KnightsPacketWriter.Failed, memo));
                        return;
                    }

                    await knightsRuntimeService.NotifyOnlineClanMembersAsync(session.KnightsId,
                        KnightsPacketWriter.MemoUpdate(
                            KnightsSubOpcode.UpdateMemo, 3, KnightsPacketWriter.Succeeded, memo));
                }
                return;

            case 6:
                {
                    var username = packet.RemainingBytes > 0 ? packet.ReadSByteString() : string.Empty;
                    var title = packet.RemainingBytes > 0 ? packet.ReadSByteString() : string.Empty;
                    await session.Client.SendPacket(KnightsPacketWriter.MemoTitle(
                        KnightsSubOpcode.UpdateMemo, 6, KnightsPacketWriter.Failed, username, title));
                }
                return;

            default:
                logger.LogDebug("Unhandled clan memo type {Type} from {Name}", memoType, session.Name);
                return;
        }
    }

    public async Task HandleHandoverListAsync(UserSession session)
    {
        if (session.KnightsId <= 0) return;

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null) return;

        var leaderState = knightsRuntimeService.IsClanLeader(session)
            ? KnightsPacketWriter.HandoverLeader
            : KnightsPacketWriter.HandoverNotLeader;

        var viceChiefs = sessionManager.GetAll()
            .Where(member => member.KnightsId == session.KnightsId
                             && member.KnightsFame == ClanRules.FameViceChief)
            .Select(member => member.Name)
            .Take(ClanRules.MaxViceChiefs)
            .ToList();

        await session.Client.SendPacket(KnightsPacketWriter.HandoverCandidates(leaderState, viceChiefs));
    }

    public async Task HandleHandoverRequestAsync(UserSession session, Packet packet)
    {
        if (!knightsRuntimeService.IsClanLeader(session))
        {
            await session.Client.SendPacket(KnightsPacketWriter.HandoverRefused(KnightsHandoverResult.NoAuthority));
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.HandoverRefused(KnightsHandoverResult.NotViceChief));
            return;
        }

        var targetName = packet.ReadString();
        var target = sessionManager.GetByName(targetName);
        if (target == null
            || target.KnightsId != session.KnightsId
            || target.KnightsFame != ClanRules.FameViceChief)
        {
            await session.Client.SendPacket(KnightsPacketWriter.HandoverRefused(KnightsHandoverResult.NotViceChief));
            return;
        }

        var oldChief = clan.Chief;
        clan.Chief = target.Name;
        session.KnightsFame = ClanRules.FameTrainee;
        target.KnightsFame = ClanRules.FameChief;

        using (var scope = scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
            await repo.UpdateAsync(clan);
            await knightsRuntimeService.SyncCharacterAsync(repo, session);
            await knightsRuntimeService.SyncCharacterAsync(repo, target);
        }

        await knightsRuntimeService.NotifyOnlineClanMembersAsync(clan.Id,
            KnightsPacketWriter.HandoverDone(oldChief, target.Name));
        await knightsRuntimeService.BroadcastFameChangeAsync(session, clan.Id, session.KnightsFame);
        await knightsRuntimeService.BroadcastFameChangeAsync(target, clan.Id, target.KnightsFame);

        logger.LogInformation("Clan {Clan} handover: {Old} → {New}", clan.Name, oldChief, target.Name);
    }

    public async Task HandleMarkVersionReqAsync(UserSession session)
    {
        short failCode = MarkVersionOk;
        var clan = sessionManager.Knights.GetClan(session.KnightsId);

        if (session.KnightsId <= 0 || !knightsRuntimeService.IsClanLeader(session) || clan == null
            || clan.Flag < (byte)ClanType.Promoted)
            failCode = 11;
        else if (session.ZoneId != (byte)session.Nation)
            failCode = 12;

        var pkt = failCode == MarkVersionOk
            ? KnightsPacketWriter.MarkVersion(
                KnightsSubOpcode.MarkVersionReq, failCode,
                clan!.MarkVersion < 0 ? (ushort)0 : (ushort)clan.MarkVersion)
            : KnightsPacketWriter.MarkVersionFailed(
                KnightsSubOpcode.MarkVersionReq, failCode);

        await session.Client.SendPacket(pkt);
    }

    public async Task HandleMarkRegisterAsync(UserSession session, Packet packet)
    {
        var size = packet.ReadUShort();

        ushort failCode = 1;
        var clan = sessionManager.Knights.GetClan(session.KnightsId);

        if (session.KnightsId <= 0 || !knightsRuntimeService.IsClanLeader(session)) failCode = 11;
        else if (clan == null) failCode = 20;
        else if (clan.Flag < (byte)ClanType.Promoted) failCode = 11;
        else if (session.ZoneId != (byte)session.Nation) failCode = 12;
        else if (size == 0 || size > KnightsPacketConstants.MaxKnightsMarkBytes) failCode = 13;
        else if (session.Money < KnightsPacketConstants.ClanSymbolCost) failCode = 14;

        if (failCode != 1)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MarkRegisterResult(
                KnightsSubOpcode.MarkRegister, failCode, 0));
            return;
        }

        var data = new byte[size];
        for (var i = 0; i < size; i++) data[i] = packet.ReadByte();

        session.Money -= KnightsPacketConstants.ClanSymbolCost;

        var newVersion = (short)(clan!.MarkVersion + 1);
        if (newVersion == 0) newVersion = 1;
        clan.MarkVersion = newVersion;
        clan.MarkData = data;

        using var scope = scopeFactory.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        var charRepo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        await knightsRepo.UpdateAsync(clan);

        var character = await charRepo.GetById(session.CharacterId);
        if (character != null)
        {
            character.Money = session.Money;
            await charRepo.UpdateAsync(character);
        }

        await knightsRuntimeService.NotifyOnlineClanMembersAsync(clan.Id, KnightsPacketWriter.MarkRegisterResult(
            KnightsSubOpcode.MarkRegister, 1, (ushort)newVersion));

        logger.LogInformation("Clan {Clan} mark registered: version={Version} size={Size}",
            clan.Name, newVersion, size);
    }

    public async Task HandleMarkReqAsync(UserSession session, Packet packet)
    {
        var clanId = packet.ReadUShort();
        var clan = sessionManager.Knights.GetClan(clanId);
        if (clan == null || clan.Flag < (byte)ClanType.Promoted || clan.MarkVersion == 0 || clan.MarkData.Length == 0)
            return;

        await session.Client.SendPacket(KnightsPacketWriter.ClanMark(
            KnightsSubOpcode.MarkReq, 1, clan.Nation, clanId,
            (ushort)clan.MarkVersion, clan.MarkData));
    }

    public async Task HandleAllyCreateAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4) return;
        var targetId = packet.ReadInt();

        if (session.Hp <= 0 || !knightsRuntimeService.IsClanLeader(session))
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyCreate);
            return;
        }

        var mainClan = sessionManager.Knights.GetClan(session.KnightsId);
        if (mainClan == null || mainClan.Flag < (byte)ClanType.Promoted || mainClan.AllianceId > 0)
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyCreate);
            return;
        }

        var target = sessionManager.GetByCharacterId(targetId);
        if (target == null || target.Hp <= 0 || target.Nation != session.Nation
            || !knightsRuntimeService.IsClanLeader(target))
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyCreate);
            return;
        }

        var targetClan = sessionManager.Knights.GetClan(target.KnightsId);
        if (targetClan == null || targetClan.AllianceId > 0 || targetClan.AllianceReq > 0)
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyCreate);
            return;
        }

        targetClan.AllianceReq = mainClan.Id;
        using (var scope = scopeFactory.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IKnightsRepository>().UpdateAsync(targetClan);

        await target.Client.SendPacket(KnightsPacketWriter.AllianceInvite(
            KnightsSubOpcode.AllyReq, mainClan.Name, mainClan.Id));

        logger.LogInformation("Clan {Main} sent an alliance request to clan {Target}", mainClan.Name, targetClan.Name);
    }

    public async Task HandleAllyReqAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1) return;
        var decision = packet.ReadByte();

        if (session.Hp <= 0 || !knightsRuntimeService.IsClanLeader(session))
            return;

        var ourClan = sessionManager.Knights.GetClan(session.KnightsId);
        if (ourClan == null || ourClan.AllianceReq == 0 || ourClan.AllianceId > 0)
            return;

        var requestingClanId = ourClan.AllianceReq;
        ourClan.AllianceReq = 0;

        using var scope = scopeFactory.CreateScope();
        var allianceRepo = scope.ServiceProvider.GetRequiredService<IKnightsAllianceRepository>();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        if (decision != 1)
        {
            await knightsRepo.UpdateAsync(ourClan);
            return;
        }

        var mainClan = sessionManager.Knights.GetClan(requestingClanId);
        if (mainClan == null || mainClan.Flag < (byte)ClanType.Promoted)
        {
            await knightsRepo.UpdateAsync(ourClan);
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyReq);
            return;
        }

        var existing = await allianceRepo.FindByMainClanAsync(mainClan.Id);
        KnightsAllianceEntity alliance;
        if (existing != null)
        {
            if (existing.SubClanId == 0) existing.SubClanId = ourClan.Id;
            else if (existing.MercenaryClan1 == 0) existing.MercenaryClan1 = ourClan.Id;
            else if (existing.MercenaryClan2 == 0) existing.MercenaryClan2 = ourClan.Id;
            else
            {
                await knightsRepo.UpdateAsync(ourClan);
                await SendAllyFailAsync(session, KnightsSubOpcode.AllyReq);
                return;
            }

            await allianceRepo.UpdateAsync(existing);
            alliance = existing;
            sessionManager.Knights.UpdateAlliance(alliance);

            ourClan.AllianceId = mainClan.Id;
            await knightsRepo.UpdateAsync(ourClan);
        }
        else
        {
            alliance = new KnightsAllianceEntity
            {
                MainClanId = mainClan.Id,
                SubClanId = ourClan.Id,
                MercenaryClan1 = 0,
                MercenaryClan2 = 0,
                Notice = string.Empty,
            };
            await allianceRepo.CreateAsync(alliance);

            mainClan.AllianceId = mainClan.Id;
            ourClan.AllianceId = mainClan.Id;
            await knightsRepo.UpdateAsync(mainClan);
            await knightsRepo.UpdateAsync(ourClan);

            sessionManager.Knights.AddAlliance(alliance);
        }

        var joined = KnightsPacketWriter.AllianceJoined(
            mainClan.Id, ourClan.Id, mainClan.Cape,
            KnightsPacketWriter.PackColour(mainClan.CapeR, mainClan.CapeG, mainClan.CapeB));
        foreach (var memberId in alliance.GetAllClanIds())
        {
            await knightsRuntimeService.NotifyOnlineClanMembersAsync(memberId, joined);
            await knightsRuntimeService.SendClanChatAsync(memberId, $"{ourClan.Name} has joined the alliance.");
        }

        await knightsRuntimeService.SendClanUpdateAsync(ourClan);
        var mainChief = sessionManager.GetByName(mainClan.Chief);
        if (mainChief != null && existing == null)
            await mainChief.Client.SendPacket(KnightsPacketWriter.Result(KnightsSubOpcode.AllyCreate, KnightsPacketWriter.Succeeded));

        logger.LogInformation("Clan {Sub} joined the alliance led by {Main}", ourClan.Name, mainClan.Name);
    }

    public async Task HandleAllyInsertAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4) return;
        var targetId = packet.ReadInt();

        if (session.Hp <= 0 || !knightsRuntimeService.IsClanLeader(session))
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyInsert);
            return;
        }

        var mainClan = sessionManager.Knights.GetClan(session.KnightsId);
        if (mainClan == null || mainClan.Flag < (byte)ClanType.Promoted || mainClan.AllianceId != mainClan.Id)
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyInsert);
            return;
        }

        var alliance = sessionManager.Knights.GetAllianceForClan(mainClan.Id);
        if (alliance == null || alliance.IsFull)
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyInsert);
            return;
        }

        var target = sessionManager.GetByCharacterId(targetId);
        if (target == null || target.Hp <= 0 || target.Nation != session.Nation
            || !knightsRuntimeService.IsClanLeader(target))
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyInsert);
            return;
        }

        var targetClan = sessionManager.Knights.GetClan(target.KnightsId);
        if (targetClan == null || targetClan.AllianceId > 0 || targetClan.AllianceReq > 0)
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyInsert);
            return;
        }

        targetClan.AllianceReq = mainClan.Id;
        using (var scope = scopeFactory.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IKnightsRepository>().UpdateAsync(targetClan);

        await target.Client.SendPacket(KnightsPacketWriter.AllianceInvite(
            KnightsSubOpcode.AllyReq, mainClan.Name, mainClan.Id));

        logger.LogInformation("Alliance {Main} invited clan {Target}", mainClan.Name, targetClan.Name);
    }

    public async Task HandleAllyPunishAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2) return;
        var targetClanId = packet.ReadShort();

        if (session.Hp <= 0 || !knightsRuntimeService.IsClanLeader(session))
            return;

        var mainClan = sessionManager.Knights.GetClan(session.KnightsId);
        if (mainClan == null || mainClan.AllianceId != mainClan.Id || targetClanId == mainClan.Id)
            return;

        var alliance = sessionManager.Knights.GetAllianceForClan(mainClan.Id);
        if (alliance == null) return;

        var targetClan = sessionManager.Knights.GetClan(targetClanId);
        if (targetClan == null || targetClan.AllianceId != mainClan.Id)
            return;

        if (!alliance.RemoveMember(targetClanId))
            return;

        using var scope = scopeFactory.CreateScope();
        var allianceRepo = scope.ServiceProvider.GetRequiredService<IKnightsAllianceRepository>();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        targetClan.AllianceId = 0;
        await knightsRepo.UpdateAsync(targetClan);

        var broadcast = KnightsPacketWriter.AllianceLeft(
            KnightsSubOpcode.AllyPunish, mainClan.Id, targetClanId, mainClan.Cape,
            KnightsPacketWriter.PackColour(mainClan.CapeR, mainClan.CapeG, mainClan.CapeB));

        var memberIds = alliance.GetAllClanIds().Append(targetClanId).ToList();

        if (alliance.IsEmpty)
        {
            mainClan.AllianceId = 0;
            await knightsRepo.UpdateAsync(mainClan);
            await allianceRepo.RemoveAsync(alliance.MainClanId);
            sessionManager.Knights.RemoveAlliance(alliance.MainClanId);
        }
        else
        {
            await allianceRepo.UpdateAsync(alliance);
            sessionManager.Knights.UpdateAlliance(alliance);
        }

        foreach (var memberId in memberIds)
        {
            await knightsRuntimeService.NotifyOnlineClanMembersAsync(memberId, broadcast);
            await knightsRuntimeService.SendClanChatAsync(memberId, $"{targetClan.Name} has been expelled from the alliance.");
        }

        await knightsRuntimeService.SendClanUpdateAsync(targetClan);
        logger.LogInformation("Clan {Target} expelled from the alliance by {Main}", targetClan.Name, mainClan.Name);
    }

    public async Task HandleAllyRemoveAsync(UserSession session)
    {
        if (session.Hp <= 0 || !knightsRuntimeService.IsClanLeader(session))
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyRemove);
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null || clan.AllianceId == 0)
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyRemove);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var allianceRepo = scope.ServiceProvider.GetRequiredService<IKnightsAllianceRepository>();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        var alliance = await allianceRepo.FindByMainClanAsync(clan.AllianceId);
        if (alliance == null)
        {
            clan.AllianceId = 0;
            await knightsRepo.UpdateAsync(clan);
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyRemove);
            return;
        }

        var allianceId = alliance.MainClanId;
        if (alliance.MainClanId == clan.Id)
        {
            var memberIds = alliance.GetAllClanIds().ToList();
            foreach (var memberId in memberIds)
            {
                var member = sessionManager.Knights.GetClan(memberId);
                if (member == null) continue;
                member.AllianceId = 0;
                await knightsRepo.UpdateAsync(member);
                await knightsRuntimeService.NotifyOnlineClanMembersAsync(memberId, KnightsPacketWriter.AllianceLeft(
                    KnightsSubOpcode.AllyRemove, allianceId, memberId, member.Cape,
                    KnightsPacketWriter.PackColour(member.CapeR, member.CapeG, member.CapeB)));
                await knightsRuntimeService.SendClanChatAsync(memberId, $"The alliance led by {clan.Name} has been dissolved.");
                await knightsRuntimeService.SendClanUpdateAsync(member);
            }

            await allianceRepo.RemoveAsync(alliance.MainClanId);
            sessionManager.Knights.RemoveAlliance(alliance.MainClanId);
            logger.LogInformation("Alliance dissolved by its main clan {Clan}", clan.Name);
            return;
        }

        if (!alliance.RemoveMember(clan.Id))
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyRemove);
            return;
        }

        await allianceRepo.UpdateAsync(alliance);
        clan.AllianceId = 0;
        await knightsRepo.UpdateAsync(clan);
        sessionManager.Knights.UpdateAlliance(alliance);

        var left = KnightsPacketWriter.AllianceLeft(
            KnightsSubOpcode.AllyRemove, allianceId, clan.Id, ClanRules.NoCape, 0);
        foreach (var memberId in alliance.GetAllClanIds().Append(clan.Id))
        {
            await knightsRuntimeService.NotifyOnlineClanMembersAsync(memberId, left);
            await knightsRuntimeService.SendClanChatAsync(memberId, $"{clan.Name} has left the alliance.");
        }

        await knightsRuntimeService.SendClanUpdateAsync(clan);
        logger.LogInformation("Clan {Clan} left the alliance led by {Main}", clan.Name, allianceId);
    }

    public async Task HandleAllyListAsync(UserSession session)
    {
        var alliance = session.KnightsId > 0
            ? sessionManager.Knights.GetAllianceForClan(session.KnightsId)
            : null;

        if (alliance == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.Result(
                KnightsSubOpcode.AllyList, KnightsPacketWriter.Failed));
            return;
        }

        var clans = new List<KnightsPacketWriter.AllianceClan>();
        foreach (var clanId in alliance.GetAllClanIds())
        {
            var clan = sessionManager.Knights.GetClan(clanId);
            if (clan == null) continue;

            var officers = new List<KnightsPacketWriter.AllianceOfficer>
            {
                new(ClanRules.FameChief, clan.Chief),
            };
            officers.AddRange(sessionManager.GetAll()
                .Where(member => member.KnightsId == clanId && member.KnightsFame == ClanRules.FameViceChief)
                .Take(ClanRules.MaxViceChiefs)
                .Select(member => new KnightsPacketWriter.AllianceOfficer(ClanRules.FameViceChief, member.Name)));

            clans.Add(new KnightsPacketWriter.AllianceClan(clanId, clan.Name, clan.AllianceId > 0, officers));
        }

        await session.Client.SendPacket(KnightsPacketWriter.AllianceList(alliance.Notice, clans));
    }

    private static async Task SendAllyFailAsync(UserSession session, KnightsSubOpcode subOpcode)
    {
        await session.Client.SendPacket(
            KnightsPacketWriter.Result(subOpcode, KnightsPacketWriter.Failed));
    }
}
