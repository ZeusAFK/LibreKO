using System;

namespace LibreKO.Network;

public partial class Net
{
    private const byte ChallengeSubRequest = 1;
    private const byte ChallengeSubCancel = 2;
    private const byte ChallengeSubAccept = 3;
    private const byte ChallengeSubReject = 4;
    private const byte ChallengeSubRequestSent = 5;
    private const byte ChallengeSubError = 11;

    public event Action<string>? ChallengeRequestEvent;
    public event Action<string>? ChallengeSentEvent;
    public event Action? ChallengeCancelledEvent;
    public event Action? ChallengeRejectedEvent;
    public event Action? ChallengeErrorEvent;

    private void HandleChallenge(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case ChallengeSubRequest:
                ChallengeRequestEvent?.Invoke(p.RemainingBytes >= 1 ? p.ReadSByteString() : "Someone");
                break;
            case ChallengeSubRequestSent:
                ChallengeSentEvent?.Invoke(p.RemainingBytes >= 1 ? p.ReadSByteString() : "Player");
                break;
            case ChallengeSubCancel:
                ChallengeCancelledEvent?.Invoke();
                break;
            case ChallengeSubReject:
                ChallengeRejectedEvent?.Invoke();
                break;
            case ChallengeSubError:
                ChallengeErrorEvent?.Invoke();
                break;
        }
    }

    public void SendChallengeRequest(string targetName)
    {
        var p = new Packet(GameOpcodes.GS_CHALLENGE);
        p.WriteByte(ChallengeSubRequest);
        p.WriteSByteString(targetName);
        _conn.Send(p);
    }

    public void SendChallengeAccept()
    {
        var p = new Packet(GameOpcodes.GS_CHALLENGE);
        p.WriteByte(ChallengeSubAccept);
        _conn.Send(p);
    }

    public void SendChallengeReject()
    {
        var p = new Packet(GameOpcodes.GS_CHALLENGE);
        p.WriteByte(ChallengeSubReject);
        _conn.Send(p);
    }

    public void SendChallengeCancel()
    {
        var p = new Packet(GameOpcodes.GS_CHALLENGE);
        p.WriteByte(ChallengeSubCancel);
        _conn.Send(p);
    }
}
