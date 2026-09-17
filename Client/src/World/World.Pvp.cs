using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private int _pvpRivalId = -1;
    private string _pvpRivalName = "";
    private string _pvpClanName = "";
    private int _pvpGauge;
    private bool _pvpFull;
    private bool _pvpMarked;

    private Node3D? _pvpHalo;
    private Color _pvpSavedNameCol;
    private bool _pvpSavedName;

    private static readonly Color PvpRivalCol = new("e0574a");

    private CanvasLayer _pvpLayer = null!;
    private PanelContainer _pvpChip = null!;
    private StyleBoxFlat _pvpChipStyle = null!;
    private Label _pvpNameLbl = null!;
    private HBoxContainer _pvpPipRow = null!;
    private const int PvpMaxGauge = 5;

    private const float PvpChipTop = 150f;

    private void PvpInit()
    {
        BuildPvpChip();
        Net.I.PvpRivalAssignedEvent += OnPvpRivalAssigned;
        Net.I.PvpRivalRemovedEvent += OnPvpRivalRemoved;
        Net.I.PvpAngerEvent += OnPvpAnger;
    }

    private void PvpDispose()
    {
        Net.I.PvpRivalAssignedEvent -= OnPvpRivalAssigned;
        Net.I.PvpRivalRemovedEvent -= OnPvpRivalRemoved;
        Net.I.PvpAngerEvent -= OnPvpAnger;
    }

    private void BuildPvpChip()
    {
        _pvpLayer = new CanvasLayer { Layer = 64, Visible = false };
        AddChild(_pvpLayer);

        _pvpChip = new PanelContainer
        {
            OffsetLeft = 16f, OffsetTop = PvpChipTop,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _pvpChipStyle = UiTheme.Chip();
        _pvpChipStyle.BorderColor = new Color(PvpRivalCol, 0.85f);
        _pvpChip.AddThemeStyleboxOverride("panel", _pvpChipStyle);
        _pvpLayer.AddChild(_pvpChip);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 4);
        _pvpChip.AddChild(col);

        _pvpNameLbl = UiTheme.Text("Rival", 13, PvpRivalCol, HorizontalAlignment.Center);
        _pvpNameLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        col.AddChild(_pvpNameLbl);

        var angerRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        angerRow.AddThemeConstantOverride("separation", 6);
        col.AddChild(angerRow);

        var angerLbl = UiTheme.Text("Anger", 11, UiTheme.TextLo, HorizontalAlignment.Left);
        angerLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        angerRow.AddChild(angerLbl);

        _pvpPipRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _pvpPipRow.AddThemeConstantOverride("separation", 3);
        angerRow.AddChild(_pvpPipRow);
        for (int i = 0; i < PvpMaxGauge; i++)
        {
            var pip = new PanelContainer
            {
                CustomMinimumSize = new Vector2(10, 10),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            pip.AddThemeStyleboxOverride("panel", PvpPipStyle(false, false));
            _pvpPipRow.AddChild(pip);
        }

        HudLayout.Attach(_pvpChip, "hud_pvp_rival", _pvpChip, () => _pvpChip.Position);
    }

    private static StyleBoxFlat PvpPipStyle(bool lit, bool full)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = lit ? (full ? new Color("ffb23c") : PvpRivalCol) : new Color(0.16f, 0.15f, 0.16f, 0.9f),
            BorderColor = new Color(PvpRivalCol, lit ? 0.95f : 0.4f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(2);
        return sb;
    }

    private void OnPvpRivalAssigned(int rivalCharId, string rivalName, string clanName)
    {
        ClearRivalMarker();

        _pvpRivalId = rivalCharId;
        _pvpRivalName = string.IsNullOrEmpty(rivalName) ? "Unknown" : rivalName;
        _pvpClanName = clanName ?? "";
        _pvpGauge = 0;
        _pvpFull = false;

        _pvpNameLbl.Text = _pvpClanName.Length > 0 ? $"Rival: {_pvpRivalName} [{_pvpClanName}]" : $"Rival: {_pvpRivalName}";
        RefreshPvpPips();
        _pvpLayer.Visible = true;

        CombatNotice($"{_pvpRivalName} is now your rival.");

        TryMarkRival();
    }

    private void OnPvpRivalRemoved()
    {
        if (_pvpRivalId >= 0)
            CombatNotice($"Your rivalry with {_pvpRivalName} has ended.");
        ClearRivalMarker();
        _pvpRivalId = -1;
        _pvpRivalName = "";
        _pvpClanName = "";
        _pvpGauge = 0;
        _pvpFull = false;
        if (_pvpLayer != null) _pvpLayer.Visible = false;
    }

    private void OnPvpAnger(int gauge, bool full)
    {
        _pvpGauge = Mathf.Clamp(gauge, 0, PvpMaxGauge);
        _pvpFull = full;
        RefreshPvpPips();
    }

    private void RefreshPvpPips()
    {
        var pips = _pvpPipRow.GetChildren();
        for (int i = 0; i < pips.Count; i++)
        {
            if (pips[i] is not PanelContainer pip) continue;
            bool lit = i < _pvpGauge;
            pip.AddThemeStyleboxOverride("panel", PvpPipStyle(lit, lit && _pvpFull));
        }
    }

    private void TryMarkRival()
    {
        if (_pvpMarked || _pvpRivalId < 0) return;
        if (!_ents.TryGetValue(_pvpRivalId, out var e) || e.Body == null) return;
        if (!GodotObject.IsInstanceValid(e.Body)) return;

        if (e.NameTag != null)
        {
            _pvpSavedNameCol = e.NameTag.Modulate;
            _pvpSavedName = true;
            e.NameTag.Modulate = PvpRivalCol;
            e.NameTag.Text = $"⚔ {e.Name}";
        }

        _pvpHalo = BuildPvpHalo();
        e.Body.AddChild(_pvpHalo);
        _pvpMarked = true;
    }

    private Node3D BuildPvpHalo()
    {
        var holder = new Node3D { Position = new Vector3(0, 2.65f, 0) };

        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.34f, OuterRadius = 0.46f, RingSegments = 24, Rings = 8 },
            RotationDegrees = new Vector3(90, 0, 0),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                DisableFog = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = new Color(PvpRivalCol, 0.85f),
                NoDepthTest = true,
                RenderPriority = 8,
            },
        };
        holder.AddChild(ring);
        return holder;
    }

    private void ClearRivalMarker()
    {
        if (_pvpHalo != null)
        {
            if (GodotObject.IsInstanceValid(_pvpHalo)) _pvpHalo.QueueFree();
            _pvpHalo = null;
        }
        if (_pvpSavedName && _pvpRivalId >= 0 && _ents.TryGetValue(_pvpRivalId, out var e) && e.NameTag != null
            && GodotObject.IsInstanceValid(e.NameTag))
        {
            e.NameTag.Modulate = _pvpSavedNameCol;
            e.NameTag.Text = e.Name;
        }
        _pvpSavedName = false;
        _pvpMarked = false;
    }

    private void PvpTick(double now)
    {
        if (_pvpRivalId < 0) return;

        if (!_pvpMarked)
        {
            TryMarkRival();
            return;
        }

        if (!_ents.ContainsKey(_pvpRivalId)
            || (_pvpHalo != null && !GodotObject.IsInstanceValid(_pvpHalo)))
        {
            if (_pvpHalo != null && !GodotObject.IsInstanceValid(_pvpHalo)) _pvpHalo = null;
            _pvpMarked = false;
            _pvpSavedName = false;
            return;
        }

        if (_pvpHalo != null && GodotObject.IsInstanceValid(_pvpHalo))
        {
            float t = (float)now;
            _pvpHalo.RotationDegrees = new Vector3(0, (t * 70f) % 360f, 0);
            float pulse = 1f + 0.12f * Mathf.Sin(t * 4f);
            _pvpHalo.Scale = new Vector3(pulse, 1f, pulse);
        }
    }
}
