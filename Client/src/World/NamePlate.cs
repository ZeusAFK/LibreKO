using Godot;

namespace LibreKO;

public static class NamePlate
{
    public const int FontPx = 12;
    public const int OutlinePx = 2;
    private const string Group = "nameplate";
    private const string BaseMeta = "plate_base_px";

    private const float ReferenceViewportHeight = 1080f;
    private const float ReferenceHalfFovTan = 0.76733f;

    public static readonly Color Ally = Color.Color8(128, 128, 255);
    public static readonly Color Enemy = Color.Color8(255, 96, 96);

    public const float MapObjectRange = 80f;
    private const float MapObjectFade = 15f;

    private static readonly float Pixels =
        ReferenceHalfFovTan / (ReferenceViewportHeight * 0.5f);

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

    public static Label3D MakeTitle(string title, float y) => Small(title, y, TitleColor);

    public static Label3D MakeClan(string clan, float y) => Small(clan, y, ClanColor);

    private static Label3D Small(string text, float y, Color color)
    {
        var label = Make(text, y);
        label.FontSize = Scaled(SmallFontPx);
        label.SetMeta(BaseMeta, SmallFontPx);
        label.Modulate = color;
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
            OutlineModulate = Colors.Black,
            NoDepthTest = true,
        };
        label.SetMeta(BaseMeta, FontPx);
        label.AddToGroup(Group);
        return label;
    }

    private static int Scaled(int basePx) => Mathf.RoundToInt(basePx * Config.NamePlateScale);

    public static void Rescale()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;
        foreach (var node in tree.GetNodesInGroup(Group))
            if (node is Label3D label && label.HasMeta(BaseMeta))
                label.FontSize = Scaled(label.GetMeta(BaseMeta).AsInt32());
    }
}
