using Godot;

namespace LibreKO.Features.Social;

internal sealed class SpeechBubbles
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly Func<byte, Color> colorOf;

    internal SpeechBubbles(IWorldContext context, SelfSession self, Func<byte, Color> colorOf)
    {
        this.context = context;
        this.self = self;
        this.colorOf = colorOf;
    }

    private const double BubbleMinSeconds = 2.0;
    private const double BubbleSecondsPerChar = 0.2;
    private const double BubbleFade = 2.0;
    private const float BubbleRange = 4f;
    private const int BubbleFontPx = NamePlate.FontPx;
    private const int BubbleOutlinePx = 2;
    private const float BubbleWrapPx = 240f;
    private const float BubblePadXPx = 8f;
    private const float BubblePadYPx = 3f;
    private const float BubbleCornerPx = 7f;
    private const float BubbleFramePx = 1.2f;
    private const float BubbleGapPx = 5f;
    private const int BubbleMaxChars = 64;
    private const string BubbleEllipsis = "…";
    private const float BubbleBackAlpha = 0.42f;
    private const float BubbleBorderAlpha = 0.32f;
    private const float BubbleOutlineAlpha = 0.9f;
    private const int BubbleTexSide = 64;
    private const int BubbleBackPriority = 18;
    private const int BubbleBorderPriority = 19;
    private const int BubbleTextPriority = 21;
    private static readonly Color BubbleBack = new(0.04f, 0.04f, 0.06f);

    private const TextServer.LineBreakFlag BubbleBreaks =
        TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive;

    private sealed class Bubble
    {
        public Node3D Holder = null!;
        public Label3D Label = null!;
        public StandardMaterial3D BgMat = null!, BorderMat = null!;
        public double Start;
        public double Duration;
    }

    private readonly Dictionary<int, Bubble> bubbles = new();

    private readonly List<int> expiredBubbles = new();

    private static bool BubbleChannel(ChatType type) =>
        type is ChatType.General or ChatType.Shout or ChatType.GameMaster or ChatType.Merchant;

    internal void Show(int charId, ChatType type, string message)
    {
        if (!BubbleChannel(type) || message.Length == 0) return;
        Node3D? host = context.Entities.BodyOf(charId);
        if (host == null) return;
        if (charId != self.MyCharId)
        {
            Node3D? selfBody = context.Entities.BodyOf(self.MyCharId);
            if (selfBody == null || selfBody.GlobalPosition.DistanceTo(host.GlobalPosition) > BubbleRange) return;
        }
        string text = BubbleText(message);

        if (bubbles.TryGetValue(charId, out var old))
        {
            if (GodotObject.IsInstanceValid(old.Holder)) old.Holder.QueueFree();
            bubbles.Remove(charId);
        }

        Node3D anchor = host;
        var anchorAt = new Vector3(0, context.Entities.HeadHeight(charId), 0);
        int lines = 0;
        if (context.Entities.PlateOf(charId) is { } plate && GodotObject.IsInstanceValid(plate.Name) && plate.Name.GetParent() is Node3D plateRoot)
        {
            anchor = plateRoot;
            anchorAt = plate.Name.Position;
            lines = plate.Name.Visible ? plate.Lines : 0;
        }

        float px = NamePlate.ScreenPixel;
        int fontPx = NamePlate.ScaledPx(BubbleFontPx);
        var col = colorOf((byte)type);
        var holder = new Node3D { Position = anchorAt };
        var label = new Label3D
        {
            Text = text,
            Width = BubbleWrapPx,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            NoDepthTest = true,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            FontSize = fontPx,
            OutlineSize = BubbleOutlinePx,
            PixelSize = px,
            Modulate = col,
            OutlineModulate = new Color(0, 0, 0, BubbleOutlineAlpha),
            RenderPriority = BubbleTextPriority,
            OutlineRenderPriority = BubbleTextPriority - 1,
        };
        holder.AddChild(label);

        var font = label.Font ?? ThemeDB.FallbackFont;
        var textSize = font.GetMultilineStringSize(text, HorizontalAlignment.Center, BubbleWrapPx, fontPx, -1, BubbleBreaks);
        float w = textSize.X + BubblePadXPx * 2f;
        float h = textSize.Y + BubblePadYPx * 2f;
        float bottom = lines > 0 ? (lines - 0.5f) * NamePlate.LinePx + BubbleGapPx : BubbleGapPx;
        float centre = bottom + h * 0.5f;
        label.Offset = new Vector2(0f, centre);

        var bgMat = BubbleMat(new Color(BubbleBack, BubbleBackAlpha), BubbleBackPriority, BubbleFillTexture());
        var borderMat = BubbleMat(new Color(col.R, col.G, col.B, BubbleBorderAlpha), BubbleBorderPriority, BubbleRingTexture());
        holder.AddChild(new MeshInstance3D { Mesh = BubblePanel(w * px, h * px, BubbleCornerPx * px, centre * px), MaterialOverride = bgMat });
        holder.AddChild(new MeshInstance3D { Mesh = BubblePanel(w * px, h * px, BubbleCornerPx * px, centre * px), MaterialOverride = borderMat });

        anchor.AddChild(holder);
        double duration = System.Math.Max(BubbleMinSeconds, BubbleSecondsPerChar * text.Length);
        bubbles[charId] = new Bubble { Holder = holder, Label = label, BgMat = bgMat, BorderMat = borderMat, Start = context.Now, Duration = duration };
    }

    internal void Tick(double now)
    {
        if (bubbles.Count == 0) return;
        var done = expiredBubbles;
        done.Clear();
        foreach (var (id, b) in bubbles)
        {
            if (!GodotObject.IsInstanceValid(b.Holder) || !GodotObject.IsInstanceValid(b.Label))
            { done.Add(id); continue; }

            double t = now - b.Start;
            if (t >= b.Duration) { b.Holder.QueueFree(); done.Add(id); continue; }

            float a = t > b.Duration - BubbleFade
                ? Mathf.Clamp((float)((b.Duration - t) / BubbleFade), 0f, 1f) : 1f;
            var m = b.Label.Modulate; m.A = a; b.Label.Modulate = m;
            var o = b.Label.OutlineModulate; o.A = BubbleOutlineAlpha * a; b.Label.OutlineModulate = o;
            var c = b.BgMat.AlbedoColor; c.A = BubbleBackAlpha * a; b.BgMat.AlbedoColor = c;
            var e = b.BorderMat.AlbedoColor; e.A = BubbleBorderAlpha * a; b.BorderMat.AlbedoColor = e;
        }
        foreach (var id in done) bubbles.Remove(id);
    }

    internal static string BubbleText(string message)
    {
        string flat = message.Replace('\n', ' ').Trim();
        if (Domain.TextRunes.Count(flat) > BubbleMaxChars) flat = Domain.TextRunes.Truncate(flat, BubbleMaxChars).TrimEnd() + BubbleEllipsis;
        return flat;
    }

    private static StandardMaterial3D BubbleMat(Color c, int renderPriority, Texture2D texture) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = c,
        AlbedoTexture = texture,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        FixedSize = true,
        NoDepthTest = true,
        RenderPriority = renderPriority,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
    };

    private static ImageTexture? bubbleFill, bubbleRing;

    private static ImageTexture BubbleFillTexture() => bubbleFill ??= BubbleDisc(0f);

    private static ImageTexture BubbleRingTexture() => bubbleRing ??= BubbleDisc(BubbleFramePx / BubbleCornerPx * BubbleTexSide * 0.5f);

    private static ImageTexture BubbleDisc(float ring)
    {
        var img = Image.CreateEmpty(BubbleTexSide, BubbleTexSide, false, Image.Format.Rgba8);
        float radius = BubbleTexSide * 0.5f;
        for (int y = 0; y < BubbleTexSide; y++)
            for (int x = 0; x < BubbleTexSide; x++)
            {
                float d = new Vector2(x + 0.5f - radius, y + 0.5f - radius).Length();
                float a = Mathf.Clamp(radius - d + 0.5f, 0f, 1f);
                if (ring > 0f) a *= Mathf.Clamp(d - (radius - ring) + 0.5f, 0f, 1f);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        return ImageTexture.CreateFromImage(img);
    }

    private static ArrayMesh BubblePanel(float w, float h, float corner, float centreY)
    {
        float r = Mathf.Min(corner, Mathf.Min(w, h) * 0.5f);
        float[] xs = { -w * 0.5f, -w * 0.5f + r, w * 0.5f - r, w * 0.5f };
        float[] ys = { centreY + h * 0.5f, centreY + h * 0.5f - r, centreY - h * 0.5f + r, centreY - h * 0.5f };
        float[] uv = { 0f, 0.5f, 0.5f, 1f };
        var verts = new Vector3[16];
        var uvs = new Vector2[16];
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
            {
                verts[j * 4 + i] = new Vector3(xs[i], ys[j], 0f);
                uvs[j * 4 + i] = new Vector2(uv[i], uv[j]);
            }
        var index = new int[54];
        int k = 0;
        for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++)
            {
                int a = j * 4 + i;
                index[k++] = a; index[k++] = a + 1; index[k++] = a + 4;
                index[k++] = a + 1; index[k++] = a + 5; index[k++] = a + 4;
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = index;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
