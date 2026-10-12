using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace LibreKO;

public partial class World
{
    private const string RepurchaseTitle = "Item repurchase";
    private const int RepurchaseVisibleRows = 10;
    private const int TextRepurchaseAsk = 16652;
    private const int TextRepurchaseExpired = 16653;
    private const int TextRepurchaseBought = 16654;
    private const int TextRepurchaseNoRoom = 18105;
    private const int TextRepurchaseNoCoins = 1702;
    private const int TextRepurchaseRetry = 6308;
    private const string RepurchaseTimeFormat = "MM-dd HH:mm";
    private static readonly Vector2 RepurchaseOrigin = new(38, 32);
    private static readonly Vector2 RepurchaseCanvas = new(356, 474);
    private const int RepurchaseHeaderSize = 10;
    private static readonly int[] RepurchaseRowTops = { 101, 141, 180, 219, 259, 299, 338, 377, 417, 457 };
    private const int RepurchaseRowHeight = 39;
    private static readonly Color RepurchaseWarning = new("ff4040");

    private CanvasLayer _rbLayer = null!;
    private HudWindow? _rbWindow;
    private int _rbNpcId = -1;
    private readonly RepurchaseSession _rbSession = new();
    private readonly List<RepurchaseRow> _rbRows = new();
    private readonly Label[] _rbNames = new Label[RepurchaseVisibleRows];
    private readonly Label[] _rbPrices = new Label[RepurchaseVisibleRows];
    private readonly Label[] _rbTimes = new Label[RepurchaseVisibleRows];
    private readonly Button[] _rbBuy = new Button[RepurchaseVisibleRows];
    private VScrollBar _rbScroll = null!;
    private Label _rbEmpty = null!;

    private bool _repurchaseShown => _rbSession.IsOpen;

    private void RepurchaseInit()
    {
        _rbLayer = new CanvasLayer { Layer = 63 };
        AddChild(_rbLayer);
        Net.I.RepurchaseEvent += OnRepurchase;
        InteractionCloses(() => _repurchaseShown, CloseRepurchase);
        PluginHost.Game.AttachRepurchase(new PluginRepurchaseBridge(this));
    }

    private void RepurchaseDispose() => Net.I.RepurchaseEvent -= OnRepurchase;

    private void OpenRepurchase(int npcId)
    {
        _rbNpcId = npcId;
        _rbSession.Request();
        Net.I.SendRepurchase(RepurchaseReply.Open);
    }

    private void CloseRepurchase()
    {
        if (!_rbSession.Close()) return;
        if (_rbWindow != null) _rbWindow.Visible = false;
        HideItemTooltip();
        Net.I.SendRepurchase(RepurchaseReply.Close);
        PluginNotifyRepurchase();
    }

    private void PluginNotifyRepurchase() => Callable.From(PluginHost.Game.RaiseRepurchase).CallDeferred();

    private GameRepurchaseRow RepurchaseRowView(RepurchaseRow row, long now) => new(
        ItemData.DisplayName(row.ItemId), ItemGrade.Tint(row.ItemId),
        $"{row.Price.ToString("n0", CultureInfo.InvariantCulture)} gold", row.Price > Sheet.Gold,
        DateTimeOffset.FromUnixTimeSeconds(row.DeletesAt).ToLocalTime().ToString(RepurchaseTimeFormat, CultureInfo.InvariantCulture),
        row.DeletesAt <= now);

    private void BuyRepurchase(RepurchaseRow row)
    {
        if (row.DeletesAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            Notice.Show(this, SystemText(TextRepurchaseExpired, "Re-purchase period has been expired."), RepurchaseTitle);
            return;
        }
        Net.I.SendRepurchaseBuy(row.Row, row.ItemId, _rbNpcId);
    }

    private sealed class PluginRepurchaseBridge : IGameRepurchase
    {
        private readonly World _w;

        public PluginRepurchaseBridge(World w) => _w = w;

        public bool IsOpen => _w._repurchaseShown;

        public IReadOnlyList<GameRepurchaseRow> Rows
        {
            get
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var rows = new List<GameRepurchaseRow>(_w._rbRows.Count);
                foreach (var row in _w._rbRows) rows.Add(_w.RepurchaseRowView(row, now));
                return rows;
            }
        }

        public string AskText => SystemText(TextRepurchaseAsk, "Do you want to purchase again?");

