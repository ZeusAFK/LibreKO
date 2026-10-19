namespace LibreKO.Features;

internal interface IEscapable
{
    bool CloseOnEscape();
}

internal interface IClosableWindow : IEscapable
{
    bool IsOpen { get; }
    void Close();

    bool IEscapable.CloseOnEscape()
    {
        if (!IsOpen) return false;
        Close();
        return true;
    }
}

internal sealed class ClosablePanel(Func<bool> isOpen, Action close) : IClosableWindow
{
    public bool IsOpen => isOpen();
    public void Close() => close();
}

internal sealed class EscapeGroup(Func<IEnumerable<IEscapable>> layers) : IEscapable
{
    public bool CloseOnEscape() => EscapeOrder.CloseFirst(layers());
}

internal static class EscapeOrder
{
    public static bool CloseFirst(IEnumerable<IEscapable> layers)
    {
        foreach (var layer in layers)
            if (layer.CloseOnEscape()) return true;
        return false;
    }
}
