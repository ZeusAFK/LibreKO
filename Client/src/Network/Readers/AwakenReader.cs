namespace LibreKO.Network.Readers;

internal sealed class AwakenReader : IPacketReader
{
    private readonly AwakenSession awaken;

    public AwakenReader(AwakenSession awaken)
    {
        this.awaken = awaken;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_AWAKEN, HandleAwaken);
    }

    private void HandleAwaken(Packet p)
    {
        if (p.RemainingBytes < 4 + 1 + 4) return;
        float scale = p.ReadFloat();
        var type = (AwakenEffectType)p.ReadByte();
        int effectId = p.ReadInt();
        if (type != AwakenEffectType.Visual) return;
        awaken.RaiseAwaken(scale, effectId);
    }
}
