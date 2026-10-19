using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public sealed partial class NearbyCard : PanelContainer
{
    private const float StripeWidth = 3f;
    private const float DistanceWidth = 44f;
    private const float ScrollbarWidth = 6f;
    private const float RowGap = 1f;
    private const int WheelRows = 3;
    private static readonly Color AllyColour = new("6fa8ff");
    private const float EdgeAlpha = 0.25f;

    public event Action<NearbyRow>? WhisperPicked;
    public event Action<NearbyRow, Vector2>? MenuPicked;

    private readonly Control list;
    private readonly VScrollBar bar;
    private readonly List<RowView> rowViews = new();
    private readonly Label empty;
    private readonly StyleBoxFlat style;
    private List<NearbyRow> all = new();
    private int first;
    private int visible = 1;
    private float rowHeight;
    private bool? scrollable;

    public NearbyCard()
    {
        MouseFilter = MouseFilterEnum.Stop;
        style = new StyleBoxFlat { BorderColor = new Color(UiTheme.Edge, EdgeAlpha) };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(6);
        style.ContentMarginLeft = style.ContentMarginRight = style.ContentMarginTop = style.ContentMarginBottom = 6;
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 4);
        AddChild(root);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 3);
        root.AddChild(body);
        list = new Control
        {
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        list.Resized += Refit;
        body.AddChild(list);
        empty = UiTheme.Text("No one nearby.", 11, UiTheme.TextDim, HorizontalAlignment.Center);
        empty.Visible = false;
        list.AddChild(empty);
        bar = new VScrollBar
        {
            Step = 1,
            CustomMinimumSize = new Vector2(ScrollbarWidth, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None,
        };
        UiTheme.ThinScrollbar(bar);
        bar.ValueChanged += value =>
        {
            first = (int)value;
            ShowRows();
        };
        body.AddChild(bar);
    }

    public void SetBackground(float alpha)
    {
        style.BgColor = new Color(0, 0, 0, alpha);
        style.BorderColor = new Color(UiTheme.Edge, alpha > 0 ? EdgeAlpha : 0f);
    }

    public void SetRows(List<NearbyRow> rows)
    {
        all = rows;
        ShowRows();
    }

    private RowView AddRow()
    {
        var row = new RowView();
        row.Clicked += view => { if (view.Row is { } r) WhisperPicked?.Invoke(r); };
        row.Wheeled += Scroll;
        row.MenuRequested += (view, at) => { if (view.Row is { } r) MenuPicked?.Invoke(r, at); };
        rowViews.Add(row);
        list.AddChild(row);
        return row;
    }

    private void Refit()
    {
        if (rowViews.Count == 0) AddRow();
        if (rowHeight <= 0) rowHeight = rowViews[0].GetCombinedMinimumSize().Y;
        visible = NearbyDock.RowsThatFit(list.Size.Y, rowHeight, RowGap);
        while (rowViews.Count <= visible) AddRow();
        for (int i = 0; i < rowViews.Count; i++)
        {
            rowViews[i].Position = new Vector2(0, i * (rowHeight + RowGap));
            rowViews[i].Size = new Vector2(list.Size.X, rowHeight);
        }
        empty.Size = new Vector2(list.Size.X, rowHeight);
        ShowRows();
    }

    private void ShowRows()
    {
        first = NearbyDock.ClampFirst(first, all.Count, visible);
        for (int i = 0; i < rowViews.Count; i++)
        {
            int at = first + i;
            rowViews[i].Show(i <= visible && at < all.Count ? all[at] : null);
        }
        empty.Visible = all.Count == 0;
        SyncBar();
    }

    private void SyncBar()
    {
        bar.MaxValue = Math.Max(all.Count, visible);
        bar.Page = visible;
        bar.SetValueNoSignal(first);
        bool canScroll = all.Count > visible;
        if (scrollable == canScroll) return;
        scrollable = canScroll;
        UiTheme.ThinScrollbarThumb(bar, canScroll);
    }

    private void Scroll(int rows)
    {
        first = NearbyDock.ClampFirst(first + rows, all.Count, visible);
        ShowRows();
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton { Pressed: true } mb) return;
        if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            Scroll(mb.ButtonIndex == MouseButton.WheelUp ? -WheelRows : WheelRows);
            AcceptEvent();
        }
    }

    public static Color RelationColour(NearbyRelation relation) => relation switch
    {
        NearbyRelation.Party => UiTheme.Good,
        NearbyRelation.Clan => UiTheme.GoldVivid,
        NearbyRelation.Ally => AllyColour,
        _ => UiTheme.Bad,
    };

    private sealed partial class RowView : PanelContainer
    {
        public event Action<RowView>? Clicked;
        public event Action<RowView, Vector2>? MenuRequested;
        public event Action<int>? Wheeled;
        public NearbyRow? Row { get; private set; }

        private readonly ColorRect stripe;
        private readonly Label name;
        private readonly Label distance;
        private bool muted;

        public RowView()
        {
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 6);
            AddChild(line);
            stripe = new ColorRect { CustomMinimumSize = new Vector2(StripeWidth, 0), MouseFilter = MouseFilterEnum.Ignore };
            line.AddChild(stripe);
            name = UiTheme.Text("", 12, UiTheme.TextHi);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.ClipText = true;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            line.AddChild(name);
            distance = UiTheme.Text("", 11, UiTheme.TextLo, HorizontalAlignment.Right);
            distance.CustomMinimumSize = new Vector2(DistanceWidth, 0);
            line.AddChild(distance);
            Style();
        }

        public void Show(NearbyRow? row)
        {
            Row = row;
            Visible = row != null;
            if (row is not { } r) return;
            bool inSight = r.Id != NearbyRoster.NotInSight;
            stripe.Color = RelationColour(r.Relation);
            name.Text = r.Name;
            name.AddThemeColorOverride("font_color", inSight ? UiTheme.TextHi : UiTheme.TextLo);
            distance.Text = $"{MathF.Round(r.Distance):0} m";
            TooltipText = $"Whisper {r.Name}";
            if (!inSight == muted) return;
            muted = !inSight;
            Style();
        }

        private void Style() => AddThemeStyleboxOverride("panel", UiTheme.Row(muted: muted));

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is not InputEventMouseButton { Pressed: true } mb) return;
            if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                Wheeled?.Invoke(mb.ButtonIndex == MouseButton.WheelUp ? -WheelRows : WheelRows);
                AcceptEvent();
                return;
            }
            if (Row == null) return;
            if (mb.ButtonIndex == MouseButton.Left)
            {
                Clicked?.Invoke(this);
                AcceptEvent();
            }
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                MenuRequested?.Invoke(this, mb.GlobalPosition);
                AcceptEvent();
            }
        }
    }
}
