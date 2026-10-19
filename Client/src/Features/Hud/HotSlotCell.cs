using System.Globalization;
using Godot;

namespace LibreKO.Features.Hud;

internal sealed partial class HotSlotCell : VBoxContainer
{
    private readonly int slotInPage;
    private readonly GenieSession genie;
    public System.Func<int>? PageOf;
    public System.Action<int>? OnActivate;
    public System.Action<int>? OnSelect;
    public System.Action<int>? OnClear;
    public System.Action<int, int, int>? OnDrop;
    public System.Action<int, bool>? OnHover;
    private int id;
    private readonly PanelContainer slot;
    private readonly TextureRect iconRect;
    private readonly Label name;
    private readonly StyleBoxFlat background;
    private readonly ColorRect cool;
    private readonly ShaderMaterial coolMat;
    private readonly TextureRect ready;
    private int readyFrame = -1;
    private static readonly Texture2D?[] ReadyFrames = new Texture2D?[ReadyFlash.FrameCount];
    private readonly Label count;
    private readonly UpgradeBadge plus;
    private float coolShown = -1f;
    private string countText = "";
    private bool? countOk;
    private bool dim;
    private bool selected;
    private bool pressed;
    private bool dragging;

    private int Abs => (PageOf?.Invoke() ?? 0) * HotbarFeature.HotSlotsPerPage + slotInPage;

    public HotSlotCell(int slotInPage, GenieSession genie, bool showKey = true, bool keyInside = false)
    {
        this.slotInPage = slotInPage;
        this.genie = genie;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeConstantOverride("separation", 1);

        var key = HudStyle.Label(10, HorizontalAlignment.Center);
        key.Text = showKey ? HotbarLayout.KeyLabel(slotInPage) : "";
        key.AddThemeColorOverride("font_color", KeyColor);
        key.MouseFilter = MouseFilterEnum.Ignore;
        if (!keyInside) AddChild(key);

        slot = new PanelContainer
        {
            CustomMinimumSize = new Vector2(HotbarFeature.HotSlotSize, HotbarFeature.HotSlotSize),
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true,
        };
        AddChild(slot);

        background = new StyleBoxFlat { BgColor = EmptyColor, BorderColor = SlotBorder };
        background.SetBorderWidthAll(1);
        slot.AddThemeStyleboxOverride("panel", background);

        iconRect = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        iconRect.SetAnchorsPreset(LayoutPreset.FullRect);
        slot.AddChild(iconRect);

        name = HudStyle.Label(11, HorizontalAlignment.Center);
        name.MouseFilter = MouseFilterEnum.Ignore;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.SetAnchorsPreset(LayoutPreset.FullRect);
        name.VerticalAlignment = VerticalAlignment.Center;
        slot.AddChild(name);

        coolMat = new ShaderMaterial { Shader = CooldownShader };
        cool = new ColorRect { Material = coolMat, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        cool.SetAnchorsPreset(LayoutPreset.FullRect);
        slot.AddChild(cool);

        ready = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, ReadyFlash.Alpha),
            Visible = false,
        };
        ready.SetAnchorsPreset(LayoutPreset.FullRect);
        slot.AddChild(ready);
        SetProcess(false);

