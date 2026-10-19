using Godot;
using LibreKO.Domain;

namespace LibreKO.Plugins;

public partial class HotSlotOverlay : Control
{
    private static readonly StringName RemainParam = "remain";
    private static readonly Texture2D?[] ReadyFrames = new Texture2D?[ReadyFlash.FrameCount];
    private const float RemainEpsilon = 0.002f;

    private readonly ColorRect sweep;
    private readonly ShaderMaterial sweepMaterial;
    private readonly TextureRect ready;
    private readonly TextureRect selectedLoop;
    private float remain;
    private float shown = -1f;
    private int readyFrame = -1;
    private bool selected;
    private double selectedFor;

    public HotSlotOverlay()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        sweepMaterial = new ShaderMaterial { Shader = Shaders.Get("cooldown") };
        sweep = new ColorRect { Material = sweepMaterial, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        sweep.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(sweep);
        ready = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, ReadyFlash.Alpha),
            Visible = false,
        };
        ready.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(ready);
        selectedLoop = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        selectedLoop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(selectedLoop);
        SetProcess(false);
    }

    public float Remain
    {
        get => remain;
        set
        {
            float clamped = Mathf.Clamp(value, 0f, 1f);
            bool on = clamped > RemainEpsilon;
            if (sweep.Visible != on) sweep.Visible = on;
            if (on && Mathf.Abs(clamped - shown) > RemainEpsilon)
            {
                sweepMaterial.SetShaderParameter(RemainParam, clamped);
                shown = clamped;
            }
            remain = on ? clamped : 0f;
        }
    }

    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            selectedFor = 0;
            selectedLoop.Visible = value;
            if (value) selectedLoop.Texture = Frame(ReadyFlash.SelectedFrameAt(0));
            UpdateProcessing();
        }
    }

    public void Flash()
    {
        readyFrame = 0;
        ShowReadyFrame();
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (selected)
        {
            selectedFor += delta;
            selectedLoop.Texture = Frame(ReadyFlash.SelectedFrameAt(selectedFor));
        }
        if (readyFrame < 0) return;
        if (++readyFrame < ReadyFlash.FrameCount)
        {
            ShowReadyFrame();
            return;
        }
        readyFrame = -1;
        ready.Visible = false;
        UpdateProcessing();
    }

    private void UpdateProcessing() => SetProcess(selected || readyFrame >= 0);

    private static Texture2D? Frame(int index) =>
        ReadyFrames[index] ??= ResourceLoader.Load<Texture2D>(ReadyFlash.FramePath(index));

    private void ShowReadyFrame()
    {
        ready.Texture = Frame(readyFrame);
        ready.Visible = ready.Texture != null;
    }
}
