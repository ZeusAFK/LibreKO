using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const string SellingStallScene = "res://assets/objects/obj_counter.glb";
    private const string PremiumStallScene = "res://assets/objects/obj_counter_g.glb";
    private const string BuyingStallScene = "res://assets/objects/buyer.glb";

    private const float StallForward = -0.95f;
    private const float StallFacing = 180f;
    private const int StallSignCell = 36;
    private const float StallSignLift = 0.62f;
    private const float StallSignRange = 34f;
    private const int StallSignLayerIndex = 55;

    private sealed class Stall
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

    private readonly Dictionary<int, Stall> _stalls = new();
    private readonly Dictionary<string, PackedScene?> _stallScenes = new();
    private CanvasLayer _stallSignCanvas = null!;
    private Control _stallSignLayer = null!;

    private bool _merchantLocked;
    private Notice? _merchantMoveConfirm;

    private void MerchantStallInit()
    {
        _stallSignCanvas = new CanvasLayer { Layer = StallSignLayerIndex };
        AddChild(_stallSignCanvas);
        _stallSignLayer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _stallSignLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _stallSignCanvas.AddChild(_stallSignLayer);

        Net.I.StallListEvent += OnStallList;
        Net.I.StallsInViewEvent += OnStallsInView;
        Net.I.BuyStallPlacedEvent += OnBuyStallPlaced;
        Net.I.ReplayKnownStalls(RestoreStall);
    }

    private void MerchantStallDispose()
    {
        Net.I.StallListEvent -= OnStallList;
        Net.I.StallsInViewEvent -= OnStallsInView;
        Net.I.BuyStallPlacedEvent -= OnBuyStallPlaced;
        _stalls.Clear();
        DismissMerchantMoveConfirm();
    }

    private void RestoreStall(StallOwner owner)
    {
        PlaceStall(owner.CharacterId, owner.IsBuying, owner.Flags, null);
        Net.I.SendStallListRequest(owner.CharacterId);
    }

    private void OnStallList(StallOwner owner, int[] itemIds) =>
        PlaceStall(owner.CharacterId, owner.IsBuying, owner.Flags, itemIds);

    private void OnBuyStallPlaced(int charId, int[] itemIds) =>
        PlaceStall(charId, isBuying: true, flags: 0, itemIds);

    private void OnStallsInView(List<StallOwner> owners)
    {
        foreach (var owner in owners) RestoreStall(owner);
    }

    private void PlaceStall(int charId, bool isBuying, byte flags, int[]? itemIds)
    {
        if (!_stalls.TryGetValue(charId, out var stall))
            _stalls[charId] = stall = new Stall();

        stall.IsBuying = isBuying;
        stall.Premium = (flags & Net.MerchantPremiumFlagMask) != 0;
        if (itemIds != null) stall.ItemIds = itemIds;

        AttachStall(charId, stall);
    }

    private void AttachStall(int charId, Stall stall)
    {
        var body = charId == _myId ? _self : _ents.TryGetValue(charId, out var ent) ? ent.Body : null;
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

    private void AttachPendingStall(int charId)
    {
        if (_stalls.TryGetValue(charId, out var stall)) AttachStall(charId, stall);
    }

    private void RemoveStall(int charId)
    {
        if (!_stalls.Remove(charId, out var stall)) return;
        FreeStallNode(stall);
    }

    private void ForgetStall(int charId)
    {
        if (_stalls.Remove(charId, out var stall)) FreeStallSign(stall);
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

    private void RefreshStallSign(int charId, Stall stall)
    {
        int shown = stall.Premium
            ? Net.MerchantStallDisplaySlotsPremium
            : Net.MerchantStallDisplaySlots;

        if (stall.Sign == null || !GodotObject.IsInstanceValid(stall.Sign) || stall.SignCells.Length != shown)
        {
            FreeStallSign(stall);
            stall.Sign = UiTheme.Section();
            stall.Sign.MouseFilter = Control.MouseFilterEnum.Pass;
            _stallSignLayer.AddChild(stall.Sign);

            var grid = MerchantGrid(Net.MerchantStallDisplaySlots, 3);
            grid.MouseFilter = Control.MouseFilterEnum.Pass;
            stall.Sign.AddChild(grid);

            stall.SignCells = new MerchantCell[shown];
            for (int i = 0; i < shown; i++)
            {
                var cell = new MerchantCell(i, StallSignCell)
                {
                    OnHover = HoverMerchantCell,
                    OnHoverEnd = HideItemTooltip,
                };
                stall.SignCells[i] = cell;
                grid.AddChild(cell);
            }
        }

        string note = stall.IsBuying ? "Wanted by this shop" : "For sale at this shop";
        for (int i = 0; i < stall.SignCells.Length; i++)
        {
            int itemId = i < stall.ItemIds.Length ? stall.ItemIds[i] : 0;
            stall.SignCells[i].Set(itemId == 0 ? default : TooltipItem(itemId), note);
        }

        PositionStallSign(charId, stall);
    }

    private void StallSignTick()
    {
        if (_stalls.Count == 0 || _camera == null) return;
        foreach (var (charId, stall) in _stalls)
            PositionStallSign(charId, stall);
    }

    private void PositionStallSign(int charId, Stall stall)
    {
        if (stall.Sign == null || !GodotObject.IsInstanceValid(stall.Sign) || _camera == null) return;

        var body = charId == _myId ? _self : _ents.TryGetValue(charId, out var ent) ? ent.Body : null;
        bool anyItem = false;
        foreach (int id in stall.ItemIds) if (id != 0) { anyItem = true; break; }

        if (body == null || !GodotObject.IsInstanceValid(body) || !anyItem)
        {
            stall.Sign.Visible = false;
            return;
        }

        var head = body.GlobalPosition + new Vector3(0, HeadHeightOf(charId) + StallSignLift, 0);
        if (_camera.IsPositionBehind(head)
            || (_self != null && body.GlobalPosition.DistanceTo(_self.Position) > StallSignRange))
        {
            stall.Sign.Visible = false;
            return;
        }

        var size = stall.Sign.Size;
        if (size.X <= 1) size = stall.Sign.GetCombinedMinimumSize();
        stall.Sign.Position = _camera.UnprojectPosition(head) - new Vector2(size.X * 0.5f, size.Y);
        stall.Sign.Visible = true;
    }

    private bool TryOpenStallAt(Vector2 mouse)
    {
        var target = PickEntityAt(mouse, ClickPickRadius, out int id, out _);
        if (target == null || target.IsNpc || id == _myId) return false;
        if (!_stalls.TryGetValue(id, out var stall)) return false;

        Select(id, target);
        if (_self != null && FlatDistance(_self.Position, target.Body.Position) > TradeRange)
        {
            CombatNotice("You are too far from that shop.");
            return true;
        }

        if (stall.IsBuying) Net.I.SendBuyMerchantList(id);
        else Net.I.SendMerchantList(id);
        return true;
    }

    private void SetMerchantLock(bool locked)
    {
        if (_merchantLocked == locked) return;
        _merchantLocked = locked;
        if (!locked) { DismissMerchantMoveConfirm(); return; }
        StopForInteraction();
    }

    private bool MerchantBlocksMove()
    {
        if (!_merchantLocked) return false;
        AskLeaveMerchant();
        return true;
    }

    private void AskLeaveMerchant()
    {
        if (_merchantMoveConfirm != null) return;
        _merchantMoveConfirm = Notice.Confirm(
            this,
            "Moving closes your stall. Leave merchant mode?",
            "Leave",
            "Stay",
            LeaveMerchantMode,
            DismissMerchantMoveConfirm,
            "Merchant");
    }

    private void LeaveMerchantMode()
    {
        _merchantMoveConfirm = null;
        if (_stalls.TryGetValue(_myId, out var mine) && mine.IsBuying) Net.I.SendBuyMerchantClose();
        else Net.I.SendMerchantClose();
        SetMerchantLock(false);
    }

    private void DismissMerchantMoveConfirm()
    {
        if (_merchantMoveConfirm != null && GodotObject.IsInstanceValid(_merchantMoveConfirm))
            _merchantMoveConfirm.Close();
        _merchantMoveConfirm = null;
    }

    private PackedScene? StallScene(string path)
    {
        if (_stallScenes.TryGetValue(path, out var cached)) return cached;
        var scene = ResourceLoader.Exists(path) ? ResourceLoader.Load(path) as PackedScene : null;
        _stallScenes[path] = scene;
        return scene;
    }
}