        count = HudStyle.Label(11, HorizontalAlignment.Right);
        count.VerticalAlignment = VerticalAlignment.Bottom;
        count.MouseFilter = MouseFilterEnum.Ignore;
        count.SizeFlagsVertical = count.SizeFlagsHorizontal = SizeFlags.Fill;
        count.SetAnchorsPreset(LayoutPreset.FullRect);
        count.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginRight = 4, ContentMarginBottom = 2 });
        slot.AddChild(count);

        plus = UpgradeBadge.Attach(slot);

        if (keyInside && showKey)
        {
            key.HorizontalAlignment = HorizontalAlignment.Left;
            key.VerticalAlignment = VerticalAlignment.Top;
            key.SizeFlagsVertical = key.SizeFlagsHorizontal = SizeFlags.Fill;
            key.AddThemeColorOverride("font_outline_color", Colors.Black);
            key.AddThemeConstantOverride("outline_size", 3);
            key.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginLeft = 3, ContentMarginTop = 1 });
            slot.AddChild(key);
        }

        MouseEntered += () => OnHover?.Invoke(Abs, true);
        MouseExited += () => OnHover?.Invoke(Abs, false);
    }

    private static readonly Color EmptyColor = new(0.07f, 0.08f, 0.10f, 0.28f);
    private static readonly Color FilledColor = new(0.07f, 0.08f, 0.10f, 0.92f);
    private static readonly Color SlotBorder = new(0.32f, 0.34f, 0.40f);
    private static readonly Color EmptyBorder = new(0.32f, 0.34f, 0.40f, 0.35f);
    public static readonly Color DimColor = new(0.5f, 0.5f, 0.5f);
    private static readonly Color ShortColor = new(1f, 0.42f, 0.36f);
    private static readonly Color KeyColor = new("6cff6c");

    public void SetDim(bool dim)
    {
        if (this.dim == dim || !GodotObject.IsInstanceValid(iconRect)) return;
        this.dim = dim;
        iconRect.Modulate = name.Modulate = dim ? DimColor : Colors.White;
    }

    public void SetSelected(bool selected)
    {
        if (this.selected == selected || !GodotObject.IsInstanceValid(slot)) return;
        this.selected = selected;
        ApplyFrame();
    }

    private void ApplyFrame()
    {
        background.BgColor = id == 0 ? EmptyColor : FilledColor;
        background.BorderColor = selected ? UiTheme.Good : id == 0 ? EmptyBorder : SlotBorder;
        background.SetBorderWidthAll(selected ? 2 : 1);
    }

    private static readonly StringName RemainParam = "remain";
    private bool coolOn;

    public void SetCooldown(float frac)
    {
        if (!GodotObject.IsInstanceValid(cool)) return;
        bool on = frac > 0.001f;
        if (coolOn != on) { coolOn = on; cool.Visible = on; }
        if (on && Mathf.Abs(frac - coolShown) > 0.002f)
        {
            coolMat.SetShaderParameter(RemainParam, frac);
            coolShown = frac;
        }
    }

    public void Flash()
    {
        if (!GodotObject.IsInstanceValid(ready)) return;
        readyFrame = 0;
        ShowReadyFrame();
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (++readyFrame < ReadyFlash.FrameCount)
        {
            ShowReadyFrame();
            return;
        }
        readyFrame = -1;
        ready.Visible = false;
        SetProcess(false);
    }

    private void ShowReadyFrame()
    {
        ReadyFrames[readyFrame] ??= ResourceLoader.Load<Texture2D>(ReadyFlash.FramePath(readyFrame));
        ready.Texture = ReadyFrames[readyFrame];
        ready.Visible = ready.Texture != null;
    }

    public void SetCount(int have, bool enough)
    {
        if (!GodotObject.IsInstanceValid(count)) return;
        string txt = have < 0 ? "" : have.ToString(CultureInfo.InvariantCulture);
        if (txt != countText) { countText = txt; count.Text = txt; }
        if (countOk != enough)
        {
            countOk = enough;
            count.AddThemeColorOverride("font_color", enough ? Colors.White : ShortColor);
        }
    }

    private static Shader CooldownShader => Shaders.Get("cooldown");

    // Icon-first like retail KO: the real skillicon/itemicon fills the slot; the name only shows as a
    // fallback caption for the (~11% of) skills with no shipped icon. Tooltip always carries the name.
    public void Set(int id)
    {
        if (!GodotObject.IsInstanceValid(this) || !GodotObject.IsInstanceValid(iconRect) || !GodotObject.IsInstanceValid(name))
            return;
        this.id = id;
        ApplyFrame();
        if (id == 0)
        {
            iconRect.Texture = null; name.Text = ""; TooltipText = "";
            plus.Clear();
            return;
        }
        if (SkillData.Get(id) is { } s)
        {
            var icon = SkillIcons.Get(s.Id);
            iconRect.Texture = icon;
            name.Text = icon == null ? s.Name : "";
            TooltipText = SkillText.Tooltip(s);
            plus.Clear();
        }
        else
        {
            iconRect.Texture = ItemIcons.Get(id);
            name.Text = "";
            TooltipText = "";
            plus.Set(id);
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (id == 0 || Config.HotbarLocked || genie.GenieRunning) return default;
        pressed = false;
        dragging = true;
        var preview = new PanelContainer { CustomMinimumSize = new Vector2(48, 40) };
        var icon = SkillData.IsSkill(id) ? SkillIcons.Get(id) : ItemIcons.Get(id);
        if (icon != null)
            preview.AddChild(new TextureRect { Texture = icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        else { var lbl = HudStyle.Label(11, HorizontalAlignment.Center); lbl.Text = name.Text; preview.AddChild(lbl); }
        DragLayer.Show(this, GmItemId.Wrap(preview, id));
        return new Godot.Collections.Dictionary { { "id", id }, { "barAbs", Abs } };
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey("id");

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var d = data.AsGodotDictionary();
        int id = d["id"].AsInt32();
        int fromAbs = d.ContainsKey("barAbs") ? d["barAbs"].AsInt32() : -1;
        OnDrop?.Invoke(Abs, id, fromAbs);
    }

    public override void _Notification(int what)
    {
        switch ((long)what)
        {
            case NotificationDragEnd:
                if (!dragging) return;
                dragging = false;
                if (!IsDragSuccessful()) OnClear?.Invoke(Abs);
                return;
            case NotificationMouseExit:
                pressed = false;
                return;
        }
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex == MouseButton.Right)
        {
            if (mb.Pressed) OnSelect?.Invoke(Abs);
            AcceptEvent();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        if (mb.Pressed) pressed = true;
        else if (pressed) { pressed = false; OnActivate?.Invoke(Abs); }
    }
}
