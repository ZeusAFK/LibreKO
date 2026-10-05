using System.Collections.Generic;
using Godot;

namespace LibreKO;

internal sealed partial class ChatSystem
{
    private const double BubbleDur = 6.0;
    private const double BubbleFade = 1.2;
    private const float BubbleHeadY = 2.35f;
    private const float BubbleWrap = 360f;

    private sealed class Bubble
    {
        public Node3D Holder = null!;
        public Label3D Label = null!;
        public StandardMaterial3D? BgMat, BorderMat;
        public double Start;
        public bool Sized;
    }

    private readonly Dictionary<int, Bubble> _bubbles = new();

    private readonly List<int> _expiredBubbles = new();

    private void ShowBubble(int charId, byte type, string message)
    {
        Node3D? host = _ctx.BodyOf(charId);
        if (host == null || message.Length == 0) return;

        if (_bubbles.TryGetValue(charId, out var old))
        {
            if (GodotObject.IsInstanceValid(old.Holder)) old.Holder.QueueFree();
            _bubbles.Remove(charId);
        }

        var col = _colors.ForLine(type);
        var holder = new Node3D { Position = new Vector3(0, BubbleHeadY, 0) };
        var label = new Label3D
        {
            Text = message,
            Width = BubbleWrap,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 48,
            OutlineSize = 10,
            PixelSize = 0.005f,
            Modulate = col,
            OutlineModulate = new Color(0, 0, 0, 0.95f),
            RenderPriority = 12,
        };
        holder.AddChild(label);
        host.AddChild(holder);
        _bubbles[charId] = new Bubble { Holder = holder, Label = label, Start = _ctx.Now };
    }

    internal void TickBubbles(double now)
    {
        if (_bubbles.Count == 0) return;
        var done = _expiredBubbles;
        done.Clear();
        foreach (var (id, b) in _bubbles)
        {
            if (!GodotObject.IsInstanceValid(b.Holder) || !GodotObject.IsInstanceValid(b.Label))
            { done.Add(id); continue; }

            double t = now - b.Start;
            if (t >= BubbleDur) { b.Holder.QueueFree(); done.Add(id); continue; }

            if (!b.Sized) SizeBubble(b);

            float a = t > BubbleDur - BubbleFade
                ? Mathf.Clamp((float)((BubbleDur - t) / BubbleFade), 0f, 1f) : 1f;
            var m = b.Label.Modulate; m.A = a; b.Label.Modulate = m;
            var o = b.Label.OutlineModulate; o.A = 0.95f * a; b.Label.OutlineModulate = o;
            if (b.BgMat != null) { var c = b.BgMat.AlbedoColor; c.A = 0.62f * a; b.BgMat.AlbedoColor = c; }
            if (b.BorderMat != null) { var c = b.BorderMat.AlbedoColor; c.A = 0.92f * a; b.BorderMat.AlbedoColor = c; }
        }
        foreach (var id in done) _bubbles.Remove(id);
    }

    private void SizeBubble(Bubble b)
    {
        var aabb = b.Label.GetAabb();
        if (aabb.Size.X <= 0f || aabb.Size.Y <= 0f) return;

        var cx = aabb.Position.X + aabb.Size.X / 2f;
        var cy = aabb.Position.Y + aabb.Size.Y / 2f;
        const float pad = 0.09f, frame = 0.035f;
        var col = b.Label.Modulate;

        b.BorderMat = BubbleMat(new Color(col.R, col.G, col.B, 0.92f), 10);
        b.Holder.AddChild(BubbleQuad(b.BorderMat,
            aabb.Size.X + (pad + frame) * 2f, aabb.Size.Y + (pad + frame) * 2f, cx, cy, -0.002f));

        b.BgMat = BubbleMat(new Color(0.05f, 0.05f, 0.07f, 0.62f), 11);
        b.Holder.AddChild(BubbleQuad(b.BgMat,
            aabb.Size.X + pad * 2f, aabb.Size.Y + pad * 2f, cx, cy, -0.001f));

        b.Sized = true;
    }

    private static StandardMaterial3D BubbleMat(Color c, int renderPriority) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = c,
        AlbedoTexture = BubbleTexture(),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        RenderPriority = renderPriority,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
    };

    private static ImageTexture? _bubbleTex;
    private static ImageTexture BubbleTexture()
    {
        if (_bubbleTex != null) return _bubbleTex;
        const int s = 96;
        const float r = 18f;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        float h = s * 0.5f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - h) - (h - r), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - h) - (h - r), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy) - r;
                float a = Mathf.Clamp(0.5f - dist, 0f, 1f);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        _bubbleTex = ImageTexture.CreateFromImage(img);
        return _bubbleTex;
    }

    private static MeshInstance3D BubbleQuad(Material mat, float w, float h, float cx, float cy, float z) => new()
    {
        Mesh = new QuadMesh { Size = new Vector2(w, h), CenterOffset = new Vector3(cx, cy, z) },
        MaterialOverride = mat,
    };
}
