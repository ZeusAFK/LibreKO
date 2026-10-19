using Godot;

namespace LibreKO.Features.Windows;

internal sealed class MerchantStallsFeature : IFeature, ILateTickFeature, IStallOwners
{
    private readonly IWorldContext context;
    private readonly MerchantSession merchant;
    private readonly IMerchantRequests requests;
    private readonly EntitySystem entities;
    private readonly EntityPlatesSystem entityPlates;

    public MerchantStallsFeature(IWorldContext context, MerchantSession merchant, IMerchantRequests requests, EntitySystem entities, EntityPlatesSystem entityPlates)
    {
        this.context = context;
        this.merchant = merchant;
        this.requests = requests;
        this.entities = entities;
        this.entityPlates = entityPlates;
    }

    public void LateTick(double delta, double nowSec) => StallSignTick();

    public void Build()
    {
        entityPlates.AttachStallOwners(this);
        entities.EntitySpawned += OnEntitySpawned;
        entities.EntityRemoved += ForgetStall;
        stallSignCanvas = new CanvasLayer { Layer = StallSignLayerIndex };
        context.Root.AddChild(stallSignCanvas);
        stallSignLayer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        stallSignLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        stallSignCanvas.AddChild(stallSignLayer);

        merchant.StallListEvent += OnStallList;
        merchant.StallShowcaseChangedEvent += OnStallShowcaseChanged;
        merchant.StallsInViewEvent += OnStallsInView;
        merchant.BuyStallPlacedEvent += OnBuyStallPlaced;
        foreach (var owner in merchant.KnownStalls) RestoreStall(owner);
    }

    public void Dispose()
    {
        entityPlates.DetachStallOwners();
        entities.EntitySpawned -= OnEntitySpawned;
        entities.EntityRemoved -= ForgetStall;
        merchant.StallListEvent -= OnStallList;
        merchant.StallShowcaseChangedEvent -= OnStallShowcaseChanged;
        merchant.StallsInViewEvent -= OnStallsInView;
        merchant.BuyStallPlacedEvent -= OnBuyStallPlaced;
        Stalls.Clear();
        DismissMoveConfirm();
    }

    private void OnEntitySpawned(EntityView e, EntitySnapshot info) => AttachPendingStall(info.Id);

    private const string SellingStallScene = "res://assets/objects/obj_counter.glb";
    private const string PremiumStallScene = "res://assets/objects/obj_counter_g.glb";
    private const string BuyingStallScene = "res://assets/objects/buyer.glb";

    private const float StallForward = -0.95f;
    private const float StallFacing = 180f;
    private const int StallSignCell = 36;
    private const float StallSignLift = 0.62f;

    private const float SittingStallSignHeight = 1.10f;
    private const float StallSignRange = 34f;
    private const int StallSignLayerIndex = 55;

    internal sealed class Stall
    {
        public bool IsBuying;
        public bool Premium;
        public int[] ItemIds = System.Array.Empty<int>();
        public Node3D? Node;
        public bool NodeIsBuying;
        public bool NodePremium;
        public PanelContainer? Sign;
        public MerchantCell[] SignCells = System.Array.Empty<MerchantCell>();
    }

    internal Dictionary<int, Stall> Stalls { get; } = new();

    private readonly Dictionary<string, PackedScene?> stallScenes = new();

    private CanvasLayer stallSignCanvas = null!;
    private Control stallSignLayer = null!;

    private bool locked;
    private Notice? moveConfirm;

    private void RestoreStall(StallOwner owner)
    {
        PlaceStall(owner.CharacterId, owner.IsBuying, owner.Flags, null);
        requests.StallListRequest(owner.CharacterId);
    }

    private void OnStallShowcaseChanged(StallShowcaseReply reply)
    {
        if (!Stalls.TryGetValue(reply.Owner, out var stall)) return;
        if (reply.ItemIds is { } ids) stall.ItemIds = ids;
        else if (reply.Slot >= 0 && reply.Slot < stall.ItemIds.Length) stall.ItemIds[reply.Slot] = 0;
        if (stall.Sign != null && GodotObject.IsInstanceValid(stall.Sign)) RefreshStallSign(reply.Owner, stall);
    }

    private void OnStallList(StallOwner owner, int[] itemIds) =>
        PlaceStall(owner.CharacterId, owner.IsBuying, owner.Flags, itemIds);