        public void Buy(int index)
        {
            if (index >= 0 && index < _w._rbRows.Count) _w.BuyRepurchase(_w._rbRows[index]);
        }

        public void Refresh() => Net.I.SendRepurchase(RepurchaseReply.Refresh);

        public void ShowTooltip(int index)
        {
            if (index >= 0 && index < _w._rbRows.Count) _w.ShowItemTooltip(-1, TooltipItem(_w._rbRows[index].ItemId));
        }

        public void HideTooltip() => _w.HideItemTooltip();
        public void Close() => _w.CloseRepurchase();

        event Action? IGameRepurchase.Changed { add { } remove { } }
    }

    private void OnRepurchase(RepurchaseReply reply)
    {
        if (reply.Op is RepurchaseReply.Open or RepurchaseReply.Refresh)
        {
            var listing = _rbSession.Accept(reply.Op, reply.Ok);
            if (listing == RepurchaseListing.Ignored) return;
            bool opening = listing == RepurchaseListing.Opened;
            _rbRows.Clear();
            _rbRows.AddRange(reply.Rows);
            _rbWindow ??= BuildRepurchase();
            if (opening) _rbScroll.SetValueNoSignal(0);
            _rbScroll.MaxValue = Math.Max(0, _rbRows.Count - RepurchaseVisibleRows);
            _rbScroll.Visible = _rbRows.Count > RepurchaseVisibleRows;
            RenderRepurchase();
            if (opening) ShowOnTop(_rbWindow);
            PluginNotifyRepurchase();
            return;
        }
        if (reply.Op != RepurchaseReply.Buy) return;

        string message = reply.Ok
            ? SystemText(TextRepurchaseBought, "You have obtained an item from the re-purchase.")
            : reply.Error switch
            {
                RepurchaseReply.NoRoom => SystemText(TextRepurchaseNoRoom, "Not enough inventory slot"),
                RepurchaseReply.Expired => SystemText(TextRepurchaseExpired, "Re-purchase period has been expired."),
                RepurchaseReply.NoCoins => SystemText(TextRepurchaseNoCoins, "You don't have enough gold."),
                _ => SystemText(TextRepurchaseRetry, "Failed. Please press the refresh button."),
            };
        if (_repurchaseShown) Notice.Show(this, message, RepurchaseTitle);
        else CombatNotice(message, OrderMessageYellow);
        if (_repurchaseShown) Net.I.SendRepurchase(RepurchaseReply.Refresh);
    }

    private HudWindow BuildRepurchase()
    {
        var window = new HudWindow("repurchase", RepurchaseTitle, new Vector2(320, 110)) { Visible = false };
        window.Closed += CloseRepurchase;
        _rbLayer.AddChild(window);
        var canvas = new Control { CustomMinimumSize = RepurchaseCanvas, MouseFilter = Control.MouseFilterEnum.Pass };
        window.Body.AddChild(canvas);

        RepurchaseHeader(canvas, "Stored item", new Rect2(43, 75, 94, 22));
        RepurchaseHeader(canvas, "Repurchasing price", new Rect2(140, 75, 94, 22));
        RepurchaseHeader(canvas, "Deletion time", new Rect2(236, 76, 82, 22));
        RepurchaseHeader(canvas, "Purchase", new Rect2(319, 76, 42, 22));

        var refresh = UiTheme.IconButton("↻", "Refresh");
        refresh.Pressed += () => Net.I.SendRepurchase(RepurchaseReply.Refresh);
        RepurchasePlace(canvas, refresh, new Rect2(42, 35, 26, 25));

        for (int i = 0; i < RepurchaseVisibleRows; i++)
        {
            int top = RepurchaseRowTops[i];
            int row = i;
            var name = RepurchaseLabel(canvas, "", new Rect2(42, top, 99, RepurchaseRowHeight), UiTheme.TextHi);
            name.MouseFilter = Control.MouseFilterEnum.Stop;
            name.MouseEntered += () => HoverRepurchase(row);
            name.MouseExited += HideItemTooltip;
            name.GuiInput += OnRepurchaseWheel;
            _rbNames[i] = name;
            _rbPrices[i] = RepurchaseLabel(canvas, "", new Rect2(144, top, 89, RepurchaseRowHeight), UiTheme.TextHi);
            _rbTimes[i] = RepurchaseLabel(canvas, "", new Rect2(236, top, 79, RepurchaseRowHeight), UiTheme.TextHi);
            var buy = UiTheme.SmallButton("Buy", "Purchase this item again");
            buy.Pressed += () => AskRepurchase(row);
            RepurchasePlace(canvas, buy, new Rect2(318, top + 6, 44, RepurchaseRowHeight - 12));
            _rbBuy[i] = buy;
        }

        _rbEmpty = RepurchaseLabel(canvas, "No item to buy back.", new Rect2(42, 101, 320, 40), UiTheme.TextLo);
        _rbEmpty.HorizontalAlignment = HorizontalAlignment.Center;

        _rbScroll = new VScrollBar { MinValue = 0, Step = 1, Page = 0, MouseFilter = Control.MouseFilterEnum.Stop };
        _rbScroll.ValueChanged += _ => RenderRepurchase();
        RepurchasePlace(canvas, _rbScroll, new Rect2(365, 101, 18, 396));
        return window;
    }

    private static void RepurchaseHeader(Control canvas, string text, Rect2 reference)
    {
        var label = RepurchaseLabel(canvas, text, reference, UiTheme.TextLo);
        label.AddThemeFontSizeOverride("font_size", RepurchaseHeaderSize);
        label.ClipText = false;
    }

    private static Label RepurchaseLabel(Control canvas, string text, Rect2 reference, Color colour)
    {
        var label = UiTheme.Text(text, 11, colour);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ClipText = true;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        RepurchasePlace(canvas, label, reference);
        return label;
    }

    private static void RepurchasePlace(Control canvas, Control child, Rect2 reference)
    {
        canvas.AddChild(child);
        child.Position = reference.Position - RepurchaseOrigin;
        child.Size = reference.Size;
        child.CustomMinimumSize = reference.Size;
    }

    private void OnRepurchaseWheel(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } wheel) return;
        if (wheel.ButtonIndex == MouseButton.WheelUp) _rbScroll.Value -= 1;
        else if (wheel.ButtonIndex == MouseButton.WheelDown) _rbScroll.Value += 1;
    }

    private RepurchaseRow? RepurchaseAt(int visibleRow)
    {
        int index = (int)_rbScroll.Value + visibleRow;
        return index >= 0 && index < _rbRows.Count ? _rbRows[index] : null;
    }

    private void RenderRepurchase()
    {
        if (_rbWindow == null) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _rbEmpty.Visible = _rbRows.Count == 0;
        for (int i = 0; i < RepurchaseVisibleRows; i++)
        {
            var entry = RepurchaseAt(i);
            bool shown = entry != null;
            _rbNames[i].Visible = _rbPrices[i].Visible = _rbTimes[i].Visible = _rbBuy[i].Visible = shown;
            if (entry is not { } row) continue;
            bool expired = row.DeletesAt <= now;
            _rbNames[i].Text = ItemData.DisplayName(row.ItemId);
            _rbNames[i].AddThemeColorOverride("font_color", ItemGrade.Tint(row.ItemId));
            _rbPrices[i].Text = $"{row.Price.ToString("n0", CultureInfo.InvariantCulture)} gold";
            _rbPrices[i].AddThemeColorOverride("font_color", row.Price > Sheet.Gold ? RepurchaseWarning : UiTheme.TextHi);
            _rbTimes[i].Text = DateTimeOffset.FromUnixTimeSeconds(row.DeletesAt).ToLocalTime()
                .ToString(RepurchaseTimeFormat, CultureInfo.InvariantCulture);
            _rbTimes[i].AddThemeColorOverride("font_color", expired ? RepurchaseWarning : UiTheme.TextHi);
        }
    }

    private void HoverRepurchase(int visibleRow)
    {
        if (RepurchaseAt(visibleRow) is { } row) ShowItemTooltip(-1, TooltipItem(row.ItemId));
    }

    private void AskRepurchase(int visibleRow)
    {
        if (RepurchaseAt(visibleRow) is not { } row) return;
        if (row.DeletesAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            Notice.Show(this, SystemText(TextRepurchaseExpired, "Re-purchase period has been expired."), RepurchaseTitle);
            return;
        }
        Notice.Confirm(this,
            $"{SystemText(TextRepurchaseAsk, "Do you want to purchase again?")}\n{ItemData.DisplayName(row.ItemId)}\n"
            + $"{row.Price.ToString("n0", CultureInfo.InvariantCulture)} gold",
            "OK", "Cancel",
            () => Net.I.SendRepurchaseBuy(row.Row, row.ItemId, _rbNpcId), title: RepurchaseTitle);
    }
}
