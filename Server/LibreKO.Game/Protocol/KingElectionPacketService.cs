using System.Text;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
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
    ILogger<KingElectionPacketService> logger) : IKingElectionPacketService
{
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
                await HandleImpeachmentRequestAsync(session);
                break;
            case KingPacketConstants.ImpeachmentRequestElect:
                await HandleImpeachmentVoteAsync(session, packet, KingPacketConstants.ImpeachmentRequestElect);
                break;
            case KingPacketConstants.ImpeachmentList:
                await HandleImpeachmentListAsync(session);
                break;
            case KingPacketConstants.ImpeachmentElect:
                await HandleImpeachmentVoteAsync(session, packet, KingPacketConstants.ImpeachmentElect);
                break;
            case KingPacketConstants.ImpeachmentRequestUiOpen:
                await HandleImpeachmentUiOpenAsync(session, isElectionStage: false);
                break;
            case KingPacketConstants.ImpeachmentElectionUiOpen:
                await HandleImpeachmentUiOpenAsync(session, isElectionStage: true);
                break;
            default:
                logger.LogDebug("WIZ_KING IMPEACHMENT: unhandled sub-opcode {Sub}", imOpcode);
                break;
        }
    }

    private async Task HandleImpeachmentRequestAsync(UserSession session)
    {

        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        if (kingData == null || kingData.ImType != KingPacketConstants.ImpeachmentTypeRequest)
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Impeachment, KingPacketConstants.ImpeachmentRequest, -1));
            return;
        }

        if (!IsSenator(session))
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Impeachment, KingPacketConstants.ImpeachmentRequest, -2));
            return;
        }

        kingData.ImType = KingPacketConstants.ImpeachmentTypeElection;
        await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Impeachment, KingPacketConstants.ImpeachmentRequest, 1));

        logger.LogInformation("{Name} requested impeachment in nation {Nation}", session.Name, session.Nation);
    }

    private async Task HandleImpeachmentVoteAsync(UserSession session, Packet packet, byte responseSub)
    {

        if (packet.RemainingBytes < 1)
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Impeachment, responseSub, -1));
            return;
        }
        _ = packet.ReadByte(); // vote — recorded as opaque ack for now

        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        if (kingData == null) return;

        var requiredPhase = responseSub == KingPacketConstants.ImpeachmentRequestElect
            ? KingPacketConstants.ImpeachmentTypeRequest
            : KingPacketConstants.ImpeachmentTypeElection;

        if (kingData.ImType != requiredPhase)
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Impeachment, responseSub, -1));
            return;
        }

        const byte MinLevelVoter = 50;
        const int MinNpVoter = 10_000;
        if (session.Level < MinLevelVoter || session.Loyalty < MinNpVoter)
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Impeachment, responseSub, -2));
            return;
        }

        await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Impeachment, responseSub, 1));
    }

    private async Task HandleImpeachmentListAsync(UserSession session)
    {

        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        if (kingData == null || kingData.ImType != KingPacketConstants.ImpeachmentTypeElection)
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Impeachment, KingPacketConstants.ImpeachmentList, -1));
            return;
        }

        await session.Client.SendPacket(KingPacketWriter.ResultWithName(
            KingPacketConstants.Impeachment, KingPacketConstants.ImpeachmentList,
            KingPacketWriter.Accepted, kingData.KingName ?? string.Empty));
    }

    private static bool IsSenator(UserSession session)
        => session.KnightsId > 0 && session.KnightsFame == 1;

    private async Task HandleImpeachmentUiOpenAsync(UserSession session, bool isElectionStage)
    {
        var subType = isElectionStage
            ? KingPacketConstants.ImpeachmentElectionUiOpen
            : KingPacketConstants.ImpeachmentRequestUiOpen;

        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        var imType = kingData?.ImType ?? 0;

        short result;
        if (isElectionStage)
        {
            result = imType != KingPacketConstants.ImpeachmentTypeElection ? (short)-1 : (short)1;
        }
        else if (imType != KingPacketConstants.ImpeachmentTypeRequest)
        {
            result = -1;
        }
        else if (session.Fame != SenatorFame)
        {
            result = -2;
        }
        else
        {
            result = 1;
        }

        await session.Client.SendPacket(KingPacketWriter.Result(
            KingPacketConstants.Impeachment, subType, result));
    }

    private async Task SendElectionScheduleAsync(UserSession session)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);

        var response = kingData == null || kingData.Type == KingPacketConstants.ElectionTypeNoTerm
            ? KingPacketWriter.Flag(
                KingPacketConstants.Election, KingPacketConstants.ElectionSchedule, 0)
            : KingPacketWriter.ElectionSchedule(
                KingPacketConstants.ElectionSchedule, KingPacketConstants.Election,
                kingData.Month, kingData.Day, kingData.Hour, kingData.Minute);

        await session.Client.SendPacket(response);
    }

    private async Task HandleElectionNominateAsync(UserSession session, Packet packet)
    {
        var response = CreateElectionResponse(KingPacketConstants.ElectionNominate);
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        if (kingData == null || kingData.Type != KingPacketConstants.ElectionTypeNomination)
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Election, KingPacketConstants.ElectionNominate, -2));
            return;
        }

        if (session.KnightsId <= 0 || session.KnightsFame != 1)
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Election, KingPacketConstants.ElectionNominate, -3));
            return;
        }

        if (packet.RemainingBytes < 2)
            return;

        var nomineeName = packet.ReadSByteString();
        var nominee = sessionManager.GetAll().FirstOrDefault(candidate =>
            string.Equals(candidate.Name, nomineeName, StringComparison.OrdinalIgnoreCase)
            && candidate.Nation == session.Nation);

        if (nominee == null || nominee.KnightsId <= 0 || nominee.KnightsFame != 1)
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Election, KingPacketConstants.ElectionNominate, -3));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKingElectionRepository>();
        var existing = await repo.FindCandidateAsync((byte)session.Nation, KingPacketConstants.ElectionListCandidate, nomineeName);

        if (existing != null)
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Election, KingPacketConstants.ElectionNominate, -4));
            return;
        }

        await repo.AddCandidateAsync(new KingElectionList
        {
            Nation = (byte)session.Nation,
            Type = KingPacketConstants.ElectionListCandidate,
            Name = nominee.Name,
            Knights = nominee.KnightsId,
            Money = 0
        });
        logger.LogInformation("{Name} nominated {NomineeName} for king election in nation {Nation}", session.Name, nominee.Name, session.Nation);

        await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Election, KingPacketConstants.ElectionNominate, 1));
    }

    private async Task HandleElectionNoticeBoardAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var boardOpcode = packet.ReadByte();
        switch (boardOpcode)
        {
            case KingPacketConstants.CandidacyBoardWrite:
                await HandleCandidateBoardWriteAsync(session, packet);
                break;

            case KingPacketConstants.CandidacyBoardRead:
                await HandleCandidateBoardReadAsync(session, packet);
                break;
        }
    }

    private async Task HandleCandidateBoardWriteAsync(UserSession session, Packet packet)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        if (kingData == null
            || kingData.Type < KingPacketConstants.ElectionTypeNomination
            || kingData.Type > KingPacketConstants.ElectionTypeElection)
        {
            await session.Client.SendPacket(BoardWriteResult(BoardWriteWrongStage));
            return;
        }

        if (packet.RemainingBytes < 2)
            return;

        var noticeText = packet.ReadSByteString();
        var noticeBytes = Encoding.UTF8.GetBytes(noticeText);
        if (noticeBytes.Length > MaxNoticeLength)
        {
            await session.Client.SendPacket(BoardWriteResult(BoardWriteNoticeTooLong));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKingElectionRepository>();
        var isCandidate = await repo.IsCandidateAsync((byte)session.Nation, KingPacketConstants.ElectionListCandidate, session.Name);

        if (!isCandidate)
        {
            await session.Client.SendPacket(BoardWriteResult(BoardWriteNotACandidate));
            return;
        }

        await repo.UpsertNoticeBoardAsync(new KingCandidacyNoticeBoard
        {
            UserId = session.Name,
            Nation = (byte)session.Nation,
            NoticeLen = (short)noticeBytes.Length,
            Notice = noticeBytes
        });

        await session.Client.SendPacket(BoardWriteResult(KingPacketWriter.Accepted));
    }

    private async Task HandleCandidateBoardReadAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var readSubOpcode = packet.ReadByte();
        var response = KingPacketWriter.NoticeBoardEntry(
            KingPacketConstants.CandidacyBoardRead, KingPacketConstants.Election,
            KingPacketConstants.ElectionNoticeBoard, readSubOpcode);

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKingElectionRepository>();

        if (readSubOpcode == BoardReadCandidateList)
        {
            var candidates = await repo.GetCandidatesAsync((byte)session.Nation, KingPacketConstants.ElectionListCandidate);

            response = KingPacketWriter.CandidateList(
                KingPacketConstants.CandidacyBoardRead, KingPacketConstants.Election,
                KingPacketConstants.ElectionNoticeBoard, readSubOpcode,
                candidates.Select(candidate => candidate.Name).ToList());
        }
        else if (readSubOpcode == BoardReadNotice)
        {
            if (packet.RemainingBytes < 2)
                return;

            var candidateName = packet.ReadSByteString();
            var notice = await repo.GetNoticeBoardEntryAsync((byte)session.Nation, candidateName);

            response = notice != null && notice.NoticeLen > 0
                ? KingPacketWriter.CandidateNotice(
                    KingPacketConstants.CandidacyBoardRead, KingPacketConstants.Election,
                    KingPacketConstants.ElectionNoticeBoard, readSubOpcode,
                    notice.NoticeLen, notice.Notice)
                : KingPacketWriter.CandidateNotice(
                    KingPacketConstants.CandidacyBoardRead, KingPacketConstants.Election,
                    KingPacketConstants.ElectionNoticeBoard, readSubOpcode, 0, []);
        }

        await session.Client.SendPacket(response);
    }

    private async Task HandleElectionPollAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var pollOpcode = packet.ReadByte();

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKingElectionRepository>();

        if (pollOpcode == PollCandidateList)
        {
            var candidates = await repo.GetCandidatesAsync((byte)session.Nation, KingPacketConstants.ElectionListCandidate);

            var listed = candidates
                .Select(listedCandidate => new KingPacketWriter.PollCandidate(
                    listedCandidate.Name,
                    sessionManager.Knights.GetClan(listedCandidate.Knights)?.Name ?? string.Empty))
                .ToList();

            await session.Client.SendPacket(KingPacketWriter.PollCandidates(
                KingPacketConstants.Election, KingPacketConstants.ElectionPoll, pollOpcode, listed));
            return;
        }

        if (pollOpcode != PollCastVote)
        {
            await session.Client.SendPacket(KingPacketWriter.PollEntry(
                KingPacketConstants.Election, KingPacketConstants.ElectionPoll, pollOpcode));
            return;
        }

        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        if (kingData == null || kingData.Type != KingPacketConstants.ElectionTypeElection)
        {
            await session.Client.SendPacket(PollResult(pollOpcode, PollWrongStage));
            return;
        }

        if (session.Level < MinimumVoterLevel)
        {
            await session.Client.SendPacket(PollResult(pollOpcode, PollLevelTooLow));
            return;
        }

        if (packet.RemainingBytes < 2)
            return;

        var candidateName = packet.ReadSByteString();
        var candidate = await repo.FindCandidateAsync((byte)session.Nation, KingPacketConstants.ElectionListCandidate, candidateName);

        if (candidate == null)
        {
            await session.Client.SendPacket(PollResult(pollOpcode, PollUnknownCandidate));
            return;
        }

        var alreadyVoted = await repo.HasVotedAsync((byte)session.Nation, session.Name);

        if (alreadyVoted)
        {
            await session.Client.SendPacket(PollResult(pollOpcode, PollAlreadyVoted));
            return;
        }

        await repo.AddVoteAsync(new KingBallotBox
        {
            AccountId = session.AccountId.ToString(),
            CharId = session.Name,
            Nation = (byte)session.Nation,
            CandidacyId = candidateName
        });
        logger.LogInformation("{Name} voted for {CandidateName} in nation {Nation}", session.Name, candidateName, session.Nation);

        await session.Client.SendPacket(PollResult(pollOpcode, KingPacketWriter.Accepted));
    }

    private async Task HandleElectionResignAsync(UserSession session)
    {
        var response = CreateElectionResponse(KingPacketConstants.ElectionResign);
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        if (kingData == null || kingData.Type != KingPacketConstants.ElectionTypeNomination)
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Election, KingPacketConstants.ElectionResign, -1));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKingElectionRepository>();
        var candidateEntry = await repo.FindCandidateAsync((byte)session.Nation, KingPacketConstants.ElectionListCandidate, session.Name);

        if (candidateEntry == null)
        {
            await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Election, KingPacketConstants.ElectionResign, -2));
            return;
        }

        await repo.RemoveCandidateAsync(candidateEntry);

        await session.Client.SendPacket(KingPacketWriter.Result(KingPacketConstants.Election, KingPacketConstants.ElectionResign, 1));
    }

    private const byte SenatorFame = 2;
    private const byte BoardReadCandidateList = 1;
    private const byte BoardReadNotice = 2;
    private const byte PollCandidateList = 1;
    private const byte PollCastVote = 2;
    private const byte MinimumVoterLevel = 20;
    private const int MaxNoticeLength = 480;
    private const short BoardWriteWrongStage = -1;
    private const short BoardWriteNoticeTooLong = -2;
    private const short BoardWriteNotACandidate = -3;
    private const short PollWrongStage = -1;
    private const short PollUnknownCandidate = -2;
    private const short PollAlreadyVoted = -3;
    private const short PollLevelTooLow = -4;

    private static Packet CreateElectionResponse(byte electionOpcode) =>
        KingPacketWriter.Election(electionOpcode, KingPacketConstants.Election);

    private static Packet BoardWriteResult(short result) =>
        KingPacketWriter.NoticeBoardResult(
            KingPacketConstants.CandidacyBoardWrite, KingPacketConstants.Election,
            KingPacketConstants.ElectionNoticeBoard, result);

    private static Packet PollResult(byte pollOpcode, short result) =>
        KingPacketWriter.PollResult(
            KingPacketConstants.Election, KingPacketConstants.ElectionPoll, pollOpcode, result);
}
