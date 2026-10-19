using Godot;

namespace LibreKO.Features.Hud;

internal sealed class MailIconFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly MailSession mail;
    private readonly TopIconsFeature topIcons;

    public MailIconFeature(IWorldContext context, MailSession mail, TopIconsFeature topIcons)
    {
        this.context = context;
        this.mail = mail;
        this.topIcons = topIcons;
    }

    private CanvasLayer layer = null!;
    internal Button IconButton { get; private set; } = null!;
    private TopIconBadge badge = null!;

    internal event Action? Pressed;

    public void Build()
    {
        BuildIcon();
        mail.MailUnreadEvent += RefreshIcon;
        RefreshIcon(mail.MailUnread);
    }

    public void Dispose()
    {
        mail.MailUnreadEvent -= RefreshIcon;
        if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
    }

    internal void BuildIcon()
    {
        layer = new CanvasLayer { Layer = UiLayers.HudButtons };
        context.Root.AddChild(layer);

        IconButton = topIcons.Add(TopIconSlot.Mail, "system/envelope", "Mail", () => Pressed?.Invoke(), out var iconImage, layer);
        badge = new TopIconBadge(IconButton, iconImage, blinks: false);

        IconButton.Resized += PlaceIcon;
        Callable.From(PlaceIcon).CallDeferred();
    }

    private void PlaceIcon() => topIcons.Place(TopIconSlot.Mail, HudPlacement.MailIcon);

    internal void RefreshIcon(int unread)
    {
        if (IconButton == null || !GodotObject.IsInstanceValid(IconButton)) return;
        badge.Show(unread);
        IconButton.TooltipText = unread > 0 ? $"Mail — {unread} unread" : "Mail";
    }
}
