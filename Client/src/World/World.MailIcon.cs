using Godot;

namespace LibreKO;

public partial class World
{
    private const float MailIconSize = TrophyIconSize;
    private const float MailIconGap = TrophyGap;
    private const float MailIconPadX = TrophyPadX;
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

        _mailIconButton = new Button { FocusMode = Control.FocusModeEnum.None, TooltipText = "Mail" };
        var flat = new StyleBoxEmpty();
        _mailIconButton.AddThemeStyleboxOverride("normal", flat);
        _mailIconButton.AddThemeStyleboxOverride("hover", flat);
        _mailIconButton.AddThemeStyleboxOverride("pressed", flat);
        _mailIconButton.Pressed += ToggleMail;
        _mailIconLayer.AddChild(_mailIconButton);

        _mailIconImage = UiIcons.Image("system/envelope", new Vector2(MailIconSize, MailIconSize), TrophyIconColor(false));
        _mailIconImage.MouseFilter = Control.MouseFilterEnum.Ignore;
        _mailIconImage.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _mailIconButton.AddChild(_mailIconImage);

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

        if (_attendanceGift != null) _attendanceGift.Resized += PlaceMailIcon;
        if (_trophy != null) _trophy.Resized += PlaceMailIcon;
        if (_premiumChip != null) _premiumChip.Resized += PlaceMailIcon;
        _mailIconButton.Resized += PlaceMailIcon;
        Callable.From(PlaceMailIcon).CallDeferred();
    }

    private void PlaceMailIcon()
    {
        if (Platform.TouchUi)
        {
            float side = HudPlacement.LauncherButtonSize;
            _mailIconButton.CustomMinimumSize = new Vector2(side, side);
            float inset = side * HudPlacement.LauncherGlyphInset;
            _mailIconImage.OffsetLeft = _mailIconImage.OffsetTop = inset;
            _mailIconImage.OffsetRight = _mailIconImage.OffsetBottom = -inset;
            HudPlacement.MailIcon.ApplyTo(_mailIconButton);
            return;
        }
        if (_premiumChip == null) return;

        float giftWidth = _attendanceGift?.Size.X ?? 0f;
        float trophyWidth = _trophy?.Size.X ?? 0f;
        _mailIconButton.CustomMinimumSize = new Vector2(
            MailIconSize + MailIconPadX * 2f,
            Mathf.Max(_premiumChip.Size.Y, MailIconSize));
        HudAnchor.Pin(_mailIconButton, HudAnchor.Spot.TopRight, new Vector2(
            HudAnchor.Edge + MiniMap.SquareSize + StatusHudGap
                + _premiumChip.Size.X + MailIconGap + giftWidth + MailIconGap + trophyWidth + MailIconGap,
            HudAnchor.Edge));
    }

    private void RefreshMailIcon(int unread)
    {
        if (_mailIconButton == null || !IsInstanceValid(_mailIconButton)) return;
        bool waiting = unread > 0;
        _mailIconBadge.Visible = waiting;
        _mailIconCount.Text = unread > MailIconMaxBadge ? $"{MailIconMaxBadge}+" : unread.ToString();
        _mailIconImage.SelfModulate = TrophyIconColor(waiting);
        _mailIconButton.TooltipText = waiting ? $"Mail — {unread} unread" : "Mail";
    }
}
