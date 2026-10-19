using System.Globalization;
using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class RepurchaseFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly VendorSession vendor;
    private readonly IVendorRequests vendorRequests;
    private readonly IInteractionDialogs interactionDialogs;

    public RepurchaseFeature(IWorldContext context, SelfSession self, VendorSession vendor, IVendorRequests vendorRequests, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.self = self;
        this.vendor = vendor;
        this.vendorRequests = vendorRequests;
        this.interactionDialogs = interactionDialogs;
    }

    private const string Title = "Item repurchase";
    private const int VisibleRows = 10;
    private const int TextAsk = 16652;
    private const int TextExpired = 16653;
    private const int TextBought = 16654;
    private const int TextNoRoom = 18105;
    private const int TextNoCoins = 1702;
    private const int TextRetry = 6308;
    private const string TimeFormat = "MM-dd HH:mm";
    private static readonly Vector2 Origin = new(38, 32);
    private static readonly Vector2 Canvas = new(356, 474);
    private const int HeaderSize = 10;

    private static readonly int[] RowTops = { 101, 141, 180, 219, 259, 299, 338, 377, 417, 457 };

    private const int RowHeight = 39;
    private static readonly Color Warning = new("ff4040");

    internal CanvasLayer Layer { get; set; } = null!;
    internal HudWindow? Window { get; set; }
    private int rbNpcId = -1;
    private readonly RepurchaseSession rbSession = new();
    internal List<RepurchaseRow> RbRows { get; } = new();
    private readonly Label[] rbNames = new Label[VisibleRows];
    private readonly Label[] rbPrices = new Label[VisibleRows];
    private readonly Label[] rbTimes = new Label[VisibleRows];
    private readonly Button[] rbBuy = new Button[VisibleRows];
    internal VScrollBar RbScroll { get; private set; } = null!;
    private Label rbEmpty = null!;

    public bool IsOpen => rbSession.IsOpen;

    public void Build()
    {
        Layer = new CanvasLayer { Layer = UiLayers.SideWindows };
        context.Root.AddChild(Layer);
        vendor.RepurchaseEvent += On;
        interactionDialogs.InteractionCloses(this);
        PluginHost.Game.AttachRepurchase(new PluginBridge(this));
    }

    public void Dispose() => vendor.RepurchaseEvent -= On;

    internal void Open(int npcId)
    {
        rbNpcId = npcId;
        rbSession.Request();
        vendorRequests.Repurchase(RepurchaseSubOpcode.Open);
    }

    public void Close()
    {
        if (!rbSession.Close()) return;
        if (Window != null) Window.Visible = false;
        context.Tooltips.Hide();
        vendorRequests.Repurchase(RepurchaseSubOpcode.Close);
        PluginNotify();
    }

    private void PluginNotify() => Callable.From(PluginHost.Game.RaiseRepurchase).CallDeferred();

    private GameRepurchaseRow RowView(RepurchaseRow row, long now) => new(
        ItemData.DisplayName(row.ItemId), ItemGrade.Tint(row.ItemId),
        $"{row.Price.ToString("n0", CultureInfo.InvariantCulture)} gold", row.Price > self.Sheet.Gold,
        DateTimeOffset.FromUnixTimeSeconds(row.DeletesAt).ToLocalTime().ToString(TimeFormat, CultureInfo.InvariantCulture),
        row.DeletesAt <= now);

    private void Buy(RepurchaseRow row)
    {
        if (row.DeletesAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            Notice.Show(context.Root, GameText.System(TextExpired, "Re-purchase period has been expired."), Title);
            return;
        }
        vendorRequests.RepurchaseBuy(row.Row, row.ItemId, rbNpcId);
    }

    private sealed class PluginBridge : IGameRepurchase
    {
        private readonly RepurchaseFeature repurchase;

        public PluginBridge(RepurchaseFeature repurchase) => this.repurchase = repurchase;

        public bool IsOpen => repurchase.IsOpen;

        public IReadOnlyList<GameRepurchaseRow> Rows
        {
            get
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var rows = new List<GameRepurchaseRow>(repurchase.RbRows.Count);
                foreach (var row in repurchase.RbRows) rows.Add(repurchase.RowView(row, now));
                return rows;
            }
        }

        public string AskText => GameText.System(TextAsk, "Do you want to purchase again?");

        public void Buy(int index)
        {
            if (index >= 0 && index < repurchase.RbRows.Count) repurchase.Buy(repurchase.RbRows[index]);
        }

        public void Refresh() => repurchase.vendorRequests.Repurchase(RepurchaseSubOpcode.Refresh);

        public void ShowTooltip(int index)
        {
            if (index >= 0 && index < repurchase.RbRows.Count) repurchase.context.Tooltips.Show(-1, UiKit.TooltipItem(repurchase.RbRows[index].ItemId));
        }

        public void HideTooltip() => repurchase.context.Tooltips.Hide();
        public void Close() => repurchase.Close();

        event Action? IGameRepurchase.Changed { add { } remove { } }
    }

    private void On(RepurchaseReply reply)
    {
        if (reply.Op is RepurchaseSubOpcode.Open or RepurchaseSubOpcode.Refresh)
        {
            var listing = rbSession.Accept(reply.Op, reply.Ok);
            if (listing == RepurchaseListing.Ignored) return;
            bool opening = listing == RepurchaseListing.Opened;
            RbRows.Clear();
            RbRows.AddRange(reply.Rows);
            Window ??= BuildRepurchase();
            if (opening) RbScroll.SetValueNoSignal(0);
            RbScroll.MaxValue = Math.Max(0, RbRows.Count - VisibleRows);
            RbScroll.Visible = RbRows.Count > VisibleRows;
            Render();
            if (opening) UiKit.ShowOnTop(Window);
            PluginNotify();
            return;
        }
        if (reply.Op != RepurchaseSubOpcode.Buy) return;

        string message = reply.Ok
            ? GameText.System(TextBought, "You have obtained an item from the re-purchase.")
            : reply.Error switch
            {
                RepurchaseError.NoRoom => GameText.System(TextNoRoom, "Not enough inventory slot"),
                RepurchaseError.Expired => GameText.System(TextExpired, "Re-purchase period has been expired."),
                RepurchaseError.NoCoins => GameText.System(TextNoCoins, "You don't have enough gold."),
                _ => GameText.System(TextRetry, "Failed. Please press the refresh button."),
            };
        if (IsOpen) Notice.Show(context.Root, message, Title);
        else context.Log.Notice(message, UiKit.OrderMessageYellow);
        if (IsOpen) vendorRequests.Repurchase(RepurchaseSubOpcode.Refresh);
    }

    internal HudWindow BuildRepurchase()
    {
        var window = new HudWindow("repurchase", Title, new Vector2(320, 110)) { Visible = false };
        window.Closed += Close;
        Layer.AddChild(window);
        var canvas = new Control { CustomMinimumSize = Canvas, MouseFilter = Control.MouseFilterEnum.Pass };
        window.Body.AddChild(canvas);

        Header(canvas, "Stored item", new Rect2(43, 75, 94, 22));
        Header(canvas, "Repurchasing price", new Rect2(140, 75, 94, 22));
        Header(canvas, "Deletion time", new Rect2(236, 76, 82, 22));
        Header(canvas, "Purchase", new Rect2(319, 76, 42, 22));

        var refresh = UiTheme.IconButton("↻", "Refresh");
        refresh.Pressed += () => vendorRequests.Repurchase(RepurchaseSubOpcode.Refresh);
        Place(canvas, refresh, new Rect2(42, 35, 26, 25));

        for (int i = 0; i < VisibleRows; i++)
        {
            int top = RowTops[i];
            int row = i;
            var name = Label(canvas, "", new Rect2(42, top, 99, RowHeight), UiTheme.TextHi);
            name.MouseFilter = Control.MouseFilterEnum.Stop;
            name.MouseEntered += () => Hover(row);
            name.MouseExited += context.Tooltips.Hide;
            name.GuiInput += OnWheel;
            rbNames[i] = name;
            rbPrices[i] = Label(canvas, "", new Rect2(144, top, 89, RowHeight), UiTheme.TextHi);
            rbTimes[i] = Label(canvas, "", new Rect2(236, top, 79, RowHeight), UiTheme.TextHi);
            var buy = UiTheme.SmallButton("Buy", "Purchase this item again");
            buy.Pressed += () => Ask(row);
            Place(canvas, buy, new Rect2(318, top + 6, 44, RowHeight - 12));
            rbBuy[i] = buy;
        }

        rbEmpty = Label(canvas, "No item to buy back.", new Rect2(42, 101, 320, 40), UiTheme.TextLo);
        rbEmpty.HorizontalAlignment = HorizontalAlignment.Center;

        RbScroll = new VScrollBar { MinValue = 0, Step = 1, Page = 0, MouseFilter = Control.MouseFilterEnum.Stop };
        RbScroll.ValueChanged += _ => Render();
        Place(canvas, RbScroll, new Rect2(365, 101, 18, 396));
        return window;
    }

    private static void Header(Control canvas, string text, Rect2 reference)
    {
        var label = Label(canvas, text, reference, UiTheme.TextLo);
        label.AddThemeFontSizeOverride("font_size", HeaderSize);
        label.ClipText = false;
    }

    private static Label Label(Control canvas, string text, Rect2 reference, Color colour)
    {
        var label = UiTheme.Text(text, 11, colour);
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
        if (wheel.ButtonIndex == MouseButton.WheelUp) RbScroll.Value -= 1;
        else if (wheel.ButtonIndex == MouseButton.WheelDown) RbScroll.Value += 1;
    }

    private RepurchaseRow? At(int visibleRow)
    {
        int index = (int)RbScroll.Value + visibleRow;
        return index >= 0 && index < RbRows.Count ? RbRows[index] : null;
    }

    internal void Render()
    {
        if (Window == null) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        rbEmpty.Visible = RbRows.Count == 0;
        for (int i = 0; i < VisibleRows; i++)
        {
            var entry = At(i);
            bool shown = entry != null;
            rbNames[i].Visible = rbPrices[i].Visible = rbTimes[i].Visible = rbBuy[i].Visible = shown;
            if (entry is not { } row) continue;
            bool expired = row.DeletesAt <= now;
            rbNames[i].Text = ItemData.DisplayName(row.ItemId);
            rbNames[i].AddThemeColorOverride("font_color", ItemGrade.Tint(row.ItemId));
            rbPrices[i].Text = $"{row.Price.ToString("n0", CultureInfo.InvariantCulture)} gold";
            rbPrices[i].AddThemeColorOverride("font_color", row.Price > self.Sheet.Gold ? Warning : UiTheme.TextHi);
            rbTimes[i].Text = DateTimeOffset.FromUnixTimeSeconds(row.DeletesAt).ToLocalTime()
                .ToString(TimeFormat, CultureInfo.InvariantCulture);
            rbTimes[i].AddThemeColorOverride("font_color", expired ? Warning : UiTheme.TextHi);
        }
    }

    private void Hover(int visibleRow)
    {
        if (At(visibleRow) is { } row) context.Tooltips.Show(-1, UiKit.TooltipItem(row.ItemId));
    }

    private void Ask(int visibleRow)
    {
        if (At(visibleRow) is not { } row) return;
        if (row.DeletesAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            Notice.Show(context.Root, GameText.System(TextExpired, "Re-purchase period has been expired."), Title);
            return;
        }
        Notice.Confirm(context.Root,
            $"{GameText.System(TextAsk, "Do you want to purchase again?")}\n{ItemData.DisplayName(row.ItemId)}\n"
            + $"{row.Price.ToString("n0", CultureInfo.InvariantCulture)} gold",
            "OK", "Cancel",
            () => vendorRequests.RepurchaseBuy(row.Row, row.ItemId, rbNpcId), title: Title);
    }
}
