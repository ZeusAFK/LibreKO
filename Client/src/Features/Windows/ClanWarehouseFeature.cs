using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class ClanWarehouseFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly ClanWarehouseSession clanWarehouse;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly IClanWarehouseRequests requests;
    private readonly WarehouseFeature warehouse;
    private readonly IInteractionDialogs interactionDialogs;

    public ClanWarehouseFeature(IWorldContext context, ClanSession clan, ClanWarehouseSession clanWarehouse, ProgressSession progress, SelfSession self, IClanWarehouseRequests requests, WarehouseFeature warehouse, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.clan = clan;
        this.clanWarehouse = clanWarehouse;
        this.progress = progress;
        this.self = self;
        this.requests = requests;
        this.warehouse = warehouse;
        this.interactionDialogs = interactionDialogs;
    }

    internal const int Slots = ClanWarehouseWire.NetSlots;
    private const int PageSize = 24;
    private const int Pages = Slots / PageSize;
    private const int Columns = 6;
    private const float CellSize = 44f;
    private const string Hint = "Right-click to store or take out · drag to place";

    private const string RightsNote = "Anyone may store; the chief or vice-chief takes out.";

    private const string NotInText = "You are not in a clan.";
    private const string NoRightsText = "Only the chief or vice-chief can take items out.";
    private const string NotStorableText = "This item cannot be stored in the clan bank.";

    private readonly ItemSlot[] slots = new ItemSlot[Slots];
    private int money;
    private int page;
    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private bool shown;

    public bool IsOpen => shown;

    internal IClosableWindow AmountPromptPanel => amountPrompt;

    private readonly ItemSlotView[] cells = new ItemSlotView[PageSize];
    private BagCompanion? companion;
    private ServiceTabs pageTabs = null!;
    private Label slotsNote = null!;
    private StatusLabel status = null!;
    private MoneyPlaque stored = null!;
    private Button depositBtn = null!;
    private Button withdrawBtn = null!;
    private QuantityPrompt amountPrompt = null!;

    private struct ClanPending { public WarehouseSubOpcode Op; public bool Gold; public bool Merge; public int InvAbs; public int WhIdx; public int WhTo; public int Count; }

    private ClanPending pending;
    private bool inFlight;

    private bool CanWithdraw => clan.MyClan.IsChief || clan.MyClan.IsViceChief;

    public void Build()
    {
        interactionDialogs.InteractionCloses(this);
        BuildPanel();
        warehouse.BlockTransfersWhile(() => inFlight);
        context.Inventory.Refreshed += OnInventoryRefreshed;
        clanWarehouse.ClanWhContentsEvent += OnContents;
        clanWarehouse.ClanWhResultEvent += OnResult;
        progress.GoldChangeEvent += OnGold;
    }

    public void Dispose()
    {
        context.Inventory.Refreshed -= OnInventoryRefreshed;
        clanWarehouse.ClanWhContentsEvent -= OnContents;
        clanWarehouse.ClanWhResultEvent -= OnResult;
        progress.GoldChangeEvent -= OnGold;
    }

    private void OnGold(GoldChange change) { if (shown) RefreshGold(); }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("clanwarehouse", "Clan Warehouse") { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        amountPrompt = new QuantityPrompt(UiLayers.Prompts);
        context.Root.AddChild(amountPrompt);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 6);
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
            cell.RightClicked += c => AskWithdraw(Abs(c.Index), -1);
            cell.Hovered += c => context.Tooltips.Show(-1, c.Item);
            cell.Unhovered += _ => context.Tooltips.Hide();
            cell.Wheeled += (_, step) => TurnPage(step);
            cell.DragOut = c => new Godot.Collections.Dictionary { { "companionFrom", Abs(c.Index) } };
            cell.CanDrop = (c, data) => CanDropOn(Abs(c.Index), data);
            cell.Dropped = (c, data) => DropOn(Abs(c.Index), data);
            cells[i] = cell;
            grid.AddChild(cell);
        }

        pageTabs = new ServiceTabs();
        var pages = new List<string>();
        for (int p = 0; p < Pages; p++) pages.Add($"{p + 1}");
        pageTabs.SetTabs(pages, 0);
        pageTabs.Selected += page =>
        {
            this.page = page;
            Refresh();
        };
        root.AddChild(pageTabs);

        status = new StatusLabel { Hint = Hint };
        root.AddChild(status);

        var footer = new FooterBand();
        stored = new MoneyPlaque("Clan gold");
        footer.Left.AddChild(stored);
        depositBtn = UiTheme.SmallButton("Deposit", "Put gold into the clan bank");
        depositBtn.Pressed += () => AskGoldTransfer(deposit: true);
        withdrawBtn = UiTheme.SmallButton("Withdraw", "Take gold out of the clan bank");
        withdrawBtn.Pressed += () => AskGoldTransfer(deposit: false);
        footer.Right.AddChild(depositBtn);
        footer.Right.AddChild(withdrawBtn);
        root.AddChild(footer);
    }

    private int Abs(int cell) => page * PageSize + cell;

    internal void Show()
    {
        Panel.Visible = true;
        shown = true;
    }

    internal void Toggle()
    {
        if (shown) { Close(); return; }
        if (!clan.MyClan.InClan) { context.Log.Notice(NotInText); return; }
        page = 0;
        pageTabs.Select(0, notify: false);
        status.ResetStatus();
        System.Array.Clear(slots, 0, slots.Length);
        money = 0;
        Show();
        companion ??= new BagCompanion(TakeFromBag, ClanBagFit, _ => "", Close, WithdrawInto);
        context.BagCompanions.Attach(companion);
        requests.ClanWhOpen();
        Refresh();
    }

    private bool TakeFromBag(int abs)
    {
        if (!self.Inventory.InMainBag(abs)) return false;
        DepositSlot(abs, -1);
        return true;
    }

    private bool WithdrawInto(int whIdx, int bagAbs, int? amount)
    {
        AskWithdraw(whIdx, bagAbs, amount);
        return true;
    }

    private BagFit ClanBagFit(int abs) =>
        ItemStacks.UseRefusal(ItemUse.ClanBank, self.Inventory[abs]) == ItemUseRules.Allowed ? BagFit.Normal : BagFit.Unfit;

    public void Close()
    {
        amountPrompt.Close();
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        context.Tooltips.Hide();
        if (companion != null) context.BagCompanions.Detach(companion);
    }

    internal void OnContents(int money, ItemSlot[] slots)
    {
        inFlight = false;
        this.money = money;
        for (int i = 0; i < Slots && i < slots.Length; i++) this.slots[i] = slots[i];
        if (shown) Refresh();
    }

    private void TurnPage(int step)
    {
        int next = Mathf.Clamp(page + step, 0, Pages - 1);
        if (next == page) return;
        page = next;
        pageTabs.Select(next, notify: false);
        Refresh();
    }

    private void OnInventoryRefreshed() { if (shown) Refresh(); }

    internal void Refresh()
    {
        for (int i = 0; i < PageSize; i++)
            cells[i].Set(slots[Abs(i)]);
        int used = slots.Count(s => !s.IsEmpty);
        slotsNote.Text = $"Slots used {used} / {Slots}";
        slotsNote.AddThemeColorOverride("font_color", used >= Slots ? UiTheme.Bad : UiTheme.TextDim);
        status.Hint = CanWithdraw ? Hint : RightsNote;
        RefreshGold();
        context.BagCompanions.RefreshFit();
    }

    private void RefreshGold()
    {
        stored.Value = money;
        depositBtn.Disabled = self.Sheet.Gold <= 0;
        withdrawBtn.Disabled = money <= 0 || !CanWithdraw;
        withdrawBtn.TooltipText = CanWithdraw ? "Take gold out of the clan bank" : NoRightsText;
    }

    private bool FitsIn(int whIdx, int itemId, int count) =>
        slots[whIdx].IsEmpty
        || (slots[whIdx].ItemId == itemId && UiKit.IsStackable(itemId) && slots[whIdx].Count + count <= Inventory.StackMax);

    private int WhDestination(int itemId, int count, out bool merge)
    {
        merge = false;
        if (UiKit.IsStackable(itemId))
            for (int i = 0; i < Slots; i++)
                if (slots[i].ItemId == itemId && slots[i].Count + count <= Inventory.StackMax)
                { merge = true; return i; }
        for (int i = 0; i < Slots; i++) if (slots[i].IsEmpty) return i;
        return -1;
    }

    private bool CanDropOn(int whIdx, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (d.ContainsKey("invFrom")) return self.Inventory.InMainBag(d["invFrom"].AsInt32());
        return d.ContainsKey("companionFrom") && d["companionFrom"].AsInt32() != whIdx;
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
        if (warehouse.StorageTransferBusy || !self.Inventory.InMainBag(abs) || self.Inventory[abs].IsEmpty) return;
        var slot = self.Inventory[abs];
        if (ItemStacks.UseRefusal(ItemUse.ClanBank, slot) is not ItemUseRules.Allowed and var bankRefusal)
        {
            status.Status(ItemData.Text(bankRefusal, NotStorableText), bad: true);
            return;
        }
        if (amount == null && UiKit.AsksStackAmount(slot))
            amountPrompt.Open(ItemIcons.Get(slot.ItemId), $"Store {ItemData.DisplayName(slot.ItemId)}",
                $"You carry {slot.Count:n0}", slot.Count, slot.Count, n => { if (shown && self.Inventory[abs].Equals(slot)) DepositCount(abs, (int)n, target); }, "Store");
        else
            DepositCount(abs, amount ?? Mathf.Max(1, (int)slot.Count), target);
    }

    private void DepositCount(int abs, int count, int target)
    {
        if (warehouse.StorageTransferBusy || !self.Inventory.InMainBag(abs) || self.Inventory[abs].IsEmpty || count <= 0) return;
        var slot = self.Inventory[abs];
        count = Mathf.Min(count, slot.Count > 0 ? slot.Count : 1);
        int whIdx;
        bool merge;
        if (target >= 0)
        {
            if (!FitsIn(target, slot.ItemId, count)) { status.Status("That slot is taken.", bad: true); return; }
            whIdx = target;
            merge = !slots[target].IsEmpty;
        }
        else
        {
            whIdx = WhDestination(slot.ItemId, count, out merge);
            if (whIdx < 0) { status.Status("The clan bank is full.", bad: true); return; }
        }
        pending = new ClanPending { Op = WarehouseSubOpcode.Input, Merge = merge, InvAbs = abs, WhIdx = whIdx, Count = count };
        inFlight = true;
        requests.ClanWhInput(slot.ItemId, (byte)(whIdx / PageSize),
            (byte)(abs - Inventory.GridStart), (byte)(whIdx % PageSize), count);
    }

    internal void AskWithdraw(int whIdx, int target, int? amount = null)
    {
        if (warehouse.StorageTransferBusy || whIdx < 0 || whIdx >= Slots || slots[whIdx].IsEmpty) return;
        if (!CanWithdraw) { status.Status(NoRightsText, bad: true); return; }
        var slot = slots[whIdx];
        if (amount == null && UiKit.AsksStackAmount(slot))
            amountPrompt.Open(ItemIcons.Get(slot.ItemId), $"Take out {ItemData.DisplayName(slot.ItemId)}",
                $"Stored {slot.Count:n0}", slot.Count, slot.Count, n => { if (shown && slots[whIdx].Equals(slot)) WithdrawCount(whIdx, (int)n, target); }, "Take out");
        else
            WithdrawCount(whIdx, amount ?? Mathf.Max(1, (int)slot.Count), target);
    }

    private void WithdrawCount(int whIdx, int count, int target)
    {
        if (warehouse.StorageTransferBusy || whIdx < 0 || whIdx >= Slots || slots[whIdx].IsEmpty || count <= 0) return;
        var slot = slots[whIdx];
        count = Mathf.Min(count, slot.Count > 0 ? slot.Count : 1);
        int dest;
        bool merge;
        if (target >= 0)
        {
            if (!warehouse.FitsInBag(target, slot.ItemId, count)) { status.Status("That slot is taken.", bad: true); return; }
            dest = target;
            merge = !self.Inventory[target].IsEmpty;
        }
        else
        {
            dest = warehouse.BagDestination(slot.ItemId, count, out merge);
            if (dest < 0) { status.Status("Your bags are full.", bad: true); return; }
        }
        pending = new ClanPending { Op = WarehouseSubOpcode.Output, Merge = merge, InvAbs = dest, WhIdx = whIdx, Count = count };
        inFlight = true;
        requests.ClanWhOutput(slot.ItemId, (byte)(whIdx / PageSize),
            (byte)(whIdx % PageSize), (byte)(dest - Inventory.GridStart), count);
    }

    private void MoveInside(int from, int to, int? amount)
    {
        if (warehouse.StorageTransferBusy || from == to || from < 0 || to < 0 || from >= Slots || to >= Slots || slots[from].IsEmpty) return;
        if (!CanWithdraw) { status.Status(NoRightsText, bad: true); return; }
        if (ItemStacks.IsPartOfStack(amount, slots[from])) { status.Status(GameText.StorageWholeStack, bad: true); return; }
        if (!slots[to].IsEmpty) { status.Status("That slot is taken.", bad: true); return; }
        pending = new ClanPending { Op = WarehouseSubOpcode.Move, WhIdx = from, WhTo = to, Count = slots[from].Count };
        inFlight = true;
        requests.ClanWhMoveInside(slots[from].ItemId, (byte)(from / PageSize), (byte)(from % PageSize),
            (byte)(to / PageSize), (byte)(to % PageSize));
    }

    private long GoldRoom(bool deposit) => CharacterSheet.CoinMax - (long)(deposit ? money : self.Sheet.Gold);

    private void AskGoldTransfer(bool deposit)
    {
        if (inFlight) return;
        if (!deposit && !CanWithdraw) { status.Status(NoRightsText, bad: true); return; }
        long have = deposit ? self.Sheet.Gold : money;
        if (have <= 0) return;
        long max = Math.Min(have, GoldRoom(deposit));
        if (max <= 0)
        {
            status.Status(deposit ? "The clan bank can't hold more gold." : "You can't carry more gold.", bad: true);
            return;
        }
        string note = ItemData.Text(GameText.GoldCapNote, "You cannot enter if it's more than 2.1 billion.");
        amountPrompt.Open(null, deposit ? "Deposit gold" : "Withdraw gold",
            deposit ? $"You carry {have:n0}" : $"Stored {have:n0}", max, max, n => GoldTransfer(deposit, (int)n),
            deposit ? "Deposit" : "Withdraw", _ => note);
    }

    private void GoldTransfer(bool deposit, int amount)
    {
        if (warehouse.StorageTransferBusy || amount <= 0) return;
        if (deposit && amount > self.Sheet.Gold) { status.Status("Not enough carried gold.", bad: true); return; }
        if (!deposit && amount > money) { status.Status("Not enough clan gold.", bad: true); return; }
        pending = new ClanPending { Op = deposit ? WarehouseSubOpcode.Input : WarehouseSubOpcode.Output, Gold = true, Count = amount };
        inFlight = true;
        if (deposit) requests.ClanWhInput(LootWire.GoldItemId, 0, 0, 0, amount);
        else requests.ClanWhOutput(LootWire.GoldItemId, 0, 0, 0, amount);
    }

    private static string RefusalText(ClanWarehouseResult result) => result switch
    {
        ClanWarehouseResult.NotInClan => NotInText,
        ClanWarehouseResult.NoRights => NoRightsText,
        ClanWarehouseResult.NotStorable => NotStorableText,
        _ => "Transfer failed.",
    };

    private void OnResult(WarehouseSubOpcode op, ClanWarehouseResult result)
    {
        if (op == WarehouseSubOpcode.Open)
        {
            string refusal = RefusalText(result);
            context.Log.Notice(refusal);
            if (result == ClanWarehouseResult.NotInClan) Close();
            else if (shown) status.Status(refusal, bad: true);
            return;
        }

        if (!inFlight) return;
        inFlight = false;
        if (result != ClanWarehouseResult.Succeeded)
        {
            status.Status(result == ClanWarehouseResult.Failed && op == WarehouseSubOpcode.Output
                ? ItemData.Text(WarehouseFeature.NoRoomText, "Your bags are full.") : RefusalText(result), bad: true);
            if (shown) Refresh();
            return;
        }

        var p = pending;
        if (p.Gold)
        {
            if (p.Op == WarehouseSubOpcode.Input) { money += p.Count; progress.TransferGoldOut(p.Count); }
            else { money -= p.Count; progress.TransferGoldIn(p.Count); }
            status.Status(p.Op == WarehouseSubOpcode.Input ? $"Deposited {p.Count:n0} gold." : $"Withdrew {p.Count:n0} gold.", bad: false);
        }
        else if (p.Op == WarehouseSubOpcode.Move)
        {
            int itemId = slots[p.WhIdx].ItemId;
            slots[p.WhTo] = slots[p.WhIdx];
            slots[p.WhIdx] = default;
            status.Status($"Moved {ItemData.DisplayName(itemId)}.", bad: false);
        }
        else if (p.Op == WarehouseSubOpcode.Input)
        {
            var bag = self.Inventory[p.InvAbs];
            int itemId = bag.ItemId;
            ItemStacks.MoveStack(ref bag, ref slots[p.WhIdx], p.Count, p.Merge);
            self.SetInventorySlot(p.InvAbs, bag);
            if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
            status.Status($"Stored {WarehouseFeature.ItemLine(itemId, p.Count)}.", bad: false);
        }
        else
        {
            var bag = self.Inventory[p.InvAbs];
            int itemId = slots[p.WhIdx].ItemId;
            ItemStacks.MoveStack(ref slots[p.WhIdx], ref bag, p.Count, p.Merge);
            self.SetInventorySlot(p.InvAbs, bag);
            if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
            status.Status($"Took out {WarehouseFeature.ItemLine(itemId, p.Count)}.", bad: false);
        }
        if (shown) Refresh();
    }
}
