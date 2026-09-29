using Godot;
using System.Collections.Generic;

namespace LibreKO;

public partial class ItemShineLight : OmniLight3D
{
    public const uint SceneryLayer = 1u << 18;
    private readonly List<ItemShineDriver> _sources = new();
    private Vector3 _lastPosition = new(float.NaN, float.NaN, float.NaN);
    private float _lastRange = float.NaN, _lastEnergy = float.NaN;

    private static readonly List<ItemShineLight> _all = new();
    private static readonly System.Comparison<ItemShineLight> ByCamDist = (a, b) => a._camDist2.CompareTo(b._camDist2);
    private static ulong _rankFrame = ulong.MaxValue;
    private float _camDist2;
    private int _rank;

    public override void _EnterTree() => _all.Add(this);

    public override void _ExitTree() => _all.Remove(this);

    private void RankAll()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame == _rankFrame) return;
        _rankFrame = frame;
        Vector3 eye = Fx.FrameCamera(this, out var camera) ? camera.Origin : Vector3.Zero;
        foreach (var light in _all)
            light._camDist2 = float.IsNaN(light._lastPosition.X) ? float.MaxValue : light._lastPosition.DistanceSquaredTo(eye);
        _all.Sort(ByCamDist);
        for (int i = 0; i < _all.Count; i++) _all[i]._rank = i;
    }

    public static void Refresh(Node3D body, bool shadow = false)
    {
        ItemShineLight? light = null;
        foreach (var child in body.GetChildren())
            if (child is ItemShineLight existing) { light = existing; break; }
        var sources = new List<ItemShineDriver>();
        Collect(body, sources);
        if (sources.Count == 0)
        {
            if (light != null) { body.RemoveChild(light); light.QueueFree(); }
            return;
        }
        if (light == null)
        {
            light = new ItemShineLight
            {
                Name = "equipment_light",
                TopLevel = true,
                LightColor = new Color(1f, 0.84f, 0.57f),
                LightSpecular = 0f,
                LightCullMask = SceneryLayer,
                ShadowCasterMask = SceneryLayer,
                OmniAttenuation = 1.3f,
                DistanceFadeEnabled = true,
                DistanceFadeBegin = 24f,
                DistanceFadeLength = 10f,
                DistanceFadeShadow = 16f,
            };
            body.AddChild(light);
        }
        light.ShadowEnabled = shadow && Config.Shadows;
        light._sources.Clear();
        light._sources.AddRange(sources);
    }

    private static void Collect(Node node, List<ItemShineDriver> sources)
    {
        if (node is FxInstance or FxWeaponGlow) return;
        if (node is ItemShineDriver shine && shine.Level > 0) sources.Add(shine);
        foreach (var child in node.GetChildren()) Collect(child, sources);
    }

    public static void MarkScenery(Node node)
    {
        if (node is VisualInstance3D visual) visual.Layers |= SceneryLayer;
        foreach (var child in node.GetChildren()) MarkScenery(child);
    }

    public override void _Process(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.Shine);
        UpdateLight(Time.GetTicksMsec() * 0.001);
    }

    internal void UpdateLight(double seconds)
    {
        if (GetParent() is not Node3D body) return;
        Vector3 position = body.GlobalPosition + Vector3.Up * 1.6f;
        if (_lastPosition != position) { GlobalPosition = position; _lastPosition = position; }
        RankAll();
        if (_rank >= Config.ShineLightBudget)
        {
            if (Visible) Visible = false;
            return;
        }
        ItemShineDriver? strongest = null;
        float visibility = 0f;
        foreach (var source in _sources)
        {
            if (!IsInstanceValid(source) || source.IsQueuedForDeletion()
                || source.GetParent() is not MeshInstance3D part) continue;
            var shown = CharacterMerge.ShownAs(part);
            if (!shown.IsVisibleInTree()) continue;
            float alpha = 1f - shown.Transparency;
            if (alpha <= 0f) continue;
            if (strongest == null || source.Level > strongest.Level
                || (source.Level == strongest.Level && source.PartIndex < strongest.PartIndex))
            {
                strongest = source;
                visibility = alpha;
            }
        }
        int level = strongest?.Level ?? 0;
        bool visible = level > 0 && body.IsVisibleInTree();
        if (Visible != visible) Visible = visible;
        if (!visible) return;
        float range = level switch { 1 => 3.5f, 2 => 5f, 3 => 6.5f, _ => 8f };
        if (_lastRange != range) { OmniRange = range; _lastRange = range; }
        float pulse = ItemShine.Envelope(level, seconds, strongest!.PartIndex) / ItemShine.PeakFor(level);
        float energy = visibility * pulse * (level switch { 1 => 0.7f, 2 => 1.1f, 3 => 1.6f, _ => 2.2f });
        if (_lastEnergy != energy) { LightEnergy = energy; _lastEnergy = energy; }
    }
}
