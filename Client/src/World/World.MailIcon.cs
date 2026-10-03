using Godot;

namespace LibreKO;

public partial class World
{
    private const int MailIconMaxBadge = 9;

    private CanvasLayer _mailIconLayer = null!;
    private Button _mailIconButton = null!;
    private TextureRect _mailIconImage = null!;
    private PanelContainer _mailIconBadge = null!;
    private Label _mailIconCount = null!;

    private void MailIconInit()
    {
        BuildMailIcon();
        Net.I.MailUnreadEvent += RefreshMailIcon;
        RefreshMailIcon(Net.I.MailUnread);
    }

    private void MailIconDispose()
    {
        Net.I.MailUnreadEvent -= RefreshMailIcon;
        if (IsInstanceValid(_mailIconLayer)) _mailIconLayer.QueueFree();
    }

    private void BuildMailIcon()
    {
        _mailIconLayer = new CanvasLayer { Layer = 66 };
        AddChild(_mailIconLayer);

        _mailIconButton = TopIconButton(_mailIconLayer, "system/envelope", Localization.Loc.Tr("Mail"), ToggleMail, out _mailIconImage);

        var badge = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        badge.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        badge.AddChild(new BadgeDisc { MouseFilter = Control.MouseFilterEnum.Ignore });
        badge.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        badge.OffsetLeft = -BadgeSizeForTrophy;
        badge.OffsetTop = -BadgeSizeForTrophy;
        badge.OffsetRight = 2;
        badge.OffsetBottom = 2;
        _mailIconButton.AddChild(badge);

        _mailIconCount = UiTheme.Text("", Platform.TouchUi ? TrophyTouchCountFont : TrophyCountFont, UiTheme.Self, HorizontalAlignment.Center);
        _mailIconCount.VerticalAlignment = VerticalAlignment.Center;
        _mailIconCount.MouseFilter = Control.MouseFilterEnum.Ignore;
        badge.AddChild(_mailIconCount);
        _mailIconBadge = badge;
        _mailIconBadge.Visible = false;

        _mailIconButton.Resized += PlaceMailIcon;
        Callable.From(PlaceMailIcon).CallDeferred();
    }

    private void PlaceMailIcon() => PlaceTopIcon(_mailIconButton, _mailIconImage, HudPlacement.MailIcon);

    private void RefreshMailIcon(int unread)
    {
        if (_mailIconButton == null || !IsInstanceValid(_mailIconButton)) return;
        bool waiting = unread > 0;
        _mailIconBadge.Visible = waiting;
        _mailIconCount.Text = unread > MailIconMaxBadge ? $"{MailIconMaxBadge}+" : unread.ToString();
        _mailIconImage.SelfModulate = TopIconColor(waiting);
        _mailIconButton.TooltipText = waiting ? $"{Localization.Loc.Tr("Mail")} — {unread} {Localization.Loc.Tr("unread")}" : Localization.Loc.Tr("Mail");
    }
}
