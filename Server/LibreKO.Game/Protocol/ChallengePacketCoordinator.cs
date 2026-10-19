using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

using LibreKO.Common.Enums;

namespace LibreKO.Game.Protocol;

public interface IChallengePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Task CancelAsync(UserSession session);
}

public class ChallengePacketCoordinator(
    SessionManager sessionManager,
    IZoneTransitionService zoneTransitionService,
    ILogger<ChallengePacketCoordinator> logger) : IChallengePacketCoordinator
{
    private const byte ChallengePvpRequest = 1;
    private const byte ChallengePvpCancel = 2;
    private const byte ChallengePvpAccept = 3;
    private const byte ChallengePvpReject = 4;
    private const byte ChallengePvpRequestSent = 5;
    private const byte ChallengeGenericError = 11;
    private const byte ChallengeZoneError = 12;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0)
            return;

        var opcode = packet.ReadByte();
        switch (opcode)
        {
            case ChallengePvpRequest:
                await RequestAsync(session, packet);
                break;

            case ChallengePvpAccept:
                await AcceptAsync(session);
                break;

            case ChallengePvpCancel:
            case ChallengePvpReject:
                await CancelAsync(session, opcode);
                break;
        }
    }

    public Task CancelAsync(UserSession session)
    {
        var opcode = session.Trade.IsRequestingChallenge ? ChallengePvpCancel : ChallengePvpReject;
        return CancelAsync(session, opcode);
    }

    private async Task RequestAsync(UserSession session, Packet packet)
    {
        if (session.Trade.IsRequestingChallenge || session.Trade.IsChallengeRequested
            || session.IsInParty || session.Trade.IsTrading || session.Trade.IsMerchanting
            || session.ZoneId == (byte)ZoneId.Arena)
        {
            await SendErrorAsync(session);
            return;
        }

        if (ChallengeBlockedIn(session.ZoneId))
        {
            await SendErrorAsync(session, ChallengeZoneError);
            return;
        }

        var targetName = packet.ReadSByteString();
        var target = sessionManager.GetByName(targetName);
        if (target == null
            || target == session
            || target.Hp <= 0
            || target.ZoneId != session.ZoneId
            || target.Trade.IsRequestingChallenge
            || target.Trade.IsChallengeRequested
            || target.IsInParty
            || target.Trade.IsTrading
            || target.Trade.IsMerchanting)
        {
            await SendErrorAsync(session);
            return;
        }

        session.Trade.IsRequestingChallenge = true;
        session.Trade.ChallengeUser = target.CharacterId;
        target.Trade.IsChallengeRequested = true;
        target.Trade.ChallengeUser = session.CharacterId;
        logger.LogInformation("Duel requested by {Name} to {TargetName}", session.Name, target.Name);

        var requestPacket = ChallengePacketWriter.Named(ChallengePvpRequest, session.Name);
        await target.Client.SendPacket(requestPacket);

        var sentPacket = ChallengePacketWriter.Named(ChallengePvpRequestSent, target.Name);
        await session.Client.SendPacket(sentPacket);
    }

    private async Task AcceptAsync(UserSession session)
    {
        if (!session.Trade.IsChallengeRequested)
            return;

        var challenger = sessionManager.GetByCharacterId(session.Trade.ChallengeUser);
        session.Trade.ChallengeUser = -1;
        session.Trade.IsChallengeRequested = false;

        if (challenger == null)
        {
            await SendErrorAsync(session);
            return;
        }

        challenger.Trade.ChallengeUser = -1;
        challenger.Trade.IsRequestingChallenge = false;

        if (challenger == session || challenger.Hp <= 0 || challenger.ZoneId != session.ZoneId
            || ChallengeBlockedIn(session.ZoneId) || IsBusy(session) || IsBusy(challenger))
        {
            await SendErrorAsync(session);
            await SendErrorAsync(challenger);
            return;
        }

        logger.LogInformation("Duel accepted between {Name} and {ChallengerName}", session.Name, challenger.Name);

        const byte zoneArena = (byte)ZoneId.Arena;
        await zoneTransitionService.ChangeZoneAsync(session, zoneArena, 135.0f, 115.0f);
        await zoneTransitionService.ChangeZoneAsync(challenger, zoneArena, 120.0f, 115.0f);
    }

    private async Task CancelAsync(UserSession session, byte opcode)
    {
        var isCancel = opcode == ChallengePvpCancel;
        if (isCancel && !session.Trade.IsRequestingChallenge)
            return;

        if (!isCancel && !session.Trade.IsChallengeRequested)
            return;

        var partner = sessionManager.GetByCharacterId(session.Trade.ChallengeUser);

        session.Trade.ChallengeUser = -1;
        session.Trade.IsRequestingChallenge = false;
        session.Trade.IsChallengeRequested = false;

        if (partner == null || partner.Trade.ChallengeUser != session.CharacterId)
            return;

        partner.Trade.ChallengeUser = -1;
        partner.Trade.IsRequestingChallenge = false;
        partner.Trade.IsChallengeRequested = false;

        await partner.Client.SendPacket(ChallengePacketWriter.Notice(opcode));
    }

    public static bool ChallengeBlockedIn(byte zoneId) =>
        BattleZoneManager.IsPvpZone(zoneId) || BattleZoneManager.IsBattleZone(zoneId)
        || zoneId is (byte)ZoneId.Delos or (byte)ZoneId.Prison;

    private static bool IsBusy(UserSession session) =>
        session.IsInParty || session.Trade.IsTrading || session.Trade.IsMerchanting;

    private static async Task SendErrorAsync(UserSession session, byte error = ChallengeGenericError)
    {
        await session.Client.SendPacket(ChallengePacketWriter.Notice(error));
    }
}
