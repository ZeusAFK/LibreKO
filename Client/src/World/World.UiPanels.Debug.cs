using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private void SelectInfoTab(string title)
    {
        if (!_infoTabPanels.TryGetValue(title, out var chosen)) return;
        foreach (var (_, panel) in _infoTabPanels) panel.Visible = panel == chosen;
        if (_infoTabBtns.TryGetValue(title, out var btn)) btn.ButtonPressed = true;
    }

    private void BuildInfoPanel()
    {
        var layer = new CanvasLayer { Layer = 199 };
        AddChild(layer);
        _infoPanel = new PanelContainer { Visible = false };
        _infoPanel.AnchorLeft = 1; _infoPanel.AnchorTop = 0; _infoPanel.AnchorRight = 1; _infoPanel.AnchorBottom = 1;
        _infoPanel.OffsetLeft = -360; _infoPanel.OffsetTop = 0; _infoPanel.OffsetRight = 0; _infoPanel.OffsetBottom = 0;
        layer.AddChild(_infoPanel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 8);
        _infoPanel.AddChild(margin);

        var root = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 6);
        margin.AddChild(root);

        _infoTabButtons = new HFlowContainer();
        _infoTabGroup = new ButtonGroup();
        root.AddChild(_infoTabButtons);

        _infoTabContent = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddChild(_infoTabContent);

        BuildTargetTab(MakeTab("Target"));
        BuildLightTab(MakeTab("Light"));
        BuildWaterTab(MakeTab("Water"));
        BuildCapeTab(MakeTab("Cape"));
        SelectInfoTab("Target");
    }

    private VBoxContainer MakeTab(string title)
    {
        var scroll = new ScrollContainer
        {
            Name = title,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(col);
        RegisterTab(title, scroll);
        return col;
    }

    private void RegisterTab(string title, Control content)
    {
        content.Visible = false;
        _infoTabContent.AddChild(content);
        _infoTabPanels[title] = content;

        var b = new Button
        {
            Text = title,
            ToggleMode = true,
            ButtonGroup = _infoTabGroup,
            CustomMinimumSize = new Vector2(72, 28),
            FocusMode = Control.FocusModeEnum.None,
        };
        b.AddThemeStyleboxOverride("normal", UiTheme.Tab(false));
        b.AddThemeStyleboxOverride("hover", UiTheme.Tab(true));
        b.AddThemeStyleboxOverride("pressed", UiTheme.Tab(true));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeFontSizeOverride("font_size", 12);
        b.AddThemeColorOverride("font_color", UiTheme.TextLo);
        b.AddThemeColorOverride("font_hover_color", UiTheme.TextHi);
        b.AddThemeColorOverride("font_pressed_color", UiTheme.TextHi);
        b.Pressed += () => SelectInfoTab(title);
        _infoTabButtons.AddChild(b);
        _infoTabBtns[title] = b;
    }

    private void BuildTargetTab(VBoxContainer col)
    {
        var modeRow = new HBoxContainer();
        modeRow.AddThemeConstantOverride("separation", 6);
        col.AddChild(modeRow);
        var modeLbl = new Label { Text = "Pick", CustomMinimumSize = new Vector2(34, 0) };
        modeLbl.AddThemeFontSizeOverride("font_size", 12);
        modeRow.AddChild(modeLbl);
        var modeGroup = new ButtonGroup();
        foreach (var (txt, kind, tip) in new (string, PickKind, string)[]
                 {
                     ("Object", PickKind.Object, "Click inspects static world objects (buildings, props)"),
                     ("Effect", PickKind.Effect, "Click inspects placed map effects (torches, glows, portals)"),
                 })
        {
            var k = kind;
            var b = new Button
            {
                Text = txt,
                ToggleMode = true,
                ButtonGroup = modeGroup,
                ButtonPressed = _pickKind == k,
                TooltipText = tip,
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            b.Pressed += () => SetPickKind(k);
            modeRow.AddChild(b);
        }

        var candRow = new HBoxContainer();
        candRow.AddThemeConstantOverride("separation", 6);
        col.AddChild(candRow);
        var candTitle = new Label { Text = "Under", CustomMinimumSize = new Vector2(34, 0) };
        candTitle.AddThemeFontSizeOverride("font_size", 12);
        candRow.AddChild(candTitle);
        var prev = new Button { Text = "◀", FocusMode = Control.FocusModeEnum.None, TooltipText = "Previous candidate under the last click" };
        prev.Pressed += () => CyclePick(-1);
        candRow.AddChild(prev);
        _pickCandLabel = new Label
        {
            Text = "–",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _pickCandLabel.AddThemeFontSizeOverride("font_size", 12);
        candRow.AddChild(_pickCandLabel);
        var next = new Button { Text = "▶", FocusMode = Control.FocusModeEnum.None, TooltipText = "Next candidate under the last click (or click the same spot again)" };
        next.Pressed += () => CyclePick(+1);
        candRow.AddChild(next);

        _infoText = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(320, 0),
        };
        _infoText.AddThemeFontSizeOverride("normal_font_size", 13);
        col.AddChild(_infoText);

        var lodRow = new HBoxContainer();
        lodRow.AddThemeConstantOverride("separation", 6);
        col.AddChild(lodRow);
        lodRow.AddChild(new Label { Text = "LOD" });
        foreach (var (txt, bias) in new (string, float)[] { ("Full", 16f), ("Auto", 1f), ("Low", 0.05f) })
        {
            float bz = bias;
            var btn = new Button { Text = txt };
            btn.Pressed += () => SetSelectedLodBias(bz);
            lodRow.AddChild(btn);
        }
    }

    private void BuildLightTab(VBoxContainer col)
    {
        AddHeader(col, "Night balance");
        AddSlider(col, "Ambient", 0f, 0.40f, _sky.NightAmbient, 0.005f, v => _sky.NightAmbient = v);
        AddSlider(col, "Sky glow", 0f, 0.40f, _sky.NightSkyEnergy, 0.005f, v => _sky.NightSkyEnergy = v);
        AddSlider(col, "Moonlight", 0f, 2.0f, _sky.NightMoonEnergy, 0.05f, v => _sky.NightMoonEnergy = v);
        var moonClouds = AddSlider(col, "Cloud dimming", 0f, 1f, _sky.MoonCloudBlock, 0.05f,
            v => _sky.MoonCloudBlock = v);
        moonClouds.TooltipText = "How much a cloud drifting over the moon dims the moonlight. "
                               + "This is why the night brightens and darkens on its own; 0 = steady moonlight.";
        var dusk = AddSlider(col, "Dusk depth", -35f, -8f, _sky.NightAltitude, 1f,
            v => _sky.NightAltitude = v);
        dusk.TooltipText = "How far the sun must sink below the horizon before the sky is fully dark. "
                         + "Deeper = a longer, slower twilight after sunset and before dawn.";

        AddHeader(col, "Atmosphere");
        AddSlider(col, "Volumetric fog", 0f, 0.05f, _sky.VolFogDensity, 0.001f, v => _sky.VolFogDensity = v);

        AddHeader(col, "Clouds & wind");
        AddSlider(col, "Cover bias", -0.6f, 0.6f, _sky.CloudBias, 0.02f, v => _sky.CloudBias = v);
        AddSlider(col, "Density", 0f, 1f, _sky.CloudDensity, 0.02f, v => _sky.CloudDensity = v);
        AddSlider(col, "Cirrus", 0f, 1f, _sky.CirrusAmount, 0.02f, v => _sky.CirrusAmount = v);
        AddSlider(col, "Formation size", 0.00012f, 0.0012f, _sky.CloudScale, 0.00002f, v => _sky.CloudScale = v);
        AddSlider(col, "Deck height", 250f, 2500f, _sky.CloudHeight, 25f, v => _sky.CloudHeight = v);
        AddSlider(col, "Wind speed", 0f, 4f, _sky.WindSpeed, 0.05f, v => _sky.WindSpeed = v);
        AddSlider(col, "Wind bearing", 0f, 360f, _sky.WindAngleDeg, 5f, v => _sky.WindAngleDeg = v);
        AddSlider(col, "Sun blocking", 0f, 1f, _sky.SunCloudInfluence, 0.02f, v => _sky.SunCloudInfluence = v);
    }

    private void BuildWaterTab(VBoxContainer col)
    {
        if (_water == null || !_water.HasWater)
        {
            col.AddChild(new Label { Text = "No water in this zone." });
            return;
        }
        AddSlider(col, "Ripple size", 0.02f, 0.15f, 0.025f, 0.005f, v => _water.SetParam("nmap_scale", v));
        AddSlider(col, "Speed", 0f, 2.5f, 1.8f, 0.05f, v => _water.SetParam("wave_speed", v));
        AddSlider(col, "Wave height", 0f, 0.8f, 0.17f, 0.01f, v => _water.SetParam("wave_amp", v));
        AddSlider(col, "Ripple strength", 0f, 1.0f, 0.08f, 0.02f, v => _water.SetParam("normal_strength", v));
        AddSlider(col, "Transparency", 0.02f, 0.4f, 0.08f, 0.005f, v => _water.SetParam("absorption", v));
        AddSlider(col, "Roughness", 0f, 0.4f, 0.215f, 0.005f, v => _water.SetParam("rough_min", v));
        AddSlider(col, "Specular", 0f, 1.0f, 0.06f, 0.02f, v => _water.SetParam("spec_amt", v));
        AddSlider(col, "Foam width", 0f, 3.0f, 0.45f, 0.05f, v => _water.SetParam("foam_depth", v));
        AddSlider(col, "Foam amount", 0f, 1.0f, 0.18f, 0.02f, v => _water.SetParam("foam_amount", v));
    }

    private static void AddHeader(VBoxContainer col, string text)
    {
        var h = new Label { Text = text };
        h.AddThemeFontSizeOverride("font_size", 14);
        col.AddChild(h);
    }

    private static HSlider AddSlider(VBoxContainer parent, string label, float min, float max,
        float val, float step, System.Action<float> onChange)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);

        var name = new Label { Text = label, CustomMinimumSize = new Vector2(104, 0) };
        name.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(name);

        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = val,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(110, 0),
        };
        row.AddChild(slider);

        var valLbl = new Label
        {
            Text = val.ToString("0.###"),
            CustomMinimumSize = new Vector2(40, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        valLbl.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(valLbl);

        slider.ValueChanged += v =>
        {
            onChange((float)v);
            valLbl.Text = ((float)v).ToString("0.###");
        };
        return slider;
    }

    private void SetSelectedLodBias(float bias)
    {
        if (_selectedId >= 0 && _ents.TryGetValue(_selectedId, out var e))
            foreach (var mi in FindAll<MeshInstance3D>(e.Body))
                mi.LodBias = bias;
        _infoNextRebuild = 0;
    }

    private void UpdateInfoPanel()
    {
        if (!_infoShown) return;
        ulong now = Time.GetTicksMsec();
        if (now < _infoNextRebuild) return;
        _infoNextRebuild = now + 200;
        UpdateCapeStats();

        if (_pickCandLabel != null)
            _pickCandLabel.Text = CandCount == 0 ? "–" : $"{_candIdx + 1} / {CandCount}";

        if (!(_selectedId >= 0 && _ents.TryGetValue(_selectedId, out var e) && e.IsNpc))
        {
            if (_pickKind == PickKind.Effect)
            {
                _infoText.Text = _pickedFx != null
                    ? BuildFxInfoText(_pickedFx)
                    : "[b]Effect info[/b]\n\n[i]No effect picked.[/i]\n\n"
                      + "click any live effect — map ambience (torch, glow, portal),\n"
                      + "NPC role/quest indicators, model plugs, weapon glows, impacts\n"
                      + "◀ ▶ steps through overlapping effects\nPick: Object = inspect world objects instead";
                return;
            }
            if (_pickedObject != null)
            {
                _infoText.Text = BuildObjectInfoText(_pickedObject);
                return;
            }
            _infoText.Text = "[b]Target info[/b]\n\n[i]Nothing selected.[/i]\n\n"
                           + "Tab = nearest mob\nB = nearest NPC\nclick = pick mob/NPC or object\n"
                           + "◀ ▶ (or click again) = objects inside a bigger one\nH = hide this panel";
            return;
        }

        var sb = new System.Text.StringBuilder();
        void Row(string k, object v) => sb.Append($"[color=8ab4f8]{k,-9}[/color] {v}\n");

        sb.Append($"[b][font_size=16]{e.Name}[/font_size][/b]");
        sb.Append(e.Level > 0 ? $"  [color=aaaaaa]Lv {e.Level}[/color]\n\n" : "\n\n");
        Row("kind", (e.IsMonster ? "monster" : "NPC") + (e.Attackable ? "  (hostile)" : "  (talk)"));
        Row("entity", _selectedId);
        Row("npcId", $"{e.NpcId}   tNpc={e.NpcType}");
        Row("modelId", e.ModelId);
        Row("size", e.Size);
        Row("nation", e.Nation);
        Row("hp", e.MaxHp > 0 ? $"{e.Hp} / {e.MaxHp}" : "(unknown until attacked)");
        Row("model", e.ModelStem);
        Row("radius", $"ring {e.BoundRadius:F2}   footprint {e.Radius:F2}");
        Row("spawn", $"({e.SpawnX:F0}, {e.SpawnZ:F0})  y={e.SpawnY:F0}  dir={e.SpawnDir:F0}");
        Row("pos", $"({e.KoX:F0}, {e.KoZ:F0})  y={e.KoY:F0}");

        float lodBias = -1f; int tris = 0;
        foreach (var mi in FindAll<MeshInstance3D>(e.Body))
        {
            lodBias = mi.LodBias;
            var m = mi.Mesh;
            if (m != null)
                for (int s = 0; s < m.GetSurfaceCount(); s++)
                {
                    var iv = m.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Index];
                    if (iv.VariantType != Variant.Type.Nil) tris += iv.AsInt32Array().Length / 3;
                }
        }
        Row("lod", lodBias < 0 ? "(no mesh)" : $"bias={lodBias:F2}   base tris={tris}");

        var ap = e.Anim;
        if (ap == null)
        {
            sb.Append("\n[i]capsule fallback — no animation[/i]");
        }
        else
        {
            string playing = ap.CurrentAnimation;
            string cur = playing.Length > 0 ? playing : (e.Clip ?? "-");
            sb.Append($"\n[b]current anim[/b]: [color=6cf0ff]{cur}[/color]");
            if (playing.Length > 0)
                sb.Append($"  [color=888888]t={ap.CurrentAnimationPosition:F2}/{ap.CurrentAnimationLength:F2}s[/color]");
            var list = ap.GetAnimationList();
            sb.Append($"\n[b]animations[/b] ([color=aaaaaa]{list.Length}[/color])  [color=888888]name / frames / length[/color]\n");
            foreach (var name in list)
            {
                var a = ap.GetAnimation(name);
                int frames = a != null && a.GetTrackCount() > 0 ? a.TrackGetKeyCount(0) : 0;
                double len = a?.Length ?? 0.0;
                bool isCur = name == cur;
                sb.Append(isCur ? "[color=6cf0ff]> " : "  ");
                sb.Append($"{name,-22} {frames,3}f  {len,5:F2}s");
                sb.Append(isCur ? "[/color]\n" : "\n");
            }
        }
        _infoText.Text = sb.ToString();
    }

    private string BuildObjectInfoText(ObjInfo o)
    {
        var sb = new System.Text.StringBuilder();
        void Row(string k, object v) => sb.Append($"[color=8ab4f8]{k,-9}[/color] {v}\n");

        string stem = SafeModelName(o.Name);
        var mesh = o.HitMesh;
        int surfaces = mesh?.GetSurfaceCount() ?? 0;
        int tris = 0;
        if (mesh != null)
            for (int s = 0; s < surfaces; s++)
            {
                var iv = mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Index];
                if (iv.VariantType != Variant.Type.Nil) tris += iv.AsInt32Array().Length / 3;
            }
        var local = mesh?.GetAabb() ?? new Aabb();
        var worldSize = (o.HitXform.Basis * local.Size).Abs();
        var g = o.HitMesh != null ? o.HitXform.Origin : o.Origin;
        var euler = o.HitXform.Basis.GetEuler() * (180f / Mathf.Pi);

        sb.Append($"[b][font_size=16]{o.Name}[/font_size][/b]\n\n");
        Row("kind", "object");
        Row("glb", stem + ".glb");
        Row("ko pos", $"({o.KoPos.X:F0}, {o.KoPos.Z:F0})  y={o.KoPos.Y:F0}");
        Row("godot", $"({g.X:F1}, {g.Y:F1}, {g.Z:F1})");
        Row("scale", $"({o.Scale.X:F2}, {o.Scale.Y:F2}, {o.Scale.Z:F2})");
        Row("rot", $"({euler.X:F0}, {euler.Y:F0}, {euler.Z:F0})°");
        Row("size", $"{worldSize.X:F1} x {worldSize.Y:F1} x {worldSize.Z:F1}  (w·h·d)");
        Row("mesh", $"{surfaces} surf, {tris} tris");
        if (CandCount > 1)
            Row("under", $"{_candIdx + 1} of {CandCount} here  [color=888888](◀ ▶ or click again)[/color]");
        sb.Append("\n[i]click another object, or a mob/NPC (or Tab/B).[/i]");
        return sb.ToString();
    }

    private string BuildFxInfoText(FxRegistry.Entry fx)
    {
        var sb = new System.Text.StringBuilder();
        void Row(string k, object v) => sb.Append($"[color=8ab4f8]{k,-9}[/color] {v}\n");

        bool alive = GodotObject.IsInstanceValid(fx.Node);
        int particles = 0, boards = 0, meshes = 0, lamps = 0, others = 0;
        if (alive) CountParts(fx.Node);
        void CountParts(Node n)
        {
            foreach (var child in n.GetChildren())
            {
                switch (child)
                {
                    case GpuParticles3D: particles++; break;
                    case FxBillboard: boards++; break;
                    case FxMesh or MeshInstance3D: meshes++; break;
                    case Light3D: lamps++; break;
                    default: others++; break;
                }
                CountParts(child);
            }
        }

        bool isMap = _mapFxIndex.TryGetValue(fx.Node, out int mapIdx);
        BuildFxOwnerLookup();
        string? owner = alive ? FxOwnerName(fx.Node) : null;

        int copies = 0;
        foreach (var other in FxRegistry.Live) if (other.Name == fx.Name) copies++;
        var euler = (alive ? fx.Node.GlobalTransform.Basis : Basis.Identity).GetEuler() * (180f / Mathf.Pi);
        var at = alive ? fx.Node.GlobalPosition : Vector3.Zero;
        float camDist = _camera != null ? _camera.GlobalPosition.DistanceTo(at) : 0f;

        sb.Append($"[b][font_size=16]{fx.Name}[/font_size][/b]\n\n");
        Row("kind", isMap ? $"map effect #{mapIdx}" : owner != null ? $"attached effect ({fx.Kind})" : $"world effect ({fx.Kind})");
        if (owner != null) Row("on", owner);
        if (fx.Kind == "baked") Row("json", $"assets/fx/{fx.Name}.json");
        else Row("backend", $"procedural ({fx.Kind})");
        if (isMap)
        {
            var pl = _mapFx[mapIdx];
            Row("ko pos", $"({pl.KoPos.X:F0}, {pl.KoPos.Z:F0})  y={pl.KoPos.Y:F0}");
            Row("placed", $"scale {pl.Scale:0.###}");
        }
        Row("godot", $"({at.X:F1}, {at.Y:F1}, {at.Z:F1})");
        Row("rot", $"({euler.X:F0}, {euler.Y:F0}, {euler.Z:F0})°");
        Row("parts", $"{particles} particles, {boards} billboards, {meshes} mesh, {lamps} light"
                     + (others > 0 ? $", {others} other" : ""));
        Row("state", !alive ? "[color=e88]freed[/color]"
            : !fx.Node.Visible ? "asleep (culled)"
            : isMap ? "awake (pinned by this pick)" : "awake");
        Row("dist", $"{camDist:F1}u from camera");
        Row("live", $"{copies} instance{(copies == 1 ? "" : "s")} of this effect");
        if (CandCount > 1)
            Row("under", $"{_candIdx + 1} of {CandCount} here  [color=888888](◀ ▶ or click again)[/color]");
        sb.Append("\n[i]click another effect, or switch Pick to Object.[/i]");
        return sb.ToString();
    }

}
