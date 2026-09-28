using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private static readonly Color WarpRowLocked = new("b50008");
    private static readonly Color WarpRowOpen = new("53cc7e");
    private static readonly Color WarpRowSelected = new("edbf72");

    private const float WarpGateInteractRange = 10f;
    private const float WarpGatePickRadius = 90f;
    private const float WarpGateTagHeight = 6.5f;
    private const float WarpGateMinHeight = 6f;
    private const int WarpImageWidth = 168;
    private const int WarpImageHeight = 193;
    private const int WarpListHeight = 331;

    private sealed class WarpGate
    {
        public ObjInfo Obj = null!;
        public Label3D Tag = null!;
        public bool TagPlaced;
    }

    private readonly List<WarpGate> _warpGates = new();
    private Node3D? _warpGateRoot;
    private WarpGate? _openGate;
    private WarpGate? _selectedGate;
    private MeshInstance3D? _gateRing;

    private CanvasLayer _warpLayer = null!;
    private HudWindow _warpPanel = null!;
    private VBoxContainer _warpList = null!;
    private ScrollContainer _warpScroll = null!;
    private TextureRect _warpImage = null!;
    private Label _warpLevels = null!;
    private Label _warpDesc = null!;
    private Label _warpGoldLbl = null!;
    private Label _warpStatus = null!;
    private Button _warpTravel = null!;
    private bool _warpShown;

    private readonly List<Net.WarpListEntry> _warpEntries = new();
    private readonly List<PanelContainer> _warpRows = new();
    private int _warpSelected = -1;
    private int _warpSourceId;

    private void WarpInit()
    {
        BuildWarpPanel();
        Net.I.WarpListEvent += OnWarpList;
        Net.I.WarpFailEvent += OnWarpFail;
        Net.I.GoldChangeEvent += OnWarpGold;
    }

    private void WarpDispose()
    {
        Net.I.WarpListEvent -= OnWarpList;
        Net.I.WarpFailEvent -= OnWarpFail;
        Net.I.GoldChangeEvent -= OnWarpGold;
    }

    private void RebuildWarpGates()
    {
        _warpGates.Clear();
        _openGate = null;
        _selectedGate = null;
        if (_gateRing != null) _gateRing.Visible = false;
        _warpGateRoot?.QueueFree();
        _warpGateRoot = null;

        foreach (var o in _objects)
        {
            if (o.EventType != Net.ObjectEventWarpGate || o.EventId <= 0) continue;
            if (_warpGateRoot == null)
            {
                _warpGateRoot = new Node3D { Name = "WarpGates" };
                AddChild(_warpGateRoot);
            }
            var tag = NamePlate.MapObject(WarpGateName(o.Belong));
            tag.Modulate = UiTheme.GoldBright;
            tag.Position = o.Origin + new Vector3(0f, WarpGateTagHeight, 0f);
            _warpGateRoot.AddChild(tag);
            _warpGates.Add(new WarpGate { Obj = o, Tag = tag });
        }
    }

    private static string WarpGateName(int belong) => belong switch
    {
        Nations.Karus => "Karus Warp Gate",
        Nations.ElMorad => "El Morad Warp Gate",
        _ => "Warp Gate",
    };

    private Aabb WarpGateVolume(WarpGate gate)
    {
        var origin = gate.Obj.Origin;
        var box = new Aabb(
            origin - new Vector3(TargetSymbol.MinRadius, 0f, TargetSymbol.MinRadius),
            new Vector3(TargetSymbol.MinRadius * 2f, WarpGateMinHeight, TargetSymbol.MinRadius * 2f));
        if (gate.Obj.Mi is { Mesh: not null } mi)
            box = box.Merge(XformAabb(mi.GlobalTransform, mi.Mesh.GetAabb()));
        return box;
    }

    private void WarpGateTick()
    {
        foreach (var gate in _warpGates)
        {
            if (gate.TagPlaced) continue;
            if (gate.Obj.Mi is { Mesh: not null }) gate.TagPlaced = true;
            var box = WarpGateVolume(gate);
            var centre = box.GetCenter();
            gate.Tag.Position = new Vector3(centre.X, box.Position.Y + box.Size.Y + 0.8f, centre.Z);
            if (ReferenceEquals(gate, _selectedGate)) UpdateWarpGateRing();
        }
    }

    private WarpGate? PickWarpGateAt(Vector2 mouse)
    {
        if (_camera == null || _warpGates.Count == 0) return null;
        var from = _camera.ProjectRayOrigin(mouse);
        var dir = _camera.ProjectRayNormal(mouse);

        WarpGate? best = null;
        float bestDist = float.MaxValue;
        foreach (var gate in _warpGates)
        {
            var anchor = gate.Tag.GlobalPosition;
            if (!RayAabbEntry(from, dir, WarpGateVolume(gate), out float hitDist))
            {
                if (_camera.IsPositionBehind(anchor)) continue;
                if (_camera.UnprojectPosition(anchor).DistanceTo(mouse) > WarpGatePickRadius) continue;
                hitDist = from.DistanceTo(anchor);
            }
            if (hitDist < bestDist) { bestDist = hitDist; best = gate; }
        }
        return best;
    }

    private bool TrySelectWarpGate(Vector2 mouse)
    {
        var gate = PickWarpGateAt(mouse);
        if (gate == null) return false;
        _selectedId = -1;
        _selfClip = null;
        StopAutoAttack();
        SelectAnvil(null);
        SelectWarpGate(gate);
        return true;
    }

    private void SelectWarpGate(WarpGate? gate)
    {
        if (ReferenceEquals(_selectedGate, gate)) return;
        if (_selectedGate != null) _selectedGate.Tag.Modulate = UiTheme.GoldBright;
        _selectedGate = gate;
        if (gate != null) gate.Tag.Modulate = Colors.White;
        UpdateWarpGateRing();
    }

    private void UpdateWarpGateRing()
    {
        if (_selectedGate == null)
        {
            if (_gateRing != null) _gateRing.Visible = false;
            return;
        }
        if (_gateRing == null)
        {
            _gateRing = new MeshInstance3D
            {
                Mesh = TargetSymbol.Mesh(),
                MaterialOverride = TargetSymbol.Material(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_gateRing);
        }
        var box = WarpGateVolume(_selectedGate);
        var origin = _selectedGate.Obj.Origin;
        TargetSymbol.Place(_gateRing, new Vector3(origin.X, origin.Y, origin.Z),
            Mathf.Max(box.Size.X, box.Size.Z) * 0.5f);
    }

    private bool TryOpenWarpGate(Vector2 mouse)
    {
        var gate = PickWarpGateAt(mouse);
        if (gate == null) return false;
        OpenWarpGate(gate);
        return true;
    }

    private void OpenWarpGate(WarpGate gate)
    {
        if (_selfDead) return;
        if (FlatDistance(_self.Position, gate.Tag.GlobalPosition) > WarpGateInteractRange) return;
        StopForInteraction();
        _openGate = gate;
        _warpSourceId = gate.Obj.NpcId;
        TryOperateObject((short)gate.Obj.EventId, (short)gate.Obj.NpcId);
    }

    private bool HasNearbyWarpGate()
    {
        if (!_worldReady || _self == null || _selfDead) return false;
        foreach (var gate in _warpGates)
            if (FlatDistance(_self.Position, gate.Tag.GlobalPosition) <= WarpGateInteractRange)
                return true;
        return false;
    }

    public bool OpenNearestWarpGate()
    {
        if (!_worldReady || _self == null || _selfDead) return false;
        WarpGate? best = null;
        float bestDist = WarpGateInteractRange;
        foreach (var gate in _warpGates)
        {
            float d = FlatDistance(_self.Position, gate.Tag.GlobalPosition);
            if (d <= bestDist) { bestDist = d; best = gate; }
        }
        if (best == null) return false;
        OpenWarpGate(best);
        return true;
    }

    private void BuildWarpPanel()
    {
        _warpLayer = new CanvasLayer { Layer = 74 };
        AddChild(_warpLayer);

        _warpPanel = new HudWindow("warp", "Warp List", new Vector2(220, 110)) { Visible = false };
        _warpPanel.Closed += CloseWarp;
        _warpLayer.AddChild(_warpPanel);

        var root = _warpPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        root.AddChild(top);

        var left = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        left.AddThemeConstantOverride("separation", 5);
        top.AddChild(left);

        var frame = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        frame.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        left.AddChild(frame);
        _warpImage = new TextureRect
        {
            CustomMinimumSize = new Vector2(WarpImageWidth, WarpImageHeight),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        frame.AddChild(_warpImage);

        var levelChip = new PanelContainer();
        levelChip.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        left.AddChild(levelChip);
        _warpLevels = UiTheme.Text("", 13, UiTheme.GoldBright, HorizontalAlignment.Center);
        levelChip.AddChild(_warpLevels);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(196, WarpListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _warpScroll = scroll;
        top.AddChild(scroll);
        _warpList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _warpList.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_warpList);

        root.AddChild(UiTheme.SectionTitle("Warning"));
        var descBox = new PanelContainer();
        descBox.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        root.AddChild(descBox);
        var descMargin = new MarginContainer();
        UiTheme.Margins(descMargin, 6);
        descBox.AddChild(descMargin);
        _warpDesc = UiTheme.Text("", 12, UiTheme.TextLo);
        _warpDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _warpDesc.CustomMinimumSize = new Vector2(384, 76);
        _warpDesc.VerticalAlignment = VerticalAlignment.Top;
        descMargin.AddChild(_warpDesc);

        root.AddChild(new HSeparator());
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        _warpGoldLbl = UiTheme.Text("", 13, UiTheme.Gold);
        _warpGoldLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(_warpGoldLbl);
        _warpStatus = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Right);
        _warpStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(_warpStatus);
        root.AddChild(footer);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        root.AddChild(buttons);
        _warpTravel = new Button { Text = "Travel", FocusMode = Control.FocusModeEnum.None };
        _warpTravel.CustomMinimumSize = new Vector2(120, 0);
        _warpTravel.Pressed += TravelSelectedWarp;
        buttons.AddChild(_warpTravel);
        var close = new Button { Text = "Close", FocusMode = Control.FocusModeEnum.None };
        close.CustomMinimumSize = new Vector2(120, 0);
        close.Pressed += CloseWarp;
        buttons.AddChild(close);
    }

    private void OnWarpList(List<Net.WarpListEntry> warps)
    {
        _warpEntries.Clear();
        foreach (var w in warps)
            if (WarpData.TryGet(w.WarpId, out _))
                _warpEntries.Add(w);

        if (_warpEntries.Count == 0)
        {
            _openGate = null;
            if (_warpShown) SetWarpStatus("No destinations available.", true);
            return;
        }

        CloseNpcDialog();
        if (_openGate == null) _warpSourceId = _vendorNpcId;
        _warpPanel.Title = _openGate != null ? _openGate.Tag.Text : _vendorNpcName;
        _warpSelected = -1;
        SetWarpStatus("", false);
        RefreshWarpGold();
        RefreshWarpList();
        SelectWarpRow(0);

        _warpPanel.Visible = true;
        _warpShown = true;
    }

    private void CloseWarp()
    {
        if (!_warpShown) return;
        _warpShown = false;
        _openGate = null;
        _warpPanel.Visible = false;
    }

    private void RefreshWarpList()
    {
        foreach (var c in _warpList.GetChildren()) c.QueueFree();
        _warpRows.Clear();
        for (int i = 0; i < _warpEntries.Count; i++)
        {
            var row = BuildWarpRow(i, _warpEntries[i]);
            _warpRows.Add(row);
            _warpList.AddChild(row);
        }
    }

    private PanelContainer BuildWarpRow(int index, Net.WarpListEntry entry)
    {
        WarpData.TryGet(entry.WarpId, out var info);

        var row = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        row.AddThemeStyleboxOverride("panel", UiTheme.Row());

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 7, 3, 7, 3);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(margin);

        var label = UiTheme.Text(info.Name, 13, WarpRowColor(info, false));
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        margin.AddChild(label);

        row.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            {
                SelectWarpRow(index);
                if (click.DoubleClick) TravelSelectedWarp();
            }
        };
        return row;
    }

    private Color WarpRowColor(WarpData.Destination info, bool selected) =>
        selected ? WarpRowSelected
        : Sheet.Level < info.MinLevel ? WarpRowLocked
        : WarpRowOpen;

    private void SelectWarpRow(int index)
    {
        if (index < 0 || index >= _warpEntries.Count) return;
        _warpSelected = index;

        for (int i = 0; i < _warpRows.Count; i++)
        {
            WarpData.TryGet(_warpEntries[i].WarpId, out var rowInfo);
            bool selected = i == index;
            _warpRows[i].AddThemeStyleboxOverride("panel", UiTheme.Row(selected));
            if (_warpRows[i].GetChild(0).GetChild(0) is Label label)
                label.AddThemeColorOverride("font_color", WarpRowColor(rowInfo, selected));
        }

        var selectedRow = _warpRows[index];
        Callable.From(() => _warpScroll.EnsureControlVisible(selectedRow)).CallDeferred();

        var entry = _warpEntries[index];
        WarpData.TryGet(entry.WarpId, out var info);
        _warpImage.Texture = WarpData.Image(info.Image);
        _warpLevels.Text = info.MinLevel > 0 || info.MaxLevel > 0
            ? $"lv{info.MinLevel}  ~  lv{info.MaxLevel}"
            : "";
        _warpDesc.Text = info.Description;
        string? blocked = WarpBlockReason(entry, info);
        SetWarpStatus(blocked ?? $"Fee  {entry.Fee:n0} Noahs", blocked != null);
        _warpTravel.Disabled = blocked != null;
    }

    private string? WarpBlockReason(Net.WarpListEntry entry, WarpData.Destination info)
    {
        if (info.MinLevel > 0 && Sheet.Level < info.MinLevel)
            return $"Requires level {info.MinLevel}.";
        if (info.MaxLevel > 0 && Sheet.Level > info.MaxLevel)
            return $"Level {info.MaxLevel} and below only.";
        if (entry.Fee > Sheet.Gold)
            return $"Need {entry.Fee:n0} Noahs.";
        return null;
    }

    private void TravelSelectedWarp()
    {
        if (!_warpShown || _selfDead) return;
        if (_warpSelected < 0 || _warpSelected >= _warpEntries.Count) return;

        var entry = _warpEntries[_warpSelected];
        WarpData.TryGet(entry.WarpId, out var info);
        if (WarpBlockReason(entry, info) is { } reason)
        {
            SetWarpStatus(reason, true);
            return;
        }

        Net.I.SendWarpSelect(_warpSourceId, entry.WarpId);
        Chat.Info(entry.Fee > 0
            ? $"Travelling to {info.Name} (−{entry.Fee:n0} Noahs)…"
            : $"Travelling to {info.Name}…");
        CloseWarp();
    }

    private void OnWarpFail()
    {
        _openGate = null;
        if (_warpShown) SetWarpStatus("The gatekeeper won't send you there.", true);
    }

    private void OnWarpGold(int total)
    {
        if (!_warpShown) return;
        RefreshWarpGold();
        SelectWarpRow(_warpSelected);
    }

    private void RefreshWarpGold() => _warpGoldLbl.Text = $"Noahs  {Sheet.Gold:n0}";

    private void SetWarpStatus(string text, bool warn)
    {
        _warpStatus.Text = text;
        _warpStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }
}
