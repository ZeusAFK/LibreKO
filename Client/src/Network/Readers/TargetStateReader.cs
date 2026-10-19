namespace LibreKO.Network.Readers;

internal sealed class TargetStateReader : IPacketReader
{
    private readonly TargetStateSession targetState;

    public TargetStateReader(TargetStateSession targetState)
    {
        this.targetState = targetState;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_TARGET_STATE, HandleTargetState);
    }

    private void HandleTargetState(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((TargetStateSubOpcode)p.ReadByte())
        {
            case TargetStateSubOpcode.Effects:
                if (TargetEffectsReply.Read(p) is { } effects) targetState.RaiseTargetEffects(effects);
                break;
            case TargetStateSubOpcode.DropList:
                if (DropListReply.Read(p) is { } drops) targetState.RaiseDropList(drops);
                break;
            case TargetStateSubOpcode.Control:
                if (ControlStateReply.Read(p) is { } control) targetState.RaiseControlState(control);
                break;
        }
    }
}
