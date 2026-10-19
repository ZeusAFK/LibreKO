using Godot;

namespace LibreKO.Network.Requests;

public interface IPlayerRequests
{
    void Move(float x, float z, float y, float speed, MoveEcho echo = MoveEcho.Move);
    void Move(float willX, float willZ, float willY, float speed, MoveEcho echo, float curX, float curZ, float curY);
    void Rotate(float degrees);
    void Sitting(bool sitting);
    void CombatStance(bool ready);
    void GoTown();
}

public sealed class PlayerRequests : IPlayerRequests
{
    private const float MaxWireSpeed = 90f;

    private readonly IPacketSink sink;
    private readonly GenieSession genie;

    public PlayerRequests(IPacketSink sink, GenieSession genie)
    {
        this.sink = sink;
        this.genie = genie;
    }

    public void Move(float x, float z, float y, float speed, MoveEcho echo = MoveEcho.Move) =>
        Move(x, z, y, speed, echo, x, z, y);

    public void Move(float willX, float willZ, float willY, float speed, MoveEcho echo, float curX, float curZ, float curY)
    {
        var p = GenieRequests.ActionPacket(GameOpcodes.GS_MOVE, GenieSystemAction.Move, genie.GenieRunning);
        p.WriteUShort(WirePosition(willX)); p.WriteUShort(WirePosition(willZ)); p.WriteUShort(WirePosition(willY));
        p.WriteShort((short)Mathf.Clamp(speed * Coord.TenthsPerUnit, -MaxWireSpeed, MaxWireSpeed));
        p.WriteByte((byte)echo);
        p.WriteUShort(WirePosition(curX)); p.WriteUShort(WirePosition(curZ)); p.WriteUShort(WirePosition(curY));
        sink.Send(p);
    }

    public void Rotate(float degrees)
    {
        var p = GenieRequests.ActionPacket(GameOpcodes.GS_ROTATE, GenieSystemAction.Rotate, genie.GenieRunning);
        p.WriteShort(Coord.HeadingToWire(degrees));
        sink.Send(p);
    }

    public void Sitting(bool sitting)
    {
        var p = new Packet(GameOpcodes.GS_STATE_CHANGE);
        p.WriteByte(StateChange.Pose);
        p.WriteInt(sitting ? UserPose.Sitting : UserPose.Standing);
        sink.Send(p);
    }

    public void CombatStance(bool ready)
    {
        var p = new Packet(GameOpcodes.GS_STATE_CHANGE);
        p.WriteByte(StateChange.CombatStance);
        p.WriteInt(ready ? StateChange.StanceReady : StateChange.StanceRelaxed);
        sink.Send(p);
    }

    public void GoTown() => sink.Send(new Packet(GameOpcodes.GS_HOME));

    private static ushort WirePosition(float value) => (ushort)Mathf.Clamp(value * Coord.TenthsPerUnit, 0f, ushort.MaxValue);
}
