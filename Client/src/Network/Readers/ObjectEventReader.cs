namespace LibreKO.Network.Readers;

internal sealed class ObjectEventReader : IPacketReader
{
    private readonly ObjectEventSession objectEvent;

    private const int EffectBytes = 8;
    private const int TurnBytes = 4 + 2;

    public ObjectEventReader(ObjectEventSession objectEvent)
    {
        this.objectEvent = objectEvent;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_OBJECT_EVENT, HandleObjectEvent);
    }

    private void HandleObjectEvent(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        var type = (ObjectEventType)p.ReadByte();
        byte result = p.ReadByte();
        bool success = result != 0;
        if (type == ObjectEventType.PoisonGas)
        {
            if (ObjectEventWire.ReadShapeState(p) is { } gas) objectEvent.RaiseObjectEventGateState(type, gas.Id, gas.On);
            return;
        }
        if (type == ObjectEventType.Effect)
        {
            if (p.RemainingBytes >= EffectBytes) objectEvent.RaiseObjectEffect(p.ReadInt(), p.ReadInt());
            return;
        }
        if (type == ObjectEventType.Turn)
        {
            if (p.RemainingBytes >= TurnBytes) objectEvent.RaiseObjectTurn(p.ReadInt(), result, p.ReadShort());
            return;
        }
        if (success && ObjectEventWire.IsGateObject(type) && ObjectEventWire.ReadShapeState(p) is { } gate)
        {
            objectEvent.RaiseObjectEventGateState(type, gate.Id, gate.On);
            return;
        }
        int objectId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        objectEvent.RaiseObjectEventResult(type, success, objectId);
    }
}
