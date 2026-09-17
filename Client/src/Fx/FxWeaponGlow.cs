using Godot;

namespace LibreKO;

public partial class FxWeaponGlow : Node3D
{
    private const int Layers = 3;
    private const int Tails = 2;
    private const float RollDegrees = 5f;
    private const float WobbleHz = 1.2f;
    private const float WobbleSpan = 0.07f;
    private const float WobbleBias = 0.035f;
    private const float ScatterBias = 0.25f;
    private const float ScatterDiv = 100f;
    private const int ScatterMod = 50;
    private const float TexPhaseMax = 10f;
    private const float ScaleFactor = 0.7f;
    private const float ShineDim = 0.3f;

    private readonly System.Collections.Generic.List<Node3D> _tails = new();
    private StandardMaterial3D[] _materials = System.Array.Empty<StandardMaterial3D>();
    private Node3D[] _meshes = System.Array.Empty<Node3D>();
    private int[] _shown = System.Array.Empty<int>();
    private Texture2D?[] _frames = System.Array.Empty<Texture2D?>();
    private bool _add;
    private int _srcBlend = 5;
    private int _destBlend = 6;
    private float _fps = 1f;
    private double _age;
    private double _texAge;
    private Vector3 _wobbleDir = Vector3.Up;
    private Aabb _bounds;

    public static Node3D? Create(string fxName, string tailFxName, string guideStem)
    {
        if (BuildShine(fxName, guideStem) is not { } root) return null;
        root.AddTails(tailFxName);
        return FxRegistry.Track(root, fxName, "weapon glow");
    }

    public static Node3D? CreateTailOnly(string tailFxName, Aabb bounds)
    {
        if (tailFxName.Length == 0 || bounds.Size == Vector3.Zero) return null;
        var root = new FxWeaponGlow { Name = $"fx_{tailFxName}_scatter", _bounds = bounds };
        root.AddTails(tailFxName);
        return root._tails.Count > 0 ? FxRegistry.Track(root, tailFxName, "weapon glow tail") : null;
    }

    private static readonly System.Collections.Generic.Dictionary<string, Mesh?> ShellCache = new();

    private static Mesh? GuideShell(string guideStem)
    {
        if (ShellCache.TryGetValue(guideStem, out var cached)) return cached;

        string guidePath = $"res://assets/items/weapon/fxguide/{guideStem}.glb";
        if (!ResourceLoader.Exists(guidePath) || ResourceLoader.Load(guidePath) is not PackedScene guideScene)
        {
            ShellCache[guideStem] = null;
            return null;
        }

        Node3D? guide = null;
        try { guide = guideScene.Instantiate<Node3D>(); }
        catch (System.InvalidOperationException e) { GD.PushWarning($"[fx] guide '{guideStem}' instantiate failed: {e.Message}"); }
        if (guide == null) return null;

        var shell = FindMesh(guide)?.Mesh;
        guide.QueueFree();
        if (shell != null) ShellCache[guideStem] = shell;
        return shell;
    }

