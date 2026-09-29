using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace LibreKO;

internal static class GlyphWarmer
{
    private static readonly (long First, long Last)[] TextRanges = { (0x20, 0x7E), (0xA0, 0xFF), (0x2013, 0x2026) };
    internal static readonly (long First, long Last)[] AsciiRange = { (0x20, 0x7E) };

    private static readonly HashSet<(Rid Font, int Size, int Outline)> _warmed = new();
    private static readonly HashSet<(Font, int, int)> _scratch = new();
    private static bool _watching;

    internal static void Watch(SceneTree tree)
    {
        if (_watching) return;
        _watching = true;
        tree.NodeAdded += OnNodeAdded;
    }

    private static void OnNodeAdded(Node node)
    {
        if (node is not (Label or Button or LineEdit or RichTextLabel or Label3D) || !Unscaled(node)) return;
        _scratch.Clear();
        AddFor(node, _scratch);
        foreach (var (font, size, outline) in _scratch) Render(font, size, outline, TextRanges);
    }

    internal static int Warm(Node root)
    {
        if (!Unscaled(root)) return 0;
        long started = Stopwatch.GetTimestamp();
        var wanted = new HashSet<(Font Font, int Size, int Outline)>();
        Collect(root, wanted);
        int rendered = 0;
        foreach (var (font, size, outline) in wanted) rendered += Render(font, size, outline, TextRanges);
        if (rendered > 0)
            GD.Print($"[glyphs] {root.Name}: {rendered} glyph caches in {(Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency:0} ms");
        return rendered;
    }

    internal static bool Unscaled(Node node) =>
        node.GetViewport() is not { } viewport || Mathf.IsEqualApprox(viewport.GetOversampling(), 1f);

    internal static int Render(Font? font, int size, int outline, (long First, long Last)[] ranges)
    {
        if (font == null || size <= 0) return 0;
        var server = TextServerManager.GetPrimaryInterface();
        int rendered = 0;
        foreach (var rid in font.GetRids())
        {
            if (!_warmed.Add((rid, size, outline))) continue;
            foreach (var (first, last) in ranges) server.FontRenderRange(rid, new Vector2I(size, outline), first, last);
            rendered++;
        }
        return rendered;
    }

    private static void Add(HashSet<(Font, int, int)> wanted, Font? font, int size, int outline, int shadowOutline)
    {
        if (font == null || size <= 0) return;
        wanted.Add((font, size, 0));
        if (outline > 0) wanted.Add((font, size, outline));
        if (shadowOutline > 0) wanted.Add((font, size, shadowOutline));
    }

    private static int ShadowOutline(Control control) =>
        control.GetThemeColor("font_shadow_color").A > 0f ? control.GetThemeConstant("shadow_outline_size") : 0;

    private static void AddFor(Node node, HashSet<(Font, int, int)> wanted)
    {
        switch (node)
        {
            case Label { LabelSettings: { } settings } label:
                Add(wanted, settings.Font ?? label.GetThemeFont("font"), settings.FontSize, settings.OutlineSize,
                    settings.ShadowColor.A > 0f ? settings.ShadowSize : 0);
                break;
            case Label or Button or LineEdit:
                var control = (Control)node;
                Add(wanted, control.GetThemeFont("font"), control.GetThemeFontSize("font_size"),
                    control.GetThemeConstant("outline_size"), ShadowOutline(control));
                break;
            case RichTextLabel rich:
                int richOutline = rich.GetThemeConstant("outline_size");
                Add(wanted, rich.GetThemeFont("normal_font"), rich.GetThemeFontSize("normal_font_size"), richOutline, 0);
                Add(wanted, rich.GetThemeFont("bold_font"), rich.GetThemeFontSize("bold_font_size"), richOutline, 0);
                break;
            case Label3D plate:
                Add(wanted, plate.Font ?? ThemeDB.FallbackFont, plate.FontSize, plate.OutlineSize, 0);
                break;
        }
    }

    private static void Collect(Node node, HashSet<(Font, int, int)> wanted)
    {
        AddFor(node, wanted);
        foreach (var child in node.GetChildren()) Collect(child, wanted);
    }
}
