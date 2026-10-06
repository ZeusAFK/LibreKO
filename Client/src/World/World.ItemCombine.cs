using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const string ItemCombineTablePath = "res://assets/ui/item_combine.json";
    private const int ItemCombineSockets = ItemCombine.MaterialSlots + 1;
    private const int ItemCombineShadowSocket = ItemCombine.MaterialSlots;
    private const int ItemCombineColumns = 5;
    private const float ItemCombineResultSize = 56f;
    private const float ItemCombineSocketSize = 44f;
    private const int ItemCombineBodyWidth = 470;
    private const int ItemCombinePleaseWaitText = 16103;
    private const string ItemCombineHint = "Right-click a bag item to place it";

    private CombineBook _combineBook = CombineBook.Empty;
    private CanvasLayer _itemCombineLayer = null!;
    private HudWindow _itemCombinePanel = null!;
    private DetailStrip _itemCombineStrip = null!;
    private FooterBand _itemCombineFooter = null!;
    private ItemSlotView _itemCombineResult = null!;
    private Button _itemCombineButton = null!;
    private readonly ItemSlotView[] _itemCombineSlots = new ItemSlotView[ItemCombineSockets];
    private readonly CombineEntry?[] _itemCombineEntries = new CombineEntry?[ItemCombineSockets];
    private BagCompanion? _itemCombineCompanion;
    private bool _itemCombineShown, _itemCombineWaiting;
    private int _itemCombineNpcTemplate;
    private double _itemCombineCooldownUntil = double.NegativeInfinity;

    private void ItemCombineInit()
    {
        BuildItemCombinePanel();
        BuildCombineRecipeBook();
        Net.I.ItemCombineReplyEvent += OnItemCombineReply;
        Net.I.CombineEffectEvent += OnCombineEffect;
    }

    private void ItemCombineDispose()
    {
        Net.I.ItemCombineReplyEvent -= OnItemCombineReply;
        Net.I.CombineEffectEvent -= OnCombineEffect;
    }

    private void EnsureCombineBook()
    {
        if (_combineBook.Recipes.Length > 0 || !Godot.FileAccess.FileExists(ItemCombineTablePath)) return;
        _combineBook = ItemCombine.Parse(Godot.FileAccess.GetFileAsString(ItemCombineTablePath));
    }

    private static string CombineText(int id, string fallback) => ItemData.Text(id, fallback);

    private void BuildItemCombinePanel()
    {
        _itemCombineLayer = new CanvasLayer { Layer = 75 };
        AddChild(_itemCombineLayer);

        _itemCombinePanel = new HudWindow("itemcombine", "Item Combination", bodyMinWidth: ItemCombineBodyWidth) { Visible = false };
        _itemCombinePanel.Closed += CloseItemCombine;
        _itemCombineLayer.AddChild(_itemCombinePanel);

        var root = _itemCombinePanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        var well = ServiceKit.Well();
        root.AddChild(well);
        var bench = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bench.AddThemeConstantOverride("separation", 10);
        well.AddChild(bench);

        _itemCombineResult = new ItemSlotView(ItemCombineResultSize);
        _itemCombineResult.Hovered += s => { if (!s.Item.IsEmpty) ShowItemTooltip(-1, s.Item); };
        _itemCombineResult.Unhovered += _ => HideItemTooltip();
        bench.AddChild(BuildUpgradeColumn("Result", _itemCombineResult));

        var materials = new GridContainer { Columns = ItemCombineColumns };
        materials.AddThemeConstantOverride("h_separation", AnvilSocketGap);
        materials.AddThemeConstantOverride("v_separation", AnvilSocketGap);
        for (int i = 0; i < ItemCombine.MaterialSlots; i++)
            materials.AddChild(CombineSocket(i));

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(BuildUpgradeColumn("Combination item", materials));
        row.AddChild(BenchSign("+"));
        row.AddChild(BuildUpgradeColumn(ItemData.DisplayName(ItemCombine.ShadowPiece), CombineSocket(ItemCombineShadowSocket)));
        bench.AddChild(row);

        _itemCombineStrip = new DetailStrip(ItemCombineResultSize);
        _itemCombineStrip.Slot.Visible = false;
        var formula = UiTheme.SmallButton("Formula", "Open the recipe book");
        formula.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        formula.Pressed += () => { if (!_itemCombineWaiting) OpenCombineRecipeBook(); };
        _itemCombineStrip.Right.AddChild(formula);
        var cancel = UiTheme.SmallButton("Cancel", "Take every material back");
        cancel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        cancel.Pressed += () => { if (!_itemCombineWaiting) ClearCombineSockets(); };
        _itemCombineStrip.Right.AddChild(cancel);
        _itemCombineButton = UiTheme.ActionButton("Combine", "Combine the materials");
        _itemCombineButton.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _itemCombineButton.Pressed += SendItemCombine;
        _itemCombineStrip.Right.AddChild(_itemCombineButton);
        root.AddChild(_itemCombineStrip);

        _itemCombineFooter = new FooterBand { Hint = ItemCombineHint };
        root.AddChild(_itemCombineFooter);
        ShowItemCombinePrompt();
    }

    private ItemSlotView CombineSocket(int index)
    {
        var socket = new ItemSlotView(ItemCombineSocketSize) { Index = index };
        socket.RightClicked += _ => ClearCombineSocket(index);
        socket.DoubleClicked += _ => ClearCombineSocket(index);
        socket.Hovered += s => { if (!s.Item.IsEmpty) ShowItemTooltip(-1, s.Item); };
        socket.Unhovered += _ => HideItemTooltip();
        socket.DragOut = _ => new Godot.Collections.Dictionary { { "companionFrom", index } };
        socket.CanDrop = (_, data) => CombineDropSlot(index, data) >= 0;
        socket.Dropped = (_, data) => DropOnCombine(index, data);
        _itemCombineSlots[index] = socket;
        return socket;
    }

    private void ShowItemCombinePrompt()
    {
        _itemCombineStrip.Title.Text = CombineText(ItemCombine.PutMaterialText, "Put on material");
        _itemCombineStrip.Sub.Text = "The craftsman decides what the materials make.";
        _itemCombineStrip.Sub.AddThemeColorOverride("font_color", UiTheme.TextLo);
    }

    private void OpenItemCombine(int npcTemplate)
    {
        EnsureCombineBook();
        _itemCombineNpcTemplate = npcTemplate;
        _npcTalkId = CombineCraftsman();
        _npcRangeAccum = 0;
        _itemCombineWaiting = false;
        CloseVendor();
        ClearCombineSockets();
        _itemCombineResult.Clear();
        _itemCombineFooter.ResetStatus();
        _itemCombinePanel.Visible = true;
        _itemCombineShown = true;
        _itemCombineCompanion ??= new BagCompanion(CombineTakeFromBag, CombineBagFit, _ => "", CloseItemCombine, CombineIntoBag);
        AttachBagCompanion(_itemCombineCompanion);
    }

    private void CloseItemCombine()
    {
        if (!_itemCombineShown) return;
        _itemCombineShown = false;
        _itemCombinePanel.Visible = false;
        HideItemTooltip();
        ReleaseBagHold();
        ClearCombineSockets();
        CloseCombineRecipeBook();
        if (_itemCombineCompanion != null) DetachBagCompanion(_itemCombineCompanion);
    }

    private int StagedSocketOf(int abs)
    {
        for (int i = 0; i < ItemCombineSockets; i++)
            if (_itemCombineEntries[i] is { } e && GridStart + e.BagSlot == abs) return i;
        return -1;
    }

    private bool CombineTakeFromBag(int abs)
    {
        if (!InMainBag(abs) || Inv[abs].IsEmpty) return false;
        if (_itemCombineWaiting) return true;
        int staged = StagedSocketOf(abs);
        if (staged >= 0)
        {
            ClearCombineSocket(staged);
            return true;
        }
        int socket = FreeCombineSocket(Inv[abs].ItemId);
        if (socket < 0)
        {
            _itemCombineFooter.Status("All material sockets are full.", bad: true);
            return true;
        }
        AskCombineCount(socket, abs);
        return true;
    }

    private BagFit CombineBagFit(int abs) => StagedSocketOf(abs) >= 0 ? BagFit.Staged : BagFit.Normal;

    private bool CombineIntoBag(int socket, int abs)
    {
        ClearCombineSocket(socket);
        return true;
    }

    private int FreeCombineSocket(int itemId)
    {
        if (itemId == ItemCombine.ShadowPiece)
            return _itemCombineEntries[ItemCombineShadowSocket] == null ? ItemCombineShadowSocket : -1;
        for (int i = 0; i < ItemCombine.MaterialSlots; i++)
            if (_itemCombineEntries[i] == null) return i;
        return -1;
    }

    private int CombineDropSlot(int socket, Variant data)
    {
        if (_itemCombineWaiting || data.VariantType != Variant.Type.Dictionary) return -1;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey("invFrom")) return -1;
        int abs = d["invFrom"].AsInt32();
        if (!InMainBag(abs) || Inv[abs].IsEmpty || StagedSocketOf(abs) >= 0) return -1;
        bool shadow = Inv[abs].ItemId == ItemCombine.ShadowPiece;
        if (shadow) return socket == ItemCombineShadowSocket && _itemCombineEntries[socket] == null ? socket : -1;
        if (socket == ItemCombineShadowSocket) return -1;
        return _itemCombineEntries[socket] == null ? socket : FreeCombineSocket(Inv[abs].ItemId);
    }

    private void DropOnCombine(int socket, Variant data)
    {
        int target = CombineDropSlot(socket, data);
        if (target >= 0) AskCombineCount(target, data.AsGodotDictionary()["invFrom"].AsInt32());
    }

    private void AskCombineCount(int socket, int abs)
    {
        var slot = Inv[abs];
        bool stack = ItemData.Get(slot.ItemId) is { Countable: not 0 } && socket != ItemCombineShadowSocket && slot.Count > 1;
        if (!stack)
        {
            StageCombine(socket, abs, 1);
            return;
        }
        AskAmount(slot, CombineText(ItemCombine.QuantityText, "Please enter the quantity."), 0,
            Math.Min((int)slot.Count, ItemCombine.MaxCount), countable: true,
            (count, _) => StageCombine(socket, abs, count), priceEditable: false);
    }

    private void StageCombine(int socket, int abs, int count)
    {
        if (!_itemCombineShown || _itemCombineWaiting || !InMainBag(abs) || Inv[abs].IsEmpty || _itemCombineEntries[socket] != null) return;
        var item = Inv[abs];
        _itemCombineEntries[socket] = new CombineEntry(item.ItemId, count, abs - GridStart);
        var shown = item;
        shown.Count = (short)count;
        _itemCombineSlots[socket].Set(shown);
        _itemCombineResult.Clear();
        _itemCombineFooter.ResetStatus();
        RefreshBagFit();
    }

    private void ClearCombineSocket(int index)
    {
        if (_itemCombineWaiting || _itemCombineEntries[index] == null) return;
        _itemCombineEntries[index] = null;
        _itemCombineSlots[index].Clear();
        RefreshBagFit();
    }

    private void ClearCombineSockets()
    {
        for (int i = 0; i < ItemCombineSockets; i++)
        {
            _itemCombineEntries[i] = null;
            _itemCombineSlots[i]?.Clear();
        }
        RefreshBagFit();
    }

    private double CombineNow => Time.GetTicksMsec() / 1000.0;

    private void SendItemCombine()
    {
        if (!_itemCombineShown || _itemCombineWaiting) return;
        double left = _itemCombineCooldownUntil - CombineNow;
        if (left > ItemCombine.CooldownReady)
        {
            _itemCombineFooter.Status(CombineText(ItemCombine.CooldownText, "You can use it after %d seconds.")
                .Replace("%d", ItemCombine.SecondsLeft(left).ToString()), bad: true);
            return;
        }
        _itemCombineCooldownUntil = CombineNow + ItemCombine.Cooldown;

        var materials = _itemCombineEntries.Take(ItemCombine.MaterialSlots).OfType<CombineEntry>().ToList();
        if (materials.Count == 0)
        {
            _itemCombineFooter.Status(CombineText(ItemCombine.PutMaterialText, "Put on material"), bad: true);
            return;
        }
        if (Inv.FreeGridSlots().Count == 0)
        {
            _itemCombineFooter.Status(CombineText(ItemCombine.NoBagSlotText, "You do not have an empty slot in your inventory."), bad: true);
            return;
        }

        var shadow = _itemCombineEntries[ItemCombineShadowSocket];
        _itemCombineWaiting = true;
        foreach (var entry in _itemCombineEntries.OfType<CombineEntry>())
            _bagHold.Hold(GridStart + entry.BagSlot, Inv[GridStart + entry.BagSlot]);
        _itemCombineStrip.Sub.Text = CombineText(ItemCombinePleaseWaitText, "Please wait");
        Net.I.SendItemCombine(CombineCraftsman(), shadow?.ItemId ?? 0, shadow?.BagSlot ?? 0, ItemCombine.WireOrder(materials));
    }

    private void OnItemCombineReply(CombineReply reply)
    {
        if (!_itemCombineWaiting) return;
        _itemCombineWaiting = false;
        ReleaseBagHold();
        if (!ItemCombine.IsDone(reply.Result))
        {
            ShowItemCombinePrompt();
            _itemCombineFooter.Status(CombineText(ItemCombine.WrongMaterialText, "Wrong material"), bad: true);
            return;
        }

        foreach (var entry in _itemCombineEntries.OfType<CombineEntry>())
        {
            int abs = GridStart + entry.BagSlot;
            if (Inv[abs].ItemId == entry.ItemId) Inv.Consume(abs, entry.Count);
        }
        ClearCombineSockets();
        RefreshInventoryUI();
        ShowItemCombinePrompt();

        var row = _combineBook.Row(reply.Row);
        if (reply.Result == ItemCombine.Succeeded)
        {
            var made = Inv[GridStart + reply.BagSlot];
            if (!made.IsEmpty)
            {
                _itemCombineResult.Set(made);
                string received = $"{ItemData.DisplayName(made.ItemId)} {CombineText(ItemCombine.ReceivedText, "has been received")}";
                _itemCombineFooter.Status(received, bad: false);
                Notice.Show(this, received, "Item Combination");
            }
            if (row != null) ShowNpcBalloon([row.SuccessText]);
        }
        else
        {
            _itemCombineFooter.Status("The combination failed. The materials are gone.", bad: true);
            if (row != null) ShowNpcBalloon([row.FailureText]);
        }
    }

    private int CombineCraftsman() => _self == null
        ? ItemCombine.NoCraftsman
        : ItemCombine.Craftsman(
            _ents.Where(e => !e.Value.Dead).Select(e => (e.Key, e.Value.NpcId, FlatDistance(_self.Position, e.Value.Body.Position))),
            _itemCombineNpcTemplate);

    private void OnCombineEffect(CombineEffect effect)
    {
        EnsureCombineBook();
        if (_combineBook.Row(effect.Row) is not { } row) return;
        int fxId = effect.Success ? row.SuccessFx : row.FailureFx;
        if (Fx.NameForId(fxId) is not { } fx) return;
        foreach (var (id, ent) in _ents)
            if (ent.NpcId == effect.NpcId && !ent.Dead && _self != null
                && FlatDistance(_self.Position, ent.Body.Position) <= NpcInteractRange * 2)
                SpawnFxOn(id, fx, 0f);
    }
}
