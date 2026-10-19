namespace LibreKO.Network.Readers;

internal sealed class ShoutReader : IPacketReader
{
    private readonly ShoutSession shout;

    public ShoutReader(ShoutSession shout)
    {
        this.shout = shout;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_LOGOSSHOUT, HandleShout);
    }

    private void HandleShout(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        var direction = (LogosShoutSubOpcode)p.ReadByte();

        if (direction == LogosShoutSubOpcode.RegisterResult)
        {
            var result = (LogosShoutRegisterResult)p.ReadByte();
            int number = result == LogosShoutRegisterResult.Accepted && p.RemainingBytes >= 1 ? p.ReadByte() : 0;
            shout.RaiseShoutResult(result, number);
            return;
        }

        var kind = (LogosShoutBroadcastKind)p.ReadByte();

        switch (kind)
        {
            case LogosShoutBroadcastKind.Message:
                {
                    if (p.RemainingBytes < 4) return;
                    byte r = p.ReadByte();
                    byte g = p.ReadByte();
                    byte b = p.ReadByte();
                    p.ReadByte();
                    string msg = p.ReadSByteString();
                    int rank = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                    shout.RaiseShout(new ShoutReply(Message: msg, R: r, G: g, B: b, Rank: rank));
                    break;
                }
            case LogosShoutBroadcastKind.RareItem:
                {
                    string finder = p.ReadSByteString();
                    int foundItemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    if (p.RemainingBytes >= 1) p.ReadByte();
                    byte nation = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    shout.RaiseShoutRareItem(finder, foundItemId, nation);
                    break;
                }
            case LogosShoutBroadcastKind.Upgrade:
                {
                    if (p.RemainingBytes < 1) return;
                    bool ok = p.ReadByte() == 1;
                    string name = p.ReadSByteString();
                    int itemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    if (p.RemainingBytes >= 1) p.ReadByte();
                    byte upgradeNation = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    shout.RaiseShoutUpgrade(ok, name, itemId, upgradeNation);
                    break;
                }
        }
    }
}
