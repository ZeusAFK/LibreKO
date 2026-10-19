using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class FxLampLight : OmniLight3D
{
    public static float BaseEnergy = 4.0f;
    public static float RangeScale = 0.9f;

    private const float LampRange = 20f;
    private const float LampAttenuation = 1.0f;
    private const float LampSpecular = 0.25f;
    private const float FlickerAmount = 0.06f;
    private const float FlickerSpeed = 7.0f;
    private const float FlickerBeatRatio = 2.3f;
    private const float FlickerBeatMix = 0.4f;
    private const float FadeSeconds = 0.4f;

    private static readonly Dictionary<string, Color> Lamps = new()
    {
        ["20060222_mora_light_01"] = new Color(1.000f, 0.786f, 0.526f),
        ["20060222_mora_light_02"] = new Color(1.000f, 0.873f, 0.742f),
        ["20060222_mora_light_03"] = new Color(1.000f, 0.763f, 0.510f),
        ["070707_itemzone_2007_bill_fx_c01"] = new Color(1.000f, 0.690f, 0.401f),
    };

    private static readonly List<FxLampLight> all = new();
    private static readonly List<FxLampLight> ranked = new();
    private static readonly System.Comparison<FxLampLight> ByDistance = (a, b) => a.distance2.CompareTo(b.distance2);

    private float phase;
    private float age;
    private float scaleComp = 1f;
    private float weight, distance2;
    private bool lit, placed, visible;
    private Vector3 position;
    private FxInstance? root;

    internal static int Lit { get; private set; }

    public static bool Attach(Node3D fx, string fxName)
    {
        if (!Lamps.TryGetValue(fxName, out var warm)) return false;

        var light = new FxLampLight
        {
            Name = "LampLight",
            LightColor = warm,
            LightEnergy = 0f,
            OmniAttenuation = LampAttenuation,
            LightSpecular = LampSpecular,
            ShadowEnabled = false,
            Visible = false,
        };
        light.scaleComp = 1f / Mathf.Max(fx.Scale.X, 0.001f);
        light.OmniRange = LampRange * RangeScale * light.scaleComp;
        fx.AddChild(light);
        return true;
    }

    public override void _EnterTree()
    {
        root = GetParent() as FxInstance;
        all.Add(this);
    }

    public override void _ExitTree()
    {
        all.Remove(this);
        root = null;
        placed = false;
    }

    public override void _Ready()
    {
        phase = GD.Randf() * Mathf.Tau;
        SetProcess(lit);
    }

    internal static void Rank(Vector3 camera)
    {
        ranked.Clear();
        foreach (var lamp in all)
        {
            if (Perf.SkipLamps || lamp.root is not { Asleep: false }) { lamp.Extinguish(); continue; }
            if (!lamp.placed) { lamp.position = lamp.GlobalPosition; lamp.placed = true; }
            lamp.distance2 = lamp.position.DistanceSquaredTo(camera);
            ranked.Add(lamp);
        }
        ranked.Sort(ByDistance);
        int litCount = 0;
        for (int i = 0; i < ranked.Count; i++)
        {
            bool on = i < Config.LampLightBudget;
            ranked[i].SetLit(on);
            if (on) litCount++;
        }
        Lit = litCount;
    }

    private void SetLit(bool lit)
    {
        if (this.lit == lit) return;
        this.lit = lit;
        if (!lit) return;
        Show(true);
        SetProcess(true);
    }

    private void Extinguish()
    {
        lit = false;
        if (weight <= 0f && !visible) return;
        weight = 0f;
        LightEnergy = 0f;
        Show(false);
        SetProcess(false);
    }

    private void Show(bool visible)
    {
        if (this.visible == visible) return;
        this.visible = visible;
        Visible = visible;
    }

    public override void _Process(double delta)
    {
        age += (float)delta;
        weight = Mathf.MoveToward(weight, lit ? 1f : 0f, (float)delta / FadeSeconds);
        if (weight <= 0f && !lit)
        {
            LightEnergy = 0f;
            Show(false);
            SetProcess(false);
            return;
        }
        float f = 1f + FlickerAmount * (
            Mathf.Sin(age * FlickerSpeed + phase) * (1f - FlickerBeatMix) +
            Mathf.Sin(age * FlickerSpeed * FlickerBeatRatio + phase * 1.7f) * FlickerBeatMix);
        LightEnergy = BaseEnergy * f * weight;
    }
}
