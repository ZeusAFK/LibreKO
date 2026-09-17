using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IKnightsMembershipPacketService
{
    Task HandleCreateAsync(UserSession session, Packet packet);
    Task HandleJoinRequestAsync(UserSession session, Packet packet);
    Task HandleWithdrawAsync(UserSession session);
    Task HandleRemoveAsync(UserSession session, Packet packet);
    Task HandleDestroyAsync(UserSession session);
    Task HandleAdmitAsync(UserSession session, Packet packet);
    Task HandleRejectAsync(UserSession session, Packet packet);
}

public class KnightsMembershipPacketService(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IKnightsRuntimeService knightsRuntimeService,
    IMagicItemUsageService itemUsage,
    ILogger<KnightsMembershipPacketService> logger) : IKnightsMembershipPacketService
{
    private async Task<int> RefundDonationAsync(KnightsEntity? clan, UserSession member)
    {
        var donated = member.KnightsPoints;
        member.KnightsPoints = 0;
        if (donated <= 0)
            return 0;

        WithdrawFromFund(clan, donated);

        var keepsEverything = false;
        foreach (var itemId in ClanDonationCalculator.RecoveryItemIds)
        {
            if (!await itemUsage.TryConsumeItemAsync(member, itemId))
                continue;

            keepsEverything = true;
            break;
        }

        var refund = ClanDonationCalculator.RefundFor(donated, keepsEverything);
        member.Loyalty = (int)Math.Min(
            ClanDonationCalculator.LoyaltyMax, (long)member.Loyalty + refund);

        try
        {
            await member.Client.SendPacket(
                LoyaltyChangePacketWriter.Totals(member.Loyalty, member.MonthlyLoyalty));
        }
        catch
        {
        }

        logger.LogInformation(
            "{Name} left clan {ClanId} with {Donated} donated and was refunded {Refund}",
            member.Name, clan?.Id ?? 0, donated, refund);
        return refund;
    }

    private static int RefundDonation(KnightsEntity? clan, Character member)
    {
        var donated = member.KnightsPoints;
        member.KnightsPoints = 0;
        if (donated <= 0)
            return 0;

        WithdrawFromFund(clan, donated);

        var refund = ClanDonationCalculator.RefundFor(donated, false);
        member.Loyalty = (int)Math.Min(
            ClanDonationCalculator.LoyaltyMax, (long)member.Loyalty + refund);
        return refund;
    }

    private static void WithdrawFromFund(KnightsEntity? clan, int donated)
    {
        if (clan == null)
            return;

        var (grade, fund) = ClanDonationCalculator.WithdrawDonation(
            (ClanType)clan.Flag, clan.ClanPointFund, donated);
        clan.Flag = (byte)grade;
        clan.ClanPointFund = fund;
    }


    public async Task HandleCreateAsync(UserSession session, Packet packet)
    {

        if (session.Level < KnightsPacketConstants.ClanLevelRequirement)
        {
            await session.Client.SendPacket(KnightsPacketWriter.CreateResult(KnightsCreateResult.LevelTooLow));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        if (await HasValidClanMembershipAsync(session, knightsRepo))
        {
            await session.Client.SendPacket(KnightsPacketWriter.CreateResult(KnightsCreateResult.AlreadyInClan));
            return;
        }

        if (session.Money < KnightsPacketConstants.ClanCoinRequirement)
        {
            await session.Client.SendPacket(KnightsPacketWriter.CreateResult(KnightsCreateResult.NotEnoughCoins));
            return;
        }

        var clanName = packet.ReadString();
        if (string.IsNullOrEmpty(clanName) || clanName.Length < 2 || clanName.Length > 20)
        {
            await session.Client.SendPacket(KnightsPacketWriter.Result(KnightsSubOpcode.Create, 3));
            return;
        }

        if (await knightsRepo.IsNameTakenAsync(clanName))
        {
            await session.Client.SendPacket(KnightsPacketWriter.Result(KnightsSubOpcode.Create, 3));
            return;
        }

        var clan = new KnightsEntity
        {
            Name = clanName,
            Chief = session.Name,
            Nation = (byte)session.Nation,
            Flag = 1,
            Members = 1
        };

        await knightsRepo.CreateAsync(clan);

        session.Money -= KnightsPacketConstants.ClanCoinRequirement;
        session.KnightsId = (short)clan.Id;
        session.KnightsFame = 1;
        session.KnightsName = clanName;
        session.Fame = 1;

        await knightsRuntimeService.SyncCharacterAsync(knightsRepo, session, includeMoney: true);

        sessionManager.Knights.AddClan(clan.Id, clan);

        var response = KnightsPacketWriter.ClanCreated(
            session.CharacterId, session.KnightsId, clanName, clan.Grade, session.Money);
        await sessionManager.Regions.SendToRegion(session, response, excludeSender: false);

        logger.LogInformation("{Name} created clan '{Clan}'", session.Name, clanName);
    }

    public async Task HandleJoinRequestAsync(UserSession session, Packet packet)
    {

        using var scope = scopeFactory.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        if (await HasValidClanMembershipAsync(session, knightsRepo))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Join, KnightsResult.AlreadyInClan));
            return;
        }

        var clanId = packet.ReadShort();
        var clan = sessionManager.Knights.GetClan(clanId);
        if (clan == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Join, KnightsResult.ClanNotValid));
            return;
        }

        var leader = sessionManager.GetByName(clan.Chief);
        if (leader == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Join, KnightsResult.NoAuthority));
            return;
        }

        var request = KnightsPacketWriter.JoinRequestForwarded(
            session.CharacterId, clanId, session.Name);
        await leader.Client.SendPacket(request);
    }

    public async Task HandleWithdrawAsync(UserSession session)
    {

        if (session.KnightsId <= 0 || session.KnightsFame == 1)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Withdraw, KnightsResult.NotInClan));
            return;
        }

        var clanId = session.KnightsId;
        var clan = sessionManager.Knights.GetClan(clanId);

        using var scope = scopeFactory.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        await RefundDonationAsync(clan, session);

        if (clan != null)
        {
            clan.Members = (short)Math.Max(0, clan.Members - 1);
            await knightsRepo.UpdateAsync(clan);
        }

        knightsRuntimeService.ClearClanState(session);
        await knightsRuntimeService.SyncCharacterAsync(knightsRepo, session, includeLoyalty: true);

        await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Withdraw, KnightsResult.Succeeded));

        logger.LogInformation("{Name} left clan", session.Name);
    }

    public async Task HandleRemoveAsync(UserSession session, Packet packet)
    {

        if (!knightsRuntimeService.IsClanLeader(session))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Remove, KnightsResult.NoAuthority));
            return;
        }

        var targetName = packet.ReadString();
        if (string.IsNullOrWhiteSpace(targetName)
            || string.Equals(targetName, session.Name, StringComparison.OrdinalIgnoreCase))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Remove, KnightsResult.CannotChooseYourself));
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Remove, KnightsResult.ClanNotValid));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        var charRepo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

        var target = sessionManager.GetByName(targetName);

        if (target != null)
        {
            if (target.Nation != session.Nation || target.KnightsId != session.KnightsId)
            {
                await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Remove, KnightsResult.NotInClan));
                return;
            }
        }
        else
        {
            var targetCharacter = await charRepo.GetByName(targetName);
            if (targetCharacter == null || targetCharacter.KnightsId != session.KnightsId)
            {
                await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Remove, KnightsResult.NoSuchUser));
                return;
            }
        }

        clan.Members = (short)Math.Max(0, clan.Members - 1);

        if (target != null)
        {
            await RefundDonationAsync(clan, target);
            await knightsRepo.UpdateAsync(clan);
            knightsRuntimeService.ClearClanState(target);
            await knightsRuntimeService.SyncCharacterAsync(knightsRepo, target, includeLoyalty: true);
        }
        else
        {
            // Offline character: clear clan state directly via repository
            var offlineChar = await charRepo.GetByName(targetName);
            if (offlineChar != null)
            {
                RefundDonation(clan, offlineChar);
                offlineChar.KnightsId = 0;
                offlineChar.Fame = 0;
                await charRepo.UpdateAsync(offlineChar);
            }

            await knightsRepo.UpdateAsync(clan);
        }

        var removed = KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Remove, KnightsResult.Succeeded);
        if (target != null)
        {
            try
            {
                await target.Client.SendPacket(removed);
            }
            catch
            {
                // Ignore target notification failures.
            }
        }

        await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Remove, KnightsResult.Succeeded));
    }

    public async Task HandleDestroyAsync(UserSession session)
    {

        if (!knightsRuntimeService.IsClanLeader(session))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Destroy, KnightsResult.NoAuthority));
            return;
        }

        var clanId = session.KnightsId;
        using var scope = scopeFactory.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        var entity = await knightsRepo.FindAsync(clanId);
        if (entity != null)
            await knightsRepo.RemoveAsync(entity);

        var online = sessionManager.GetAll()
            .Where(member => member.KnightsId == clanId)
            .ToDictionary(member => member.Name, StringComparer.OrdinalIgnoreCase);

        var members = await knightsRepo.GetCharactersByClanAsync(clanId);
        var charRepo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        foreach (var member in members)
        {
            if (!online.ContainsKey(member.Name))
                RefundDonation(null, member);
            member.KnightsId = 0;
            member.Fame = 0;
            await charRepo.UpdateAsync(member);
        }

        foreach (var onlineMember in online.Values)
        {
            await RefundDonationAsync(null, onlineMember);
            knightsRuntimeService.ClearClanState(onlineMember);

            var notify = KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Destroy, KnightsResult.Succeeded);
            try
            {
                await onlineMember.Client.SendPacket(notify);
                await onlineMember.Client.SendPacket(
                    KnightsBroadcastBuilders.BuildClanPointsBattleNotification(
                        KnightsBroadcastBuilders.ClanPointsBattleDisband));
            }
            catch
            {
                // Ignore per-member delivery failures.
            }
        }

        sessionManager.Knights.RemoveClan(clanId);

        await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Destroy, KnightsResult.Succeeded));

        logger.LogInformation("{Name} destroyed clan {ClanId}", session.Name, clanId);
    }

    public async Task HandleAdmitAsync(UserSession session, Packet packet)
    {

        if (!knightsRuntimeService.CanAdmitCandidates(session))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Admit, KnightsResult.NoAuthority));
            return;
        }

        var targetName = packet.ReadString();
        var target = sessionManager.GetByName(targetName);
        if (target == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Admit, KnightsResult.NoSuchUser));
            return;
        }

        if (string.Equals(target.Name, session.Name, StringComparison.OrdinalIgnoreCase))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Admit, KnightsResult.CannotChooseYourself));
            return;
        }

        if (target.KnightsId > 0)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Admit, KnightsResult.AlreadyInClan));
            return;
        }

        if (target.Nation != session.Nation)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Admit, KnightsResult.DifferentNation));
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Admit, KnightsResult.ClanNotValid));
            return;
        }

        if (clan.Members >= KnightsPacketConstants.MaxClanUsers)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Admit, KnightsResult.ClanFull));
            return;
        }

        target.KnightsId = session.KnightsId;
        target.KnightsFame = 5;
        target.KnightsName = clan.Name;
        clan.Members++;

        using var scope = scopeFactory.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        await knightsRepo.UpdateAsync(clan);
        await knightsRuntimeService.SyncCharacterAsync(knightsRepo, target);

        var joined = KnightsPacketWriter.JoinAccepted(new KnightsPacketWriter.JoinedState(
            target.CharacterId, session.KnightsId, clan.Name, target.KnightsFame, clan.Flag,
            clan.MarkVersion, clan.Cape, KnightsPacketWriter.PackColour(clan.CapeR, clan.CapeG, clan.CapeB),
            clan.Grade));
        try
        {
            await target.Client.SendPacket(joined);
        }
        catch
        {
            // Ignore target notification failures.
        }

        await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Admit, KnightsResult.Succeeded));
    }

    public async Task HandleRejectAsync(UserSession session, Packet packet)
    {
        if (!knightsRuntimeService.CanAdmitCandidates(session))
            return;

        var targetName = packet.ReadString();
        var target = sessionManager.GetByName(targetName);
        if (target == null || target.Nation != session.Nation)
            return;

        var response = KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Reject, KnightsResult.UserDeclined);
        try
        {
            await target.Client.SendPacket(response);
        }
        catch
        {
            // Ignore target notification failures.
        }
    }

    private static Packet CreateProcessResponse(KnightsSubOpcode subOpcode)
    {
        return KnightsPacketWriter.ProcessResponse(subOpcode);
    }

    private async Task<bool> HasValidClanMembershipAsync(UserSession session, IKnightsRepository knightsRepo)
    {
        if (session.KnightsId <= 0)
            return false;

        if (sessionManager.Knights.GetClan(session.KnightsId) != null)
            return true;

        logger.LogWarning(
            "Repairing stale clan membership for {Name}: session references missing clan {ClanId}",
            session.Name,
            session.KnightsId);

        knightsRuntimeService.ClearClanState(session);
        await knightsRuntimeService.SyncCharacterAsync(knightsRepo, session);
        return false;
    }
}
