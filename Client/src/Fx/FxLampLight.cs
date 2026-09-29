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

    private static readonly List<FxLampLight> _all = new();
    private static readonly List<FxLampLight> _ranked = new();
    private static readonly System.Comparison<FxLampLight> ByDistance = (a, b) => a._distance2.CompareTo(b._distance2);

    private float _phase;
    private float _age;
    private float _scaleComp = 1f;
    private float _weight, _distance2;
    private bool _lit, _placed, _visible;
    private Vector3 _position;
    private FxInstance? _root;

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
        light._scaleComp = 1f / Mathf.Max(fx.Scale.X, 0.001f);
        light.OmniRange = LampRange * RangeScale * light._scaleComp;
        fx.AddChild(light);
        return true;
    }

    public override void _EnterTree()
    {
        _root = GetParent() as FxInstance;
        _all.Add(this);
    }

    public override void _ExitTree()
    {
        _all.Remove(this);
        _root = null;
        _placed = false;
    }

    public override void _Ready()
    {
        _phase = GD.Randf() * Mathf.Tau;
        SetProcess(_lit);
    }

    internal static void Rank(Vector3 camera)
    {
        _ranked.Clear();
        foreach (var lamp in _all)
        {
            if (Perf.SkipLamps || lamp._root is not { Asleep: false }) { lamp.Extinguish(); continue; }
            if (!lamp._placed) { lamp._position = lamp.GlobalPosition; lamp._placed = true; }
            lamp._distance2 = lamp._position.DistanceSquaredTo(camera);
            _ranked.Add(lamp);
        }
        _ranked.Sort(ByDistance);
        int lit = 0;
        for (int i = 0; i < _ranked.Count; i++)
        {
            bool on = i < Config.LampLightBudget;
            _ranked[i].SetLit(on);
            if (on) lit++;
        }
        Lit = lit;
    }

    private void SetLit(bool lit)
    {
        if (_lit == lit) return;
        _lit = lit;
        if (!lit) return;
        Show(true);
        SetProcess(true);
    }

    private void Extinguish()
    {
        _lit = false;
        if (_weight <= 0f && !_visible) return;
        _weight = 0f;
        LightEnergy = 0f;
        Show(false);
        SetProcess(false);
    }

    private void Show(bool visible)
    {
        if (_visible == visible) return;
        _visible = visible;
        Visible = visible;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        _weight = Mathf.MoveToward(_weight, _lit ? 1f : 0f, (float)delta / FadeSeconds);
        if (_weight <= 0f && !_lit)
        {
            LightEnergy = 0f;
            Show(false);
            SetProcess(false);
            return;
        }
        float f = 1f + FlickerAmount * (
            Mathf.Sin(_age * FlickerSpeed + _phase) * (1f - FlickerBeatMix) +
            Mathf.Sin(_age * FlickerSpeed * FlickerBeatRatio + _phase * 1.7f) * FlickerBeatMix);
        LightEnergy = BaseEnergy * f * _weight;
    }
}
