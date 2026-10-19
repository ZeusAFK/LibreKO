using Godot;

namespace LibreKO.Features.Windows;

internal sealed class TimedItemsFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly ItemsSession items;
    private readonly PetSession pet;
    private readonly SelfSession self;
    private readonly EntryPopupsFeature entryPopups;
    private readonly TopIconsFeature topIcons;

    public TimedItemsFeature(IWorldContext context, ItemsSession items, PetSession pet, SelfSession self, EntryPopupsFeature entryPopups, TopIconsFeature topIcons)
    {
        this.context = context;
        this.items = items;
        this.pet = pet;
        this.self = self;
        this.entryPopups = entryPopups;
        this.topIcons = topIcons;
    }

    public void Dispose()
    {
        context.Inventory.Refreshed -= RefreshIcon;
    }

    public void Tick(double delta, double nowSec)
    {
        TimedItemsTick(delta);
    }

    private const string ButtonTip = "Open Temporary Item List.";
    private const string TimedItemsTitle = "Temporary Item List";
    private const double RefreshSeconds = 30.0;
    private static readonly Vector2 Origin = new(50, 76);
    private static readonly Vector2 Canvas = new(456, 424);
    internal static readonly Color UrgentColor = new("dc143c");
    internal static readonly Color PlaceColor = new("c0c0c0");
    private static readonly Color NoticeColor = new("ffffff");

    private static readonly int[] RowTops = { 119, 230, 341 };

    private const int RowHeight = 103;

    internal CanvasLayer Layer { get; set; } = null!;
    internal HudWindow? TiWindow { get; private set; }
    private Button tiIcon = null!;
    private TextureRect tiIconImage = null!;
    private readonly List<TimedItem> tiEntries = new();
    private readonly PanelContainer[] tiRows = new PanelContainer[TimedItems.RowsPerPage];
    private readonly ItemSlotView[] tiSlots = new ItemSlotView[TimedItems.RowsPerPage];
    private readonly Label[] tiNames = new Label[TimedItems.RowsPerPage];
    private readonly Label[] tiPlaces = new Label[TimedItems.RowsPerPage];
    private readonly Label[] tiTimes = new Label[TimedItems.RowsPerPage];
    private VScrollBar tiScroll = null!;
    private CheckBox tiHideToday = null!;
    private StyleBoxFlat tiRowNormal = null!;
    private StyleBoxFlat tiRowUrgent = null!;
    private double tiRefreshIn;

    public void Build()
    {
        context.Inventory.Refreshed += RefreshIcon;
        Layer = new CanvasLayer { Layer = UiLayers.SideWindows };
        context.Root.AddChild(Layer);
        tiIcon = topIcons.Add(TopIconSlot.TimedItems, "system/clock", ButtonTip, Toggle, out tiIconImage);
        tiIcon.Visible = false;
        tiIcon.Resized += PlaceIcon;
        tiIcon.VisibilityChanged += PlaceIcon;
        Callable.From(OfferTimedItems).CallDeferred();
    }

    private void PlaceIcon() => topIcons.Place(TopIconSlot.TimedItems, HudPlacement.TimedItemsIcon);

    private void OfferTimedItems()
    {
        RefreshIcon();
        if (items.TimedItemsOffered) return;
        items.NoteTimedItemsOffered();
        if (TimedItems.HiddenToday(Config.TimedItemsHiddenDay, DateTime.Now)) return;
        entryPopups.QueuePopup(EntryPopup.TimedItems, () =>
        {
            Open();
            return TiWindow;
        });
    }

    internal void RefreshIcon()
    {
        if (!GodotObject.IsInstanceValid(tiIcon)) return;
        tiIcon.Visible = Collect().Count > 0;
    }

    private List<TimedItem> Collect() => TimedItems.Collect(self.Inventory.Length, abs => self.Inventory[abs], pet.Pet?.Items);

    private void Toggle()
    {
        if (TiWindow is { Visible: true })
        {
            TiWindow.Visible = false;
            return;
        }
        Open();
    }

    internal void Open()
    {
        tiEntries.Clear();
        tiEntries.AddRange(Collect());
        if (tiEntries.Count == 0) return;
        TiWindow ??= BuildTimedItems();
        tiHideToday.SetPressedNoSignal(TimedItems.HiddenToday(Config.TimedItemsHiddenDay, DateTime.Now));
        tiScroll.MaxValue = TimedItems.PageCount(tiEntries.Count);
        tiScroll.Visible = tiEntries.Count > TimedItems.RowsPerPage;
        tiScroll.SetValueNoSignal(0);
        Render();
        UiKit.ShowOnTop(TiWindow);
    }

    private HudWindow BuildTimedItems()
    {
        tiRowNormal = UiTheme.Panel();
        tiRowUrgent = UiTheme.Panel();
        tiRowUrgent.BgColor = new Color(UrgentColor, 0.18f);
        tiRowUrgent.BorderColor = new Color(UrgentColor, 0.6f);

        var window = new HudWindow("timed_items", TimedItemsTitle, new Vector2(320, 110)) { Visible = false };
        window.Closed += () => window.Visible = false;
        Layer.AddChild(window);
        var canvas = new Control { CustomMinimumSize = Canvas, MouseFilter = Control.MouseFilterEnum.Pass };
        window.Body.AddChild(canvas);

        var notice = UiTheme.Text(GameText.System(TimedItems.TitleText, "Show all temporary items in the inventory and Magic Bag"),
            12, NoticeColor, HorizontalAlignment.Center);
        notice.VerticalAlignment = VerticalAlignment.Center;
        Place(canvas, notice, new Rect2(56, 82, 442, 34));

        for (int i = 0; i < TimedItems.RowsPerPage; i++)
        {
            int top = RowTops[i];
            var row = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
            row.AddThemeStyleboxOverride("panel", tiRowNormal);
            row.GuiInput += OnWheel;
            Place(canvas, row, new Rect2(55, top, 417, RowHeight));
            tiRows[i] = row;

            var slot = new ItemSlotView(45) { Veiled = true };
            slot.Hovered += view => context.Tooltips.Show(-1, view.Item);
            slot.Unhovered += _ => context.Tooltips.Hide();
            Place(canvas, slot, new Rect2(84, top + 29, 45, 45));
            tiSlots[i] = slot;

            tiNames[i] = Label(canvas, new Rect2(164, top + 9, 300, 29), 13, UiTheme.TextHi);
            tiPlaces[i] = Label(canvas, new Rect2(164, top + 38, 300, 28), 11, PlaceColor);
            tiTimes[i] = Label(canvas, new Rect2(164, top + 66, 300, 27), 13, UiTheme.TextHi);
        }

        tiScroll = new VScrollBar { MinValue = 0, Step = 1, Page = 1, MouseFilter = Control.MouseFilterEnum.Stop };
        tiScroll.ValueChanged += _ => Render();
        Place(canvas, tiScroll, new Rect2(479, 119, 18, 325));

        var hideLabel = UiTheme.Text(GameText.System(TimedItems.HideTodayText, "Do not show today."), 12, NoticeColor, HorizontalAlignment.Right);
        hideLabel.VerticalAlignment = VerticalAlignment.Center;
        Place(canvas, hideLabel, new Rect2(56, 457, 392, 38));
        tiHideToday = UiTheme.FlatCheck("", false);
        tiHideToday.Toggled += OnHideToday;
        Place(canvas, tiHideToday, new Rect2(457, 459, 36, 33));
        return window;
    }

    private static Label Label(Control canvas, Rect2 reference, int size, Color colour)
    {
        var label = UiTheme.Text("", size, colour);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ClipText = true;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        Place(canvas, label, reference);
        return label;
    }

    private static void Place(Control canvas, Control child, Rect2 reference)
    {
        canvas.AddChild(child);
        child.Position = reference.Position - Origin;
        child.Size = reference.Size;
        child.CustomMinimumSize = reference.Size;
    }

    private void OnWheel(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } wheel) return;
        if (wheel.ButtonIndex == MouseButton.WheelUp) tiScroll.Value -= 1;
        else if (wheel.ButtonIndex == MouseButton.WheelDown) tiScroll.Value += 1;
    }

    private void OnHideToday(bool on)
    {
        Config.SetTimedItemsHiddenDay(on ? TimedItems.DayKey(DateTime.Now) : 0);
        if (on && TiWindow != null) TiWindow.Visible = false;
    }

    private void Render()
    {
        if (TiWindow == null) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int first = (int)tiScroll.Value * TimedItems.RowsPerPage;
        for (int i = 0; i < TimedItems.RowsPerPage; i++)
        {
            int index = first + i;
            bool shown = index < tiEntries.Count;
            tiRows[i].Visible = tiSlots[i].Visible = tiNames[i].Visible = tiPlaces[i].Visible = tiTimes[i].Visible = shown;
            if (!shown) continue;
            var entry = tiEntries[index];
            tiSlots[i].Set(entry.Slot);
            tiNames[i].Text = ItemData.DisplayName(entry.Slot.ItemId);
            tiNames[i].AddThemeColorOverride("font_color", ItemGrade.Tint(entry.Slot.ItemId));
            tiPlaces[i].Text = TimedItems.PlaceText(entry.Place) is { } placeText ? GameText.System(placeText, "") : "";
            var left = TimedItems.Remaining(entry.Slot.ExpiresAt, now);
            tiTimes[i].Text = GameText.System(TimedItems.TextId(left.Unit), "%d").Replace("%d", left.Amount.ToString());
            tiTimes[i].AddThemeColorOverride("font_color", left.Urgent ? UrgentColor : UiTheme.TextHi);
            tiRows[i].AddThemeStyleboxOverride("panel", left.Urgent ? tiRowUrgent : tiRowNormal);
        }
    }

    private void TimedItemsTick(double delta)
    {
        if (TiWindow is not { Visible: true }) return;
        tiRefreshIn -= delta;
        if (tiRefreshIn > 0) return;
        tiRefreshIn = RefreshSeconds;
        Render();
    }
}