    private void OnBuyStallPlaced(int charId, int[] itemIds) =>
        PlaceStall(charId, isBuying: true, flags: 0, itemIds);

    private void OnStallsInView(List<StallOwner> owners)
    {
        foreach (var owner in owners) RestoreStall(owner);
    }

    internal void PlaceStall(int charId, bool isBuying, byte flags, int[]? itemIds)
    {
        if (!Stalls.TryGetValue(charId, out var stall))
            Stalls[charId] = stall = new Stall();

        stall.IsBuying = isBuying;
        stall.Premium = (flags & MerchantWire.PremiumFlagMask) != 0;
        if (itemIds != null) stall.ItemIds = itemIds;

        AttachStall(charId, stall);
        context.Entities.RefreshClanLine(charId);
    }

    private void AttachStall(int charId, Stall stall)
    {
        var body = charId == context.Self.CharId ? context.Self.Body : context.Entities.Views.TryGetValue(charId, out var ent) ? ent.Body : null;
        if (body == null || !GodotObject.IsInstanceValid(body)) return;

        bool rebuild = stall.Node == null
                       || !GodotObject.IsInstanceValid(stall.Node)
                       || stall.Node.GetParent() != body
                       || stall.NodeIsBuying != stall.IsBuying
                       || stall.NodePremium != stall.Premium;

        if (rebuild)
        {
            FreeStallNode(stall);

            var scene = StallScene(stall.Premium ? PremiumStallScene
                                 : stall.IsBuying ? BuyingStallScene
                                 : SellingStallScene);
            if (scene?.Instantiate() is not Node3D model) return;

            var mount = new Node3D
            {
                Position = new Vector3(0, 0, StallForward),
                RotationDegrees = new Vector3(0, StallFacing, 0),
            };
            mount.AddChild(model);
            body.AddChild(mount);
            stall.Node = mount;
            stall.NodeIsBuying = stall.IsBuying;
            stall.NodePremium = stall.Premium;
        }

        RefreshStallSign(charId, stall);
    }

    public bool HasStall(int charId) => Stalls.ContainsKey(charId);

    private void AttachPendingStall(int charId)
    {
        if (Stalls.TryGetValue(charId, out var stall)) AttachStall(charId, stall);
    }

    internal void RemoveStall(int charId)
    {
        if (!Stalls.Remove(charId, out var stall)) return;
        FreeStallNode(stall);
        context.Entities.RefreshClanLine(charId);
    }

    internal void ForgetStall(int charId)
    {
        if (Stalls.Remove(charId, out var stall)) FreeStallSign(stall);
    }

    private static void FreeStallNode(Stall stall)
    {
        if (stall.Node != null && GodotObject.IsInstanceValid(stall.Node)) stall.Node.QueueFree();
        stall.Node = null;
        FreeStallSign(stall);
    }

    private static void FreeStallSign(Stall stall)
    {
        if (stall.Sign != null && GodotObject.IsInstanceValid(stall.Sign)) stall.Sign.QueueFree();
        stall.Sign = null;
        stall.SignCells = System.Array.Empty<MerchantCell>();
    }

    internal void RefreshStallSign(int charId, Stall stall)
    {
        int shown = stall.Premium
            ? MerchantWire.StallDisplaySlotsPremium
            : MerchantWire.StallDisplaySlots;

        if (stall.Sign == null || !GodotObject.IsInstanceValid(stall.Sign) || stall.SignCells.Length != shown)
        {
            FreeStallSign(stall);
            stall.Sign = UiTheme.Section();
            stall.Sign.MouseFilter = Control.MouseFilterEnum.Pass;
            stallSignLayer.AddChild(stall.Sign);

            var grid = MerchantUi.Grid(MerchantWire.StallDisplaySlots, 3);
            grid.MouseFilter = Control.MouseFilterEnum.Pass;
            stall.Sign.AddChild(grid);

            stall.SignCells = new MerchantCell[shown];
            for (int i = 0; i < shown; i++)
            {
                var cell = new MerchantCell(i, StallSignCell)
                {
                    OnHover = cell => MerchantUi.HoverCell(context.Tooltips, cell),
                    OnHoverEnd = context.Tooltips.Hide,
                };
                stall.SignCells[i] = cell;
                grid.AddChild(cell);
            }
        }

        string note = stall.IsBuying ? "Wanted by this shop" : "For sale at this shop";
        for (int i = 0; i < stall.SignCells.Length; i++)
        {
            int itemId = i < stall.ItemIds.Length ? stall.ItemIds[i] : 0;
            stall.SignCells[i].Set(itemId == 0 ? default : UiKit.TooltipItem(itemId), note);
        }

        PositionStallSign(charId, stall);
    }

