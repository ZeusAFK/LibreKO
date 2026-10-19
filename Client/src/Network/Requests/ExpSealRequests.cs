namespace LibreKO.Network.Requests;

public interface IExpSealRequests
{
    void Toggle(bool on);
    void Store(byte bagPos, long amount);
    void Use(int jar, byte bagPos);
}

public sealed class ExpSealRequests : IExpSealRequests
{
    private readonly IPacketSink sink;

    public ExpSealRequests(IPacketSink sink) => this.sink = sink;

    public void Toggle(bool on) => SendExpSeal(on ? ExpSealSubOpcode.On : ExpSealSubOpcode.Off, null);

    public void Store(byte bagPos, long amount) => SendExpSeal(ExpSealSubOpcode.Store, p =>
        {
            p.WriteInt(ExpSeal.EmptyJar);
            p.WriteByte(bagPos);
            p.WriteLong(amount);
        });

    public void Use(int jar, byte bagPos) => SendExpSeal(ExpSealSubOpcode.UseJar, p =>
        {
            p.WriteInt(jar);
            p.WriteByte(bagPos);
        });

    private void SendExpSeal(ExpSealSubOpcode sub, Action<Packet>? body)
    {
        var p = new Packet(GameOpcodes.GS_EXP_SEAL);
        p.WriteByte((byte)sub);
        body?.Invoke(p);
        sink.Send(p);
    }
}
