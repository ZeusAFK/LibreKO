using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class LootFeature : IFeature, ILateTickFeature, IEscapable, ILootBoxes
{
    private readonly IWorldContext context;
    private readonly ItemsSession items;
    private readonly LootSession loot;
    private readonly SelfSession self;
    private readonly IItemsRequests itemsRequests;
    private readonly LootFilterFeature filterWindow;
    private readonly PetFeature pet;
    private readonly EntityActionSystem entityAction;
    private readonly EntitySystem entities;

    public LootFeature(IWorldContext context, ItemsSession items, LootSession loot, SelfSession self, IItemsRequests itemsRequests, LootFilterFeature lootFilterWindow, PetFeature pet, EntityActionSystem entityAction, EntitySystem entities)
    {
        this.context = context;
        this.items = items;
        this.loot = loot;
        this.self = self;
        this.itemsRequests = itemsRequests;
        filterWindow = lootFilterWindow;
        this.pet = pet;
        this.entityAction = entityAction;
        this.entities = entities;
    }

    public void LateTick(double delta, double nowSec)
    {
        LootTick(nowSec);
    }

    private sealed class LootBox
    {
        public Node3D Node = null!;
        public Vector3 BasePos;
        public double SpawnTime;
        public int OwnerId;
        public bool Placed;
    }

    private readonly Dictionary<int, LootBox> boxes = new();

    private const double BoxLife = 60.0;
    private const float RangeSq = LootWire.Range * LootWire.Range;

    private int openBundleId = -1;
    internal List<LootEntry> Entries { get; } = new();
    private CanvasLayer layer = null!;
    internal PanelContainer Panel { get; private set; } = null!;
    private readonly ItemSlotView[] cells = new ItemSlotView[CellCount];
    private Label prompt = null!;

    private const int CellCount = 12;
    private const int Columns = 3;
    private const int Rows = 4;
    private const float CellSize = 45f;
    private const int CellGap = 6;
    private const float OpenLift = 150f;
    private const float BoxAnchorHeight = 0.4f;
    private const float ScreenMargin = 8f;
    private const string ItemBoxScenePath = "res://assets/objects/itembox_jo_a1.glb";
    private const float ItemBoxScale = 1.0f;

    private const float ClickPickRadius = 56f;
    private const int FailNoRoom = -11;
    private const int FailRetry = -7;
    private const int RetryText = 14042;
    private const int FailCaptcha = -20;
    private const int CaptchaText = 43583;

    private PackedScene? itemBoxScene;
    private bool itemBoxLoaded;

    public void Build()
    {
        entities.EntityRevived += DropBoxesOf;
        entities.EntityRemoved += DropBoxesOf;
        entityAction.AttachLootBoxes(this);
        BuildWindow();
        BuildPrompt();
        loot.LootDropEvent += OnDrop;
        loot.LootContentsEvent += OnContents;
        loot.LootTakenEvent += OnTaken;
        loot.LootFailEvent += OnFail;
        loot.LootRefusedEvent += OnRefused;
        loot.LootItemEvent += OnItem;
        loot.PartyLootEvent += OnParty;
        loot.TemporaryLootEvent += OnTemporary;
        items.ItemReceivedEvent += OnItemReceived;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (layer.Visible) Close();
        else return false;
        return true;
    }

    public void Dispose()
    {
        entities.EntityRevived -= DropBoxesOf;
        entities.EntityRemoved -= DropBoxesOf;
        entityAction.DetachLootBoxes();
        loot.LootDropEvent -= OnDrop;
        loot.LootContentsEvent -= OnContents;
        loot.LootTakenEvent -= OnTaken;
        loot.LootFailEvent -= OnFail;
        loot.LootRefusedEvent -= OnRefused;
        loot.LootItemEvent -= OnItem;
        loot.PartyLootEvent -= OnParty;
        loot.TemporaryLootEvent -= OnTemporary;
        items.ItemReceivedEvent -= OnItemReceived;
    }

    private const int ReceivedText = 6092;
    private const int ReceivedNewItemText = 7613;
    private static readonly Color ReceivedTint = Color.Color8(255, 255, 0);

    private void OnItemReceived(int itemId, bool newItem) =>
        context.Log.Add(ItemData.Text(newItem ? ReceivedNewItemText : ReceivedText, "Received %s.")
            .Replace("%s", ItemData.DisplayName(itemId)), CombatLogKind.Resource, ReceivedTint, tab: LogTab.Item);

    private const int PickedUpText = 6069;
    private const int PartyMemberGotText = 3413;
    private const int TemporaryKeptText = 43742;
    private const int TemporaryLostText = 43741;
    private static readonly Color LogTint = Color.Color8(155, 155, 255);
    private static readonly Color TemporaryKeptTint = Color.Color8(155, 237, 255);
    private static readonly Color TemporaryLostTint = Color.Color8(220, 20, 60);

    private void OnItem(int itemId) =>
        context.Log.Add(ItemData.Text(PickedUpText, "Picked up %s").Replace("%s", ItemData.DisplayName(itemId)),
            CombatLogKind.Resource, LogTint, tab: LogTab.Item);

    private void OnParty(string looter, int itemId)
    {
        string text = ItemData.Text(PartyMemberGotText, "%s got %s");
        int first = text.IndexOf("%s", System.StringComparison.Ordinal);
        if (first >= 0) text = text[..first] + looter + text[(first + 2)..];
        context.Log.Add(text.Replace("%s", ItemData.DisplayName(itemId)), CombatLogKind.Resource, LogTint);
    }

    private void OnTemporary(int itemId, bool lost) =>
        context.Log.Add(ItemData.Text(lost ? TemporaryLostText : TemporaryKeptText, "[%s]")
                .Replace("%s", ItemData.DisplayName(itemId)),
            lost ? CombatLogKind.Status : CombatLogKind.Resource, lost ? TemporaryLostTint : TemporaryKeptTint, tab: LogTab.Item);

    private void OnRefused(int bundleId, int result)
    {
        if (bundleId != openBundleId) return;
        bool quiet = autoLooting;
        if (LootOpenResult.BoxIsEmpty(result)) DespawnBox(bundleId);
        else Close();
        if (!quiet) context.Log.Notice(ItemData.Text(LootOpenResult.TextId(result), LootOpenResult.Fallback(result)));
    }

    private void OnDrop(int sourceId, int bundleId)
    {
        if (context.Self.Body == null) return;
        if (boxes.ContainsKey(bundleId)) return;
        if (sourceId == context.Self.CharId || !context.Entities.Views.TryGetValue(sourceId, out var e)) return;

        var pos = e.Body.Position - new Vector3(0, e.Lift, 0);
        var node = BuildBoxNode();
        node.Position = pos;
        node.Visible = false;
        context.Entities.Layer.AddChild(node);
        var box = new LootBox { Node = node, BasePos = pos, SpawnTime = context.Now, OwnerId = sourceId };
        boxes[bundleId] = box;
        if (e.Dead) e.CorpseRemoveAt = 0;
        TryPlaceBox(box, context.Now);
    }

    private void TryPlaceBox(LootBox box, double now)
    {
        if (box.Placed) return;
        bool known = context.Entities.Views.TryGetValue(box.OwnerId, out var owner) && GodotObject.IsInstanceValid(owner.Body);
        if (!LootBoxPlacement.Ready(known, known && owner!.Dead, now, known ? owner!.DeathClipEnd : 0)) return;
        float radius = known ? owner!.Radius : 0f;
        var feet = box.BasePos;
        if (known)
        {
            feet = owner!.Body.Position - new Vector3(0, owner.Lift, 0);
            var side = owner.Body.GlobalBasis.X;
            side.Y = 0f;
            if (side.LengthSquared() > 0f) feet += side.Normalized() * LootBoxPlacement.Offset(radius);
        }
        var at = context.Terrain.RestingSpot(feet);
        box.BasePos = at;
        box.Node.Position = at;
        box.Node.Scale = Vector3.One * LootBoxPlacement.Scale(radius);
        box.Node.Visible = true;
        box.Placed = true;
    }

    public bool HoldsBox(int entityId)
    {
        foreach (var (_, box) in boxes)
            if (box.OwnerId == entityId) return true;
        return false;
    }

    private readonly List<int> ownedBoxScratch = new();

    internal void DropBoxesOf(int entityId)
    {
        ownedBoxScratch.Clear();
        foreach (var (id, box) in boxes)
            if (box.OwnerId == entityId) ownedBoxScratch.Add(id);
        foreach (var id in ownedBoxScratch) DespawnBox(id);
    }

    private Node3D BuildBoxNode()
    {
        var root = new Node3D();

        if (!itemBoxLoaded)
        {
            itemBoxLoaded = true;
            if (ResourceLoader.Exists(ItemBoxScenePath))
                itemBoxScene = ResourceLoader.Load(ItemBoxScenePath) as PackedScene;
        }

        if (itemBoxScene?.Instantiate() is Node3D model)
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
        AutoTick();
        if (boxes.Count == 0) { if (prompt.Visible) prompt.Visible = false; return; }

        despawnScratch.Clear();
        Vector3 me = context.Self.Body != null ? context.Self.Body.Position : Vector3.Zero;
        bool anyInRange = false;
        foreach (var (id, box) in boxes)
        {
            if (now - box.SpawnTime >= BoxLife) { despawnScratch.Add(id); continue; }
            TryPlaceBox(box, now);
            if (box.Placed && box.BasePos.DistanceSquaredTo(me) <= RangeSq) anyInRange = true;
        }
        foreach (var id in despawnScratch) DespawnBox(id);

        bool showPrompt = anyInRange && openBundleId < 0;
        if (showPrompt != prompt.Visible)
        {
            prompt.Visible = showPrompt;
            if (showPrompt)
            {
                var vp = context.Root.GetViewport().GetVisibleRect().Size;
                prompt.Position = new Vector2((vp.X - prompt.Size.X) * 0.5f, vp.Y - 132f);
            }
        }
    }

    private readonly List<int> despawnScratch = new();

    private void DespawnBox(int bundleId)
    {
        if (boxes.TryGetValue(bundleId, out var box))
        {
            if (GodotObject.IsInstanceValid(box.Node)) box.Node.QueueFree();
            boxes.Remove(bundleId);
            if (context.Entities.Views.TryGetValue(box.OwnerId, out var owner) && owner.Dead && owner.IsNpc
                && owner.CorpseRemoveAt <= 0 && !HoldsBox(box.OwnerId))
                context.Entities.ReleaseCorpse(owner);
        }
        autoTried.Remove(bundleId);
        if (openBundleId == bundleId) Close();
    }

    internal bool TryClickBox(Vector2 mouse)
    {
        if (context.Self.Body == null || context.Self.Dead || boxes.Count == 0 || context.Camera == null) return false;
        Vector3 me = context.Self.Body.Position;
        int best = -1; float bestD = ClickPickRadius;
        foreach (var (id, box) in boxes)
        {
            if (!box.Placed) continue;
            var world = box.BasePos + Vector3.Up * 0.4f;
            if (context.Camera.IsPositionBehind(world)) continue;
            float px = context.Camera.UnprojectPosition(world).DistanceTo(mouse);
            if (px >= bestD) continue;
            if (box.BasePos.DistanceSquaredTo(me) > RangeSq) continue;
            bestD = px; best = id;
        }
        if (best < 0)
            return false;
        openBundleId = best;
        itemsRequests.BundleOpen(best);
        return true;
    }

    internal bool HasNearbyBox()
    {
        if (context.Self.Body == null || context.Self.Dead || boxes.Count == 0) return false;
        Vector3 me = context.Self.Body.Position;
        foreach (var (_, box) in boxes)
            if (box.Placed && box.BasePos.DistanceSquaredTo(me) <= RangeSq)
                return true;
        return false;
    }

    public bool OpenNearestBox()
    {
        if (context.Self.Body == null || context.Self.Dead || boxes.Count == 0) return false;
        Vector3 me = context.Self.Body.Position;
        int best = -1;
        float bestDistSq = RangeSq;
        foreach (var (id, box) in boxes)
        {
            if (!box.Placed) continue;
            float d = box.BasePos.DistanceSquaredTo(me);
            if (d <= bestDistSq)
            {
                bestDistSq = d;
                best = id;
            }
        }
        if (best < 0) return false;
        openBundleId = best;
        itemsRequests.BundleOpen(best);
        return true;
    }

    internal void BuildWindow()
    {
        layer = new CanvasLayer { Layer = UiLayers.HudPopups, Visible = false };
        context.Root.AddChild(layer);

        Panel = new PanelContainer();
        Panel.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        layer.AddChild(Panel);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, CellGap);
        Panel.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", CellGap);
        margin.AddChild(root);

        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", CellGap);
        grid.AddThemeConstantOverride("v_separation", CellGap);
        root.AddChild(grid);
        for (int k = 0; k < CellCount; k++)
        {
            int index = k % Columns * Rows + k / Columns;
            var cell = new ItemSlotView(CellSize) { Index = index };
            cell.Clicked += c => { if (!c.Item.IsEmpty) Take(c.Index, c.Item.ItemId); };
            cell.RightClicked += c => { if (!c.Item.IsEmpty) filterWindow.ShowRuleMenu(c.Item.ItemId); };
            cell.Hovered += c => { if (c.Item.ItemId != LootWire.GoldItemId) context.Tooltips.Show(-1, UiKit.TooltipItem(c.Item.ItemId)); };
            cell.Unhovered += _ => context.Tooltips.Hide();
            cells[index] = cell;
            grid.AddChild(cell);
        }

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", CellGap);
        var takeAll = UiTheme.SmallButton("Take all", "Take every item in the box");
        takeAll.Pressed += TakeAll;
        takeAll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actions.AddChild(takeAll);
        var filter = UiTheme.SmallButton("Filter", "Choose what auto-loot picks up");
        filter.Pressed += filterWindow.Open;
        actions.AddChild(filter);
        root.AddChild(actions);
    }

    private void BuildPrompt()
    {
        var layer = new CanvasLayer { Layer = UiLayers.HudTrackers };
        context.Root.AddChild(layer);
        prompt = HudStyle.Label(16, HorizontalAlignment.Center);
        prompt.Text = "Click the box to loot";
        prompt.AddThemeColorOverride("font_color", new Color("ffe08a"));
        prompt.CustomMinimumSize = new Vector2(240, 0);
        prompt.Visible = false;
        layer.AddChild(prompt);
    }

    private void OnContents(int bundleId, List<LootEntry> entries)
    {
        if (bundleId != openBundleId) return;
        if (entries.Count == 0) { DespawnBox(bundleId); return; }

        Entries.Clear();
        Entries.AddRange(entries);
        if (AutoNext()) return;
        RefreshWindow();
        PlaceWindow(bundleId);
        layer.Visible = true;
    }

    private void PlaceWindow(int bundleId)
    {
        var screen = context.Root.GetViewport().GetVisibleRect().Size;
        var size = Panel.GetCombinedMinimumSize();
        var at = new Vector2((screen.X - size.X) * 0.5f, (screen.Y - size.Y) * 0.5f);
        if (context.Camera != null && boxes.TryGetValue(bundleId, out var box))
        {
            var anchor = box.BasePos + Vector3.Up * BoxAnchorHeight;
            if (!context.Camera.IsPositionBehind(anchor))
                at = context.Camera.UnprojectPosition(anchor) - new Vector2(0, OpenLift);
        }
        Panel.Position = new Vector2(
            Mathf.Clamp(at.X, ScreenMargin, Mathf.Max(ScreenMargin, screen.X - size.X - ScreenMargin)),
            Mathf.Clamp(at.Y, ScreenMargin, Mathf.Max(ScreenMargin, screen.Y - size.Y - ScreenMargin)));
    }

    private void TakeAll()
    {
        if (openBundleId < 0 || Entries.Count == 0) return;
        autoLooting = true;
        AutoNext();
    }

    internal void RefreshWindow()
    {
        for (int i = 0; i < CellCount; i++)
        {
            if (i >= Entries.Count) { cells[i].Clear(); continue; }
            var entry = Entries[i];
            cells[i].Set(new ItemSlot { ItemId = entry.ItemId, Count = 1 }, entry.Count);
        }
    }

    private void Take(int slot, int itemId)
    {
        if (openBundleId < 0) return;
        itemsRequests.ItemGet(openBundleId, itemId, slot);
    }

    private void OnTaken(int bundleId, int bundleSlot, int itemId)
    {
        if (bundleId != openBundleId) return;
        int taken = bundleSlot >= 0 ? bundleSlot : Entries.FindIndex(entry => entry.ItemId == itemId);
        if (taken >= 0 && taken < Entries.Count) Entries.RemoveAt(taken);
        // The floater comes from ItemGainedEvent, which knows the real stack delta.
        Audio.PlayUi(itemId != LootWire.GoldItemId ? Sfx.GetItem : Sfx.CoinGet);
        if (Entries.Count == 0) { DespawnBox(bundleId); return; }
        if (AutoNext()) return;
        RefreshWindow();
    }

    private void OnFail(int reason)
    {
        if (reason == FailRetry)
        {
            context.Log.Notice(ItemData.Text(RetryText, "You have failed to loot. Please try again."));
            Close();
            return;
        }
        if (reason == FailCaptcha)
        {
            context.Log.Notice(ItemData.Text(CaptchaText, "Loot/Trade/Menu Usage Fail. Requires Captcha verification"));
            if (autoLooting) StopAuto();
            return;
        }
        if (reason == FailNoRoom)
        {
            context.Log.Notice(ItemData.Text(GameText.LootNoRoom, "You cannot trade or pick up items because you have either exceeded the possible quantity or the weight."));
            if (autoLooting) StopAuto();
            return;
        }
        if (autoLooting) { StopAuto(); return; }
        if (openBundleId >= 0) itemsRequests.BundleOpen(openBundleId);
    }

    internal void Close()
    {
        autoLooting = false;
        openBundleId = -1;
        Entries.Clear();
        if (layer != null && layer.Visible)
        {
            layer.Visible = false;
            context.Tooltips.Hide();
        }
    }

    private const int ItemIdsPerBase = 1000;
    private const float AutoRange = 10f;
    private const float AutoRangeSq = AutoRange * AutoRange;

    private static readonly HashSet<int> AutoFairyBases = new() { 700039, 1700039, 1700040 };

    private readonly HashSet<int> autoTried = new();
    private bool autoLooting;

    private bool WearsAutoFairy()
    {
        var fairy = self.Inventory[InventoryConstants.CosFairy];
        return !fairy.IsEmpty && AutoFairyBases.Contains(fairy.ItemId / ItemIdsPerBase);
    }

    private void AutoTick()
    {
        if (context.Self.Body == null || context.Self.Dead || openBundleId >= 0 || boxes.Count == 0 || !(WearsAutoFairy() || pet.FamiliarLoots())) return;

        var me = context.Self.Body.Position;
        foreach (var (id, box) in boxes)
        {
            if (!box.Placed || autoTried.Contains(id) || box.BasePos.DistanceSquaredTo(me) > AutoRangeSq) continue;
            autoTried.Add(id);
            autoLooting = true;
            openBundleId = id;
            itemsRequests.BundleOpen(id);
            return;
        }
    }

    private bool AutoNext()
    {
        if (!autoLooting) return false;
        if (Entries.Count == 0) return true;
        int next = Entries.FindIndex(entry => filterWindow.Takes(entry.ItemId));
        if (next < 0)
        {
            StopAuto();
            return true;
        }
        Take(next, Entries[next].ItemId);
        return true;
    }

    private void StopAuto()
    {
        if (!autoLooting) return;
        autoLooting = false;
        Close();
    }
}
