namespace LibreKO.Network.Requests;

public interface IGenieRequests
{
    void Status();
    void Claim();
    void Hammer(byte threshold);
    void System(GenieSystemCommand command, byte[]? options = null);
}

public sealed class GenieRequests : IGenieRequests
{
    private readonly IPacketSink sink;

    public GenieRequests(IPacketSink sink) => this.sink = sink;

    public void Status()
    {
        var p = new Packet(GameOpcodes.GS_GENIE);
        p.WriteByte((byte)GenieSubOpcode.Status);
        sink.Send(p);
    }

    public void Claim()
    {
        var p = new Packet(GameOpcodes.GS_GENIE);
        p.WriteByte((byte)GenieSubOpcode.Claim);
        sink.Send(p);
    }

    public void Hammer(byte threshold)
    {
        var p = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        p.WriteByte((byte)GenieSystemSubOpcode.InfoRequest);
        p.WriteByte((byte)GenieSystemCommand.UseHammer);
        p.WriteByte(threshold);
        sink.Send(p);
    }

    public void System(GenieSystemCommand command, byte[]? options = null)
    {
        var p = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        p.WriteByte((byte)GenieSystemSubOpcode.InfoRequest);
        p.WriteByte((byte)command);
        if (command == GenieSystemCommand.SaveOptions)
            for (int i = 0; i < GenieSystemWire.OptionBytes; i++) p.WriteByte(options != null && i < options.Length ? options[i] : (byte)0);
        sink.Send(p);
    }

    internal static Packet ActionPacket(GameOpcodes opcode, GenieSystemAction action, bool genieRunning)
    {
        if (!genieRunning) return new Packet(opcode);
        var p = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        p.WriteByte((byte)GenieSystemSubOpcode.UpdateRequest);
        p.WriteByte((byte)action);
        return p;
    }
}
