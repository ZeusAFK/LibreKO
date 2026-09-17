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

    private static readonly Dictionary<string, Color> Lamps = new()
    {
        ["20060222_mora_light_01"] = new Color(1.000f, 0.786f, 0.526f),
        ["20060222_mora_light_02"] = new Color(1.000f, 0.873f, 0.742f),
        ["20060222_mora_light_03"] = new Color(1.000f, 0.763f, 0.510f),
        ["070707_itemzone_2007_bill_fx_c01"] = new Color(1.000f, 0.690f, 0.401f),
    };

    private float _phase;
    private float _age;
    private float _scaleComp = 1f;

    public static bool Attach(Node3D fx, string fxName)
    {
        if (!Lamps.TryGetValue(fxName, out var warm)) return false;

        var light = new FxLampLight
        {
            Name = "LampLight",
            LightColor = warm,
            LightEnergy = BaseEnergy,
            OmniAttenuation = LampAttenuation,
            LightSpecular = LampSpecular,
            ShadowEnabled = false,
        };
        light._scaleComp = 1f / Mathf.Max(fx.Scale.X, 0.001f);
        light.OmniRange = LampRange * RangeScale * light._scaleComp;
        fx.AddChild(light);
        return true;
    }

    public override void _Ready() => _phase = GD.Randf() * Mathf.Tau;

    public override void _Process(double delta)
    {
        _age += (float)delta;
        float f = 1f + FlickerAmount * (
            Mathf.Sin(_age * FlickerSpeed + _phase) * (1f - FlickerBeatMix) +
            Mathf.Sin(_age * FlickerSpeed * FlickerBeatRatio + _phase * 1.7f) * FlickerBeatMix);
        LightEnergy = BaseEnergy * f;
        OmniRange = LampRange * RangeScale * _scaleComp;
    }
}
