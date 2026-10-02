using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const float StatuePlaqueNameY = 2.3f;
    private const float StatuePlaqueNameZ = -1.75f;
    private const float StatuePlaqueNamePixel = 0.008f;
    private const int StatuePlaqueNameFont = 32;
    private const int StatuePlaqueNameOutline = 6;
    private const float StatuePlaqueNameRange = 60f;
    private const string StatueOverheadName = " ";
    private static readonly Color StatuePlaqueNameColor = new(1f, 0.86f, 0.55f);

    private Vector3 StatueSpot(float koX, float koZ, float serverY) =>
        GroundPos(koX, koZ, serverY, RankerStatue.PedestalLift);

    internal static Label3D MakeStatuePlaque(string ranker) => new()
    {
        Name = "statue_plaque",
        Text = ranker,
        FontSize = StatuePlaqueNameFont,
        OutlineSize = StatuePlaqueNameOutline,
        PixelSize = StatuePlaqueNamePixel,
        Modulate = StatuePlaqueNameColor,
        OutlineModulate = Colors.Black,
        VisibilityRangeEnd = StatuePlaqueNameRange,
        Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(0f, StatuePlaqueNameY, StatuePlaqueNameZ)),
    };

    private void AttachStatuePlaque(Node3D body, string ranker, float koX, float koZ, float serverY)
    {
        var plaque = MakeStatuePlaque(ranker);
        var local = plaque.Transform;
        plaque.TopLevel = true;
        body.AddChild(plaque);
        plaque.GlobalTransform = new Transform3D(Basis.Identity, GroundPos(koX, koZ, serverY, 0f)) * local;
    }
}
