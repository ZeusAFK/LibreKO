using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _fullMapLayer = null!;
    private bool _fullMapShown;
    private FullMapView _fullMapView = null!;
    private Label _fullMapTitle = null!;

    private void BuildFullMap()
    {
        _fullMapLayer = new CanvasLayer { Layer = 80, Visible = false };
        AddChild(_fullMapLayer);

        var dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.04f, 0.82f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true }) ToggleFullMap(); };
        _fullMapLayer.AddChild(dim);

        _fullMapView = new FullMapView { MouseFilter = Control.MouseFilterEnum.Ignore };
        _fullMapView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _fullMapLayer.AddChild(_fullMapView);

        _fullMapTitle = UiTheme.Heading(28, "Map");
        _fullMapTitle.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _fullMapTitle.OffsetTop = 14; _fullMapTitle.OffsetBottom = 56;
        _fullMapTitle.AddThemeConstantOverride("outline_size", 6);
        _fullMapTitle.MouseFilter = Control.MouseFilterEnum.Ignore;
        _fullMapLayer.AddChild(_fullMapTitle);

        var close = UiTheme.IconButton("✕", "Close map (M)");
        close.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        close.OffsetLeft = -52; close.OffsetTop = 16; close.OffsetRight = -18; close.OffsetBottom = 50;
        close.Pressed += ToggleFullMap;
        _fullMapLayer.AddChild(close);

        var hint = UiTheme.Text("M or Esc to close", 13, UiTheme.TextDim, HorizontalAlignment.Center);
        hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        hint.OffsetTop = -34; hint.OffsetBottom = -12;
        hint.MouseFilter = Control.MouseFilterEnum.Ignore;
        _fullMapLayer.AddChild(hint);
    }

    private void ToggleFullMap()
    {
        _fullMapShown = !_fullMapShown;
        _fullMapLayer.Visible = _fullMapShown;
        if (_fullMapShown)
        {
            _fullMapTitle.Text = MapName(_zone);
            _fullMapView.SetMap(_miniMap?.MapTexture, _miniMap?.MapExtent ?? 0f);
            UpdateFullMap();
        }
    }

    private void UpdateFullMap()
    {
        if (!_fullMapShown) return;
        float heading = Coord.KoHeading(Mathf.Sin(_camYaw), -Mathf.Cos(_camYaw));
        _fullMapView.SetView(_myKoX, _myKoZ, heading, _blipScratch);
    }

    private sealed partial class FullMapView : Control
    {
        private Texture2D? _tex;
        private float _extent;
        private float _koX, _koZ, _heading;
        private readonly List<MiniMap.Blip> _blips = new();

        public void SetMap(Texture2D? tex, float extent) { _tex = tex; _extent = extent; QueueRedraw(); }

        public void SetView(float koX, float koZ, float heading, IReadOnlyList<MiniMap.Blip> blips)
        {
            _koX = koX; _koZ = koZ; _heading = heading;
            _blips.Clear();
            _blips.AddRange(blips);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var vp = Size;
            float side = Mathf.Min(vp.X, vp.Y) - 56f;
            if (side <= 0f) return;
            var origin = (vp - new Vector2(side, side)) * 0.5f;
            var rect = new Rect2(origin, new Vector2(side, side));

            if (_tex != null)
                DrawTextureRect(_tex, rect, false);
            else
                DrawRect(rect, new Color(0.06f, 0.07f, 0.09f));
            DrawRect(rect, new Color(UiTheme.Gold, 0.85f), false, 2.5f);

            if (_extent <= 0f) return;
            float scale = side / _extent;
            Vector2 ToPx(float x, float z) => origin + new Vector2(x * scale, side - z * scale);

            foreach (var b in _blips)
            {
                var p = ToPx(b.X, b.Z);
                float r = (b.Radius + 1.5f) * 1.4f;
                if (b.Hollow)
                    DrawArc(p, r + 1.5f, 0, Mathf.Tau, 18, b.Color, 2.5f, true);
                else
                {
                    DrawCircle(p, r + 1.5f, new Color(0, 0, 0, 0.7f));
                    DrawCircle(p, r, b.Color);
                }
            }

            var pp = ToPx(_koX, _koZ);
            float a = Mathf.DegToRad(_heading);
            Vector2 Rot(Vector2 v) => new(v.X * Mathf.Cos(a) - v.Y * Mathf.Sin(a), v.X * Mathf.Sin(a) + v.Y * Mathf.Cos(a));
            var tip = pp + Rot(new Vector2(0, -14));
            var bl = pp + Rot(new Vector2(-9, 9));
            var br = pp + Rot(new Vector2(9, 9));
            var notch = pp + Rot(new Vector2(0, 4));
            DrawColoredPolygon(new[] { tip, bl, notch }, UiTheme.Self);
            DrawColoredPolygon(new[] { tip, notch, br }, UiTheme.Self.Darkened(0.18f));
            DrawPolyline(new[] { tip, bl, notch, br, tip }, new Color(0, 0, 0, 0.85f), 2f, true);
        }
    }
}
