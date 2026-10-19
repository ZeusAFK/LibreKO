using Godot;

namespace LibreKO.Features.Windows;

internal sealed class PetFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly PetSession pet;
    private readonly SelfSession self;
    private readonly IPetRequests requests;
    private readonly PetHatchFeature hatch;

    public PetFeature(IWorldContext context, PetSession pet, SelfSession self, IPetRequests requests, PetHatchFeature petHatch)
    {
        this.context = context;
        this.pet = pet;
        this.self = self;
        this.requests = requests;
        hatch = petHatch;
    }

    private const int FoodKind = 176;
    private const string NoneText = "No familiar is out. Equip one and use a Familiar Summon.";

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private Label nameLbl = null!;
    private Label levelLbl = null!;
    private Label status = null!;

    private StatBar hpBar = null!;
    private StatBar mpBar = null!;
    private StatBar expBar = null!;
    private StatBar satBar = null!;
    private Button attackBtn = null!;
    private Button defendBtn = null!;
    private Button lootBtn = null!;
    private Button feedBtn = null!;
    private Button dismissBtn = null!;
    private readonly ItemSlotView[] bagCells = new ItemSlotView[PetSheet.InventorySize];
    private bool shown;

    internal bool IsOpen => shown;

    private const float BagCellSize = 44f;
    private const string BagHint = "Familiar items only, one of each kind. Automatic Looting lets Looting mode pick up loot.";

    public void Build()
    {
        BuildPanel();
        pet.PetSummonedEvent += OnSummoned;
        pet.PetGoneEvent += OnGone;
        pet.PetModeEvent += OnMode;
        pet.PetVitalsEvent += RefreshUI;
        pet.PetExpEvent += OnExp;
        pet.PetFedEvent += OnFed;
        pet.PetFoodRefusedEvent += OnFoodRefused;
        pet.PetStrikeEvent += OnStrike;
        pet.PetHpDeltaEvent += OnHpDelta;
        pet.PetMpDeltaEvent += OnMpDelta;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (IsOpen) Toggle();
        else return false;
        return true;
    }

    public void Dispose()
    {
        pet.PetSummonedEvent -= OnSummoned;
        pet.PetGoneEvent -= OnGone;
        pet.PetModeEvent -= OnMode;
        pet.PetVitalsEvent -= RefreshUI;
        pet.PetExpEvent -= OnExp;
        pet.PetFedEvent -= OnFed;
        pet.PetFoodRefusedEvent -= OnFoodRefused;
        pet.PetStrikeEvent -= OnStrike;
        pet.PetHpDeltaEvent -= OnHpDelta;
        pet.PetMpDeltaEvent -= OnMpDelta;
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("pet", "Familiar", new Vector2(90, 140), 280) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 7);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        root.AddChild(header);
        nameLbl = UiTheme.Text("", 16, UiTheme.GoldBright);
        nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(nameLbl);
        levelLbl = UiTheme.Text("", 13, UiTheme.TextLo, HorizontalAlignment.Right);
        header.AddChild(levelLbl);

        hpBar = Bar(root, "HP", UiTheme.Hp);
        mpBar = Bar(root, "MP", UiTheme.Mp);
        expBar = Bar(root, "EXP", UiTheme.Gold);
        satBar = Bar(root, "Satisfaction", UiTheme.Good);

        root.AddChild(UiTheme.Rule());
        root.AddChild(UiTheme.SectionTitle("Mode"));
        var modes = new HBoxContainer();
        modes.AddThemeConstantOverride("separation", 6);
        root.AddChild(modes);
        attackBtn = ModeButton(modes, "Attack", PetMode.Attack);
        defendBtn = ModeButton(modes, "Defend", PetMode.Defence);
        lootBtn = ModeButton(modes, "Loot", PetMode.Looting);

        root.AddChild(UiTheme.Rule());
        root.AddChild(UiTheme.SectionTitle("Bag"));
        var bag = new HBoxContainer { TooltipText = BagHint };
        bag.AddThemeConstantOverride("separation", 6);
        root.AddChild(bag);
        for (int i = 0; i < bagCells.Length; i++)
        {
            var cell = new ItemSlotView(BagCellSize) { Index = i, Veiled = true, CanDrop = CanDropOnBag, Dropped = DropOnBag };
            cell.RightClicked += c => TakeFromBag(c.Index);
            bagCells[i] = cell;
            bag.AddChild(cell);
        }

        root.AddChild(UiTheme.Rule());
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        root.AddChild(actions);
        feedBtn = UiTheme.ActionButton("Feed", "Give your familiar the most filling food in your bag");
        feedBtn.Pressed += Feed;
        actions.AddChild(feedBtn);
        dismissBtn = UiTheme.ActionButton("Dismiss", "Send your familiar away");
        dismissBtn.Pressed += Dismiss;
        actions.AddChild(dismissBtn);

        status = HudStyle.Label(12);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(status);

        RefreshUI();
    }

    private static StatBar Bar(VBoxContainer root, string caption, Color tint)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        root.AddChild(row);
        var label = UiTheme.Text(caption, 12, UiTheme.TextLo);
        label.CustomMinimumSize = new Vector2(78, 0);
        row.AddChild(label);
        var bar = new StatBar(tint, new Vector2(168, 14)) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(bar);
        return bar;
    }

    private Button ModeButton(HBoxContainer row, string text, PetMode mode)
    {
        var btn = UiTheme.UnderlineTabButton(text, 13);
        btn.Pressed += () => RequestMode(mode);
        row.AddChild(btn);
        return btn;
    }

    internal void Toggle()
    {
        if (shown) { Close(); return; }
        Open();
    }

    private void Open()
    {
        RefreshUI();
        Panel.Visible = true;
        shown = true;
        RefreshPlate(pet.Pet);
    }

    private void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        RefreshPlate(pet.Pet);
    }

    private void RequestMode(PetMode mode)
    {
        if (pet.Pet == null || context.Self.Dead)
        {
            RefreshUI();
            return;
        }
        requests.Mode(mode);
    }

    private void Feed()
    {
        if (context.Self.Dead || pet.Pet is not { } petSheet) return;
        if (petSheet.Satisfaction >= PetSheet.MaxSatisfaction)
        {
            SetStatus("Your familiar is already full.", false);
            return;
        }

        int slot = BestFoodSlot();
        if (slot < 0)
        {
            SetStatus("You have no familiar food.", true);
            return;
        }
        requests.Feed(slot - Inventory.GridStart, self.Inventory[slot].ItemId);
    }

    internal int BestFoodSlot()
    {
        int best = -1, bestValue = -1;
        for (int abs = Inventory.GridStart; abs < Inventory.GridStart + Inventory.GridCount && abs < self.Inventory.Length; abs++)
        {
            if (self.Inventory[abs].IsEmpty || self.Inventory[abs].Count <= 0 || self.Inventory[abs].State == ItemFlag.Duplicate) continue;
            if (ItemData.Get(self.Inventory[abs].ItemId) is not { Kind: FoodKind } food) continue;
            if (food.Damage <= bestValue) continue;
            bestValue = food.Damage;
            best = abs;
        }
        return best;
    }

    private void Dismiss()
    {
        if (pet.Pet == null) return;
        requests.Dismiss();
    }

    private void OnSummoned(PetSheet petSheet)
    {
        SetStatus("", false);
        context.Log.Notice($"{petSheet.Name} answers your call.");
        RefreshUI();
    }

    private void OnGone()
    {
        SetStatus("", false);
        RefreshUI();
    }

    private static readonly Color ModeColour = new("ff00ff");
    private static readonly Color ExpColour = new("ffff00");
    private const string ExpPlaceholder = "%I64d";

    private void OnMode(PetMode mode)
    {
        context.Log.Add(mode switch
        {
            PetMode.Attack => GameText.System(PetLogText.AttackMode, "Familiar Attack Mode"),
            PetMode.Looting => GameText.System(PetLogText.LootingMode, "Familiar Looting Mode"),
            _ => GameText.System(PetLogText.DefenseMode, "Familiar Defense Mode"),
        }, CombatLogKind.Status, ModeColour);
        RefreshUI();
    }

    private void OnExp(long gained)
    {
        if (gained != 0)
            context.Log.Add((gained > 0
                    ? GameText.System(PetLogText.ExpGained, "Familiar awarded %I64d EXP.")
                    : GameText.System(PetLogText.ExpLost, "Familiar has lost %I64d EXP."))
                .Replace(ExpPlaceholder, System.Math.Abs(gained).ToString()), CombatLogKind.Status, ExpColour);
        RefreshUI();
    }

    private void OnHpDelta(int delta)
    {
        if (delta < 0)
            context.Log.Add(TextTemplate.Fill(GameText.System(PetLogText.HpLost, "Familiar sustained %d damage."), -delta), CombatLogKind.Incoming);
        else
            context.Log.Add(TextTemplate.Fill(GameText.System(PetLogText.HpRecovered, "Familiar recovered %d HP."), delta), CombatLogKind.Recovery);
    }

    private void OnMpDelta(int delta)
    {
        if (delta < 0)
            context.Log.Add(TextTemplate.Fill(GameText.System(PetLogText.MpUsed, "Familiar employing %d MP."), -delta), CombatLogKind.Spent);
        else
            context.Log.Add(TextTemplate.Fill(GameText.System(PetLogText.MpRecovered, "Familiar recovered %d MP."), delta), CombatLogKind.Resource);
    }

    private void OnFed(int bagSlot, int itemId, int countLeft, int increase)
    {
        if (ItemData.Get(itemId) is not { Kind: FoodKind })
        {
            SetStatus($"{ItemData.DisplayName(itemId)} used on your familiar.", false);
            RefreshUI();
            return;
        }
        SetStatus($"{increase / 100f:0.00}% satisfaction rate increase", false);
        RefreshUI();
    }

    private void OnFoodRefused(int itemId) =>
        SetStatus($"Your familiar would not eat the {ItemData.DisplayName(itemId)}.", true);

    private void OnStrike(int targetId, int damage)
    {
        if (damage == 0 || context.Entities.Find(targetId) is not { } target) return;
        if (damage > 0)
            context.Log.Add(TextTemplate.Fill(GameText.System(PetLogText.Inflicted, "Familiar on %s inflicted %d damage."), target.Name, damage),
                CombatLogKind.Damage);
        else
            context.Log.Add(TextTemplate.Fill(GameText.System(PetLogText.Healed, "Familiar on %s recovered %d HP."), target.Name, -damage),
                CombatLogKind.Recovery);
    }

    internal void RefreshUI()
    {
        ShowSheet(pet.Pet);
        RefreshPlate(pet.Pet);
    }

    internal void ShowSheet(PetSheet? petSheet)
    {
        if (!GodotObject.IsInstanceValid(Panel)) return;
        bool out_ = petSheet != null && !context.Self.Dead;
        attackBtn.Disabled = !out_;
        defendBtn.Disabled = !out_;
        lootBtn.Disabled = !out_;
        feedBtn.Disabled = !out_;
        dismissBtn.Disabled = petSheet == null;

        if (petSheet == null)
        {
            foreach (var cell in bagCells) cell.Set(default);
            var equipped = InventoryConstants.Pet < self.Inventory.Length ? self.Inventory[InventoryConstants.Pet] : default;
            if (equipped.IsLinked && pet.Items.TryGetValue(equipped.UniqueId, out var info))
            {
                nameLbl.Text = info.Name;
                levelLbl.Text = $"Lv {info.Level}";
                expBar.SetFraction(info.ExpPercent / (float)PetSheet.ExpPercentScale, $"{info.ExpPercent / 100f:0.00}%");
                satBar.SetFraction(info.Satisfaction / (float)PetSheet.MaxSatisfaction, $"{info.Satisfaction / 100f:0.00}%");
            }
            else
            {
                nameLbl.Text = "Familiar";
                levelLbl.Text = "";
                expBar.SetFraction(0f, "");
                satBar.SetFraction(0f, "");
            }
            hpBar.SetFraction(0f, "");
            mpBar.SetFraction(0f, "");
            HighlightMode(null);
            if (status.Text.Length == 0) SetStatus(NoneText, false);
            return;
        }

        if (status.Text == NoneText) SetStatus("", false);
        nameLbl.Text = petSheet.Name;
        levelLbl.Text = $"Lv {petSheet.Level}";
        hpBar.Set(petSheet.Hp, petSheet.MaxHp);
        mpBar.Set(petSheet.Mp, petSheet.MaxMp);
        expBar.SetFraction(petSheet.ExpFraction, $"{petSheet.ExpPercent / 100f:0.00}%");
        satBar.SetFraction(petSheet.SatisfactionFraction, $"{petSheet.Satisfaction / 100f:0.00}%");
        HighlightMode(petSheet.Mode);
        for (int i = 0; i < bagCells.Length && i < petSheet.Items.Length; i++) bagCells[i].Set(petSheet.Items[i]);
    }

    private bool CanDropOnBag(ItemSlotView cell, Variant data)
    {
        if (context.Self.Dead || pet.Pet is not { } petSheet || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (cell.Index < 0 || cell.Index >= petSheet.Items.Length
            || !d.ContainsKey("invFrom") || d["invFrom"].VariantType != Variant.Type.Int) return false;
        int abs = d["invFrom"].AsInt32();
        return abs >= Inventory.GridStart && abs < Inventory.GridStart + Inventory.GridCount && abs < self.Inventory.Length && !self.Inventory[abs].IsEmpty && !self.Inventory[abs].IsLinked
               && ItemData.Get(self.Inventory[abs].ItemId) is { } item
               && PetBag.Fits(petSheet.Items, cell.Index, item, ItemData.Get);
    }

    private void DropOnBag(ItemSlotView cell, Variant data)
    {
        if (!CanDropOnBag(cell, data) || context.Inventory.MoveBusy) return;
        int abs = data.AsGodotDictionary()["invFrom"].AsInt32();
        context.Inventory.EnqueuePetMove(ItemMoveDirection.InventoryToPet, self.Inventory[abs].ItemId, (byte)(abs - Inventory.GridStart), (byte)cell.Index, abs, cell.Index);
    }

    private void TakeFromBag(int petPos)
    {
        if (context.Self.Dead || pet.Pet is not { } petSheet || petPos < 0 || petPos >= petSheet.Items.Length || petSheet.Items[petPos].IsEmpty) return;
        if (context.Inventory.MoveBusy) return;
        int free = self.Inventory.FirstFreeGridSlot();
        if (free < 0)
        {
            SetStatus("Your bag is full.", true);
            return;
        }
        context.Inventory.EnqueuePetMove(ItemMoveDirection.PetToInventory, petSheet.Items[petPos].ItemId, (byte)petPos, (byte)(free - Inventory.GridStart), free, petPos);
    }

    internal bool FamiliarLoots() =>
        pet.Pet is { Mode: PetMode.Looting } petSheet && PetBag.Loots(petSheet.Items, ItemData.Get);

    private void HighlightMode(PetMode? mode)
    {
        attackBtn.ButtonPressed = mode == PetMode.Attack;
        defendBtn.ButtonPressed = mode == PetMode.Defence;
        lootBtn.ButtonPressed = mode == PetMode.Looting;
    }

    internal void SetStatus(string text, bool warn)
    {
        status.Text = text;
        status.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextHi);
    }

    private const string PlateLayoutId = "pet_plate";
    private static readonly Vector2 PlateSize = new(152, 90);
    private const float PlateGap = 8f;
    private static readonly Color PlateLevelColour = new("ffff00");
    private static readonly Color PlateExpColour = new("e0bb52");

    private CanvasLayer plateLayer = null!;
    internal PanelContainer? Plate { get; set; }
    private Label plateName = null!;
    private Label plateLevel = null!;
    private StatBar plateHp = null!;
    private StatBar plateMp = null!;
    private Control plateSatisfaction = null!;
    private Control plateExp = null!;

    private static readonly Color PlateTrack = new(0.02f, 0.02f, 0.02f, 0.9f);

    internal void PlateInit()
    {
        plateLayer = new CanvasLayer { Layer = UiLayers.WorldText };
        context.Root.AddChild(plateLayer);
        Plate = BuildPlate();
        plateLayer.AddChild(Plate);
        var layout = HudLayout.Attach(Plate, PlateLayoutId, Plate,
            () => new Vector2(HudAnchor.Edge + UiKit.PartyHudWidth + PlateGap, UiKit.PartyHudTop));
        layout.Clicked += Open;
        RefreshPlate(pet.Pet);
    }

    internal PanelContainer BuildPlate()
    {
        var plate = new PanelContainer
        {
            CustomMinimumSize = PlateSize,
            Size = PlateSize,
            Visible = false,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        plate.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var canvas = new Control { CustomMinimumSize = PlateSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddChild(canvas);

        plateName = PlateLabel(canvas, new Rect2(17, 4, 86, 13), UiTheme.TextHi, HorizontalAlignment.Left);
        PlateLabel(canvas, new Rect2(104, 4, 17, 13), PlateLevelColour, HorizontalAlignment.Center).Text = "LV";
        plateLevel = PlateLabel(canvas, new Rect2(120, 4, 17, 13), UiTheme.TextHi, HorizontalAlignment.Center);
        plateHp = PlateBar(canvas, new Rect2(14, 27, 118, 14), UiTheme.Hp);
        plateMp = PlateBar(canvas, new Rect2(14, 43, 118, 14), UiTheme.Mp);
        plateSatisfaction = PlateThinBar(canvas, new Rect2(14, 60, 118, 8), PlateShadedFill(UiTheme.Good));
        plateExp = PlateThinBar(canvas, new Rect2(26, 70, 97, 3), new ColorRect { Color = PlateExpColour });
        return plate;
    }

    private static Label PlateLabel(Control canvas, Rect2 rect, Color colour, HorizontalAlignment align)
    {
        var label = UiTheme.Text("", 11, colour, align);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ClipText = true;
        label.Position = rect.Position;
        label.Size = rect.Size;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        canvas.AddChild(label);
        return label;
    }

    private static StatBar PlateBar(Control canvas, Rect2 rect, Color colour)
    {
        var bar = new StatBar(colour, rect.Size) { Position = rect.Position, Size = rect.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        canvas.AddChild(bar);
        return bar;
    }

    private static Control PlateThinBar(Control canvas, Rect2 rect, Control fill)
    {
        var track = new ColorRect { Color = PlateTrack, Position = rect.Position, Size = rect.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        canvas.AddChild(track);
        fill.Size = new Vector2(0, rect.Size.Y);
        fill.MouseFilter = Control.MouseFilterEnum.Ignore;
        track.AddChild(fill);
        return fill;
    }

    private static TextureRect PlateShadedFill(Color colour) => new()
    {
        Texture = new GradientTexture2D
        {
            Gradient = new Gradient { Colors = [colour.Lightened(StatBar.FillLighten), colour.Darkened(StatBar.FillDarken)], Offsets = [0f, 1f] },
            FillFrom = Vector2.Zero,
            FillTo = Vector2.Down,
            Width = 1,
            Height = PlateGradientHeight,
        },
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
    };

    private const int PlateGradientHeight = 16;

    private static void SetThinBar(Control fill, float fraction)
    {
        if (fill.GetParent() is not Control track) return;
        fill.Size = new Vector2(track.Size.X * Mathf.Clamp(fraction, 0f, 1f), track.Size.Y);
    }

    internal void RefreshPlate(PetSheet? petSheet)
    {
        if (Plate == null || !GodotObject.IsInstanceValid(Plate)) return;
        bool shown = petSheet != null && !context.Self.Dead && !this.shown;
        Plate.Visible = shown;
        if (!shown || petSheet == null) return;
        plateName.Text = petSheet.Name;
        plateLevel.Text = petSheet.Level.ToString();
        plateHp.Set(petSheet.Hp, petSheet.MaxHp);
        plateMp.Set(petSheet.Mp, petSheet.MaxMp);
        SetThinBar(plateSatisfaction, petSheet.SatisfactionFraction);
        SetThinBar(plateExp, petSheet.ExpFraction);
        Plate.TooltipText = $"{petSheet.Satisfaction / 100f:0.00}% satisfaction\n{petSheet.ExpPercent / 100f:0.00}% EXP";
    }
}
