namespace LibreKO.Session;

public sealed class DisguiseSession
{
    public event Action<int>? TransformationListEvent;

    internal void RaiseTransformationList(int value) => TransformationListEvent?.Invoke(value);

    public event Action<int>? TransformationRefusedEvent;

    internal void RaiseTransformationRefused(int value) => TransformationRefusedEvent?.Invoke(value);
}
