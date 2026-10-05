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

    private readonly Control _list;
    private readonly VScrollBar _bar;
    private readonly List<RowView> _rows = new();
    private readonly Label _empty;
    private readonly StyleBoxFlat _style;
    private List<NearbyRow> _all = new();
    private int _first;
    private int _visible = 1;
    private float _rowHeight;
    private bool? _scrollable;

    public NearbyCard()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _style = new StyleBoxFlat { BorderColor = new Color(UiTheme.Edge, EdgeAlpha) };
        _style.SetBorderWidthAll(1);
        _style.SetCornerRadiusAll(6);
        _style.ContentMarginLeft = _style.ContentMarginRight = _style.ContentMarginTop = _style.ContentMarginBottom = 6;
        AddThemeStyleboxOverride("panel", _style);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 4);
        AddChild(root);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 3);
        root.AddChild(body);
        _list = new Control
        {
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _list.Resized += Refit;
        body.AddChild(_list);
        _empty = UiTheme.Text("No one nearby.", 11, UiTheme.TextDim, HorizontalAlignment.Center);
        _empty.Visible = false;
        _list.AddChild(_empty);
        _bar = new VScrollBar
        {
            Step = 1,
            CustomMinimumSize = new Vector2(ScrollbarWidth, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None,
        };
        UiTheme.ThinScrollbar(_bar);
        _bar.ValueChanged += value =>
        {
            _first = (int)value;
            ShowRows();
        };
        body.AddChild(_bar);
    }

    public void SetBackground(float alpha)
    {
        _style.BgColor = new Color(0, 0, 0, alpha);
        _style.BorderColor = new Color(UiTheme.Edge, alpha > 0 ? EdgeAlpha : 0f);
    }

    public void SetRows(List<NearbyRow> rows)
    {
        _all = rows;
        ShowRows();
    }

    private RowView AddRow()
    {
        var row = new RowView();
        row.Clicked += view => { if (view.Row is { } r) WhisperPicked?.Invoke(r); };
        row.Wheeled += Scroll;
        row.MenuRequested += (view, at) => { if (view.Row is { } r) MenuPicked?.Invoke(r, at); };
        _rows.Add(row);
        _list.AddChild(row);
        return row;
    }

    private void Refit()
    {
        if (_rows.Count == 0) AddRow();
        if (_rowHeight <= 0) _rowHeight = _rows[0].GetCombinedMinimumSize().Y;
        _visible = NearbyDock.RowsThatFit(_list.Size.Y, _rowHeight, RowGap);
        while (_rows.Count <= _visible) AddRow();
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Position = new Vector2(0, i * (_rowHeight + RowGap));
            _rows[i].Size = new Vector2(_list.Size.X, _rowHeight);
        }
        _empty.Size = new Vector2(_list.Size.X, _rowHeight);
        ShowRows();
    }

    private void ShowRows()
    {
        _first = NearbyDock.ClampFirst(_first, _all.Count, _visible);
        for (int i = 0; i < _rows.Count; i++)
        {
            int at = _first + i;
            _rows[i].Show(i <= _visible && at < _all.Count ? _all[at] : null);
        }
        _empty.Visible = _all.Count == 0;
        SyncBar();
    }

    private void SyncBar()
    {
        _bar.MaxValue = Math.Max(_all.Count, _visible);
        _bar.Page = _visible;
        _bar.SetValueNoSignal(_first);
        bool scrollable = _all.Count > _visible;
        if (_scrollable == scrollable) return;
        _scrollable = scrollable;
        UiTheme.ThinScrollbarThumb(_bar, scrollable);
    }

    private void Scroll(int rows)
    {
        _first = NearbyDock.ClampFirst(_first + rows, _all.Count, _visible);
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

        private readonly ColorRect _stripe;
        private readonly Label _name;
        private readonly Label _distance;
        private bool _muted;

        public RowView()
        {
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 6);
            AddChild(line);
            _stripe = new ColorRect { CustomMinimumSize = new Vector2(StripeWidth, 0), MouseFilter = MouseFilterEnum.Ignore };
            line.AddChild(_stripe);
            _name = UiTheme.Text("", 12, UiTheme.TextHi);
            _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _name.ClipText = true;
            _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            line.AddChild(_name);
            _distance = UiTheme.Text("", 11, UiTheme.TextLo, HorizontalAlignment.Right);
            _distance.CustomMinimumSize = new Vector2(DistanceWidth, 0);
            line.AddChild(_distance);
            Style();
        }

        public void Show(NearbyRow? row)
        {
            Row = row;
            Visible = row != null;
            if (row is not { } r) return;
            bool inSight = r.Id != NearbyRoster.NotInSight;
            _stripe.Color = RelationColour(r.Relation);
            _name.Text = r.Name;
            _name.AddThemeColorOverride("font_color", inSight ? UiTheme.TextHi : UiTheme.TextLo);
            _distance.Text = $"{MathF.Round(r.Distance):0} m";
            TooltipText = $"Whisper {r.Name}";
            if (!inSight == _muted) return;
            _muted = !inSight;
            Style();
        }

        private void Style() => AddThemeStyleboxOverride("panel", UiTheme.Row(muted: _muted));

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
