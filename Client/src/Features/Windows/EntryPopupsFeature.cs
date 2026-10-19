using Godot;

namespace LibreKO.Features.Windows;

internal sealed class EntryPopupsFeature : IFeature
{
    private readonly IWorldContext context;

    public EntryPopupsFeature(IWorldContext context)
    {
        this.context = context;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    private readonly EntryPopupQueue entryPopups = new();
    private readonly Dictionary<EntryPopup, Func<Control?>> openers = new();
    private Control? popupShown;

    internal void QueuePopup(EntryPopup popup, Func<Control?> open)
    {
        openers[popup] = open;
        entryPopups.Add(popup);
        ShowNextPopup();
    }

    internal void Release()
    {
        entryPopups.Release();
        ShowNextPopup();
    }

    private void ShowNextPopup()
    {
        while (entryPopups.TakeNext() is { } popup)
        {
            if (openers.Remove(popup, out var open) && open() is { Visible: true } shown && GodotObject.IsInstanceValid(shown))
            {
                popupShown = shown;
                shown.VisibilityChanged += OnPopupVisibility;
                shown.TreeExiting += OnPopupGone;
                return;
            }
            entryPopups.Closed();
        }
    }

    private void OnPopupVisibility()
    {
        if (popupShown is { Visible: true }) return;
        OnPopupGone();
    }

    private void OnPopupGone()
    {
        if (popupShown != null && GodotObject.IsInstanceValid(popupShown))
        {
            popupShown.VisibilityChanged -= OnPopupVisibility;
            popupShown.TreeExiting -= OnPopupGone;
        }
        popupShown = null;
        entryPopups.Closed();
        if (context.Root.IsInsideTree()) Callable.From(ShowNextPopup).CallDeferred();
    }
}
