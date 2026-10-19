namespace LibreKO.Session;

public sealed class ObjectEventSession
{
    public event Action<ObjectEventType, bool, int>? ObjectEventResultEvent;

    internal void RaiseObjectEventResult(ObjectEventType arg1, bool arg2, int arg3) => ObjectEventResultEvent?.Invoke(arg1, arg2, arg3);

    public event Action<ObjectEventType, int, bool>? ObjectEventGateStateEvent;

    internal void RaiseObjectEventGateState(ObjectEventType arg1, int arg2, bool arg3) => ObjectEventGateStateEvent?.Invoke(arg1, arg2, arg3);

    public event Action<int, int>? ObjectEffectEvent;

    internal void RaiseObjectEffect(int arg1, int arg2) => ObjectEffectEvent?.Invoke(arg1, arg2);

    public event Action<int, byte, short>? ObjectTurnEvent;

    internal void RaiseObjectTurn(int arg1, byte arg2, short arg3) => ObjectTurnEvent?.Invoke(arg1, arg2, arg3);
}
