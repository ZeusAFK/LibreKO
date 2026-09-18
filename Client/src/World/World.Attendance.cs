using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int TextAttendanceDays = 33601;
    private const int TextAttendanceCumulative = 33602;
    private const int TextAttendanceMore = 33603;
    private const int TextAttendanceObtainable = 33604;
    private const int TextAttendanceAcquired = 33605;
    private const int TextAttendanceCount = 33606;
    private const int TextAttendanceOpenFailed = 33607;
    private const int TextAttendanceClaimFailed = 33621;
    private const int TextAttendanceNoNoah = 33622;
    private const int TextAttendanceNoNoahItem = 33623;
    private const int TextAttendanceClaimFailedCode = 33624;

    private const float AttendanceGiftIconSize = 20f;
    private const float AttendanceGiftBadgeSize = 13f;
    private const float AttendanceGiftTouchBadgeSize = 30f;
    private const int AttendanceGiftTouchCountFont = 17;
    private const int AttendanceGiftCountFont = 9;

    private static float GiftBadgeSize =>
        Platform.TouchUi ? AttendanceGiftTouchBadgeSize : AttendanceGiftBadgeSize;

    private static Color GiftIconColor(bool waiting) => Platform.TouchUi
        ? new Color(1f, 1f, 1f, waiting ? 0.96f : 0.55f)
        : waiting ? UiTheme.GoldBright : UiTheme.TextDim;
    private const float AttendanceGiftGap = 6f;
    private const float AttendanceGiftPadX = 9f;
    private const float AttendanceGiftBlinkDim = 0.3f;
    private const float AttendanceGiftBlinkStep = 0.6f;

    private const int AttendanceDailyColumns = 5;
    private const int AttendanceBonusColumns = 3;
    private const int AttendanceGridSeparation = 5;
    private const int AttendanceBodyWidth = 324;
    private const float AttendanceSlotSize = 50f;
    private const float AttendanceBonusSlotSize = 60f;
    private const float AttendanceCaptionHeight = 22f;
    private static readonly Color AttendanceClaimedColor = new(0.42f, 0.78f, 0.45f);
    private static readonly Color AttendanceLockedIcon = new(0.6f, 0.6f, 0.6f, 0.55f);
    private const byte AttendanceStateLocked = 5;

    private CanvasLayer _attendanceLayer = null!;
    private HudWindow _attendancePanel = null!;
    private GridContainer _attendanceGrid = null!;
    private GridContainer _attendanceBonusRow = null!;
    private Label _attendanceCountLabel = null!;
    private Label _attendanceNotice = null!;
    private CanvasLayer _attendanceGiftLayer = null!;
    private Button _attendanceGift = null!;
    private TextureRect _attendanceGiftIcon = null!;
    private PanelContainer _attendanceGiftBadge = null!;
    private Label _attendanceGiftCount = null!;
    private Tween? _attendanceGiftBlink;
    private bool _attendanceShown;

    private readonly int[] _attendanceSlots =
        new int[Net.AttendanceDailySlots + Net.AttendanceBonusSlots];
    private readonly byte[] _attendanceStates =
        new byte[Net.AttendanceDailySlots + Net.AttendanceBonusSlots];

    private void AttendanceInit()
    {
        BuildAttendanceGift();
        BuildAttendancePanel();
        Net.I.AttendanceBoardEvent += OnAttendanceBoard;
        Net.I.AttendanceFailedEvent += OnAttendanceFailed;
        Net.I.SendAttendanceBoardRequest();
    }

    private void BuildAttendanceGift()
    {
        _attendanceGiftLayer = new CanvasLayer { Layer = 66 };
        AddChild(_attendanceGiftLayer);

        _attendanceGift = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Daily attendance",
        };
        var flat = new StyleBoxEmpty();
        _attendanceGift.AddThemeStyleboxOverride("normal", flat);
        _attendanceGift.AddThemeStyleboxOverride("hover", flat);
        _attendanceGift.AddThemeStyleboxOverride("pressed", flat);
        _attendanceGift.Pressed += OpenAttendance;
        _attendanceGiftLayer.AddChild(_attendanceGift);

        _attendanceGiftIcon = UiIcons.Image("system/gift",
            new Vector2(AttendanceGiftIconSize, AttendanceGiftIconSize), GiftIconColor(false));
        _attendanceGiftIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
        _attendanceGiftIcon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _attendanceGift.AddChild(_attendanceGiftIcon);

        var badge = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        badge.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        badge.AddChild(new BadgeDisc { MouseFilter = Control.MouseFilterEnum.Ignore });
        badge.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        badge.OffsetLeft = -GiftBadgeSize;
        badge.OffsetTop = -GiftBadgeSize;
        badge.OffsetRight = 2;
        badge.OffsetBottom = 2;
        _attendanceGift.AddChild(badge);

        _attendanceGiftCount = UiTheme.Text("",
            Platform.TouchUi ? AttendanceGiftTouchCountFont : AttendanceGiftCountFont,
            UiTheme.Self, HorizontalAlignment.Center);
        _attendanceGiftCount.VerticalAlignment = VerticalAlignment.Center;
        _attendanceGiftCount.MouseFilter = Control.MouseFilterEnum.Ignore;
        badge.AddChild(_attendanceGiftCount);
        _attendanceGiftBadge = badge;

        if (_premiumChip != null) _premiumChip.Resized += PlaceAttendanceGift;
        _attendanceGift.Resized += PlaceAttendanceGift;
        Callable.From(PlaceAttendanceGift).CallDeferred();
    }

    private void PlaceAttendanceGift()
    {
        if (Platform.TouchUi)
        {
            float side = HudPlacement.LauncherButtonSize;
            _attendanceGift.CustomMinimumSize = new Vector2(side, side);
            float inset = side * HudPlacement.LauncherGlyphInset;
            _attendanceGiftIcon.OffsetLeft = _attendanceGiftIcon.OffsetTop = inset;
            _attendanceGiftIcon.OffsetRight = _attendanceGiftIcon.OffsetBottom = -inset;
            HudPlacement.AttendanceGift.ApplyTo(_attendanceGift);
            return;
        }
        if (_premiumChip == null) return;
        _attendanceGift.CustomMinimumSize = new Vector2(
            AttendanceGiftIconSize + AttendanceGiftPadX * 2f,
            Mathf.Max(_premiumChip.Size.Y, AttendanceGiftIconSize));
        HudAnchor.Pin(_attendanceGift, HudAnchor.Spot.TopRight, new Vector2(
            HudAnchor.Edge + MiniMap.SquareSize + StatusHudGap
                + _premiumChip.Size.X + AttendanceGiftGap,
            HudAnchor.Edge));
    }

    private void SetAttendanceGift(int claimable)
    {
        if (_attendanceGift == null) return;

        bool waiting = claimable > 0;
        _attendanceGiftBadge.Visible = waiting;
        _attendanceGiftCount.Text = claimable > 9 ? "9+" : claimable.ToString();
        _attendanceGiftIcon.SelfModulate = GiftIconColor(waiting);
        _attendanceGift.TooltipText = waiting
            ? "Daily attendance — a reward is waiting"
            : "Daily attendance";

        if (_attendanceGiftBlink != null && _attendanceGiftBlink.IsValid())
            _attendanceGiftBlink.Kill();
        _attendanceGiftBlink = null;
        _attendanceGift.Modulate = Colors.White;
        if (!waiting) return;

        var blink = _attendanceGift.CreateTween().SetLoops();
        blink.TweenProperty(_attendanceGift, "modulate:a",
            AttendanceGiftBlinkDim, AttendanceGiftBlinkStep);
        blink.TweenProperty(_attendanceGift, "modulate:a", 1f, AttendanceGiftBlinkStep);
        _attendanceGiftBlink = blink;
    }

    private void OpenAttendance()
    {
        if (_attendanceShown) return;
        ShowAttendancePanel();
        Net.I.SendAttendanceBoardRequest();
    }

    private void ShowAttendancePanel()
    {
        _attendancePanel.Visible = true;
        _attendanceShown = true;
        CenterAttendancePanel();
        Callable.From(CenterAttendancePanel).CallDeferred();
    }

    private void CenterAttendancePanel()
    {
        if (!_attendanceShown || !_attendancePanel.IsInsideTree()) return;
        Vector2 viewport = _attendancePanel.GetViewportRect().Size;
        Vector2 size = _attendancePanel.Size;
        _attendancePanel.Position = new Vector2(
            Mathf.Max(0f, (viewport.X - size.X) * 0.5f),
            Mathf.Max(0f, (viewport.Y - size.Y) * 0.5f));
    }

    private void BuildAttendancePanel()
    {
        _attendanceLayer = new CanvasLayer { Layer = 74 };
        AddChild(_attendanceLayer);
        _attendancePanel = new HudWindow("attendance", "Attendance", new Vector2(200, 110),
            AttendanceBodyWidth, persistLayout: false) { Visible = false };
        _attendancePanel.Closed += CloseAttendance;
        _attendancePanel.Resized += CenterAttendancePanel;
        _attendanceLayer.AddChild(_attendancePanel);

        var root = _attendancePanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddChild(UiTheme.SectionTitle("Daily Rewards"));
        _attendanceCountLabel = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Right);
        _attendanceCountLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(_attendanceCountLabel);
        root.AddChild(header);

        _attendanceNotice = UiTheme.Text("", 11, UiTheme.Warning, HorizontalAlignment.Center);
        _attendanceNotice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _attendanceNotice.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _attendanceNotice.Visible = false;
        root.AddChild(_attendanceNotice);

        _attendanceGrid = AttendanceCellGrid(AttendanceDailyColumns);
        root.AddChild(AttendanceSection(_attendanceGrid));

        root.AddChild(UiTheme.SectionTitle("Cumulative Rewards"));

        _attendanceBonusRow = AttendanceCellGrid(AttendanceBonusColumns);
        root.AddChild(AttendanceSection(_attendanceBonusRow));
        root.AddChild(AttendanceLegend());

        ResetAttendanceBoard();
        RebuildAttendance();
    }

    private static PanelContainer AttendanceSection(Control content)
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        section.AddChild(content);
        return section;
    }

    private static GridContainer AttendanceCellGrid(int columns)
    {
        var grid = new GridContainer
        {
            Columns = columns,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", AttendanceGridSeparation);
        grid.AddThemeConstantOverride("v_separation", AttendanceGridSeparation);
        return grid;
    }

    private void ResetAttendanceBoard()
    {
        for (int i = 0; i < Net.AttendanceDailySlots; i++)
        {
            _attendanceSlots[i] = i + 1;
            _attendanceStates[i] = AttendanceStateLocked;
        }
        for (int i = 0; i < Net.AttendanceBonusSlots; i++)
        {
            int index = Net.AttendanceDailySlots + i;
            _attendanceSlots[index] = Net.AttendanceBonusFirstSlot + i;
            _attendanceStates[index] = AttendanceStateLocked;
        }
    }

    private void AttendanceDispose()
    {
        Net.I.AttendanceBoardEvent -= OnAttendanceBoard;
        Net.I.AttendanceFailedEvent -= OnAttendanceFailed;
    }

    private void ToggleAttendance()
    {
        if (_attendanceShown) { CloseAttendance(); return; }
        OpenAttendance();
    }

    private void CloseAttendance()
    {
        if (!_attendanceShown) return;
        _attendanceShown = false;
        _attendancePanel.Visible = false;
    }

    private void OnAttendanceBoard(int[] slots, byte[] states)
    {
        _attendanceNotice.Visible = false;
        ResetAttendanceBoard();
        int count = Mathf.Min(slots.Length, _attendanceSlots.Length);
        for (int i = 0; i < count; i++)
        {
            if (slots[i] == 0) continue;
            _attendanceSlots[i] = slots[i];
            _attendanceStates[i] = states[i];
        }
        RebuildAttendance();

        if (!Net.I.AttendanceAutoOpened)
        {
            Net.I.AttendanceAutoOpened = true;
            ShowAttendancePanel();
        }
    }

    private void OnAttendanceFailed(byte sub, uint result)
    {
        string text = AttendanceFailureText(sub, result);
        CombatNotice(text);
        if (sub != Net.EventBoardAttendanceClaim) return;
        _attendanceNotice.Text = text;
        _attendanceNotice.Visible = true;
    }

    private static string AttendanceFailureText(byte sub, uint result)
    {
        if (sub == Net.EventBoardAttendanceList)
            return ItemData.Text(TextAttendanceOpenFailed, "Failed to open the attendance board. (%d)")
                .Replace("%d", result.ToString());

        return result switch
        {
            0 or 200 => ItemData.Text(TextAttendanceClaimFailed, "Failed to obtain the item."),
            Net.AttendanceClaimInventoryFull => "Your inventory is full. Free a slot and claim again.",
            Net.AttendanceClaimTooHeavy => "You are carrying too much to take this reward.",
            2 => ItemData.Text(TextAttendanceNoNoah, "You don't have enough noah."),
            20 => ItemData.Text(TextAttendanceNoNoahItem,
                "Failed to obtain the item due to not enough Noah."),
            _ => ItemData.Text(TextAttendanceClaimFailedCode, "Failed to obtain the item. (%d)")
                .Replace("%d", result.ToString()),
        };
    }

    private void RebuildAttendance()
    {
        foreach (var child in _attendanceGrid.GetChildren()) child.QueueFree();
        foreach (var child in _attendanceBonusRow.GetChildren()) child.QueueFree();

        int attended = 0;
        for (int i = 0; i < Net.AttendanceDailySlots; i++)
        {
            byte state = _attendanceStates[i];
            if (state == Net.AttendanceStateClaimed || state == Net.AttendanceStateExpired)
                attended++;
            _attendanceGrid.AddChild(
                BuildAttendanceCell(_attendanceSlots[i], state, AttendanceSlotSize));
        }

        SetAttendanceGift(ClaimableAttendanceCount());

        for (int i = 0; i < Net.AttendanceBonusSlots; i++)
        {
            int index = Net.AttendanceDailySlots + i;
            _attendanceBonusRow.AddChild(BuildAttendanceCell(
                _attendanceSlots[index], _attendanceStates[index], AttendanceBonusSlotSize));
        }

        _attendanceCountLabel.Text = ItemData.Text(TextAttendanceCount, "Currently attended %d times")
            .Replace("%d", attended.ToString());
    }

    private int ClaimableAttendanceCount()
    {
        int count = 0;
        foreach (byte state in _attendanceStates)
        {
            if (state is >= Net.AttendanceStateClaimable and <= Net.AttendanceStateClaimableExtra)
                count++;
        }
        return count;
    }

    private Control BuildAttendanceCell(int slot, byte state, float size)
    {
        bool claimed = state == Net.AttendanceStateClaimed || state == Net.AttendanceStateExpired;
        bool claimable = state is >= Net.AttendanceStateClaimable and <= Net.AttendanceStateClaimableExtra;
        var (itemId, itemCount) = ItemData.AttendanceReward(slot);

        var cell = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cell.AddThemeConstantOverride("separation", 2);

        var socket = new Button
        {
            CustomMinimumSize = new Vector2(size, size),
            FocusMode = Control.FocusModeEnum.None,
        };
        var frame = UiTheme.Slot(claimable ? UiTheme.Gold : null, locked: !claimable && !claimed);
        var lit = claimable ? UiTheme.Slot(UiTheme.Gold, hover: true) : frame;
        socket.AddThemeStyleboxOverride("normal", frame);
        socket.AddThemeStyleboxOverride("disabled", frame);
        socket.AddThemeStyleboxOverride("hover", lit);
        socket.AddThemeStyleboxOverride("pressed", lit);
        if (claimable)
        {
            int claimSlot = slot;
            byte claimState = state;
            socket.Pressed += () => Net.I.SendAttendanceClaim(claimSlot, claimState);
        }
        if (itemId > 0)
        {
            var hovered = new ItemSlot
            {
                ItemId = itemId,
                Count = (short)itemCount,
                Durability = (short)(ItemData.Get(itemId)?.Duration ?? 0),
            };
            socket.MouseEntered += () => ShowItemTooltip(-1, hovered);
            socket.MouseExited += HideItemTooltip;
        }
        var centered = new CenterContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        centered.AddChild(socket);
        cell.AddChild(centered);

        if (itemId > 0)
        {
            var icon = new TextureRect
            {
                Texture = ItemData.Icon(itemId),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = claimed ? new Color(1, 1, 1, 0.35f) : claimable ? Colors.White : AttendanceLockedIcon,
            };
            icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            socket.AddChild(icon);

            if (itemCount > 1)
            {
                var countLabel = UiTheme.Text(itemCount.ToString(), 11, UiTheme.TextHi,
                    HorizontalAlignment.Right);
                OutlineAttendanceLabel(countLabel);
                countLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                countLabel.VerticalAlignment = VerticalAlignment.Bottom;
                countLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
                countLabel.OffsetRight = -3;
                countLabel.OffsetBottom = -2;
                socket.AddChild(countLabel);
            }
        }

        if (claimed)
            socket.AddChild(AttendanceGlyph.Make(AttendanceGlyph.Kind.Check, AttendanceClaimedColor));
        else if (!claimable)
            socket.AddChild(AttendanceGlyph.Make(AttendanceGlyph.Kind.Lock, UiTheme.TextLo, 18f));
        else
            socket.Ready += () => PulseAttendanceSlot(socket);

        if (state != AttendanceStateLocked)
        {
            int shown = slot >= Net.AttendanceBonusFirstSlot
                ? Net.AttendanceBonusThreshold(slot)
                : slot;
            var badge = UiTheme.Text(shown.ToString(), 10,
                claimable ? UiTheme.GoldBright : UiTheme.TextHi);
            OutlineAttendanceLabel(badge);
            badge.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            badge.MouseFilter = Control.MouseFilterEnum.Ignore;
            badge.OffsetLeft = 4;
            badge.OffsetTop = 2;
            socket.AddChild(badge);
        }

        var caption = UiTheme.Text(AttendanceSlotCaption(slot, state), 10,
            claimable ? UiTheme.GoldBright : claimed ? AttendanceClaimedColor : UiTheme.TextLo,
            HorizontalAlignment.Center);
        caption.CustomMinimumSize = new Vector2(0, AttendanceCaptionHeight);
        caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cell.AddChild(caption);

        return cell;
    }

    private static void PulseAttendanceSlot(Control socket)
    {
        var tween = socket.CreateTween().SetLoops();
        tween.TweenProperty(socket, "modulate", new Color(1f, 1f, 1f, 0.65f), 0.8)
            .SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(socket, "modulate", Colors.White, 0.8).SetTrans(Tween.TransitionType.Sine);
    }

    private static Control AttendanceLegend()
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(AttendanceLegendItem(AttendanceGlyph.Kind.Check, AttendanceClaimedColor, "Acquired"));
        row.AddChild(AttendanceLegendItem(null, UiTheme.GoldBright, "Ready to claim"));
        row.AddChild(AttendanceLegendItem(AttendanceGlyph.Kind.Lock, UiTheme.TextLo, "Not yet"));
        return row;
    }

    private static Control AttendanceLegendItem(AttendanceGlyph.Kind? kind, Color color, string text)
    {
        var item = new HBoxContainer();
        item.AddThemeConstantOverride("separation", 5);
        var sample = new Panel
        {
            CustomMinimumSize = new Vector2(16, 16),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        sample.AddThemeStyleboxOverride("panel",
            UiTheme.Slot(kind == null ? UiTheme.Gold : null, locked: kind == AttendanceGlyph.Kind.Lock));
        if (kind is { } glyph)
            sample.AddChild(AttendanceGlyph.Make(glyph, color, 11f));
        item.AddChild(sample);
        item.AddChild(UiTheme.Text(text, 10, color));
        return item;
    }

    private static void OutlineAttendanceLabel(Label label)
    {
        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
    }

    private static string AttendanceSlotCaption(int slot, byte state) => state switch
    {
        Net.AttendanceStateClaimed or Net.AttendanceStateExpired =>
            ItemData.Text(TextAttendanceAcquired, "Acquired"),
        Net.AttendanceStateClaimableExtra =>
            ItemData.Text(TextAttendanceMore, "More can be obtained"),
        Net.AttendanceStateClaimable =>
            ItemData.Text(TextAttendanceObtainable, "Obtainable"),
        _ => slot >= Net.AttendanceBonusFirstSlot
            ? ItemData.Text(TextAttendanceCumulative, "%d Cumulative Reward")
                .Replace("%d", Net.AttendanceBonusThreshold(slot).ToString())
            : ItemData.Text(TextAttendanceDays, "%d day(s)").Replace("%d", slot.ToString()),
    };
}
