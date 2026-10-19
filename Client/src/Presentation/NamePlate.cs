using Godot;

namespace LibreKO.Presentation;

public static class NamePlate
{
    public const int FontPx = 12;
    public const int OutlinePx = 2;
    public const float SittingHeight = 0.92f;
    public const float StandingHeight = 1.70f;
    public const float StandingSelfHeight = 1.90f;
    private const string Group = "nameplate";
    private const string BaseMeta = "plate_base_px";
    private const string LineMeta = "plate_line";
    private const string TintMeta = "plate_tint";

    public static Color Outline => Colors.Black;

    public static Color Tone(Color relation) => relation;

    public static void Tint(Label3D label, Color relation)
    {
        label.SetMeta(TintMeta, relation);
        label.Modulate = Tone(relation);
    }

    public static Color TintOf(Label3D label) =>
        label.HasMeta(TintMeta) ? label.GetMeta(TintMeta).AsColor() : label.Modulate;

    private const float ReferenceViewportHeight = 1080f;
    private const float ReferenceHalfFovTan = 0.76733f;

    public static readonly Color Ally = Color.Color8(128, 128, 255);
    public static readonly Color Enemy = Color.Color8(255, 96, 96);

    public const float MapObjectRange = 80f;
    private const float MapObjectFade = 15f;

    private static readonly float Pixels =
        ReferenceHalfFovTan / (ReferenceViewportHeight * 0.5f);

    public static float ScreenPixel => Pixels;

    public static int ScaledPx(int basePx) => Scaled(basePx);

    public static Label3D MapObject(string name)
    {
        var label = Make(name, 0f);
        label.VisibilityRangeEnd = MapObjectRange;
        label.VisibilityRangeEndMargin = MapObjectFade;
        label.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
        return label;
    }

    public const int SmallFontPx = 10;
    public static float LinePx => 13f * Config.NamePlateScale;
    public const int StackPriority = 4;

    public static readonly Color TitleColor = Color.Color8(236, 217, 166);
    public static readonly Color ClanColor = Color.Color8(226, 62, 52);

    public static void Stack(Label3D label, int line)
    {
        label.SetMeta(LineMeta, line);
        label.Offset = new Vector2(0f, line * LinePx);
    }

    private const float MarkScreenPx = 16f;
    private const float MarkGapPx = 4f;

    public static Sprite3D MakeMark(Texture2D texture, Label3D name) => new()
    {
        Texture = texture,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        FixedSize = true,
        NoDepthTest = true,
        Shaded = false,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        Position = name.Position,
        RenderPriority = StackPriority,
    };

    public static readonly Color LeaderColour = new(1f, 0.84f, 0.3f);
    public static readonly Color NameMarkColour = new(0f, 1f, 0f);
    private const int NameMarkPadPx = 2;
    private const float NameMarkShiftPx = -1f;
    private const int NameMarkMargin = 1;

    public static Sprite3D? MakeNameMark(Label3D name, LibreKO.Domain.NameMarkKind kind)
    {
        if (kind == LibreKO.Domain.NameMarkKind.None) return null;
        var font = name.Font ?? ThemeDB.FallbackFont;
        int textWidth = Mathf.CeilToInt(font.GetStringSize(name.Text, HorizontalAlignment.Left, -1, name.FontSize).X);
        int textHeight = Mathf.CeilToInt(font.GetHeight(name.FontSize));
        int width = textWidth + NameMarkPadPx + OutlinePx * 2 + 1;
        int height = textHeight + (NameMarkPadPx + OutlinePx) * 2 + 1;
        int last = NameMarkMargin + width - 1, bottom = NameMarkMargin + height - 1;
        var image = Image.CreateEmpty(width + NameMarkMargin * 2, height + NameMarkMargin * 2, false, Image.Format.Rgba8);
        for (int x = NameMarkMargin; x <= last; x++) image.SetPixel(x, bottom, Colors.White);
        if (kind == LibreKO.Domain.NameMarkKind.Box)
        {
            for (int x = NameMarkMargin; x <= last; x++) image.SetPixel(x, NameMarkMargin, Colors.White);
            for (int y = NameMarkMargin; y <= bottom; y++)
            {
                image.SetPixel(NameMarkMargin, y, Colors.White);
                image.SetPixel(last, y, Colors.White);
            }
        }
        return new Sprite3D
        {
            Texture = ImageTexture.CreateFromImage(image),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            NoDepthTest = true,
            Shaded = false,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            PixelSize = Pixels,
            Position = name.Position,
            Offset = new Vector2(NameMarkShiftPx, 0f),
            Modulate = NameMarkColour,
            RenderPriority = StackPriority,
        };
    }

    public static void PlaceMark(Sprite3D mark, Label3D name, int slot = 0)
    {
        float side = MarkScreenPx * Config.NamePlateScale;
        float texels = Mathf.Max(1, mark.Texture?.GetWidth() ?? 1);
        mark.PixelSize = Pixels * side / texels;
        var font = name.Font ?? ThemeDB.FallbackFont;
        float nameWidth = font.GetStringSize(name.Text, HorizontalAlignment.Left, -1, name.FontSize).X;
        float leftPx = nameWidth * 0.5f + MarkGapPx + side * 0.5f + slot * (side + MarkGapPx);
        mark.Offset = new Vector2(-leftPx * texels / side, 0f);
        mark.Position = name.Position;
    }

    public static Label3D MakeTitle(string title, float y) => Small(title, y, TitleColor);

    public static Label3D MakeClan(string clan, float y) => Small(clan, y, ClanColor);

    public static Label3D MakeStatus(string status, float y) => Small(status, y, Colors.White);

    private static Label3D Small(string text, float y, Color color)
    {
        var label = Make(text, y);
        label.FontSize = Scaled(SmallFontPx);
        label.SetMeta(BaseMeta, SmallFontPx);
        Tint(label, color);
        return label;
    }

    public static Label3D Make(string name, float y)
    {
        var label = new Label3D
        {
            Text = name,
            Position = new Vector3(0, y, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            FontSize = Scaled(FontPx),
            PixelSize = Pixels,
            Modulate = Colors.White,
            OutlineSize = OutlinePx,
            OutlineModulate = Outline,
            NoDepthTest = true,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        };
        label.SetMeta(BaseMeta, FontPx);
        label.AddToGroup(Group);
        return label;
    }

    private static int Scaled(int basePx) => Mathf.RoundToInt(basePx * Config.NamePlateScale);

    public static event System.Action? RescaledEvent;

    public static void Rescale()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;
        foreach (var node in tree.GetNodesInGroup(Group))
        {
            if (node is not Label3D label) continue;
            if (label.HasMeta(BaseMeta)) label.FontSize = Scaled(label.GetMeta(BaseMeta).AsInt32());
            if (label.HasMeta(LineMeta)) Stack(label, label.GetMeta(LineMeta).AsInt32());
            if (label.HasMeta(TintMeta)) label.Modulate = Tone(label.GetMeta(TintMeta).AsColor());
            label.OutlineModulate = new Color(Outline, label.OutlineModulate.A);
        }
        RescaledEvent?.Invoke();
    }
}
