namespace LibreKO.Network.Readers;

internal sealed class ChallengeReader : IPacketReader
{
    private readonly ChallengeSession challenge;

    public ChallengeReader(ChallengeSession challenge)
    {
        this.challenge = challenge;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_CHALLENGE, HandleChallenge);
    }

    private void HandleChallenge(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (ChallengeSubOpcode)p.ReadByte();
        switch (sub)
        {
            case ChallengeSubOpcode.PvpRequest:
                challenge.RaiseChallengeRequest(p.RemainingBytes >= 1 ? p.ReadSByteString() : "Someone");
                break;
            case ChallengeSubOpcode.PvpRequestSent:
                challenge.RaiseChallengeSent(p.RemainingBytes >= 1 ? p.ReadSByteString() : "Player");
                break;
            case ChallengeSubOpcode.PvpCancel:
                challenge.RaiseChallengeCancelled();
                break;
            case ChallengeSubOpcode.PvpReject:
                challenge.RaiseChallengeRejected();
                break;
            case ChallengeSubOpcode.GenericError:
            case ChallengeSubOpcode.ZoneError:
            case ChallengeSubOpcode.ClanError:
                challenge.RaiseChallengeError(sub);
                break;
        }
    }
}
