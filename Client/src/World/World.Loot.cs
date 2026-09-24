using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private sealed class LootBox
    {
        public Node3D Node = null!;
        public Vector3 BasePos;
        public double SpawnTime;
    }

    private readonly Dictionary<int, LootBox> _boxes = new();
    private const double LootBoxLife = 60.0;
    private const float LootRangeSq = Net.LootRange * Net.LootRange;

    private int _openBundleId = -1;
    private readonly List<LootEntry> _lootEntries = new();

    private CanvasLayer _lootLayer = null!;
    private PanelContainer _lootPanel = null!;
    private Label _lootTitle = null!;
    private VBoxContainer _lootListBox = null!;
    private Label _lootPrompt = null!;

    private const string ItemBoxScenePath = "res://assets/objects/itembox_jo_a1.glb";
    private const float ItemBoxScale = 1.0f;
    private const float LootClickPickRadius = 56f;
    private PackedScene? _itemBoxScene;
    private bool _itemBoxLoaded;

    private void LootInit()
    {
        BuildLootWindow();
        BuildLootPrompt();
        Net.I.LootDropEvent += OnLootDrop;
        Net.I.LootContentsEvent += OnLootContents;
        Net.I.LootTakenEvent += OnLootTaken;
        Net.I.LootFailEvent += OnLootFail;
    }

    private void LootDispose()
    {
        Net.I.LootDropEvent -= OnLootDrop;
        Net.I.LootContentsEvent -= OnLootContents;
        Net.I.LootTakenEvent -= OnLootTaken;
        Net.I.LootFailEvent -= OnLootFail;
    }

    private void OnLootDrop(int sourceId, int bundleId)
    {
        if (_self == null) return;
        if (_boxes.ContainsKey(bundleId)) return;

        Vector3 pos;
        if (sourceId != _myId && _ents.TryGetValue(sourceId, out var e))
            pos = e.Body.Position - new Vector3(0, e.Lift, 0);
        else
            pos = _self.Position - new Vector3(0, _selfLift, 0);

        var node = BuildLootBoxNode();
        node.Position = pos;
        _entities.AddChild(node);
        _boxes[bundleId] = new LootBox { Node = node, BasePos = pos, SpawnTime = Now() };
    }

    private Node3D BuildLootBoxNode()
    {
        var root = new Node3D();

        if (!_itemBoxLoaded)
        {
            _itemBoxLoaded = true;
            if (ResourceLoader.Exists(ItemBoxScenePath))
                _itemBoxScene = ResourceLoader.Load(ItemBoxScenePath) as PackedScene;
        }

        if (_itemBoxScene?.Instantiate() is Node3D model)
        {
            model.Scale = Vector3.One * ItemBoxScale;
            root.AddChild(model);
        }
        else
        {
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.42f, 0.5f) },
                Position = new Vector3(0, 0.21f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.27f, 0.13f), Roughness = 0.7f },
            });
        }

        return root;
    }

    private void LootTick(double now)
    {
        if (_boxes.Count == 0) { if (_lootPrompt.Visible) _lootPrompt.Visible = false; return; }

        _despawnScratch.Clear();
        Vector3 me = _self != null ? _self.Position : Vector3.Zero;
        bool anyInRange = false;
        foreach (var (id, box) in _boxes)
        {
            if (now - box.SpawnTime >= LootBoxLife) { _despawnScratch.Add(id); continue; }
            if (box.BasePos.DistanceSquaredTo(me) <= LootRangeSq) anyInRange = true;
        }
        foreach (var id in _despawnScratch) DespawnBox(id);

        bool showPrompt = anyInRange && _openBundleId < 0;
        if (showPrompt != _lootPrompt.Visible)
        {
            _lootPrompt.Visible = showPrompt;
            if (showPrompt)
            {
                var vp = GetViewport().GetVisibleRect().Size;
                _lootPrompt.Position = new Vector2((vp.X - _lootPrompt.Size.X) * 0.5f, vp.Y - 132f);
            }
        }
    }

    private readonly List<int> _despawnScratch = new();

    private void DespawnBox(int bundleId)
    {
        if (_boxes.TryGetValue(bundleId, out var box))
        {
            if (GodotObject.IsInstanceValid(box.Node)) box.Node.QueueFree();
            _boxes.Remove(bundleId);
        }
        if (_openBundleId == bundleId) CloseLoot();
    }

    private bool TryClickLootBox(Vector2 mouse)
    {
        if (_self == null || _selfDead || _boxes.Count == 0 || _camera == null) return false;
        Vector3 me = _self.Position;
        int best = -1; float bestD = LootClickPickRadius;
        foreach (var (id, box) in _boxes)
        {
            var world = box.BasePos + Vector3.Up * 0.4f;
            if (_camera.IsPositionBehind(world)) continue;
            float px = _camera.UnprojectPosition(world).DistanceTo(mouse);
            if (px >= bestD) continue;
            if (box.BasePos.DistanceSquaredTo(me) > LootRangeSq) continue;
            bestD = px; best = id;
        }
        if (best < 0)
            return false;
        _openBundleId = best;
        Net.I.SendBundleOpen(best);
        return true;
    }

    private bool HasNearbyLootBox()
    {
        if (_self == null || _selfDead || _boxes.Count == 0) return false;
        Vector3 me = _self.Position;
        foreach (var (_, box) in _boxes)
            if (box.BasePos.DistanceSquaredTo(me) <= LootRangeSq)
                return true;
        return false;
    }

    public bool OpenNearestLootBox()
    {
        if (_self == null || _selfDead || _boxes.Count == 0) return false;
        Vector3 me = _self.Position;
        int best = -1;
        float bestDistSq = LootRangeSq;
        foreach (var (id, box) in _boxes)
        {
            float d = box.BasePos.DistanceSquaredTo(me);
            if (d <= bestDistSq)
            {
                bestDistSq = d;
                best = id;
            }
        }
        if (best < 0) return false;
        _openBundleId = best;
        Net.I.SendBundleOpen(best);
        return true;
    }

    private void BuildLootWindow()
    {
        _lootLayer = new CanvasLayer { Layer = 71, Visible = false };
        AddChild(_lootLayer);

        _lootPanel = new PanelContainer { Position = new Vector2(420, 150) };
        _lootLayer.AddChild(_lootPanel);

        var margin = new MarginContainer();
        foreach (var s in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{s}", 10);
        _lootPanel.AddChild(margin);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
        root.AddThemeConstantOverride("separation", 6);
        margin.AddChild(root);

        var titleRow = new HBoxContainer();
        root.AddChild(titleRow);
        _lootTitle = HudStyle.Label(17);
        _lootTitle.Text = "Loot";
        _lootTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleRow.AddChild(_lootTitle);
        var close = new Button { Text = "✕", FocusMode = Control.FocusModeEnum.None };
        close.AddThemeFontSizeOverride("font_size", 12);
        close.Pressed += CloseLoot;
        titleRow.AddChild(close);

        root.AddChild(new HSeparator());

        _lootListBox = new VBoxContainer();
        _lootListBox.AddThemeConstantOverride("separation", 4);
        root.AddChild(_lootListBox);

        root.AddChild(new HSeparator());
        var hint = HudStyle.Label(12);
        hint.Text = "Click an item to take it";
        root.AddChild(hint);

        HudLayout.Attach(
            _lootPanel,
            "loot",
            titleRow,
            () => new Vector2(420f, 150f));
    }

    private void BuildLootPrompt()
    {
        var layer = new CanvasLayer { Layer = 65 };
        AddChild(layer);
        _lootPrompt = HudStyle.Label(16, HorizontalAlignment.Center);
        _lootPrompt.Text = "Click the box to loot";
        _lootPrompt.AddThemeColorOverride("font_color", new Color("ffe08a"));
        _lootPrompt.CustomMinimumSize = new Vector2(240, 0);
        _lootPrompt.Visible = false;
        layer.AddChild(_lootPrompt);
    }

    private void OnLootContents(int bundleId, List<LootEntry> entries)
    {
        if (bundleId != _openBundleId) return;
        if (entries.Count == 0) { DespawnBox(bundleId); return; }

        _lootEntries.Clear();
        _lootEntries.AddRange(entries);
        RefreshLootWindow();
        _lootLayer.Visible = true;
    }

    private void RefreshLootWindow()
    {
        foreach (var c in _lootListBox.GetChildren()) c.QueueFree();
        for (int i = 0; i < _lootEntries.Count; i++)
            _lootListBox.AddChild(BuildLootRow(i, _lootEntries[i]));
    }

    private Control BuildLootRow(int slot, LootEntry entry)
    {
        var row = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        var rowBg = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.10f, 0.13f, 0.92f),
            BorderColor = new Color(0.34f, 0.36f, 0.42f),
        };
        rowBg.SetBorderWidthAll(1);
        rowBg.ContentMarginLeft = rowBg.ContentMarginRight = 6;
        rowBg.ContentMarginTop = rowBg.ContentMarginBottom = 4;
        row.AddThemeStyleboxOverride("panel", rowBg);

        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 8);
        row.AddChild(hb);

        bool isGold = entry.ItemId == Net.GoldItemId;
        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(40, 40),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = isGold ? null : ItemData.Icon(entry.ItemId),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hb.AddChild(icon);

        var name = HudStyle.Label(14);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Text = isGold
            ? $"Gold  x{entry.Count}"
            : entry.Count > 1 ? $"{ItemData.DisplayName(entry.ItemId)}  x{entry.Count}"
                              : ItemData.DisplayName(entry.ItemId);
        if (isGold) name.AddThemeColorOverride("font_color", new Color("ffd24a"));
        hb.AddChild(name);

        int takeSlot = slot;
        int takeItem = entry.ItemId;
        row.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                TakeLoot(takeSlot, takeItem);
        };
        return row;
    }

    private void TakeLoot(int slot, int itemId)
    {
        if (_openBundleId < 0) return;
        Net.I.SendItemGet(_openBundleId, itemId, slot);
    }

    private void OnLootTaken(int bundleId, int bundleSlot, int itemId)
    {
        if (bundleId != _openBundleId) return;
        if (bundleSlot >= 0 && bundleSlot < _lootEntries.Count) _lootEntries.RemoveAt(bundleSlot);
        // The floater comes from ItemGainedEvent, which knows the real stack delta.
        Audio.PlayUi(itemId != Net.GoldItemId ? Sfx.GetItem : Sfx.CoinGet);
        if (_lootEntries.Count == 0) { DespawnBox(bundleId); return; }
        RefreshLootWindow();
    }

    private void OnLootFail(byte code)
    {
        if (code == 7) return;
        if (_openBundleId >= 0) Net.I.SendBundleOpen(_openBundleId);
    }

    private void CloseLoot()
    {
        _openBundleId = -1;
        _lootEntries.Clear();
        if (_lootLayer != null) _lootLayer.Visible = false;
    }
}
