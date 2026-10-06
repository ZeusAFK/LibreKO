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

public interface IKingElectionPacketService
{
    Task HandleElectionAsync(UserSession session, Packet packet);
    Task HandleImpeachmentAsync(UserSession session, Packet packet);
}

public class KingElectionPacketService(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IKingSystemRuntimeService kingSystemRuntimeService,
    ILogger<KingElectionPacketService> logger,
    TimeProvider? clock = null) : IKingElectionPacketService
{
    private const byte MinimumVoterLevel = 50;
    private const int MinimumImpeachmentVoterLoyalty = 10_000;
    private const int MaxPlanLength = 500;
    private const int MaxCandidates = 10;
    private const int ImpeachmentCost = 30_000_000;
    private const int ImpeachmentBlackoutDays = 5;
    private const int LastHour = 23;
    private const int LastMinute = 59;

    private TimeProvider Clock => clock ?? TimeProvider.System;

    private readonly record struct Nominee(string Name, AccountNation Nation, short KnightsId, bool LeadsAClan);

    public async Task HandleElectionAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var electionOpcode = packet.ReadByte();
        switch (electionOpcode)
        {
            case KingPacketConstants.ElectionSchedule:
                await SendElectionScheduleAsync(session);
                break;

            case KingPacketConstants.ElectionNominate:
                await HandleElectionNominateAsync(session, packet);
                break;

            case KingPacketConstants.ElectionNoticeBoard:
                await HandleElectionNoticeBoardAsync(session, packet);
                break;

            case KingPacketConstants.ElectionPoll:
                await HandleElectionPollAsync(session, packet);
                break;

            case KingPacketConstants.ElectionResign:
                await HandleElectionResignAsync(session);
                break;
        }
    }

    public async Task HandleImpeachmentAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1) return;
        var imOpcode = packet.ReadByte();

        switch (imOpcode)
        {
            case KingPacketConstants.ImpeachmentRequest:
                await HandleImpeachmentProposalAsync(session);
                break;
            case KingPacketConstants.ImpeachmentRequestElect:
                await HandleSenatorVoteAsync(session, packet);
                break;
            case KingPacketConstants.ImpeachmentList:
                await SendImpeachmentSupportersAsync(session);
                break;
            case KingPacketConstants.ImpeachmentElect:
                await HandlePublicVoteAsync(session, packet);
                break;
            case KingPacketConstants.ImpeachmentRequestUiOpen:
                await SendImpeachmentVoteOpenAsync(
                    session, KingPacketConstants.ImpeachmentRequestUiOpen, KingPacketConstants.ImpeachmentTypeRequest);
                break;
            case KingPacketConstants.ImpeachmentElectionUiOpen:
                await SendImpeachmentVoteOpenAsync(
                    session, KingPacketConstants.ImpeachmentElectionUiOpen, KingPacketConstants.ImpeachmentTypeElection);
                break;
            default:
                logger.LogDebug("WIZ_KING IMPEACHMENT: unhandled sub-opcode {Sub}", imOpcode);
                break;
        }
    }

    private async Task HandleImpeachmentProposalAsync(UserSession session)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        var result = ProposalResult(session, kingData);
        if (result != KingPacketWriter.Accepted || kingData == null)
        {
            await SendImpeachmentResultAsync(session, KingPacketConstants.ImpeachmentRequest, result);
            return;
        }

        logger.LogInformation("{Name} proposed impeaching the king of nation {Nation}; impeachment votes are not run yet",
            session.Name, session.Nation);
    }

    private short ProposalResult(UserSession session, KingSystemData? kingData)
    {
        if (!IsSenator(session))
            return KingPacketConstants.ProposeNotSenator;
        if (kingData == null || string.IsNullOrWhiteSpace(kingData.KingName))
            return KingPacketConstants.ProposeNoKing;
        if (kingData.ImType != KingPacketConstants.ImpeachmentTypeNone)
            return KingPacketConstants.ProposeInProgress;
        if (ElectionIsNear(kingData))
            return KingPacketConstants.ProposeTooCloseToElection;
        if (session.Money < ImpeachmentCost)
            return KingPacketConstants.ProposeNotEnoughCoins;
        return KingPacketWriter.Accepted;
    }

    private bool ElectionIsNear(KingSystemData kingData)
    {
        if (kingData.Type is >= KingPacketConstants.ElectionTypeNomination and <= KingPacketConstants.ElectionTypeElection)
            return true;

        var election = ElectionDate(kingData);
        if (election == null)
            return false;

        var now = Clock.GetLocalNow().DateTime;
        return now >= election.Value.AddDays(-ImpeachmentBlackoutDays) && now <= election.Value;
    }

    private static DateTime? ElectionDate(KingSystemData kingData)
    {
        if (kingData.Year < DateTime.MinValue.Year || kingData.Year > DateTime.MaxValue.Year
            || kingData.Month is < 1 or > 12
            || kingData.Day < 1 || kingData.Day > DateTime.DaysInMonth(kingData.Year, kingData.Month)
            || kingData.Hour > LastHour || kingData.Minute > LastMinute)
            return null;

        return new DateTime(kingData.Year, kingData.Month, kingData.Day, kingData.Hour, kingData.Minute, 0);
    }

    private async Task HandleSenatorVoteAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var vote = packet.ReadByte();
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        var result = kingData?.ImType != KingPacketConstants.ImpeachmentTypeRequest
            ? KingPacketConstants.SenatorVoteClosed
            : !IsSenator(session)
                ? KingPacketConstants.SenatorVoteNotSenator
                : KingPacketWriter.Accepted;

        if (result == KingPacketWriter.Accepted)
            logger.LogInformation("Senator {Name} cast impeachment vote {Vote} in nation {Nation}", session.Name, vote, session.Nation);

        await SendImpeachmentResultAsync(session, KingPacketConstants.ImpeachmentRequestElect, result);
    }

    private async Task HandlePublicVoteAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var vote = packet.ReadByte();
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        var result = kingData?.ImType != KingPacketConstants.ImpeachmentTypeElection
            ? KingPacketConstants.PublicVoteClosed
            : session.Level < MinimumVoterLevel || session.Loyalty < MinimumImpeachmentVoterLoyalty
                ? KingPacketConstants.PublicVoteLevelTooLow
                : KingPacketWriter.Accepted;

        if (result == KingPacketWriter.Accepted)
            logger.LogInformation("{Name} cast impeachment vote {Vote} in nation {Nation}", session.Name, vote, session.Nation);

        await SendImpeachmentResultAsync(session, KingPacketConstants.ImpeachmentElect, result);
    }

    private async Task SendImpeachmentSupportersAsync(UserSession session)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        if (kingData == null || kingData.ImType == KingPacketConstants.ImpeachmentTypeNone)
        {
            await SendImpeachmentResultAsync(session, KingPacketConstants.ImpeachmentList, KingPacketConstants.SupportersClosed);
            return;
        }

        var proposer = kingData.ImRequestId?.Trim();
        IReadOnlyCollection<string> supporters = string.IsNullOrEmpty(proposer) ? [] : [proposer];
        await session.Client.SendPacket(KingPacketWriter.ImpeachmentSupporters(supporters));
    }

    private async Task SendImpeachmentVoteOpenAsync(UserSession session, byte subType, byte requiredStage)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        var result = kingData?.ImType == requiredStage
            ? KingPacketWriter.Accepted
            : KingPacketConstants.ImpeachmentVoteClosed;

        await SendImpeachmentResultAsync(session, subType, result);
    }

    private static Task SendImpeachmentResultAsync(UserSession session, byte subType, short result) =>
        session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Impeachment, subType, result));

    private static bool IsSenator(UserSession session)
        => session.KnightsId > 0 && session.KnightsFame == ClanRules.FameChief;

    private async Task SendElectionScheduleAsync(UserSession session)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        await session.Client.SendPacket(ScheduleFor(kingData));
    }

    private static Packet ScheduleFor(KingSystemData? kingData)
    {
        if (kingData == null)
            return KingPacketWriter.NoElectionSchedule(KingPacketConstants.ScheduleNoImpeachment);

        return kingData.ImType switch
        {
            KingPacketConstants.ImpeachmentTypeRequest =>
                KingPacketWriter.NoElectionSchedule(KingPacketConstants.ScheduleSenatorVote),
            KingPacketConstants.ImpeachmentTypeElection => KingPacketWriter.ElectionSchedule(
                KingPacketConstants.ScheduleImpeachmentVote,
                kingData.ImMonth, kingData.ImDay, kingData.ImHour, kingData.ImMinute),
            _ => ElectionDate(kingData) == null
                ? KingPacketWriter.NoElectionSchedule(KingPacketConstants.ScheduleNoImpeachment)
                : KingPacketWriter.ElectionSchedule(
                    KingPacketConstants.ScheduleKingElection,
                    kingData.Month, kingData.Day, kingData.Hour, kingData.Minute),
        };
    }

    private async Task HandleElectionNominateAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var nomineeName = packet.ReadSByteString().Trim();
        var result = await NominateAsync(session, nomineeName);
        await session.Client.SendPacket(
            KingPacketWriter.Result(KingPacketConstants.Election, KingPacketConstants.ElectionNominate, result));
    }

    private async Task<short> NominateAsync(UserSession session, string nomineeName)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        if (kingData == null || kingData.Type != KingPacketConstants.ElectionTypeNomination)
            return KingPacketConstants.NominateClosed;

        if (!IsSenator(session))
            return KingPacketConstants.NominateNoAuthority;

        using var scope = scopeFactory.CreateScope();
        var found = await FindNomineeAsync(scope.ServiceProvider, nomineeName);
        if (found is not { } nominee)
            return KingPacketConstants.NominateUnknownId;
        if (!nominee.LeadsAClan)
            return KingPacketConstants.NominateFailed;
        if (nominee.Nation != session.Nation)
            return KingPacketConstants.NominateOtherNation;

        var nation = (byte)session.Nation;
        var repo = scope.ServiceProvider.GetRequiredService<IKingElectionRepository>();
        if (await repo.FindCandidateAsync(nation, KingPacketConstants.ElectionListCandidate, nominee.Name) != null)
            return KingPacketConstants.NominateAlreadyNominated;

        var candidates = await repo.GetCandidatesAsync(nation, KingPacketConstants.ElectionListCandidate);
        if (candidates.Count >= MaxCandidates)
            return KingPacketConstants.NominateListFull;

        await repo.AddCandidateAsync(new KingElectionList
        {
            Nation = nation,
            Type = KingPacketConstants.ElectionListCandidate,
            Name = nominee.Name,
            Knights = nominee.KnightsId,
            Money = 0
        });
        logger.LogInformation("{Name} nominated {NomineeName} for king election in nation {Nation}", session.Name, nominee.Name, session.Nation);
        return KingPacketWriter.Accepted;
    }

    private async Task<Nominee?> FindNomineeAsync(IServiceProvider services, string name)
    {
        if (sessionManager.GetByName(name) is { } online)
            return new Nominee(
                online.Name, online.Nation, online.KnightsId,
                online.KnightsId > 0 && online.KnightsFame == ClanRules.FameChief);

        var clan = sessionManager.Knights.GetAll()
            .FirstOrDefault(entry => string.Equals(entry.Chief?.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (clan != null)
            return new Nominee(clan.Chief.Trim(), (AccountNation)clan.Nation, clan.Id, LeadsAClan: true);

        var character = await services.GetRequiredService<ICharacterRepository>().GetByName(name);
        return character == null
            ? null
            : new Nominee(character.Name, AccountNation.None, character.KnightsId, LeadsAClan: false);
    }

    private async Task HandleElectionNoticeBoardAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var boardOpcode = packet.ReadByte();
        switch (boardOpcode)
        {
            case KingPacketConstants.CandidacyBoardWrite:
                await HandleCandidatePlanWriteAsync(session, packet);
                break;

            case KingPacketConstants.CandidacyBoardRead:
                await HandleCandidateBoardReadAsync(session, packet);
                break;
        }
    }

    private async Task HandleCandidatePlanWriteAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < sizeof(ushort))
            return;

        var length = packet.ReadUShort();
        if (packet.RemainingBytes < length)
            return;

        var plan = packet.ReadBytes(length);
        var result = await WritePlanAsync(session, plan);
        await session.Client.SendPacket(KingPacketWriter.PlanWriteResult(result));
    }

    private async Task<short> WritePlanAsync(UserSession session, byte[] plan)
    {
        if (!IsElectionRunning(kingSystemRuntimeService.GetKingData(session.Nation)))
            return KingPacketConstants.PlanWriteClosed;

        if (plan.Length > MaxPlanLength)
            return KingPacketConstants.PlanWriteTooLong;

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKingElectionRepository>();
        if (!await repo.IsCandidateAsync((byte)session.Nation, KingPacketConstants.ElectionListCandidate, session.Name))
            return KingPacketConstants.PlanWriteNotANominee;

        await repo.UpsertNoticeBoardAsync(new KingCandidacyNoticeBoard
        {
            UserId = session.Name,
            Nation = (byte)session.Nation,
            NoticeLen = (short)plan.Length,
            Notice = plan
        });
        return KingPacketWriter.Accepted;
    }

    private async Task HandleCandidateBoardReadAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var readSubOpcode = packet.ReadByte();
        switch (readSubOpcode)
        {
            case KingPacketConstants.BoardReadCandidateList:
                await SendCandidateListAsync(session);
                break;

            case KingPacketConstants.BoardReadPlan when packet.RemainingBytes >= 1:
                await SendCandidatePlanAsync(session, packet.ReadSByteString().Trim());
                break;
        }
    }

    private async Task SendCandidatePlanAsync(UserSession session, string candidateName)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKingElectionRepository>();
        var nation = (byte)session.Nation;

        if (!await repo.IsCandidateAsync(nation, KingPacketConstants.ElectionListCandidate, candidateName))
        {
            await session.Client.SendPacket(KingPacketWriter.PlanRefused(KingPacketConstants.PlanReadNotANominee));
            return;
        }

        var entry = await repo.GetNoticeBoardEntryAsync(nation, candidateName);
        await session.Client.SendPacket(entry == null || entry.Notice.Length == 0
            ? KingPacketWriter.PlanRefused(KingPacketConstants.PlanReadEmpty)
            : KingPacketWriter.Plan(entry.Notice));
    }

    private async Task SendCandidateListAsync(UserSession session)
    {
        if (!IsElectionRunning(kingSystemRuntimeService.GetKingData(session.Nation)))
        {
            await session.Client.SendPacket(KingPacketWriter.PollResult(
                KingPacketConstants.PollCandidateList, KingPacketConstants.PollClosed));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKingElectionRepository>();
        var candidates = await repo.GetCandidatesAsync((byte)session.Nation, KingPacketConstants.ElectionListCandidate);

        var listed = candidates
            .OrderBy(candidate => candidate.Id)
            .Take(MaxCandidates)
            .Select((candidate, index) => new KingPacketWriter.PollCandidate(
                (byte)(index + 1),
                candidate.Name,
                sessionManager.Knights.GetClan(candidate.Knights)?.Name ?? string.Empty))
            .ToList();

        await session.Client.SendPacket(KingPacketWriter.PollCandidates(listed));
    }

    private static bool IsElectionRunning(KingSystemData? kingData) =>
        kingData is { Type: >= KingPacketConstants.ElectionTypeNomination and <= KingPacketConstants.ElectionTypeElection };

    private async Task HandleElectionPollAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var pollOpcode = packet.ReadByte();
        switch (pollOpcode)
        {
            case KingPacketConstants.PollCandidateList:
                await SendCandidateListAsync(session);
                break;

            case KingPacketConstants.PollCastVote when packet.RemainingBytes >= 1:
                var result = await CastVoteAsync(session, packet.ReadSByteString().Trim());
                await session.Client.SendPacket(KingPacketWriter.PollResult(KingPacketConstants.PollCastVote, result));
                break;

            default:
                logger.LogDebug("WIZ_KING POLL: unhandled sub-opcode {Sub}", pollOpcode);
                break;
        }
    }

    private async Task<short> CastVoteAsync(UserSession session, string candidateName)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        if (kingData == null || kingData.Type != KingPacketConstants.ElectionTypeElection)
            return KingPacketConstants.PollClosed;

        if (session.Level < MinimumVoterLevel)
            return KingPacketConstants.PollLevelTooLow;

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKingElectionRepository>();
        var nation = (byte)session.Nation;

        if (await repo.FindCandidateAsync(nation, KingPacketConstants.ElectionListCandidate, candidateName) == null)
            return KingPacketConstants.PollNotANominee;

        if (await repo.HasVotedAsync(nation, session.Name))
            return KingPacketConstants.PollAlreadyVoted;

        await repo.AddVoteAsync(new KingBallotBox
        {
            AccountId = session.AccountId.ToString(),
            CharId = session.Name,
            Nation = nation,
            CandidacyId = candidateName
        });
        logger.LogInformation("{Name} voted for {CandidateName} in nation {Nation}", session.Name, candidateName, session.Nation);
        return KingPacketWriter.Accepted;
    }

    private async Task HandleElectionResignAsync(UserSession session)
    {
        var result = await ResignAsync(session);
        await session.Client.SendPacket(
            KingPacketWriter.Result(KingPacketConstants.Election, KingPacketConstants.ElectionResign, result));
    }

    private async Task<short> ResignAsync(UserSession session)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        if (kingData == null || kingData.Type != KingPacketConstants.ElectionTypeNomination)
            return KingPacketConstants.ResignClosed;

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKingElectionRepository>();
        var candidateEntry = await repo.FindCandidateAsync((byte)session.Nation, KingPacketConstants.ElectionListCandidate, session.Name);
        if (candidateEntry == null)
            return KingPacketConstants.ResignNotANominee;

        await repo.RemoveCandidateAsync(candidateEntry);
        return KingPacketWriter.Accepted;
    }
}
