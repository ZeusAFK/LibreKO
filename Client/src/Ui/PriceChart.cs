using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class PriceChart : Control
{
    private const float AxisWidth = 64f;
    private const float LabelBand = 26f;
    private const float PlotPadding = 12f;
    private const float MarkerRoom = 14f;
    private const float BarWidth = 24f;
    private const float MarkerHalf = 7f;
    private const float MarkerHeight = 8f;
    private const int AxisFontSize = 12;
    private const int DayFontSize = 13;

    private static readonly Color BarFill = new(UiTheme.Gold, 0.55f);
    private static readonly Color BarHover = new(UiTheme.GoldBright, 0.75f);
    private static readonly Color BarEdge = UiTheme.GoldVivid;
    private static readonly Color MaxColor = UiTheme.Bad;
    private static readonly Color MinColor = UiTheme.Good;
    private static readonly Color Midline = new(1f, 1f, 1f, 0.10f);
    private static readonly Color Baseline = new(1f, 1f, 1f, 0.22f);

    private enum Part { None, Bar, Max, Min }

    private IReadOnlyList<MarketPriceDay> _days = Array.Empty<MarketPriceDay>();
    private long _top = 10, _bottom;
    private int _hoverDay = -1;
    private Part _hoverPart;

    public Func<int, string> DayLabel { get; set; } = index => $"{index + 1}";
    public Func<string, long, string> ValueTip { get; set; } = (label, value) => $"{label} {value:n0}";
    public string AverageLabel { get; set; } = "AVG:";
    public string MaxLabel { get; set; } = "MAX:";
    public string MinLabel { get; set; } = "MIN:";

    public PriceChart()
    {
        MouseFilter = MouseFilterEnum.Pass;
        CustomMinimumSize = new Vector2(460, 250);
    }

    public bool HasData { get; private set; }

    public void ShowDays(IReadOnlyList<MarketPriceDay> days)
    {
        _days = days;
        (_top, _bottom) = MarketPrice.Scale(days);
        HasData = true;
        QueueRedraw();
    }

    public void Clear()
    {
        _days = Array.Empty<MarketPriceDay>();
        HasData = false;
        TooltipText = "";
        QueueRedraw();
    }

    private Rect2 Plot => new(AxisWidth, PlotPadding + MarkerRoom, Size.X - AxisWidth - PlotPadding,
        Size.Y - PlotPadding * 2 - MarkerRoom * 2 - LabelBand);

    private float Column(int index)
    {
        var plot = Plot;
        float slot = plot.Size.X / MarketPrice.DaysShown;
        return plot.Position.X + slot * (index + 0.5f);
    }

    private float ValueY(long value)
    {
        var plot = Plot;
        long range = Math.Max(1, _top - _bottom);
        float share = Mathf.Clamp((float)(value - _bottom) / range, 0f, 1f);
        return plot.End.Y - share * plot.Size.Y;
    }

    public override void _Draw()
    {
        DrawStyleBox(UiTheme.Inset(), new Rect2(Vector2.Zero, Size));
        var plot = Plot;
        var font = GetThemeDefaultFont();

        float mid = plot.Position.Y + plot.Size.Y / 2f;
        DrawLine(new Vector2(plot.Position.X, mid), new Vector2(plot.End.X, mid), Midline, 1f);
        DrawLine(new Vector2(plot.Position.X, plot.End.Y), new Vector2(plot.End.X, plot.End.Y), Baseline, 1f);
        DrawLine(new Vector2(plot.Position.X, plot.Position.Y), new Vector2(plot.Position.X, plot.End.Y), Baseline, 1f);

        if (HasData)
        {
            DrawAxis(font, _top, plot.Position.Y);
            DrawAxis(font, (_top + _bottom) / 2, mid);
            DrawAxis(font, _bottom, plot.End.Y);
        }

        for (int i = 0; i < MarketPrice.DaysShown; i++)
        {
            float x = Column(i);
            DrawString(font, new Vector2(x - 60f, Size.Y - PlotPadding - 4f), DayLabel(i), HorizontalAlignment.Center, 120f,
                DayFontSize, UiTheme.TextLo);
            if (!HasData || i >= _days.Count) continue;

            var day = _days[i];
            if (day.HasTrades)
            {
                float y = ValueY(day.Average);
                var bar = new Rect2(x - BarWidth / 2f, y, BarWidth, Math.Max(1f, plot.End.Y - y));
                DrawRect(bar, _hoverDay == i && _hoverPart == Part.Bar ? BarHover : BarFill);
                DrawRect(bar, BarEdge, filled: false, width: 1f);
            }
            if (day.Max > 0) DrawMarker(x, ValueY(day.Max), MaxColor, pointsDown: true, _hoverDay == i && _hoverPart == Part.Max);
            if (day.Min > 0) DrawMarker(x, ValueY(day.Min), MinColor, pointsDown: false, _hoverDay == i && _hoverPart == Part.Min);
        }
    }

    private void DrawAxis(Font font, long value, float y)
    {
        DrawString(font, new Vector2(4f, y + AxisFontSize / 2f - 2f), MarketPrice.AxisLabel(value), HorizontalAlignment.Right,
            AxisWidth - 10f, AxisFontSize, UiTheme.TextLo);
    }

    private void DrawMarker(float x, float y, Color color, bool pointsDown, bool hover)
    {
        float half = hover ? MarkerHalf + 2f : MarkerHalf;
        DrawLine(new Vector2(x - half - 3f, y), new Vector2(x + half + 3f, y), color, 2f);
        float tip = pointsDown ? y - 1f : y + 1f;
        float back = pointsDown ? y - MarkerHeight - 1f : y + MarkerHeight + 1f;
        DrawColoredPolygon(new[] { new Vector2(x, tip), new Vector2(x - half, back), new Vector2(x + half, back) }, color);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseMotion motion) return;
        var (day, part) = HitTest(motion.Position);
        if (day == _hoverDay && part == _hoverPart) return;
        _hoverDay = day;
        _hoverPart = part;
        TooltipText = TipFor(day, part);
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || _hoverDay < 0) return;
        _hoverDay = -1;
        _hoverPart = Part.None;
        TooltipText = "";
        QueueRedraw();
    }

    private (int Day, Part Part) HitTest(Vector2 at)
    {
        if (!HasData) return (-1, Part.None);
        for (int i = 0; i < _days.Count && i < MarketPrice.DaysShown; i++)
        {
            float x = Column(i);
            if (Math.Abs(at.X - x) > MarkerHalf + 6f) continue;
            var day = _days[i];
            if (day.Max > 0 && Math.Abs(at.Y - ValueY(day.Max)) <= MarkerHeight + 2f) return (i, Part.Max);
            if (day.Min > 0 && Math.Abs(at.Y - ValueY(day.Min)) <= MarkerHeight + 2f) return (i, Part.Min);
            if (day.HasTrades && at.Y >= ValueY(day.Average) && at.Y <= Plot.End.Y) return (i, Part.Bar);
        }
        return (-1, Part.None);
    }

    private string TipFor(int index, Part part)
    {
        if (index < 0) return "";
        var day = _days[index];
        return part switch
        {
            Part.Bar => ValueTip(AverageLabel, day.Average),
            Part.Max => ValueTip(MaxLabel, day.Max),
            Part.Min => ValueTip(MinLabel, day.Min),
            _ => "",
        };
    }
}
