namespace LibreKO.Network.Readers;

internal sealed class NationTransferReader : IPacketReader
{
    private readonly NationTransferSession nationTransfer;
    private readonly SelfSession self;

    public NationTransferReader(NationTransferSession nationTransfer, SelfSession self)
    {
        this.nationTransfer = nationTransfer;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_NATION_TRANSFER, HandleNationTransfer);
    }

    private void HandleNationTransfer(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        var sub = (NationTransferSubOpcode)p.ReadByte();
        var result = (NationTransferResult)p.ReadByte();
        switch (sub)
        {
            case NationTransferSubOpcode.OpenBox when result == NationTransferResult.Accepted:
                if (nationTransfer.Pending) return;
                var candidates = NationTransferWire.ReadCandidates(p);
                if (candidates == null) return;
                nationTransfer.ApplyCandidates(candidates);
                break;
            case NationTransferSubOpcode.Submit when NationTransferWire.IsSubmitSuccess(result):
                if (!nationTransfer.Pending) return;
                self.ApplyNation(nationTransfer.Target);
                nationTransfer.Clear();
                nationTransfer.RaiseNationTransferDone();
                break;
            case NationTransferSubOpcode.WarStatus or NationTransferSubOpcode.ErrorBox when result == NationTransferResult.WarRunning && p.RemainingBytes >= 2:
                nationTransfer.ApplyWar(p.ReadByte(), p.ReadByte());
                break;
            case NationTransferSubOpcode.WarStatus or NationTransferSubOpcode.OpenBox or NationTransferSubOpcode.Submit or NationTransferSubOpcode.ErrorBox:
                if (!NationTransferWire.IsRefusal(sub, (byte)result)) return;
                if (sub == NationTransferSubOpcode.Submit && !nationTransfer.Pending) return;
                nationTransfer.ApplyRefused(NationTransferWire.RefusalText(sub, (byte)result));
                break;
        }
    }
}
