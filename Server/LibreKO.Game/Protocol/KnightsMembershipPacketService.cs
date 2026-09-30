using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IKnightsMembershipPacketService
{
    Task HandleCreateAsync(UserSession session, Packet packet);
    Task HandleJoinAsync(UserSession session, Packet packet);
    Task HandleInviteAnswerAsync(UserSession session, Packet packet);
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
    IClanStandingService clanStanding,
    IMagicItemUsageService itemUsage,
    ILogger<KnightsMembershipPacketService> logger) : IKnightsMembershipPacketService
{
    private const int InviteRequestBytes = 4;
    private const byte InviteAccepted = 1;
    private const int MinClanNameLength = 2;
    private const int MaxClanNameLength = 20;

    public async Task HandleCreateAsync(UserSession session, Packet packet)
    {
        var clanName = packet.ReadString();

        if (session.Level < ClanRules.CreationLevel)
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

        if (!ZoneRules.Allows(session.ZoneId, ZoneFlags.ClanUpdate))
        {
            await session.Client.SendPacket(KnightsPacketWriter.CreateResult(KnightsCreateResult.WrongTown));
            return;
        }

        if (session.Money < ClanRules.CreationCoins)
        {
            await session.Client.SendPacket(KnightsPacketWriter.CreateResult(KnightsCreateResult.NotEnoughCoins));
            return;
        }

        if (string.IsNullOrWhiteSpace(clanName)
            || clanName.Length < MinClanNameLength
            || clanName.Length > MaxClanNameLength
            || await knightsRepo.IsNameTakenAsync(clanName))
        {
            await session.Client.SendPacket(KnightsPacketWriter.CreateResult(KnightsCreateResult.NameRefused));
            return;
        }

        var clan = new KnightsEntity
        {
            Name = clanName,
            Chief = session.Name,
            Nation = (byte)session.Nation,
            Flag = (byte)ClanType.Training,
            Grade = ClanRules.GradeFromPoints(session.Loyalty),
            Points = session.Loyalty,
            Members = 1,
            Cape = ClanRules.NoCape,
        };

        await knightsRepo.CreateAsync(clan);

        session.Money -= ClanRules.CreationCoins;
        session.KnightsId = clan.Id;
        session.KnightsFame = ClanRules.FameChief;
        session.KnightsName = clanName;
        session.KnightsPoints = 0;
        session.Fame = ClanRules.FameChief;

        await knightsRuntimeService.SyncCharacterAsync(knightsRepo, session, includeMoney: true);

        sessionManager.Knights.AddClan(clan.Id, clan);

        var response = KnightsPacketWriter.ClanCreated(
            session.CharacterId, clan.Id, clanName, clan.Grade, clan.Ranking, session.Money);
        await sessionManager.Regions.SendToRegion(session, response, excludeSender: false);

        logger.LogInformation("{Name} created clan '{Clan}'", session.Name, clanName);
    }

    public async Task HandleJoinAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes >= InviteRequestBytes)
        {
            await HandleInviteAsync(session, packet.ReadInt());
            return;
        }

        logger.LogInformation("{Name} sent a clan application, which nothing on this server admits", session.Name);
    }

    private async Task HandleInviteAsync(UserSession session, int targetId)
    {
        KnightsResult result = KnightsResult.Succeeded;
        KnightsEntity? clan = null;
        UserSession? target = null;

        if (!ZoneRules.Allows(session.ZoneId, ZoneFlags.ClanUpdate))
            result = KnightsResult.NotInThisZone;
        else if (session.KnightsId <= 0 || !ClanRules.CanInvite(session.KnightsFame))
            result = KnightsResult.NoAuthority;
        else if ((clan = sessionManager.Knights.GetClan(session.KnightsId)) == null)
            result = KnightsResult.ClanNotValid;
        else if ((target = sessionManager.GetByCharacterId(targetId)) == null)
            result = KnightsResult.NoSuchUser;
        else if (target.Hp <= 0)
            result = KnightsResult.UserIsDead;
        else if (target.Nation != session.Nation)
            result = KnightsResult.DifferentNation;
        else if (target.KnightsId > 0)
            result = KnightsResult.AlreadyInClan;
        else if (clan.Members >= ClanRules.MaxMembers)
            result = KnightsResult.ClanFull;

        if (result != KnightsResult.Succeeded || clan == null || target == null)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Join, result));
            return;
        }

        target.ClanInviteFrom = session.CharacterId;
        target.ClanInviteClanId = clan.Id;
        await target.Client.SendPacket(KnightsPacketWriter.Invitation(session.CharacterId, clan.Id, clan.Name));
    }

    public async Task HandleInviteAnswerAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 7 || session.ClanInviteFrom == 0)
            return;

        var accepted = packet.ReadByte() == InviteAccepted;
        var inviterId = packet.ReadInt();
        var clanId = packet.ReadShort();

        var pendingFrom = session.ClanInviteFrom;
        var pendingClan = session.ClanInviteClanId;
        session.ClanInviteFrom = 0;
        session.ClanInviteClanId = 0;

        if (inviterId != pendingFrom || clanId != pendingClan)
            return;

        var inviter = sessionManager.GetByCharacterId(inviterId);
        var clan = sessionManager.Knights.GetClan(clanId);

        KnightsResult result = KnightsResult.Succeeded;
        if (inviter == null)
            result = KnightsResult.NoSuchUser;
        else if (!accepted)
            result = KnightsResult.UserDeclined;
        else if (clan == null)
            result = KnightsResult.ClanNotValid;
        else if (clan.Members >= ClanRules.MaxMembers)
            result = KnightsResult.ClanFull;
        else if (session.KnightsId > 0)
            result = KnightsResult.AlreadyInClan;

        if (result != KnightsResult.Succeeded || clan == null)
        {
            var refusal = KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Join, result);
            await session.Client.SendPacket(refusal);
            if (inviter != null)
                await inviter.Client.SendPacket(refusal);
            return;
        }

        await JoinClanAsync(session, clan);
    }

    private async Task JoinClanAsync(UserSession target, KnightsEntity clan)
    {
        target.KnightsId = clan.Id;
        target.KnightsFame = ClanRules.FameTrainee;
        target.KnightsName = clan.Name;
        target.KnightsPoints = 0;
        clan.Members++;

        using (var scope = scopeFactory.CreateScope())
        {
            var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
            await knightsRepo.UpdateAsync(clan);
            await knightsRuntimeService.SyncCharacterAsync(knightsRepo, target);
        }

        await clanStanding.RefreshClanAsync(clan.Id);

        var allianceId = sessionManager.Knights.GetAllianceForClan(clan.Id)?.MainClanId ?? 0;
        var joined = KnightsPacketWriter.JoinAccepted(new KnightsPacketWriter.JoinedState(
            target.CharacterId, clan.Id, clan.Name, target.KnightsFame, clan.Flag, allianceId,
            clan.MarkVersion, clan.Cape, KnightsPacketWriter.PackColour(clan.CapeR, clan.CapeG, clan.CapeB),
            clan.Grade, clan.Ranking));
        await sessionManager.Regions.SendToRegion(target, joined, excludeSender: false);
        await knightsRuntimeService.SendClanChatAsync(clan.Id, $"{target.Name} has joined the clan.");

        logger.LogInformation("{Name} joined clan {Clan}", target.Name, clan.Name);
    }

    public async Task HandleWithdrawAsync(UserSession session)
    {
        if (session.KnightsId <= 0)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Withdraw, KnightsResult.NotInClan));
            return;
        }

        if (session.KnightsFame == ClanRules.FameChief)
        {
            await HandleDestroyAsync(session);
            return;
        }

        if (!ZoneRules.Allows(session.ZoneId, ZoneFlags.ClanUpdate))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Withdraw, KnightsResult.NotInThisZone));
            return;
        }

        var clanId = session.KnightsId;
        var fame = session.KnightsFame;
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

        await sessionManager.Regions.SendToRegion(
            session, KnightsPacketWriter.Withdrew(session.CharacterId, clanId, fame), excludeSender: false);
        await knightsRuntimeService.SendClanChatAsync(clanId, $"{session.Name} has left the clan.");
        await clanStanding.RefreshClanAsync(clanId);

        logger.LogInformation("{Name} left clan {ClanId}", session.Name, clanId);
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

        if (!ZoneRules.Allows(session.ZoneId, ZoneFlags.ClanUpdate))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Remove, KnightsResult.NotInThisZone));
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
        string removedName;

        if (target != null)
        {
            if (target.Nation != session.Nation || target.KnightsId != session.KnightsId)
            {
                await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Remove, KnightsResult.NotInClan));
                return;
            }

            removedName = target.Name;
            clan.Members = (short)Math.Max(0, clan.Members - 1);
            await RefundDonationAsync(clan, target);
            await knightsRepo.UpdateAsync(clan);
            knightsRuntimeService.ClearClanState(target);
            await knightsRuntimeService.SyncCharacterAsync(knightsRepo, target, includeLoyalty: true);
            await knightsRuntimeService.BroadcastFameChangeAsync(target, 0, 0);
        }
        else
        {
            var offline = await charRepo.GetByName(targetName);
            if (offline == null || offline.KnightsId != session.KnightsId)
            {
                await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Remove, KnightsResult.NoSuchUser));
                return;
            }

            removedName = offline.Name;
            clan.Members = (short)Math.Max(0, clan.Members - 1);
            RefundDonation(clan, offline);
            offline.KnightsId = 0;
            offline.Fame = 0;
            await charRepo.UpdateAsync(offline);
            await knightsRepo.UpdateAsync(clan);
        }

        await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Remove, KnightsResult.Succeeded));
        await knightsRuntimeService.SendClanChatAsync(clan.Id, $"{removedName} has been removed from the clan.");
        await clanStanding.RefreshClanAsync(clan.Id);
    }

    public async Task HandleDestroyAsync(UserSession session)
    {
        if (!knightsRuntimeService.IsClanLeader(session))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Destroy, KnightsResult.NoAuthority));
            return;
        }

        if (!ZoneRules.Allows(session.ZoneId, ZoneFlags.ClanUpdate))
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Destroy, KnightsResult.NotInThisZone));
            return;
        }

        var clanId = session.KnightsId;
        var clanName = session.KnightsName;
        using var scope = scopeFactory.CreateScope();
        var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        await knightsRuntimeService.SendClanChatAsync(clanId, $"The clan {clanName} has been disbanded.");

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

        var disbanded = KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Destroy, KnightsResult.Succeeded);
        foreach (var onlineMember in online.Values)
        {
            await RefundDonationAsync(null, onlineMember);
            knightsRuntimeService.ClearClanState(onlineMember);
            await knightsRuntimeService.SyncCharacterAsync(knightsRepo, onlineMember, includeLoyalty: true);

            try
            {
                await onlineMember.Client.SendPacket(disbanded);
                await onlineMember.Client.SendPacket(
                    KnightsBroadcastBuilders.BuildClanPointsBattleNotification(
                        KnightsBroadcastBuilders.ClanPointsBattleDisband));
                await knightsRuntimeService.BroadcastFameChangeAsync(onlineMember, 0, 0);
            }
            catch
            {
            }
        }

        sessionManager.Knights.RemoveClan(clanId);
        await clanStanding.RefreshRankingsAsync();

        logger.LogInformation("{Name} disbanded clan {Clan}", session.Name, clanName);
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

        if (clan.Members >= ClanRules.MaxMembers)
        {
            await session.Client.SendPacket(KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Admit, KnightsResult.ClanFull));
            return;
        }

        await JoinClanAsync(target, clan);
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

        try
        {
            await target.Client.SendPacket(
                KnightsPacketWriter.MembershipResult(KnightsSubOpcode.Reject, KnightsResult.UserDeclined));
        }
        catch
        {
        }
    }

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
