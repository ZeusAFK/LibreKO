using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class AttendanceFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly AttendanceSession attendance;
    private readonly IAttendanceRequests requests;
    private readonly TopIconsFeature topIcons;

    public AttendanceFeature(IWorldContext context, AttendanceSession attendance, IAttendanceRequests requests, TopIconsFeature topIcons)
    {
        this.context = context;
        this.attendance = attendance;
        this.requests = requests;
        this.topIcons = topIcons;
    }

    private const int TextDays = 33601;
    private const int TextCumulative = 33602;
    private const int TextMore = 33603;
    private const int TextObtainable = 33604;
    private const int TextAcquired = 33605;
    private const int TextCount = 33606;
    private const int TextOpenFailed = 33607;
    private const int TextClaimFailed = 33621;
    private const int TextNoNoah = 33622;
    private const int TextNoNoahItem = 33623;
    private const int TextClaimFailedCode = 33624;
    private const int DailyColumns = 5;
    private const int BonusColumns = 3;
    private const int GridSeparation = 5;
    private const int BodyWidth = 324;
    private const float SlotSize = 50f;
    private const float BonusSlotSize = 60f;
    private const float CaptionHeight = 22f;
    private static readonly Color ClaimedColor = new(0.42f, 0.78f, 0.45f);
    private static readonly Color LockedIcon = new(0.6f, 0.6f, 0.6f, 0.55f);
    internal const byte StateLocked = 5;

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    internal GridContainer Grid { get; private set; } = null!;
    private GridContainer bonusRow = null!;
    private Label countLabel = null!;
    private Label notice = null!;
    private CanvasLayer giftLayer = null!;
    internal Button Gift { get; private set; } = null!;
    private TopIconBadge giftBadge = null!;
    private bool shown;

    public bool IsOpen => shown;

    private readonly int[] slots =
    new int[AttendanceWire.DailySlots + AttendanceWire.BonusSlots];

    private readonly byte[] states =
    new byte[AttendanceWire.DailySlots + AttendanceWire.BonusSlots];

    public void Build()
    {
        BuildGift();
        BuildPanel();
        attendance.AttendanceBoardEvent += OnBoard;
        attendance.AttendanceFailedEvent += OnFailed;
        PluginHost.Game.AttachAttendance(new PluginAttendanceBridge(this));
        requests.BoardRequest();
    }

    internal void BuildGift()
    {
        giftLayer = new CanvasLayer { Layer = UiLayers.HudButtons };
        context.Root.AddChild(giftLayer);

        Gift = topIcons.Add(TopIconSlot.AttendanceGift, "system/gift", "Daily attendance",
            Open, out var giftIcon, giftLayer);
        giftBadge = new TopIconBadge(Gift, giftIcon, blinks: true);

        Gift.Resized += PlaceGift;
        Callable.From(PlaceGift).CallDeferred();
    }

    private void PlaceGift() =>
        topIcons.Place(TopIconSlot.AttendanceGift, HudPlacement.AttendanceGift);

    internal void SetGift(int claimable)
    {
        if (Gift == null) return;

        giftBadge.Show(claimable);
        Gift.TooltipText = claimable > 0
            ? "Daily attendance — a reward is waiting"
            : "Daily attendance";
    }

    private void Open()
    {
        if (shown) return;
        ShowPanel();
        requests.BoardRequest();
    }

    internal void ShowPanel()
    {
        Show();
        PluginNotify();
        CenterPanel();
        Callable.From(CenterPanel).CallDeferred();
    }

    private void CenterPanel()
    {
        if (!shown || !Panel.IsInsideTree()) return;
        Vector2 viewport = Panel.GetViewportRect().Size;
        Vector2 size = Panel.Size;
        Panel.Position = new Vector2(
            Mathf.Max(0f, (viewport.X - size.X) * 0.5f),
            Mathf.Max(0f, (viewport.Y - size.Y) * 0.5f));
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);
        Panel = new HudWindow("attendance", "Attendance", new Vector2(200, 110),
            BodyWidth, persistLayout: false) { Visible = false };
        Panel.Closed += Close;
        Panel.Resized += CenterPanel;
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddChild(UiTheme.SectionTitle("Daily Rewards"));
        countLabel = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Right);
        countLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(countLabel);
        root.AddChild(header);

        notice = UiTheme.Text("", 11, UiTheme.Warning, HorizontalAlignment.Center);
        notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        notice.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        notice.Visible = false;
        root.AddChild(notice);

        Grid = CellGrid(DailyColumns);
        root.AddChild(Section(Grid));

        root.AddChild(UiTheme.SectionTitle("Cumulative Rewards"));

        bonusRow = CellGrid(BonusColumns);
        root.AddChild(Section(bonusRow));
        root.AddChild(Legend());

        ResetBoard();
        Rebuild();
    }

    private static PanelContainer Section(Control content)
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        section.AddChild(content);
        return section;
    }

    private static GridContainer CellGrid(int columns)
    {
        var grid = new GridContainer
        {
            Columns = columns,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", GridSeparation);
        grid.AddThemeConstantOverride("v_separation", GridSeparation);
        return grid;
    }

    private void ResetBoard()
    {
        for (int i = 0; i < AttendanceWire.DailySlots; i++)
        {
            slots[i] = i + 1;
            states[i] = StateLocked;
        }
        for (int i = 0; i < AttendanceWire.BonusSlots; i++)
        {
            int index = AttendanceWire.DailySlots + i;
            slots[index] = AttendanceWire.BonusFirstSlot + i;
            states[index] = StateLocked;
        }
    }

    public void Dispose()
    {
        attendance.AttendanceBoardEvent -= OnBoard;
        attendance.AttendanceFailedEvent -= OnFailed;
        giftBadge?.StopBlink();
    }

    internal void Toggle()
    {
        if (shown) { Close(); return; }
        Open();
    }

    internal void Show()
    {
        Panel.Visible = true;
        shown = true;
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        PluginNotify();
    }

    internal void OnBoard(int[] slots, byte[] states)
    {
        notice.Visible = false;
        ResetBoard();
        int count = Mathf.Min(slots.Length, this.slots.Length);
        for (int i = 0; i < count; i++)
        {
            if (slots[i] == 0) continue;
            this.slots[i] = slots[i];
            this.states[i] = states[i];
        }
        Rebuild();

        if (!attendance.AttendanceAutoOpened)
        {
            attendance.NoteAutoOpened();
            if (ClaimableCount() > 0) ShowPanel();
        }
    }

    internal void OnFailed(EventBoardSubOpcode sub, EventBoardResult result)
    {
        string text = FailureText(sub, result);
        context.Log.Notice(text);
        if (sub != EventBoardSubOpcode.AttendanceClaim) return;
        notice.Text = text;
        notice.Visible = true;
    }

    private static string FailureText(EventBoardSubOpcode sub, EventBoardResult result)
    {
        if (sub == EventBoardSubOpcode.AttendanceBoard)
            return ItemData.Text(TextOpenFailed, "Failed to open the attendance board. (%d)")
                .Replace("%d", ((uint)result).ToString());

        return result switch
        {
            EventBoardResult.Failed or EventBoardResult.ItemNotObtained => ItemData.Text(TextClaimFailed, "Failed to obtain the item."),
            EventBoardResult.InventoryFull => "Your inventory is full. Free a slot and claim again.",
            EventBoardResult.TooHeavy => "You are carrying too much to take this reward.",
            EventBoardResult.NotEnoughGold => ItemData.Text(TextNoNoah, "You don't have enough gold."),
            EventBoardResult.NotEnoughGoldForItem => ItemData.Text(TextNoNoahItem,
                "Failed to obtain the item due to not enough gold."),
            _ => ItemData.Text(TextClaimFailedCode, "Failed to obtain the item. (%d)")
                .Replace("%d", ((uint)result).ToString()),
        };
    }

    private void Rebuild()
    {
        foreach (var child in Grid.GetChildren()) child.QueueFree();
        foreach (var child in bonusRow.GetChildren()) child.QueueFree();

        int attended = 0;
        for (int i = 0; i < AttendanceWire.DailySlots; i++)
        {
            byte state = states[i];
            if (state == AttendanceSlotState.Claimed || state == AttendanceSlotState.Expired)
                attended++;
            Grid.AddChild(
                BuildCell(slots[i], state, SlotSize));
        }

        SetGift(ClaimableCount());

        for (int i = 0; i < AttendanceWire.BonusSlots; i++)
        {
            int index = AttendanceWire.DailySlots + i;
            bonusRow.AddChild(BuildCell(
                slots[index], states[index], BonusSlotSize));
        }

        countLabel.Text = ItemData.Text(TextCount, "Currently attended %d times")
            .Replace("%d", attended.ToString());
        PluginNotify();
    }

    private int ClaimableCount()
    {
        int count = 0;
        foreach (byte state in states)
        {
            if (state is >= AttendanceSlotState.Claimable and <= AttendanceSlotState.ClaimableExtra)
                count++;
        }
        return count;
    }

    private Control BuildCell(int slot, byte state, float size)
    {
        bool claimed = state == AttendanceSlotState.Claimed || state == AttendanceSlotState.Expired;
        bool claimable = state is >= AttendanceSlotState.Claimable and <= AttendanceSlotState.ClaimableExtra;
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
            socket.Pressed += () => requests.Claim(claimSlot, claimState);
        }
        if (itemId > 0)
        {
            var hovered = new ItemSlot
            {
                ItemId = itemId,
                Count = (short)itemCount,
                Durability = (short)(ItemData.Get(itemId)?.Duration ?? 0),
            };
            socket.MouseEntered += () => context.Tooltips.Show(-1, hovered);
            socket.MouseExited += context.Tooltips.Hide;
        }
        var centered = new CenterContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        centered.AddChild(socket);
        cell.AddChild(centered);

        if (itemId > 0)
        {
            var icon = new TextureRect
            {
                Texture = ItemIcons.Get(itemId),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = claimed ? new Color(1, 1, 1, 0.35f) : claimable ? Colors.White : LockedIcon,
            };
            icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            socket.AddChild(icon);

            if (itemCount > 1)
            {
                var countLabel = UiTheme.Text(itemCount.ToString(), 11, UiTheme.TextHi,
                    HorizontalAlignment.Right);
                OutlineLabel(countLabel);
                countLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                countLabel.VerticalAlignment = VerticalAlignment.Bottom;
                countLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
                countLabel.OffsetRight = -3;
                countLabel.OffsetBottom = -2;
                socket.AddChild(countLabel);
            }
        }

        if (claimed)
            socket.AddChild(AttendanceGlyph.Make(AttendanceGlyph.Kind.Check, ClaimedColor));
        else if (!claimable)
            socket.AddChild(AttendanceGlyph.Make(AttendanceGlyph.Kind.Lock, UiTheme.TextLo, 18f));
        else
            socket.Ready += () => PulseSlot(socket);

        if (state != StateLocked)
        {
            int shownDay = slot >= AttendanceWire.BonusFirstSlot
                ? AttendanceWire.BonusThreshold(slot)
                : slot;
            var badge = UiTheme.Text(shownDay.ToString(), 10,
                claimable ? UiTheme.GoldBright : UiTheme.TextHi);
            OutlineLabel(badge);
            badge.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            badge.MouseFilter = Control.MouseFilterEnum.Ignore;
            badge.OffsetLeft = 4;
            badge.OffsetTop = 2;
            socket.AddChild(badge);
        }

        var caption = UiTheme.Text(SlotCaption(slot, state), 10,
            claimable ? UiTheme.GoldBright : claimed ? ClaimedColor : UiTheme.TextLo,
            HorizontalAlignment.Center);
        caption.CustomMinimumSize = new Vector2(0, CaptionHeight);
        caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cell.AddChild(caption);

        return cell;
    }

    private static void PulseSlot(Control socket)
    {
        var tween = socket.CreateTween().SetLoops();
        tween.TweenProperty(socket, "modulate", new Color(1f, 1f, 1f, 0.65f), 0.8)
            .SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(socket, "modulate", Colors.White, 0.8).SetTrans(Tween.TransitionType.Sine);
    }

    private static Control Legend()
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(LegendItem(AttendanceGlyph.Kind.Check, ClaimedColor, "Acquired"));
        row.AddChild(LegendItem(null, UiTheme.GoldBright, "Ready to claim"));
        row.AddChild(LegendItem(AttendanceGlyph.Kind.Lock, UiTheme.TextLo, "Not yet"));
        return row;
    }

    private static Control LegendItem(AttendanceGlyph.Kind? kind, Color color, string text)
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

    private static void OutlineLabel(Label label)
    {
        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
    }

    private string SlotCaption(int slot, byte state) => state switch
    {
        AttendanceSlotState.Claimed or AttendanceSlotState.Expired =>
            ItemData.Text(TextAcquired, "Acquired"),
        AttendanceSlotState.ClaimableExtra =>
            ItemData.Text(TextMore, "More can be obtained"),
        AttendanceSlotState.Claimable =>
            ItemData.Text(TextObtainable, "Obtainable"),
        _ => slot >= AttendanceWire.BonusFirstSlot
            ? ItemData.Text(TextCumulative, "%d Cumulative Reward")
                .Replace("%d", AttendanceWire.BonusThreshold(slot).ToString())
            : ItemData.Text(TextDays, "%d day(s)").Replace("%d", slot.ToString()),
    };

    private void PluginNotify() => Godot.Callable.From(PluginHost.Game.RaiseAttendance).CallDeferred();

    private GameAttendanceCell CellAt(int index)
    {
        int slot = slots[index];
        byte state = states[index];
        var (itemId, count) = ItemData.AttendanceReward(slot);
        var look = state is AttendanceSlotState.Claimed or AttendanceSlotState.Expired ? GameAttendanceState.Claimed
            : state is >= AttendanceSlotState.Claimable and <= AttendanceSlotState.ClaimableExtra ? GameAttendanceState.Claimable
            : GameAttendanceState.Locked;
        return new GameAttendanceCell(itemId, count, itemId > 0 ? ItemData.DisplayName(itemId) : "",
            itemId > 0 ? ItemIcons.Get(itemId) : null, SlotCaption(slot, state), look);
    }

    private int AttendedDays()
    {
        int attended = 0;
        for (int i = 0; i < AttendanceWire.DailySlots; i++)
            if (states[i] is AttendanceSlotState.Claimed or AttendanceSlotState.Expired) attended++;
        return attended;
    }

    private void ClaimCell(int index)
    {
        if (index < 0 || index >= slots.Length) return;
        byte state = states[index];
        if (state is < AttendanceSlotState.Claimable or > AttendanceSlotState.ClaimableExtra) return;
        requests.Claim(slots[index], state);
    }

    private void ShowTooltip(int index)
    {
        if (index < 0 || index >= slots.Length) return;
        var (itemId, count) = ItemData.AttendanceReward(slots[index]);
        if (itemId <= 0) return;
        context.Tooltips.Show(-1, new ItemSlot
        {
            ItemId = itemId,
            Count = (short)count,
            Durability = (short)(ItemData.Get(itemId)?.Duration ?? 0),
        });
    }

    private sealed class PluginAttendanceBridge : IGameAttendance
    {
        private readonly AttendanceFeature attendance;

        public PluginAttendanceBridge(AttendanceFeature attendance) => this.attendance = attendance;

        public bool IsOpen => attendance.IsOpen;
        public int Attended => attendance.AttendedDays();

        public IReadOnlyList<GameAttendanceCell> Daily
        {
            get
            {
                var cells = new GameAttendanceCell[AttendanceWire.DailySlots];
                for (int i = 0; i < cells.Length; i++) cells[i] = attendance.CellAt(i);
                return cells;
            }
        }

        public IReadOnlyList<GameAttendanceCell> Bonus
        {
            get
            {
                var cells = new GameAttendanceCell[AttendanceWire.BonusSlots];
                for (int i = 0; i < cells.Length; i++) cells[i] = attendance.CellAt(AttendanceWire.DailySlots + i);
                return cells;
            }
        }

        public void Claim(int index) => attendance.ClaimCell(index);
        public void ShowTooltip(int index) => attendance.ShowTooltip(index);
        public void HideTooltip() => attendance.context.Tooltips.Hide();
        public void Close() => attendance.Close();

        event Action? IGameAttendance.Changed { add { } remove { } }
    }
}
