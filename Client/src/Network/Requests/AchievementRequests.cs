namespace LibreKO.Network.Requests;

public interface IAchievementRequests
{
    void List();
    void Summary();
    void TitleSelect(int titleId);
    void TitleReset();
    void Claim(int achievementId);
}

public sealed class AchievementRequests : IAchievementRequests
{
    private readonly IPacketSink sink;

    public AchievementRequests(IPacketSink sink) => this.sink = sink;

    public void List()
    {
        var p = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        p.WriteByte((byte)AchievementSubOpcode.List);
        sink.Send(p);
    }

    public void Summary()
    {
        var p = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        p.WriteByte((byte)AchievementSubOpcode.Summary);
        sink.Send(p);
    }

    public void TitleSelect(int titleId)
    {
        var p = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        p.WriteByte((byte)AchievementSubOpcode.SelectDisplayTitle);
        p.WriteUShort((ushort)titleId);
        p.WriteUShort(0);
        sink.Send(p);
    }

    public void TitleReset()
    {
        var p = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        p.WriteByte((byte)AchievementSubOpcode.ResetDisplayTitle);
        sink.Send(p);
    }

    public void Claim(int achievementId)
    {
        var p = new Packet(GameOpcodes.GS_ACHIEVEMENT);
        p.WriteByte((byte)AchievementSubOpcode.ClaimReward);
        p.WriteShort((short)achievementId);
        sink.Send(p);
    }
}
