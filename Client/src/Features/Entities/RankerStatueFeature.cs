using Godot;

namespace LibreKO.Features.Entities;

internal sealed class RankerStatueFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly EntitySystem entities;

    public RankerStatueFeature(IWorldContext context, EntitySystem entities)
    {
        this.context = context;
        this.entities = entities;
    }

    public void Build()
    {
        entities.EntitySpawned += OnEntitySpawned;
    }

    public void Dispose()
    {
        entities.EntitySpawned -= OnEntitySpawned;
    }

    private void OnEntitySpawned(EntityView e, EntitySnapshot info)
    {
        if (e.Statue) AttachStatuePlaque(e.Body, info.Name, info.X, info.Z, info.Y);
    }

    private const float StatuePlaqueNameY = 2.3f;
    private const float StatuePlaqueNameZ = -1.75f;
    private const float StatuePlaqueNamePixel = 0.008f;
    private const int StatuePlaqueNameFont = 32;
    private const int StatuePlaqueNameOutline = 6;
    private const float StatuePlaqueNameRange = 60f;
    internal const string StatueOverheadName = " ";
    private static readonly Color StatuePlaqueNameColor = new(1f, 0.86f, 0.55f);

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

    internal void AttachStatuePlaque(Node3D body, string ranker, float mapX, float mapZ, float serverY)
    {
        var plaque = MakeStatuePlaque(ranker);
        var local = plaque.Transform;
        plaque.TopLevel = true;
        body.AddChild(plaque);
        plaque.GlobalTransform = new Transform3D(Basis.Identity, context.Terrain.GroundPos(mapX, mapZ, serverY, 0f)) * local;
    }
}