    private static FxWeaponGlow? BuildShine(string fxName, string guideStem)
    {
        if (GuideShell(guideStem) is not { } shell || Fx.LoadDescriptor(fxName) is not { } descriptor)
            return null;

        Godot.Collections.Dictionary? part = null;
        foreach (var value in descriptor["parts"].AsGodotArray())
        {
            var candidate = value.AsGodotDictionary();
            if (candidate["type"].AsString() == "BillBoard")
            {
                part = candidate;
                break;
            }
        }
        if (part == null) return null;

        var root = new FxWeaponGlow
        {
            Name = $"fx_{fxName}_guide",
            _bounds = shell.GetAabb(),
            _materials = new StandardMaterial3D[Layers],
            _meshes = new Node3D[Layers],
            _shown = new[] { -1, -1, -1 },
        };
        var textures = part["tex"].AsGodotArray();
        root._frames = new Texture2D?[textures.Count];
        for (int i = 0; i < textures.Count; i++)
            root._frames[i] = Fx.FrameTexture(part, i);
        root._fps = Mathf.Max(0.01f, Fx.ReadF(part, "texFPS", 1f));
        root._srcBlend = Fx.SrcBlend(part);
        root._destBlend = Fx.DestBlend(part);
        root._wobbleDir = FirstNormal(shell);
        root._texAge = GD.Randf() * TexPhaseMax;

        var frame0 = root._frames.Length > 0 ? root._frames[0] : null;
        for (int i = 0; i < Layers; i++)
        {
            var layer = new Node3D { Name = $"layer{i}" };
            root._meshes[i] = layer;
            root.AddChild(layer);
            var mat = ShineState(Fx.MakeMaterial(part, BaseMaterial3D.BillboardModeEnum.Disabled, frame0));
            mat.RenderPriority = i;
            mat.AlbedoColor = new Color(ShineDim, ShineDim, ShineDim, 1f);
            root._materials[i] = mat;
            layer.AddChild(new MeshInstance3D
            {
                Name = $"shine{i}",
                Mesh = shell,
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        root._add = root._materials[0].BlendMode == BaseMaterial3D.BlendModeEnum.Add;
        return root;
    }

    private static StandardMaterial3D ShineState(StandardMaterial3D mat)
    {
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
        return mat;
    }

    private void AddTails(string tailFxName)
    {
        if (tailFxName.Length == 0 || _bounds.Size == Vector3.Zero) return;
        float scale = 1f;
        if (Fx.LoadDescriptor(tailFxName) is { } d
            && d.ContainsKey("scaleMode") && d["scaleMode"].AsInt32() != 0)
            scale = (_bounds.Size.Y + _bounds.Size.Z) * ScaleFactor;
        for (int i = 0; i < Tails; i++)
        {
            if (Fx.Spawn(tailFxName, this, ScatterPoint(), sizeScale: scale, forceAdditive: true) is { } tail)
                _tails.Add(tail);
        }
    }

    private Vector3 ScatterPoint()
    {
        var p = Vector3.Zero;
        for (int axis = 0; axis < 3; axis++)
        {
            float u = ScatterBias + GD.Randi() % ScatterMod / ScatterDiv;
            p[axis] = _bounds.Position[axis] + _bounds.Size[axis] * u;
        }
        return p;
    }

    public override void _Process(double delta)
    {
        if (Fx.ShuttingDown) return;
        _age += delta;
        _texAge += delta;

        for (int i = _tails.Count - 1; i >= 0; i--)
        {
            if (!IsInstanceValid(_tails[i])) { _tails.RemoveAt(i); continue; }
            _tails[i].Position = ScatterPoint();
        }

        if (_frames.Length == 0 || _meshes.Length < Layers) return;

        float phase = (float)(_age * WobbleHz);
        float wobble = (phase - Mathf.Floor(phase)) * WobbleSpan - WobbleBias;
        _meshes[1].Transform = new Transform3D(
            new Basis(Vector3.Up, Mathf.DegToRad(-RollDegrees)), _wobbleDir * wobble);
        _meshes[2].Transform = new Transform3D(
            new Basis(Vector3.Up, Mathf.DegToRad(RollDegrees)), _wobbleDir * -wobble);

        int frame = Mathf.PosMod((int)(_texAge * _fps), _frames.Length);
        for (int i = 0; i < Layers; i++)
        {
            int f = (frame + i) % _frames.Length;
            if (f == _shown[i]) continue;
            _shown[i] = f;
            if (_frames[f] == null) continue;
            _materials[i].AlbedoTexture = Fx.TextureForBlend(_frames[f]!, _add, _srcBlend, _destBlend);
        }
    }

    private static Vector3 FirstNormal(Mesh shell)
    {
        if (shell.GetSurfaceCount() < 1) return Vector3.Up;
        var arrays = shell.SurfaceGetArrays(0);
        int slot = (int)Mesh.ArrayType.Normal;
        if (arrays.Count <= slot || arrays[slot].VariantType != Variant.Type.PackedVector3Array)
            return Vector3.Up;
        var normals = arrays[slot].AsVector3Array();
        if (normals.Length == 0) return Vector3.Up;
        var n = normals[0];
        return n.LengthSquared() > 1e-6f ? n.Normalized() : Vector3.Up;
    }

    private static MeshInstance3D? FindMesh(Node node)
    {
        if (node is MeshInstance3D mesh) return mesh;
        foreach (var child in node.GetChildren())
            if (FindMesh(child) is { } found)
                return found;
        return null;
    }
}
