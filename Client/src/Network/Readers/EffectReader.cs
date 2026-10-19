namespace LibreKO.Network.Readers;

internal sealed class EffectReader : IPacketReader
{
    private readonly EffectSession effect;

    public EffectReader(EffectSession effect)
    {
        this.effect = effect;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_EFFECT, HandleEffect);
    }

    private void HandleEffect(Packet p)
    {
        if (EffectReply.Read(p) is { } reply) effect.RaiseEffect(reply);
    }
}