    private void StallSignTick()
    {
        if (Stalls.Count == 0 || context.Camera == null) return;
        foreach (var (charId, stall) in Stalls)
            PositionStallSign(charId, stall);
    }

    private void PositionStallSign(int charId, Stall stall)
    {
        if (stall.Sign == null || !GodotObject.IsInstanceValid(stall.Sign) || context.Camera == null) return;

        var body = charId == context.Self.CharId ? context.Self.Body : context.Entities.Views.TryGetValue(charId, out var ent) ? ent.Body : null;
        bool anyItem = false;
        foreach (int id in stall.ItemIds) if (id != 0) { anyItem = true; break; }

        if (body == null || !GodotObject.IsInstanceValid(body) || !anyItem)
        {
            stall.Sign.Visible = false;
            return;
        }

        Vector3 anchor;
        if (stall.Node != null && GodotObject.IsInstanceValid(stall.Node))
        {
            anchor = (body.GlobalPosition + stall.Node.GlobalPosition) * 0.5f;
            anchor.Y = body.GlobalPosition.Y + SittingStallSignHeight;
        }
        else
        {
            bool isSitting = charId == context.Self.CharId
                ? (context.Self.Sitting || Stalls.ContainsKey(charId))
                : (context.Entities.Views.TryGetValue(charId, out var entCheck) && (entCheck.Sitting || Stalls.ContainsKey(charId)));

            float headY = isSitting ? SittingStallSignHeight : (context.Entities.HeadHeight(charId) + StallSignLift);
            anchor = body.GlobalPosition + new Vector3(0, headY, 0);
        }

        if (context.Camera.IsPositionBehind(anchor)
            || (context.Self.Body != null && body.GlobalPosition.DistanceTo(context.Self.Body.Position) > StallSignRange))
        {
            stall.Sign.Visible = false;
            return;
        }

        var size = stall.Sign.Size;
        if (size.X <= 1) size = stall.Sign.GetCombinedMinimumSize();
        stall.Sign.Position = context.Camera.UnprojectPosition(anchor) - new Vector2(size.X * 0.5f, size.Y);
        stall.Sign.Visible = true;
    }

    internal bool TryOpenStallAt(Vector2 mouse)
    {
        var target = context.Selection.PickAt(mouse, out int id);
        if (target == null || target.IsNpc || id == context.Self.CharId) return false;
        if (!Stalls.TryGetValue(id, out var stall)) return false;

        context.Selection.Select(id);
        if (context.Self.Body != null && Coord.FlatDistance(context.Self.Body.Position, target.Body.Position) > ExchangeFeature.TradeRange)
        {
            context.Log.Notice("You are too far from that shop.");
            return true;
        }

        if (stall.IsBuying) requests.BuyMerchantList(id);
        else requests.List(id);
        return true;
    }

    internal void SetLock(bool locked)
    {
        if (this.locked == locked) return;
        this.locked = locked;
        if (!locked) { DismissMoveConfirm(); return; }
        context.Movement.StopForInteraction();
    }

    internal bool BlocksMove()
    {
        if (!locked) return false;
        AskLeave();
        return true;
    }

    private void AskLeave()
    {
        if (moveConfirm != null) return;
        moveConfirm = Notice.Confirm(
            context.Root,
            "Moving closes your stall. Leave merchant mode?",
            "Leave",
            "Stay",
            LeaveMode,
            DismissMoveConfirm,
            "Merchant");
    }

    private void LeaveMode()
    {
        moveConfirm = null;
        if (Stalls.TryGetValue(context.Self.CharId, out var mine) && mine.IsBuying) requests.BuyMerchantClose();
        else requests.Close();
        SetLock(false);
    }

    private void DismissMoveConfirm()
    {
        if (moveConfirm != null && GodotObject.IsInstanceValid(moveConfirm))
            moveConfirm.Close();
        moveConfirm = null;
    }

    private PackedScene? StallScene(string path)
    {
        if (stallScenes.TryGetValue(path, out var cached)) return cached;
        var scene = ResourceLoader.Exists(path) ? ResourceLoader.Load(path) as PackedScene : null;
        stallScenes[path] = scene;
        return scene;
    }
}
