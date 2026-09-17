using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IKnightsManagementPacketService
{
    Task HandleCapeAsync(UserSession session, Packet packet);
    Task HandlePromoteAsync(UserSession session, Packet packet, KnightsSubOpcode rank);
    Task HandleMemberRequestAsync(UserSession session);
    Task HandleCurrentRequestAsync(UserSession session);
    Task HandleDonateAsync(UserSession session, Packet packet);
    Task HandleUpdateNoticeAsync(UserSession session, Packet packet);
    Task HandleUpdateMemoAsync(UserSession session, Packet packet);
    Task HandleHandoverListAsync(UserSession session);
    Task HandleHandoverRequestAsync(UserSession session, Packet packet);
    Task HandleDonationListAsync(UserSession session);
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
    private const byte ClanChiefFame = 1;
    private const byte ViceChiefFame = 2;
    private const int MaxNoticeLength = 255;

    public async Task HandleCapeAsync(UserSession session, Packet packet)
    {
        if (session.KnightsId <= 0 || session.KnightsFame != 1)
            return;

        var subOpcode = packet.ReadByte();
        if (subOpcode != 1)
            return;

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
            return;

        clan.Cape = packet.ReadShort();
        clan.CapeR = packet.ReadByte();
        clan.CapeG = packet.ReadByte();
        clan.CapeB = packet.ReadByte();
        logger.LogInformation("{Name} changed cape for clan {ClanId}", session.Name, session.KnightsId);

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        await repo.UpdateAsync(clan);

        var notify = KnightsPacketWriter.Cape(
            session.KnightsId, clan.Cape, clan.CapeR, clan.CapeG, clan.CapeB);

        await knightsRuntimeService.NotifyOnlineClanMembersAsync(session.KnightsId, notify);
    }

    public async Task HandlePromoteAsync(UserSession session, Packet packet, KnightsSubOpcode rank)
    {
        if (!knightsRuntimeService.IsClanLeader(session))
        {
            await session.Client.SendPacket(KnightsPacketWriter.Result(rank, 0));
            return;
        }

        var targetName = packet.ReadString();
        var target = sessionManager.GetByName(targetName);
        if (target == null
            || target.KnightsId != session.KnightsId
            || target.Nation != session.Nation
            || string.Equals(target.Name, session.Name, StringComparison.OrdinalIgnoreCase))
        {
            await session.Client.SendPacket(KnightsPacketWriter.Result(rank, 0));
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.Result(rank, 0));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        if (rank == KnightsSubOpcode.Chief)
        {
            logger.LogInformation("{Name} promoted {TargetName} to clan chief in clan {ClanId}", session.Name, target.Name, session.KnightsId);
            session.KnightsFame = 5;
            target.KnightsFame = 1;
            clan.Chief = target.Name;
        }
        else
        {
            if (rank == KnightsSubOpcode.Vicechief
                && !await knightsRuntimeService.CanPromoteToViceChiefAsync(repo, clan.Id, target.Name))
            {
                await session.Client.SendPacket(KnightsPacketWriter.Result(rank, 0));
                return;
            }

            target.KnightsFame = rank switch
            {
                KnightsSubOpcode.Vicechief => 2,
                KnightsSubOpcode.Officer => 3,
                _ => 5
            };
        }

        await repo.UpdateAsync(clan);
        await knightsRuntimeService.SyncCharacterAsync(repo, session);
        await knightsRuntimeService.SyncCharacterAsync(repo, target);

        await session.Client.SendPacket(KnightsPacketWriter.Result(rank, 1));

        var notify = KnightsPacketWriter.Result(rank, 1);
        
        try
        {
            await target.Client.SendPacket(notify);
        }
        catch
        {
            // Ignore target notification failures.
        }
    }

    public async Task HandleMemberRequestAsync(UserSession session)
    {
        if (session.KnightsId <= 0)
        {
            await session.Client.SendPacket(KnightsPacketWriter.EmptyMemberList(
                KnightsSubOpcode.MemberRequest, 0, string.Empty));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        var members = await repo.GetMembersAsync(session.KnightsId);
        var clan = sessionManager.Knights.GetClan(session.KnightsId);

        var response = KnightsPacketWriter.MemberList(
            KnightsSubOpcode.MemberRequest,
            (short)session.KnightsId,
            clan?.Name ?? string.Empty,
            (short)(clan?.Members ?? members.Count),
            members.Select(member =>
            {
                var online = sessionManager.GetByName(member.Name);
                return new KnightsPacketWriter.Member(
                    member.Name,
                    online?.KnightsFame ?? member.Fame,
                    online?.Level ?? member.Level,
                    online?.Class ?? member.Class,
                    online != null);
            }).ToList());

        await session.Client.SendPacket(response);
    }

    public async Task HandleCurrentRequestAsync(UserSession session)
    {
        var response = CreateProcessResponse(KnightsSubOpcode.CurrentRequest);

        if (session.KnightsId <= 0)
        {
            await session.Client.SendPacket(KnightsPacketWriter.Result(KnightsSubOpcode.CurrentRequest, 0));
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.Result(KnightsSubOpcode.CurrentRequest, 0));
            return;
        }

        response = KnightsPacketWriter.ClanInfo(
            KnightsSubOpcode.CurrentRequest,
            (short)clan.Id, clan.Name, clan.Flag, clan.Members, clan.Chief,
            clan.Grade, clan.Points, clan.ClanPointFund, clan.Notice);
        await session.Client.SendPacket(response);
    }

    public async Task HandleDonateAsync(UserSession session, Packet packet)
    {
        var response = CreateProcessResponse(KnightsSubOpcode.DonatePoints);

        if (session.KnightsId <= 0)
        {
            await session.Client.SendPacket(KnightsPacketWriter.Result(KnightsSubOpcode.DonatePoints, 0));
            return;
        }

        var amount = packet.ReadInt();
        if (amount <= 0 || amount > session.Loyalty)
        {
            await session.Client.SendPacket(KnightsPacketWriter.Result(KnightsSubOpcode.DonatePoints, 0));
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan != null)
            clan.ClanPointFund += amount;

        await loyaltyService.DonateToKnightsAsync(session, amount);

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        if (clan != null)
            await repo.UpdateAsync(clan);
        await knightsRuntimeService.SyncCharacterAsync(repo, session, includeLoyalty: true);

        response = KnightsPacketWriter.DonateAccepted(
            KnightsSubOpcode.DonatePoints, session.Loyalty);
        await session.Client.SendPacket(response);
    }

    private const short MarkVersionOk = 1;

    private static Packet CreateProcessResponse(KnightsSubOpcode subOpcode)
    {
        return KnightsPacketWriter.ProcessResponse(subOpcode);
    }

    public async Task HandleUpdateNoticeAsync(UserSession session, Packet packet)
    {
        if (session.KnightsId <= 0 || session.KnightsFame != ClanChiefFame)
        {
            await session.Client.SendPacket(
                KnightsPacketWriter.NoticeRefused(KnightsNoticeResult.NoAuthority));
            return;
        }

        var notice = packet.ReadString();
        if (notice.Length > MaxNoticeLength) notice = notice[..MaxNoticeLength];

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await session.Client.SendPacket(
                KnightsPacketWriter.NoticeRefused(KnightsNoticeResult.CommandUnavailable));
            return;
        }

        clan.Notice = notice;

        using var scope = scopeFactory.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        await knightsRepo.UpdateAsync(clan);

        // Broadcast updated notice to every online clan member.
        if (!string.IsNullOrEmpty(notice))
        {
            var broadcast = KnightsPacketWriter.NoticeUpdate(
                KnightsSubOpcode.UpdateNotice, KnightsPacketWriter.Succeeded, notice);

            foreach (var member in sessionManager.GetAll())
            {
                if (member.KnightsId == clan.Id)
                    await member.Client.SendPacket(broadcast);
            }
        }

        logger.LogInformation("Clan {Clan} notice updated by {Name}: {Notice}", clan.Name, session.Name, notice);
    }

    public async Task HandleUpdateMemoAsync(UserSession session, Packet packet)
    {
        if (session.KnightsId <= 0)
            return;

        if (packet.RemainingBytes < 1)
            return;

        var memoType = packet.ReadByte();
        switch (memoType)
        {
            case 2:
                // Alliance notice — needs the alliance system rework. Reject for now.
                {
                    var resp = KnightsPacketWriter.MemoUpdate(
                        KnightsSubOpcode.UpdateMemo, 2, KnightsPacketWriter.Failed, string.Empty);
                    await session.Client.SendPacket(resp);
                }
                return;

            case 3:
                {
                    var memo = packet.ReadString();
                    if (memo.Length > 20)
                    {
                        var fail = KnightsPacketWriter.MemoUpdate(
                            KnightsSubOpcode.UpdateMemo, 3, KnightsPacketWriter.Failed, memo);
                        await session.Client.SendPacket(fail);
                        return;
                    }

                    // Persist on the Character row. We don't currently track per-member clan memos in our schema,
                    // so the broadcast carries the value but DB persistence requires Character.ClanMemo column —
                    // flagged as a follow-up. For now, broadcast in-memory.
                    var broadcast = KnightsPacketWriter.MemoUpdate(
                        KnightsSubOpcode.UpdateMemo, 3, KnightsPacketWriter.Succeeded, memo);

                    foreach (var member in sessionManager.GetAll())
                    {
                        if (member.KnightsId == session.KnightsId)
                            await member.Client.SendPacket(broadcast);
                    }
                }
                return;

            case 6:
                {
                    var username = packet.RemainingBytes > 0 ? packet.ReadSByteString() : string.Empty;
                    var title = packet.RemainingBytes > 0 ? packet.ReadSByteString() : string.Empty;

                    var resp = KnightsPacketWriter.MemoTitle(
                        KnightsSubOpcode.UpdateMemo, 6, KnightsPacketWriter.Failed, username, title);
                    await session.Client.SendPacket(resp);
                }
                return;

            default:
                logger.LogDebug("Unhandled WIZ_KNIGHTS_PROCESS memo type {Type} from {Name}", memoType, session.Name);
                return;
        }
    }

    public async Task HandleHandoverListAsync(UserSession session)
    {
        if (session.KnightsId <= 0) return;

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null) return;

        byte isClanLeader = session.KnightsFame == 1 ? (byte)1 : (byte)2;

        var viceChiefs = sessionManager.GetAll()
            .Where(member => member.KnightsId == session.KnightsId
                             && member.KnightsFame == ViceChiefFame)
            .Select(member => member.Name)
            .ToList();

        await session.Client.SendPacket(KnightsPacketWriter.HandoverCandidates(
            KnightsSubOpcode.HandoverList, isClanLeader, viceChiefs!));
    }

    public async Task HandleHandoverRequestAsync(UserSession session, Packet packet)
    {
        if (session.KnightsId <= 0 || session.KnightsFame != 1)
        {
            var fail = KnightsPacketWriter.Result(KnightsSubOpcode.HandoverReq, 3);
            await session.Client.SendPacket(fail);
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            var fail = KnightsPacketWriter.Result(KnightsSubOpcode.HandoverReq, 3);
            await session.Client.SendPacket(fail);
            return;
        }

        var targetName = packet.ReadString();
        var target = sessionManager.GetByName(targetName);
        if (target == null
            || target.KnightsId != session.KnightsId
            || target.KnightsFame != 2) // VICECHIEF
        {
            var fail = KnightsPacketWriter.Result(KnightsSubOpcode.HandoverReq, 3);
            await session.Client.SendPacket(fail);
            return;
        }

        // Apply runtime: target becomes chief, current chief drops to trainee.
        var oldChief = clan.Chief;
        clan.Chief = target.Name;
        session.KnightsFame = 5; // TRAINEE
        target.KnightsFame = 1;  // CHIEF

        // Persist: clan + both characters.
        using var scope = scopeFactory.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        var characterRepo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

        await knightsRepo.UpdateAsync(clan);

        var oldChiefChar = await characterRepo.GetById(session.CharacterId);
        if (oldChiefChar != null) { oldChiefChar.Fame = 5; await characterRepo.UpdateAsync(oldChiefChar); }

        var newChiefChar = await characterRepo.GetById(target.CharacterId);
        if (newChiefChar != null) { newChiefChar.Fame = 1; await characterRepo.UpdateAsync(newChiefChar); }

        var broadcast = KnightsPacketWriter.HandoverDone(
            KnightsSubOpcode.HandoverReq, oldChief, target.Name);
        foreach (var member in sessionManager.GetAll())
        {
            if (member.KnightsId == clan.Id)
                await member.Client.SendPacket(broadcast);
        }

        logger.LogInformation("Clan {Clan} handover: {Old} → {New}", clan.Name, oldChief, target.Name);
    }

    public async Task HandleDonationListAsync(UserSession session)
    {
        if (session.KnightsId <= 0) return;

        // Without a per-member donation log, list online clan members ordered by Loyalty.
        var members = sessionManager.GetAll()
            .Where(s => s.KnightsId == session.KnightsId)
            .OrderByDescending(s => s.Loyalty)
            .Take(50)
            .ToList();

        var response = KnightsPacketWriter.DonationList(
            KnightsSubOpcode.DonationList,
            members.Select(m => new KnightsPacketWriter.Donator(m.Name, m.Loyalty)).ToList());
        await session.Client.SendPacket(response);
    }

    public async Task HandleMarkVersionReqAsync(UserSession session)
    {
        short failCode = 1;
        var clan = sessionManager.Knights.GetClan(session.KnightsId);

        if (session.KnightsId <= 0 || session.KnightsFame != 1 || clan == null || clan.Flag < 3)
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
        await Task.CompletedTask;
    }

    public async Task HandleMarkRegisterAsync(UserSession session, Packet packet)
    {
        var size = packet.ReadUShort();

        ushort failCode = 1;
        var clan = sessionManager.Knights.GetClan(session.KnightsId);

        if (session.KnightsId <= 0 || session.KnightsFame != 1) failCode = 11;
        else if (clan == null) failCode = 20;
        else if (clan.Flag < 2) failCode = 11;
        else if (session.ZoneId != (byte)session.Nation) failCode = 12;
        else if (size == 0 || size > KnightsPacketConstants.MaxKnightsMarkBytes) failCode = 13;
        else if (session.Money < KnightsPacketConstants.ClanSymbolCost) failCode = 14;

        if (failCode != 1)
        {
            var fail = KnightsPacketWriter.MarkRegisterResult(
                KnightsSubOpcode.MarkRegister, failCode, 0);
            await session.Client.SendPacket(fail);
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

        // Broadcast success to all online clan members so their UI refreshes.
        var ok = KnightsPacketWriter.MarkRegisterResult(
            KnightsSubOpcode.MarkRegister, 1, (ushort)newVersion);
        await knightsRuntimeService.NotifyOnlineClanMembersAsync(clan.Id, ok);

        logger.LogInformation("Clan {Clan} mark registered: version={Version} size={Size}",
            clan.Name, newVersion, size);
    }

    public async Task HandleMarkReqAsync(UserSession session, Packet packet)
    {
        var clanId = packet.ReadUShort();
        var clan = sessionManager.Knights.GetClan(clanId);
        if (clan == null || clan.Flag < 2 || clan.MarkVersion == 0 || clan.MarkData.Length == 0)
            return;

        var pkt = KnightsPacketWriter.ClanMark(
            KnightsSubOpcode.MarkReq, 1, clan.Nation, clanId,
            (ushort)clan.MarkVersion, clan.MarkData);

        await session.Client.SendPacket(pkt);
    }

    public async Task HandleAllyCreateAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4) return;
        var targetId = packet.ReadInt();

        if (session.Hp <= 0 || session.KnightsId <= 0 || session.KnightsFame != 1)
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyCreate);
            return;
        }

        var mainClan = sessionManager.Knights.GetClan(session.KnightsId);
        if (mainClan == null || mainClan.Flag < 2 || mainClan.AllianceId > 0)
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyCreate);
            return;
        }

        var target = sessionManager.GetByCharacterId(targetId);
        if (target == null || target.Hp <= 0 || target.Nation != session.Nation
            || target.KnightsId <= 0 || target.KnightsFame != 1)
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

        // Record the pending request on the target clan.
        targetClan.AllianceReq = mainClan.Id;
        using (var scope = scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
            await repo.UpdateAsync(targetClan);
        }

        // Send invitation to target. Wire: [u8 sub=28][u8 1 success][string mainClanName][i16 mainClanId].
        var invite = KnightsPacketWriter.AllianceInvite(
            KnightsSubOpcode.AllyCreate, KnightsPacketWriter.Succeeded,
            mainClan.Name, mainClan.Id);
        await target.Client.SendPacket(invite);

        logger.LogInformation("Clan {Main} sent alliance invite to clan {Target}", mainClan.Name, targetClan.Name);
    }

    public async Task HandleAllyReqAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1) return;
        var decision = packet.ReadByte();

        if (session.Hp <= 0 || session.KnightsId <= 0 || session.KnightsFame != 1)
            return;

        var ourClan = sessionManager.Knights.GetClan(session.KnightsId);
        if (ourClan == null || ourClan.AllianceReq == 0 || ourClan.AllianceId > 0)
            return;

        var requestingClanId = ourClan.AllianceReq;
        ourClan.AllianceReq = 0;

        if (decision != 1)
        {
            // Decline — clear the request and persist.
            using var declineScope = scopeFactory.CreateScope();
            await declineScope.ServiceProvider.GetRequiredService<IKnightsRepository>().UpdateAsync(ourClan);
            return;
        }

        var mainClan = sessionManager.Knights.GetClan(requestingClanId);
        if (mainClan == null || mainClan.Flag < 2)
        {
            using var failScope = scopeFactory.CreateScope();
            await failScope.ServiceProvider.GetRequiredService<IKnightsRepository>().UpdateAsync(ourClan);
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyReq);
            return;
        }

        // Phase 1 only supports CREATE (no existing alliance). INSERT into an
        // existing alliance is deferred — we always create a fresh 2-clan alliance.
        using var scope = scopeFactory.CreateScope();
        var allianceRepo = scope.ServiceProvider.GetRequiredService<IKnightsAllianceRepository>();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        var existing = await allianceRepo.FindByMainClanAsync(mainClan.Id);
        KnightsAllianceEntity alliance;
        if (existing != null)
        {
            if (existing.SubClanId == 0) existing.SubClanId = ourClan.Id;
            else if (existing.MercenaryClan1 == 0) existing.MercenaryClan1 = ourClan.Id;
            else if (existing.MercenaryClan2 == 0) existing.MercenaryClan2 = ourClan.Id;
            else
            {
                // Alliance is full.
                await SendAllyFailAsync(session, KnightsSubOpcode.AllyReq);
                return;
            }

            await allianceRepo.UpdateAsync(existing);
            alliance = existing;
            sessionManager.Knights.UpdateAlliance(alliance);

            ourClan.AllianceId = mainClan.Id;
            await knightsRepo.UpdateAsync(ourClan);

            logger.LogInformation("Clan {Sub} joined existing alliance with main={Main}", ourClan.Name, mainClan.Name);
        }
        else
        {
            // Fresh alliance: main + caller as sub clan.
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

            logger.LogInformation("Alliance created: main={Main} sub={Sub}", mainClan.Name, ourClan.Name);
        }

        // Broadcast INSERT to all current alliance members + the newly-joined clan.
        var insert = KnightsPacketWriter.AllianceMembership(
            KnightsSubOpcode.AllyInsert, KnightsPacketWriter.Succeeded,
            mainClan.Id, ourClan.Id, mainClan.Cape);
        foreach (var memberId in alliance.GetAllClanIds())
            await knightsRuntimeService.NotifyOnlineClanMembersAsync(memberId, insert);
    }

    public async Task HandleAllyInsertAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4) return;
        var targetId = packet.ReadInt();

        if (session.Hp <= 0 || session.KnightsId <= 0 || session.KnightsFame != 1)
        {
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyInsert);
            return;
        }

        var mainClan = sessionManager.Knights.GetClan(session.KnightsId);
        if (mainClan == null || mainClan.Flag < 2 || mainClan.AllianceId != mainClan.Id)
        {
            // Caller's clan must be the MAIN of an existing alliance.
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
            || target.KnightsId <= 0 || target.KnightsFame != 1)
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
        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IKnightsRepository>().UpdateAsync(targetClan);

        var invite = KnightsPacketWriter.AllianceInvite(
            KnightsSubOpcode.AllyReq, KnightsPacketWriter.Succeeded,
            mainClan.Name, mainClan.Id);
        await target.Client.SendPacket(invite);

        logger.LogInformation("Alliance {Main} sent INSERT invite to clan {Target}", mainClan.Name, targetClan.Name);
    }

    public async Task HandleAllyPunishAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2) return;
        var targetClanId = packet.ReadShort();

        if (session.Hp <= 0 || session.KnightsId <= 0 || session.KnightsFame != 1)
            return;

        var mainClan = sessionManager.Knights.GetClan(session.KnightsId);
        if (mainClan == null || mainClan.AllianceId != mainClan.Id)
            return; // Caller must be the main-clan chief.

        // Can't punish self.
        if (targetClanId == mainClan.Id) return;

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

        // Build the broadcast before we potentially dissolve the alliance.
        var broadcast = KnightsPacketWriter.AllianceMembership(
            KnightsSubOpcode.AllyPunish, KnightsPacketWriter.Succeeded,
            mainClan.Id, targetClanId, mainClan.Cape);

        // Capture member IDs before potential dissolution.
        var memberIds = alliance.GetAllClanIds().Append(targetClanId).ToList();

        if (alliance.IsEmpty)
        {
            // Only main clan left — dissolve the alliance.
            mainClan.AllianceId = 0;
            await knightsRepo.UpdateAsync(mainClan);
            await allianceRepo.RemoveAsync(alliance.MainClanId);
            sessionManager.Knights.RemoveAlliance(alliance.MainClanId);
            logger.LogInformation("Alliance dissolved after punishing {Target}: only main clan {Main} left",
                targetClan.Name, mainClan.Name);
        }
        else
        {
            await allianceRepo.UpdateAsync(alliance);
            sessionManager.Knights.UpdateAlliance(alliance);
        }

        foreach (var memberId in memberIds)
            await knightsRuntimeService.NotifyOnlineClanMembersAsync(memberId, broadcast);

        logger.LogInformation("Clan {Target} punished from alliance by {Main}", targetClan.Name, mainClan.Name);
    }

    public async Task HandleAllyRemoveAsync(UserSession session)
    {
        if (session.Hp <= 0 || session.KnightsId <= 0 || session.KnightsFame != 1)
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
            // In-memory state stale — clear it.
            clan.AllianceId = 0;
            await knightsRepo.UpdateAsync(clan);
            await SendAllyFailAsync(session, KnightsSubOpcode.AllyRemove);
            return;
        }

        var success = KnightsPacketWriter.AllianceRemoved(
            KnightsSubOpcode.AllyRemove, KnightsPacketWriter.Succeeded, clan.Id);

        if (alliance.MainClanId == clan.Id)
        {
            // Main clan leaves — dissolve the whole alliance.
            foreach (var memberId in alliance.GetAllClanIds().ToList())
            {
                var member = sessionManager.Knights.GetClan(memberId);
                if (member != null)
                {
                    member.AllianceId = 0;
                    await knightsRepo.UpdateAsync(member);
                }
                await knightsRuntimeService.NotifyOnlineClanMembersAsync(memberId, success);
            }
            await allianceRepo.RemoveAsync(alliance.MainClanId);
            sessionManager.Knights.RemoveAlliance(alliance.MainClanId);
            logger.LogInformation("Alliance dissolved by main clan {Clan}", clan.Name);
        }
        else
        {
            // Member clan leaves a slot.
            if (!alliance.RemoveMember(clan.Id))
            {
                await SendAllyFailAsync(session, KnightsSubOpcode.AllyRemove);
                return;
            }
            await allianceRepo.UpdateAsync(alliance);
            clan.AllianceId = 0;
            await knightsRepo.UpdateAsync(clan);
            sessionManager.Knights.UpdateAlliance(alliance);

            // Notify ALL current alliance members + the leaving clan.
            foreach (var memberId in alliance.GetAllClanIds().Append(clan.Id))
                await knightsRuntimeService.NotifyOnlineClanMembersAsync(memberId, success);

            logger.LogInformation("Clan {Clan} left alliance with main={Main}", clan.Name, alliance.MainClanId);
        }
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

        var members = alliance.GetAllClanIds()
            .Select(clanId => new KnightsPacketWriter.AllianceMember(
                clanId,
                sessionManager.Knights.GetClan(clanId)?.Name ?? string.Empty,
                (short)sessionManager.GetAll().Count(s => s.KnightsId == clanId)))
            .ToList();

        await session.Client.SendPacket(
            KnightsPacketWriter.AllianceList(KnightsSubOpcode.AllyList, members));
    }

    private async Task SendAllyFailAsync(UserSession session, KnightsSubOpcode subOpcode)
    {
        await session.Client.SendPacket(
            KnightsPacketWriter.Result(subOpcode, KnightsPacketWriter.Failed));
    }
}
