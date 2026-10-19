using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ItemCombineFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly ItemCombineSession itemCombine;
    private readonly SelfSession self;
    private readonly IItemCombineRequests requests;
    private readonly VendorFeature vendor;
    private readonly IInteractionDialogs interactionDialogs;

    public ItemCombineFeature(IWorldContext context, ItemCombineSession itemCombine, SelfSession self, IItemCombineRequests requests, VendorFeature vendor, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.itemCombine = itemCombine;
        this.self = self;
        this.requests = requests;
        this.vendor = vendor;
        this.interactionDialogs = interactionDialogs;
    }

    private const string TablePath = "res://assets/ui/item_combine.json";
    private const int Sockets = ItemCombine.MaterialSlots + 1;
    internal const int ShadowSocket = ItemCombine.MaterialSlots;
    private const int Columns = 5;
    private const float ResultSize = 56f;
    private const float SocketSize = 44f;
    private const int BodyWidth = 470;
    private const int PleaseWaitText = 16103;
    private const string Hint = "Right-click a bag item to place it";

    private CombineBook combineBook = CombineBook.Empty;

    private CanvasLayer layer = null!;
    private QuantityPrompt combineAmount = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private DetailStrip strip = null!;
    private FooterBand footer = null!;
    private ItemSlotView result = null!;
    private Button button = null!;
    private readonly ItemSlotView[] slots = new ItemSlotView[Sockets];
    private readonly CombineEntry?[] entries = new CombineEntry?[Sockets];
    private BagCompanion? companion;
    private bool shown;

    public bool IsOpen => shown;

    private bool waiting;
    private int npcTemplate;
    private double cooldownUntil = double.NegativeInfinity;

    public void Build()
    {
        interactionDialogs.InteractionCloses(this);
        BuildPanel();
        BuildCombineRecipeBook();
        itemCombine.ItemCombineReplyEvent += OnReply;
        itemCombine.CombineEffectEvent += OnCombineEffect;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (combineBookShown) CloseCombineRecipeBook();
        else if (IsOpen) Close();
        else return false;
        return true;
    }

    public void Dispose()
    {
        itemCombine.ItemCombineReplyEvent -= OnReply;
        itemCombine.CombineEffectEvent -= OnCombineEffect;
    }

    private void EnsureCombineBook()
    {
        if (combineBook.Recipes.Length > 0 || !Godot.FileAccess.FileExists(TablePath)) return;
        combineBook = ItemCombine.Parse(Godot.FileAccess.GetFileAsString(TablePath));
    }

    private static string CombineText(int id, string fallback) => ItemData.Text(id, fallback);

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);
        combineAmount = new QuantityPrompt(UiLayers.Prompts);
        context.Root.AddChild(combineAmount);

        Panel = new HudWindow("itemcombine", "Item Combination", bodyMinWidth: BodyWidth) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 6);

        var well = ServiceKit.Well();
        root.AddChild(well);
        var bench = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bench.AddThemeConstantOverride("separation", 10);
        well.AddChild(bench);

        result = new ItemSlotView(ResultSize);
        result.Hovered += s => { if (!s.Item.IsEmpty) context.Tooltips.Show(-1, s.Item); };
        result.Unhovered += _ => context.Tooltips.Hide();
        bench.AddChild(UiKit.UpgradeColumn("Result", result));

        var materials = new GridContainer { Columns = Columns };
        materials.AddThemeConstantOverride("h_separation", UiKit.AnvilSocketGap);
        materials.AddThemeConstantOverride("v_separation", UiKit.AnvilSocketGap);
        for (int i = 0; i < ItemCombine.MaterialSlots; i++)
            materials.AddChild(CombineSocket(i));

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(UiKit.UpgradeColumn("Combination item", materials));
        row.AddChild(UiKit.BenchSign("+"));
        row.AddChild(UiKit.UpgradeColumn(ItemData.DisplayName(ItemCombine.ShadowPiece), CombineSocket(ShadowSocket)));
        bench.AddChild(row);

        strip = new DetailStrip(ResultSize);
        strip.Slot.Visible = false;
        var formula = UiTheme.SmallButton("Formula", "Open the recipe book");
        formula.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        formula.Pressed += () => { if (!waiting) OpenCombineRecipeBook(); };
        strip.Right.AddChild(formula);
        var cancel = UiTheme.SmallButton("Cancel", "Take every material back");
        cancel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        cancel.Pressed += () => { if (!waiting) ClearCombineSockets(); };
        strip.Right.AddChild(cancel);
        button = UiTheme.ActionButton("Combine", "Combine the materials");
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        button.Pressed += Send;
        strip.Right.AddChild(button);
        root.AddChild(strip);

        footer = new FooterBand { Hint = Hint };
        root.AddChild(footer);
        ShowPrompt();
    }

    private ItemSlotView CombineSocket(int index)
    {
        var socket = new ItemSlotView(SocketSize) { Index = index, Veiled = true };
        socket.RightClicked += _ => ClearCombineSocket(index);
        socket.DoubleClicked += _ => ClearCombineSocket(index);
        socket.Hovered += s => { if (!s.Item.IsEmpty) context.Tooltips.Show(-1, s.Item); };
        socket.Unhovered += _ => context.Tooltips.Hide();
        socket.DragOut = _ => new Godot.Collections.Dictionary { { "companionFrom", index } };
        socket.CanDrop = (_, data) => CombineDropSlot(index, data) >= 0;
        socket.Dropped = (_, data) => DropOnCombine(index, data);
        slots[index] = socket;
        return socket;
    }

    private void ShowPrompt()
    {
        strip.Title.Text = CombineText(ItemCombine.PutMaterialText, "Put on material");
        strip.Sub.Text = "The craftsman decides what the materials make.";
        strip.Sub.AddThemeColorOverride("font_color", UiTheme.TextLo);
    }

    internal void Open(int npcTemplate)
    {
        EnsureCombineBook();
        this.npcTemplate = npcTemplate;
        context.NpcDialog.TrackTalker(CombineCraftsman());
        waiting = false;
        vendor.Close();
        ClearCombineSockets();
        result.Clear();
        footer.ResetStatus();
        Show();
        companion ??= new BagCompanion(CombineTakeFromBag, CombineBagFit, _ => "", Close, (from, to, _) => CombineIntoBag(from, to));
        context.BagCompanions.Attach(companion);
    }

    internal void Show()
    {
        Panel.Visible = true;
        shown = true;
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        context.Tooltips.Hide();
        context.BagCompanions.ReleaseHold();
        ClearCombineSockets();
        CloseCombineRecipeBook();
        combineAmount.Close();
        if (companion != null) context.BagCompanions.Detach(companion);
    }

    private int StagedSocketOf(int abs)
    {
        for (int i = 0; i < Sockets; i++)
            if (entries[i] is { } e && Inventory.GridStart + e.BagSlot == abs) return i;
        return -1;
    }

    private bool CombineTakeFromBag(int abs)
    {
        if (!self.Inventory.InMainBag(abs) || self.Inventory[abs].IsEmpty) return false;
        if (waiting) return true;
        int staged = StagedSocketOf(abs);
        if (staged >= 0)
        {
            ClearCombineSocket(staged);
            return true;
        }
        int socket = FreeCombineSocket(self.Inventory[abs].ItemId);
        if (socket < 0)
        {
            footer.Status("All material sockets are full.", bad: true);
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
            return entries[ShadowSocket] == null ? ShadowSocket : -1;
        for (int i = 0; i < ItemCombine.MaterialSlots; i++)
            if (entries[i] == null) return i;
        return -1;
    }

    private int CombineDropSlot(int socket, Variant data)
    {
        if (waiting || data.VariantType != Variant.Type.Dictionary) return -1;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey("invFrom")) return -1;
        int abs = d["invFrom"].AsInt32();
        if (!self.Inventory.InMainBag(abs) || self.Inventory[abs].IsEmpty || StagedSocketOf(abs) >= 0) return -1;
        bool shadow = self.Inventory[abs].ItemId == ItemCombine.ShadowPiece;
        if (shadow) return socket == ShadowSocket && entries[socket] == null ? socket : -1;
        if (socket == ShadowSocket) return -1;
        return entries[socket] == null ? socket : FreeCombineSocket(self.Inventory[abs].ItemId);
    }

    private void DropOnCombine(int socket, Variant data)
    {
        int target = CombineDropSlot(socket, data);
        if (target >= 0) AskCombineCount(target, data.AsGodotDictionary()["invFrom"].AsInt32());
    }

    private void AskCombineCount(int socket, int abs)
    {
        var slot = self.Inventory[abs];
        bool stack = ItemData.Get(slot.ItemId) is { Countable: not 0 } && socket != ShadowSocket && slot.Count > 1;
        if (!stack)
        {
            StageCombine(socket, abs, 1);
            return;
        }
        combineAmount.Open(ItemIcons.Get(slot.ItemId), ItemData.DisplayName(slot.ItemId),
            CombineText(ItemCombine.QuantityText, "Please enter the quantity."), Math.Min((int)slot.Count, ItemCombine.MaxCount), 1,
            n => StageCombine(socket, abs, (int)n));
    }

    internal void StageCombine(int socket, int abs, int count)
    {
        if (!shown || waiting || !self.Inventory.InMainBag(abs) || self.Inventory[abs].IsEmpty || entries[socket] != null) return;
        var item = self.Inventory[abs];
        entries[socket] = new CombineEntry(item.ItemId, count, abs - Inventory.GridStart);
        var shownSlot = item;
        shownSlot.Count = (short)count;
        slots[socket].Set(shownSlot);
        result.Clear();
        footer.ResetStatus();
        context.BagCompanions.RefreshFit();
    }

    private void ClearCombineSocket(int index)
    {
        if (waiting || entries[index] == null) return;
        entries[index] = null;
        slots[index].Clear();
        context.BagCompanions.RefreshFit();
    }

    private void ClearCombineSockets()
    {
        for (int i = 0; i < Sockets; i++)
        {
            entries[i] = null;
            slots[i]?.Clear();
        }
        context.BagCompanions.RefreshFit();
    }

    private double CombineNow => FrameClock.Now();

    private void Send()
    {
        if (!shown || waiting) return;
        double left = cooldownUntil - CombineNow;
        if (left > ItemCombine.CooldownReady)
        {
            footer.Status(CombineText(ItemCombine.CooldownText, "You can use it after %d seconds.")
                .Replace("%d", ItemCombine.SecondsLeft(left).ToString()), bad: true);
            return;
        }
        cooldownUntil = CombineNow + ItemCombine.Cooldown;

        var materials = entries.Take(ItemCombine.MaterialSlots).OfType<CombineEntry>().ToList();
        if (materials.Count == 0)
        {
            footer.Status(CombineText(ItemCombine.PutMaterialText, "Put on material"), bad: true);
            return;
        }
        if (self.Inventory.FreeGridSlots().Count == 0)
        {
            footer.Status(CombineText(ItemCombine.NoBagSlotText, "You do not have an empty slot in your inventory."), bad: true);
            return;
        }

        var shadow = entries[ShadowSocket];
        waiting = true;
        foreach (var entry in entries.OfType<CombineEntry>())
            context.BagCompanions.Hold.Hold(Inventory.GridStart + entry.BagSlot, self.Inventory[Inventory.GridStart + entry.BagSlot]);
        strip.Sub.Text = CombineText(PleaseWaitText, "Please wait");
        requests.Send(CombineCraftsman(), shadow?.ItemId ?? 0, shadow?.BagSlot ?? 0, ItemCombine.WireOrder(materials));
    }

    private void OnReply(CombineReply reply)
    {
        if (!waiting) return;
        waiting = false;
        context.BagCompanions.ReleaseHold();
        if (!ItemCombine.IsDone(reply.Result))
        {
            ShowPrompt();
            footer.Status(CombineText(ItemCombine.WrongMaterialText, "Wrong material"), bad: true);
            return;
        }

        foreach (var entry in entries.OfType<CombineEntry>())
        {
            int abs = Inventory.GridStart + entry.BagSlot;
            if (self.Inventory[abs].ItemId == entry.ItemId) self.SetInventorySlot(abs, Inventory.Consumed(self.Inventory[abs], entry.Count));
        }
        ClearCombineSockets();
        context.Inventory.Refresh();
        ShowPrompt();

        var row = combineBook.Row(reply.Row);
        if (reply.Result == ItemCombine.Succeeded)
        {
            var made = self.Inventory[Inventory.GridStart + reply.BagSlot];
            if (!made.IsEmpty)
            {
                result.Set(made);
                string received = $"{ItemData.DisplayName(made.ItemId)} {CombineText(ItemCombine.ReceivedText, "has been received")}";
                footer.Status(received, bad: false);
                Notice.Show(context.Root, received, "Item Combination");
            }
            if (row != null) context.NpcDialog.Balloon([row.SuccessText]);
        }
        else
        {
            footer.Status("The combination failed. The materials are gone.", bad: true);
            if (row != null) context.NpcDialog.Balloon([row.FailureText]);
        }
    }

    private int CombineCraftsman() => context.Self.Body == null
    ? ItemCombine.NoCraftsman
    : ItemCombine.Craftsman(
        context.Entities.All().Where(e => !e.Dead).Select(e => (e.Id, e.NpcId, Coord.FlatDistance(context.Self.Body.Position, e.Position))),
        npcTemplate);

    private void OnCombineEffect(CombineEffect effect)
    {
        EnsureCombineBook();
        if (combineBook.Row(effect.Row) is not { } row) return;
        int fxId = effect.Success ? row.SuccessFx : row.FailureFx;
        if (Fx.NameForId(fxId) is not { } fx) return;
        foreach (var ent in context.Entities.All())
            if (ent.NpcId == effect.NpcId && !ent.Dead && context.Self.Body != null
                && Coord.FlatDistance(context.Self.Body.Position, ent.Position) <= NpcQuestWire.InteractRange * 2)
                context.Entities.SpawnFxOn(ent.Id, fx, 0f);
    }

    private const int CombineBookWidth = 760;
    private const float CombineBookHeight = 400f;
    private const float CombineBookListWidth = 210f;
    private const float CombineBookRecipeWidth = 250f;
    private const float CombineBookDetailWidth = 284f;
    private const int CombineBookIconSize = 28;
    private const float CombineBookSlotSize = 40f;
    private static readonly Color CombineUncraftable = new("6f9cff");

    private CanvasLayer combineBookLayer = null!;
    internal HudWindow CombineBookPanel { get; private set; } = null!;
    private VBoxContainer combineBookCategories = null!;
    private VBoxContainer combineBookRecipes = null!;
    private VBoxContainer combineBookDetail = null!;
    private Label combineBookRecipeTitle = null!;
    private bool combineBookShown;
    private int combineBookCategory = -1;
    private int combineBookRecipe = -1;

    internal void BuildCombineRecipeBook()
    {
        combineBookLayer = new CanvasLayer { Layer = UiLayers.Prompts };
        context.Root.AddChild(combineBookLayer);

        CombineBookPanel = new HudWindow("combinerecipes", "Book of Transformation", bodyMinWidth: CombineBookWidth) { Visible = false };
        CombineBookPanel.Closed += CloseCombineRecipeBook;
        combineBookLayer.AddChild(CombineBookPanel);

        var columns = new HBoxContainer { CustomMinimumSize = new Vector2(0, CombineBookHeight) };
        columns.AddThemeConstantOverride("separation", 8);
        CombineBookPanel.Body.AddChild(columns);

        combineBookCategories = CombineBookColumn(columns, CombineText(ItemCombine.ClassesText, "Compounding Item Class"), CombineBookListWidth, out _);
        combineBookRecipes = CombineBookColumn(columns, "", CombineBookRecipeWidth, out combineBookRecipeTitle);
        combineBookDetail = CombineBookColumn(columns, "", CombineBookDetailWidth, out var detailHeading);
        detailHeading.Visible = false;
    }

    private static VBoxContainer CombineBookColumn(HBoxContainer columns, string title, float width, out Label heading)
    {
        var section = UiTheme.Section();
        section.CustomMinimumSize = new Vector2(width, 0);
        columns.AddChild(section);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        section.AddChild(box);
        heading = UiTheme.SectionTitle(title);
        box.AddChild(heading);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        box.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);
        return list;
    }

    internal void OpenCombineRecipeBook()
    {
        if (combineBookShown) return;
        EnsureCombineBook();
        combineBookShown = true;
        CombineBookPanel.Visible = true;
        combineBookCategory = -1;
        combineBookRecipe = -1;
        RenderCombineBook();
    }

    internal void CloseCombineRecipeBook()
    {
        if (!combineBookShown) return;
        combineBookShown = false;
        CombineBookPanel.Visible = false;
        context.Tooltips.Hide();
    }

    private void RenderCombineBook()
    {
        foreach (var child in combineBookCategories.GetChildren()) child.QueueFree();
        foreach (var category in combineBook.Shown)
        {
            int key = category.Key;
            combineBookCategories.AddChild(CombineBookRow(category.Name, null, key == combineBookCategory, UiTheme.TextHi,
                () => SelectCombineCategory(key)));
        }
        RenderCombineRecipes();
        RenderCombineDetail();
    }

    internal void SelectCombineCategory(int key)
    {
        combineBookCategory = key;
        combineBookRecipe = -1;
        RenderCombineBook();
    }

    internal void SelectCombineRecipe(int id)
    {
        combineBookRecipe = id;
        RenderCombineRecipes();
        RenderCombineDetail();
    }

    private void RenderCombineRecipes()
    {
        foreach (var child in combineBookRecipes.GetChildren()) child.QueueFree();
        var category = Array.Find(combineBook.Categories, c => c.Key == combineBookCategory);
        combineBookRecipeTitle.Text = category == null ? CombineText(ItemCombine.ItemListText, "Item list")
            : $"{category.Name} {CombineText(ItemCombine.ItemListText, "Item list")}";
        if (category == null) return;
        foreach (var recipe in combineBook.Listed(category.Key))
        {
            if (ItemData.Get(recipe.Result) == null) continue;
            int id = recipe.Id;
            bool craftable = ItemCombine.Craftable(recipe, item => ItemData.Get(item) != null);
            combineBookRecipes.AddChild(CombineBookRow(
                craftable ? recipe.Name : CombineText(ItemCombine.UncraftableText, "Item cannot be crafted"),
                ItemIcons.Get(recipe.Result), id == combineBookRecipe, craftable ? UiTheme.TextHi : CombineUncraftable,
                () => SelectCombineRecipe(id)));
        }
    }

    private void RenderCombineDetail()
    {
        foreach (var child in combineBookDetail.GetChildren()) child.QueueFree();
        if (combineBook.Row(combineBookRecipe) is not { } recipe)
        {
            var intro = UiTheme.Text(ItemCombine.Paragraphs(CombineText(ItemCombine.IntroText, "")), 12, UiTheme.TextLo);
            intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            intro.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            combineBookDetail.AddChild(intro);
            return;
        }

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        combineBookDetail.AddChild(head);
        head.AddChild(CombineBookSlot(recipe.Result, 1));
        var name = UiTheme.Text(recipe.Name, 15, ItemGrade.Tint(recipe.Result));
        name.VerticalAlignment = VerticalAlignment.Center;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        head.AddChild(name);

        foreach (var material in recipe.Materials)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 10);
            combineBookDetail.AddChild(line);
            line.AddChild(CombineBookSlot(material.Item, material.Count));
            var label = UiTheme.Text(ItemData.Get(material.Item) == null
                ? CombineText(ItemCombine.UncraftableText, "Item cannot be crafted")
                : ItemData.DisplayName(material.Item), 13, UiTheme.TextHi);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.AddChild(label);
            var count = UiTheme.Text($"x{material.Count}", 13, UiTheme.Gold);
            count.VerticalAlignment = VerticalAlignment.Center;
            line.AddChild(count);
        }
    }

    private ItemSlotView CombineBookSlot(int itemId, int count)
    {
        var slot = new ItemSlotView(CombineBookSlotSize);
        if (ItemData.Get(itemId) != null) slot.Set(UiKit.TooltipItem(itemId, count));
        slot.Hovered += s => { if (!s.Item.IsEmpty) context.Tooltips.Show(-1, s.Item); };
        slot.Unhovered += _ => context.Tooltips.Hide();
        return slot;
    }

    private static Button CombineBookRow(string text, Texture2D? icon, bool selected, Color colour, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            Icon = icon,
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.None,
            ClipText = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.AddThemeConstantOverride("icon_max_width", CombineBookIconSize);
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_color", colour);
        button.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        button.AddThemeStyleboxOverride("normal", UiTheme.ListRow(selected));
        button.AddThemeStyleboxOverride("hover", UiTheme.ListRow(true));
        button.AddThemeStyleboxOverride("pressed", UiTheme.ListRow(true));
        button.Pressed += pressed;
        return button;
    }
}
