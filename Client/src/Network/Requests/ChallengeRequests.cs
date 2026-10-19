namespace LibreKO.Network.Requests;

public interface IChallengeRequests
{
    void ChallengeRequest(string targetName);
    void Accept();
    void Reject();
    void Cancel();
}

public sealed class ChallengeRequests : IChallengeRequests
{
    private readonly IPacketSink sink;

    public ChallengeRequests(IPacketSink sink) => this.sink = sink;

    public void ChallengeRequest(string targetName)
    {
        var p = new Packet(GameOpcodes.GS_CHALLENGE);
        p.WriteByte((byte)ChallengeSubOpcode.PvpRequest);
        p.WriteSByteString(targetName);
        sink.Send(p);
    }

    public void Accept()
    {
        var p = new Packet(GameOpcodes.GS_CHALLENGE);
        p.WriteByte((byte)ChallengeSubOpcode.PvpAccept);
        sink.Send(p);
    }

    public void Reject()
    {
        var p = new Packet(GameOpcodes.GS_CHALLENGE);
        p.WriteByte((byte)ChallengeSubOpcode.PvpReject);
        sink.Send(p);
    }

    public void Cancel()
    {
        var p = new Packet(GameOpcodes.GS_CHALLENGE);
        p.WriteByte((byte)ChallengeSubOpcode.PvpCancel);
        sink.Send(p);
    }
}
