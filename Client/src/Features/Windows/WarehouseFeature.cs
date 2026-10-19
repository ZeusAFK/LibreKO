using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class WarehouseFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly NpcQuestSession npcQuest;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly WarehouseSession warehouse;
    private readonly IWarehouseRequests requests;
    private readonly IInteractionDialogs interactionDialogs;

    public WarehouseFeature(IWorldContext context, NpcQuestSession npcQuest, ProgressSession progress, SelfSession self, WarehouseSession warehouse, IWarehouseRequests requests, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.npcQuest = npcQuest;
        this.progress = progress;
        this.self = self;
        this.warehouse = warehouse;
        this.requests = requests;
        this.interactionDialogs = interactionDialogs;
    }

    private const int Slots = WarehouseView.Slots;
    internal const int PageSize = WarehouseView.UiPageSize;
    private const int Columns = 8;
    private const float CellSize = 44f;
    private const string Hint = "Right-click to store or take out · drag to place";
    private const string HitBadge = "•";

    internal ItemSlot[] StoredItems { get; } = new ItemSlot[Slots];
    internal int Money { get; set; }
    internal int Page { get; private set; }
    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private bool shown;

    public bool IsOpen => shown;

    internal IClosableWindow AmountPromptPanel => AmountPrompt;

    internal ItemSlotView[] Cells { get; } = new ItemSlotView[PageSize];
    internal LineEdit Search { get; private set; } = null!;
    private ServiceTabs pageTabs = null!;
    private Label slotsNote = null!;
    internal StatusLabel Status { get; private set; } = null!;
    private MoneyPlaque stored = null!;
    private Button depositBtn = null!;
    private Button withdrawBtn = null!;
    internal QuantityPrompt AmountPrompt { get; private set; } = null!;
    internal HashSet<int> Hits { get; private set; } = new();
    private BagCompanion? companion;

    private const byte OpInput = 2;
    private const byte OpOutput = 3;
    private const byte OpMove = 4;

    private struct Pending { public byte Op; public bool Gold; public bool Merge; public int InvAbs; public int WhIdx; public int WhTo; public int Count; }

    private Pending pending;
    private bool inFlight;
    private readonly List<Func<bool>> otherTransfers = new();

    internal void BlockTransfersWhile(Func<bool> isInFlight) => otherTransfers.Add(isInFlight);

    internal bool StorageTransferBusy => inFlight || context.Inventory.MoveBusy || otherTransfers.Exists(busy => busy());

    public void Build()
    {
        interactionDialogs.InteractionCloses(this);
        BuildPanel();
        warehouse.WarehouseContentsEvent += OnContents;
        warehouse.WarehouseResultEvent += OnResult;
        warehouse.WarehouseOpenRefusedEvent += OnOpenRefused;
        progress.GoldChangeEvent += OnGold;
        context.Inventory.Refreshed += OnInventoryRefreshed;
    }

    public void Dispose()
    {
        warehouse.WarehouseContentsEvent -= OnContents;
        warehouse.WarehouseResultEvent -= OnResult;
        warehouse.WarehouseOpenRefusedEvent -= OnOpenRefused;
        progress.GoldChangeEvent -= OnGold;
        context.Inventory.Refreshed -= OnInventoryRefreshed;
    }

    private const byte RefusedMoney = 2;
    private const int TextRefused = 10411;
    private const int TextNoMoney = 10412;

    private void OnOpenRefused(WarehouseOpenResult result) => context.Log.Notice(result == (WarehouseOpenResult)RefusedMoney
    ? ItemData.Text(TextNoMoney, "Could not access the storage because you didn't have enough money.")
    : ItemData.Text(TextRefused, "Could not access the storage."));

    private void OnGold(GoldChange change) { if (shown) RefreshGold(); }

    private void OnInventoryRefreshed() { if (shown) Refresh(); }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("warehouse", "Warehouse") { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        AmountPrompt = new QuantityPrompt(UiLayers.Prompts);
        context.Root.AddChild(AmountPrompt);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 6);

        Search = new LineEdit { PlaceholderText = "Search stored items", ClearButtonEnabled = true };
        Search.AddThemeFontSizeOverride("font_size", 13);
        Search.TextChanged += _ => Refresh();
        root.AddChild(Search);

        root.AddChild(ServiceKit.Section("Stored items", UiIcons.Get("system/package"), out slotsNote));

        var well = ServiceKit.Well();
        root.AddChild(well);
        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        well.AddChild(grid);
        for (int i = 0; i < PageSize; i++)
        {
            var cell = new ItemSlotView(CellSize) { Index = i, Veiled = true, PicksAmount = true };
            cell.RightClicked += c => WithdrawSlot(Abs(c.Index));
            cell.Hovered += c => context.Tooltips.Show(-1, c.Item);
            cell.Unhovered += _ => context.Tooltips.Hide();
            cell.Wheeled += (_, step) => TurnPage(step);
            cell.DragOut = c => new Godot.Collections.Dictionary { { "companionFrom", Abs(c.Index) } };
            cell.CanDrop = (c, data) => CanDropOn(Abs(c.Index), data);
            cell.Dropped = (c, data) => DropOn(Abs(c.Index), data);
            Cells[i] = cell;
            grid.AddChild(cell);
        }

        pageTabs = new ServiceTabs();
        var pages = new List<string>();
        for (int p = 0; p < WarehouseView.UiPages; p++) pages.Add($"{p + 1}");
        pageTabs.SetTabs(pages, 0);
        pageTabs.Selected += page =>
        {
            Page = page;
            Refresh();
        };
        root.AddChild(pageTabs);

        Status = new StatusLabel { Hint = Hint };
        root.AddChild(Status);

        var footer = new FooterBand();
        stored = new MoneyPlaque("Stored gold");
        footer.Left.AddChild(stored);
        depositBtn = UiTheme.SmallButton("Deposit", "Put gold into the warehouse");
        depositBtn.Pressed += () => AskGoldTransfer(deposit: true);
        withdrawBtn = UiTheme.SmallButton("Withdraw", "Take gold out of the warehouse");
        withdrawBtn.Pressed += () => AskGoldTransfer(deposit: false);
        footer.Right.AddChild(depositBtn);
        footer.Right.AddChild(withdrawBtn);
        root.AddChild(footer);
    }

    internal int Abs(int cell) => Page * PageSize + cell;

    internal void Show()
    {
        Panel.Visible = true;
        shown = true;
    }

    internal void OpenStorage()
    {
        context.NpcDialog.Close();
        Page = 0;
        pageTabs.Select(0, notify: false);
        Search.Text = "";
        Status.ResetStatus();
        Show();
        if (!inFlight)
        {
            System.Array.Clear(StoredItems, 0, StoredItems.Length);
            Money = 0;
        }
        companion ??= new BagCompanion(TakeFromBag, WhBagFit, _ => "", Close, WithdrawInto);
        context.BagCompanions.Attach(companion);
        requests.Open();
        Refresh();
    }

    public void Close()
    {
        AmountPrompt.Close();
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        context.Tooltips.Hide();
        if (companion != null) context.BagCompanions.Detach(companion);
        context.Plugins.Warehouse();
    }

    private void OnContents(int money, ItemSlot[] slots)
    {
        this.Money = money;
        for (int i = 0; i < Slots && i < slots.Length; i++) this.StoredItems[i] = slots[i];
        if (shown) Refresh();
    }

    internal void TurnPage(int step)
    {
        int next = Mathf.Clamp(Page + step, 0, WarehouseView.UiPages - 1);
        if (next == Page) return;
        Page = next;
        pageTabs.Select(next, notify: false);
        Refresh();
    }

    internal void Refresh()
    {
        var ids = StoredItems.Select(s => s.ItemId).ToArray();
        Hits = WarehouseView.Matches(ids, Search.Text, ItemData.DisplayName);
        bool searching = !new ItemQuery(Search.Text).IsEmpty;
        var pages = WarehouseView.PagesWithHits(Hits);
        for (int p = 0; p < pages.Length; p++) pageTabs.SetBadge(p, searching && pages[p] ? HitBadge : "");

        for (int i = 0; i < PageSize; i++)
        {
            int abs = Abs(i);
            Cells[i].Set(StoredItems[abs]);
            Cells[i].Look = !searching || StoredItems[abs].IsEmpty ? SlotLook.Normal
                : Hits.Contains(abs) ? SlotLook.Selected : SlotLook.Dimmed;
        }

        int used = StoredItems.Count(s => !s.IsEmpty);
        slotsNote.Text = $"Slots used {used} / {Slots}";
        slotsNote.AddThemeColorOverride("font_color", used >= Slots ? UiTheme.Bad : UiTheme.TextDim);
        RefreshGold();
        context.BagCompanions.RefreshFit();
    }

    private void RefreshGold()
    {
        stored.Value = Money;
        depositBtn.Disabled = self.Sheet.Gold <= 0;
        withdrawBtn.Disabled = Money <= 0;
        context.Plugins.Warehouse();
    }

    private static bool Storable(int itemId) =>
        ItemData.Get(itemId) is { } def && WarehouseRules.Storable(itemId, def.Race);

    internal static int WholeStack(ItemSlot slot) => UiKit.IsStackable(slot.ItemId) ? Mathf.Max(1, (int)slot.Count) : 1;

    internal bool TakeFromBag(int abs)
    {
        if (!self.Inventory.InMainBag(abs)) return false;
        DepositSlot(abs, -1);
        return true;
    }

    private BagFit WhBagFit(int abs)
    {
        if (abs < Inventory.GridStart) return BagFit.Normal;
        return self.Inventory.InMainBag(abs) && Storable(self.Inventory[abs].ItemId) ? BagFit.Normal : BagFit.Unfit;
    }

    private bool FitsIn(int whIdx, int itemId, int count) =>
        StorageDestination.Fits(StoredItems[whIdx], itemId, count, UiKit.IsStackable(itemId));

    internal bool FitsInBag(int abs, int itemId, int count) =>
        self.Inventory.InMainBag(abs) && (self.Inventory[abs].IsEmpty
        || (self.Inventory[abs].ItemId == itemId && UiKit.IsStackable(itemId) && self.Inventory[abs].Count + count <= Inventory.StackMax));

    private int WarehouseDestination(int itemId, int count, out bool merge)
    {
        int whIdx = StorageDestination.Find(StoredItems, itemId, count, UiKit.IsStackable(itemId));
        merge = whIdx >= 0 && !StoredItems[whIdx].IsEmpty;
        return whIdx;
    }

    internal int BagDestination(int itemId, int count, out bool merge)
    {
        int abs = self.Inventory.GridSlotFor(itemId, count, UiKit.IsStackable(itemId));
        merge = abs >= 0 && !self.Inventory[abs].IsEmpty;
        return abs;
    }

    private bool CanDropOn(int whIdx, Variant data)
    {
        if (StorageTransferBusy || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (d.ContainsKey("invFrom"))
        {
            int abs = d["invFrom"].AsInt32();
            return self.Inventory.InMainBag(abs) && Storable(self.Inventory[abs].ItemId);
        }
        if (!d.ContainsKey("companionFrom")) return false;
        int from = d["companionFrom"].AsInt32();
        return from is >= 0 and < Slots && from != whIdx && !StoredItems[from].IsEmpty && StoredItems[whIdx].IsEmpty;
    }

    private void DropOn(int whIdx, Variant data)
    {
        var d = data.AsGodotDictionary();
        int? amount = DragLayer.ChosenAmount(d);
        if (d.ContainsKey("invFrom")) DepositSlot(d["invFrom"].AsInt32(), whIdx, amount);
        else if (d.ContainsKey("companionFrom")) MoveInside(d["companionFrom"].AsInt32(), whIdx, amount);
    }

    private void DepositSlot(int abs, int target, int? amount = null)
    {
        if (StorageTransferBusy || !self.Inventory.InMainBag(abs) || self.Inventory[abs].IsEmpty) return;
        var slot = self.Inventory[abs];
        if (!Storable(slot.ItemId))
        {
            Status.Status(ItemData.Text(WarehouseRules.NonStorableText, "This item is non-storable"), bad: true);
            return;
        }
        if (amount == null && UiKit.AsksStackAmount(slot))
            AskDepositAmount(abs, target);
        else
            DepositCount(abs, amount ?? Mathf.Max(1, (int)slot.Count), target);
    }

    internal void AskDepositAmount(int abs, int target)
    {
        var slot = self.Inventory[abs];
        AmountPrompt.Open(ItemIcons.Get(slot.ItemId), $"Store {ItemData.DisplayName(slot.ItemId)}",
            $"You carry {slot.Count:n0}", slot.Count, slot.Count, n => { if (shown && self.Inventory[abs].Equals(slot)) DepositCount(abs, (int)n, target); }, "Store");
    }

    private void DepositCount(int abs, int count, int target)
    {
        if (StorageTransferBusy || !self.Inventory.InMainBag(abs) || self.Inventory[abs].IsEmpty || count <= 0 || target >= Slots) return;
        var slot = self.Inventory[abs];
        count = Mathf.Min(count, slot.Count > 0 ? slot.Count : 1);
        int whIdx;
        bool merge;
        if (target >= 0)
        {
            if (!FitsIn(target, slot.ItemId, count)) { Status.Status("That slot is taken.", bad: true); return; }
            whIdx = target;
            merge = !StoredItems[target].IsEmpty;
        }
        else
        {
            whIdx = WarehouseDestination(slot.ItemId, count, out merge);
            if (whIdx < 0) { Status.Status("Warehouse is full.", bad: true); return; }
        }
        pending = new Pending { Op = OpInput, Merge = merge, InvAbs = abs, WhIdx = whIdx, Count = count };
        inFlight = true;
        requests.Input(npcQuest.LastNpcId, slot.ItemId, WarehouseView.ServerPage(whIdx),
            (byte)(abs - Inventory.GridStart), WarehouseView.ServerCell(whIdx), count);
    }

    internal void WithdrawSlot(int whIdx) => AskWithdraw(whIdx, -1);

    private bool WithdrawInto(int whIdx, int bagAbs, int? amount)
    {
        AskWithdraw(whIdx, bagAbs, amount);
        return true;
    }

    private void AskWithdraw(int whIdx, int target, int? amount = null)
    {
        if (StorageTransferBusy || whIdx < 0 || whIdx >= Slots || StoredItems[whIdx].IsEmpty) return;
        var slot = StoredItems[whIdx];
        if (amount == null && UiKit.AsksStackAmount(slot))
            AmountPrompt.Open(ItemIcons.Get(slot.ItemId), $"Take out {ItemData.DisplayName(slot.ItemId)}",
                $"Stored {slot.Count:n0}", slot.Count, slot.Count, n => { if (shown && StoredItems[whIdx].Equals(slot)) WithdrawCount(whIdx, (int)n, target); }, "Take out");
        else
            WithdrawCount(whIdx, amount ?? Mathf.Max(1, (int)slot.Count), target);
    }

    private void WithdrawCount(int whIdx, int count, int target)
    {
        if (StorageTransferBusy || whIdx < 0 || whIdx >= Slots || StoredItems[whIdx].IsEmpty || count <= 0) return;
        var slot = StoredItems[whIdx];
        count = Mathf.Min(count, slot.Count > 0 ? slot.Count : 1);
        int dest;
        bool merge;
        if (target >= 0)
        {
            if (!FitsInBag(target, slot.ItemId, count)) { Status.Status("That slot is taken.", bad: true); return; }
            dest = target;
            merge = !self.Inventory[target].IsEmpty;
        }
        else
        {
            dest = BagDestination(slot.ItemId, count, out merge);
            if (dest < 0) { Status.Status("Your bags are full.", bad: true); return; }
        }
        pending = new Pending { Op = OpOutput, Merge = merge, InvAbs = dest, WhIdx = whIdx, Count = count };
        inFlight = true;
        requests.Output(npcQuest.LastNpcId, slot.ItemId, WarehouseView.ServerPage(whIdx),
            WarehouseView.ServerCell(whIdx), (byte)(dest - Inventory.GridStart), count);
    }

    private void MoveInside(int from, int to, int? amount)
    {
        if (StorageTransferBusy || from == to || from < 0 || to < 0 || from >= Slots || to >= Slots || StoredItems[from].IsEmpty) return;
        if (ItemStacks.IsPartOfStack(amount, StoredItems[from])) { Status.Status(GameText.StorageWholeStack, bad: true); return; }
        if (!StoredItems[to].IsEmpty) { Status.Status("That slot is taken.", bad: true); return; }
        pending = new Pending { Op = OpMove, WhIdx = from, WhTo = to, Count = StoredItems[from].Count };
        inFlight = true;
        requests.MoveInside(npcQuest.LastNpcId, StoredItems[from].ItemId, WarehouseView.ServerPage(from),
            WarehouseView.ServerCell(from), WarehouseView.ServerPage(to), WarehouseView.ServerCell(to));
    }

    private long GoldRoom(bool deposit) => CharacterSheet.CoinMax - (long)(deposit ? Money : self.Sheet.Gold);

    private void AskGoldTransfer(bool deposit)
    {
        if (StorageTransferBusy) return;
        long have = deposit ? self.Sheet.Gold : Money;
        if (have <= 0) return;
        long max = Math.Min(have, GoldRoom(deposit));
        if (max <= 0) { Status.Status(GoldRoomRefusal(deposit), bad: true); return; }
        string note = ItemData.Text(GameText.GoldCapNote, "You cannot enter if it's more than 2.1 billion.");
        AmountPrompt.Open(null, deposit ? "Deposit gold" : "Withdraw gold",
            deposit ? $"You carry {have:n0}" : $"Stored {have:n0}", max, max, n => GoldTransfer(deposit, (int)n),
            deposit ? "Deposit" : "Withdraw", _ => note);
    }

    private static string GoldRoomRefusal(bool deposit) =>
        deposit ? "The warehouse can't hold more gold." : "You can't carry more gold.";

    private void GoldTransfer(bool deposit, int amount)
    {
        if (StorageTransferBusy || amount <= 0) return;
        if (deposit && amount > self.Sheet.Gold) { Status.Status("Not enough carried gold.", bad: true); return; }
        if (!deposit && amount > Money) { Status.Status("Not enough stored gold.", bad: true); return; }
        if (amount > GoldRoom(deposit)) { Status.Status(GoldRoomRefusal(deposit), bad: true); return; }

        pending = new Pending { Op = deposit ? OpInput : OpOutput, Gold = true, Count = amount };
        inFlight = true;
        if (deposit) requests.Input(npcQuest.LastNpcId, LootWire.GoldItemId, 0, 0, 0, amount);
        else requests.Output(npcQuest.LastNpcId, LootWire.GoldItemId, 0, 0, 0, amount);
    }

    private const byte ResultOk = 1;
    private const byte ResultDuplicate = 2;
    private const int DuplicateText = 10762;
    internal const int NoRoomText = 1708;

    private void OnResult(byte op, byte result)
    {
        if (!inFlight || op != pending.Op) return;
        inFlight = false;
        if (result != ResultOk)
        {
            string text = result == ResultDuplicate
                ? ItemData.Text(DuplicateText, "You are holding a duplicated item.")
                : op == OpOutput ? ItemData.Text(NoRoomText, "Your bags are full.") : "Transfer failed.";
            Status.Status(text, bad: true);
            if (result == ResultDuplicate || op == OpOutput)
                context.Log.Add(text, CombatLogKind.Status, UiKit.WhRefusalTint);
            if (shown) Refresh();
            return;
        }

        var p = pending;
        if (p.Gold)
        {
            if (p.Op == OpInput) { Money += p.Count; progress.TransferGoldOut(p.Count); }
            else { Money -= p.Count; progress.TransferGoldIn(p.Count); }
            Status.Status(p.Op == OpInput ? $"Deposited {p.Count:n0} gold." : $"Withdrew {p.Count:n0} gold.", bad: false);
        }
        else if (p.Op == OpMove)
        {
            int itemId = StoredItems[p.WhIdx].ItemId;
            StoredItems[p.WhTo] = StoredItems[p.WhIdx];
            StoredItems[p.WhIdx] = default;
            Status.Status($"Moved {ItemData.DisplayName(itemId)}.", bad: false);
        }
        else if (p.Op == OpInput)
        {
            var bag = self.Inventory[p.InvAbs];
            int itemId = bag.ItemId;
            ItemStacks.MoveStack(ref bag, ref StoredItems[p.WhIdx], p.Count, p.Merge);
            self.SetInventorySlot(p.InvAbs, bag);
            if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
            Status.Status($"Stored {ItemLine(itemId, p.Count)}.", bad: false);
        }
        else
        {
            var bag = self.Inventory[p.InvAbs];
            int itemId = StoredItems[p.WhIdx].ItemId;
            ItemStacks.MoveStack(ref StoredItems[p.WhIdx], ref bag, p.Count, p.Merge);
            self.SetInventorySlot(p.InvAbs, bag);
            if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
            Status.Status($"Took out {ItemLine(itemId, p.Count)}.", bad: false);
        }
        if (shown) Refresh();
    }

    internal static string ItemLine(int itemId, int count) =>
        count > 1 ? $"{ItemData.DisplayName(itemId)} x{count:n0}" : ItemData.DisplayName(itemId);
}
