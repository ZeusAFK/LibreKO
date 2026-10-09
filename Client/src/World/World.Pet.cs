using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int PetFoodKind = 176;
    private const string PetNoneText = "No familiar is out. Equip one and use a Familiar Summon.";

    private CanvasLayer _petLayer = null!;
    private HudWindow _petPanel = null!;
    private Label _petNameLbl = null!, _petLevelLbl = null!, _petStatus = null!;
    private StatBar _petHpBar = null!, _petMpBar = null!, _petExpBar = null!, _petSatBar = null!;
    private Button _petAttackBtn = null!, _petDefendBtn = null!, _petLootBtn = null!;
    private Button _petFeedBtn = null!, _petDismissBtn = null!;
    private readonly ItemSlotView[] _petBagCells = new ItemSlotView[PetSheet.InventorySize];
    private bool _petShown;
    private const float PetBagCellSize = 44f;
    private const string PetBagHint = "Familiar items only, one of each kind. Automatic Looting lets Looting mode pick up loot.";

    private void PetInit()
    {
        BuildPetPanel();
        Net.I.PetSummonedEvent += OnPetSummoned;
        Net.I.PetGoneEvent += OnPetGone;
        Net.I.PetModeEvent += OnPetMode;
        Net.I.PetVitalsEvent += RefreshPetUI;
        Net.I.PetExpEvent += OnPetExp;
        Net.I.PetFedEvent += OnPetFed;
        Net.I.PetFoodRefusedEvent += OnPetFoodRefused;
        Net.I.PetStrikeEvent += OnPetStrike;
        PetHatchInit();
    }

    private void PetDispose()
    {
        Net.I.PetSummonedEvent -= OnPetSummoned;
        Net.I.PetGoneEvent -= OnPetGone;
        Net.I.PetModeEvent -= OnPetMode;
        Net.I.PetVitalsEvent -= RefreshPetUI;
        Net.I.PetExpEvent -= OnPetExp;
        Net.I.PetFedEvent -= OnPetFed;
        Net.I.PetFoodRefusedEvent -= OnPetFoodRefused;
        Net.I.PetStrikeEvent -= OnPetStrike;
        PetHatchDispose();
    }

    private void BuildPetPanel()
    {
        _petLayer = new CanvasLayer { Layer = 75 };
        AddChild(_petLayer);

        _petPanel = new HudWindow("pet", "Familiar", new Vector2(90, 140), 280) { Visible = false };
        _petPanel.Closed += ClosePet;
        _petLayer.AddChild(_petPanel);

        var root = _petPanel.Body;
        root.AddThemeConstantOverride("separation", 7);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        root.AddChild(header);
        _petNameLbl = UiTheme.Text("", 16, UiTheme.GoldBright);
        _petNameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(_petNameLbl);
        _petLevelLbl = UiTheme.Text("", 13, UiTheme.TextLo, HorizontalAlignment.Right);
        header.AddChild(_petLevelLbl);

        _petHpBar = PetBar(root, "HP", UiTheme.Hp);
        _petMpBar = PetBar(root, "MP", UiTheme.Mp);
        _petExpBar = PetBar(root, "EXP", UiTheme.Gold);
        _petSatBar = PetBar(root, "Satisfaction", UiTheme.Good);

        root.AddChild(UiTheme.Rule());
        root.AddChild(UiTheme.SectionTitle("Mode"));
        var modes = new HBoxContainer();
        modes.AddThemeConstantOverride("separation", 6);
        root.AddChild(modes);
        _petAttackBtn = PetModeButton(modes, "Attack", PetSheet.ModeAttack);
        _petDefendBtn = PetModeButton(modes, "Defend", PetSheet.ModeDefence);
        _petLootBtn = PetModeButton(modes, "Loot", PetSheet.ModeLooting);

        root.AddChild(UiTheme.Rule());
        root.AddChild(UiTheme.SectionTitle("Bag"));
        var bag = new HBoxContainer { TooltipText = PetBagHint };
        bag.AddThemeConstantOverride("separation", 6);
        root.AddChild(bag);
        for (int i = 0; i < _petBagCells.Length; i++)
        {
            var cell = new ItemSlotView(PetBagCellSize) { Index = i, CanDrop = CanDropOnPetBag, Dropped = DropOnPetBag };
            cell.RightClicked += c => TakeFromPetBag(c.Index);
            _petBagCells[i] = cell;
            bag.AddChild(cell);
        }

        root.AddChild(UiTheme.Rule());
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        root.AddChild(actions);
        _petFeedBtn = UiTheme.ActionButton("Feed", "Give your familiar the most filling food in your bag");
        _petFeedBtn.Pressed += FeedPet;
        actions.AddChild(_petFeedBtn);
        _petDismissBtn = UiTheme.ActionButton("Dismiss", "Send your familiar away");
        _petDismissBtn.Pressed += DismissPet;
        actions.AddChild(_petDismissBtn);

        _petStatus = HudStyle.Label(12);
        _petStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_petStatus);

        RefreshPetUI();
    }

    private static StatBar PetBar(VBoxContainer root, string caption, Color tint)
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

    private Button PetModeButton(HBoxContainer row, string text, int mode)
    {
        var btn = UiTheme.UnderlineTabButton(text, 13);
        btn.Pressed += () => RequestPetMode(mode);
        row.AddChild(btn);
        return btn;
    }

    private void TogglePet()
    {
        if (_petShown) { ClosePet(); return; }
        OpenPet();
    }

    private void OpenPet()
    {
        RefreshPetUI();
        _petPanel.Visible = true;
        _petShown = true;
    }

    private void ClosePet()
    {
        if (!_petShown) return;
        _petShown = false;
        _petPanel.Visible = false;
    }

    private void RequestPetMode(int mode)
    {
        if (Net.I.Pet == null || _selfDead)
        {
            RefreshPetUI();
            return;
        }
        Net.I.SendPetMode(mode);
    }

    private void FeedPet()
    {
        if (_selfDead || Net.I.Pet is not { } pet) return;
        if (pet.Satisfaction >= PetSheet.MaxSatisfaction)
        {
            SetPetStatus("Your familiar is already full.", false);
            return;
        }

        int slot = BestPetFoodSlot();
        if (slot < 0)
        {
            SetPetStatus("You have no familiar food.", true);
            return;
        }
        Net.I.SendPetFeed(slot - GridStart, Inv[slot].ItemId);
    }

    private int BestPetFoodSlot()
    {
        int best = -1, bestValue = -1;
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
        {
            if (Inv[abs].IsEmpty || Inv[abs].Count <= 0 || Inv[abs].State == ItemFlag.Duplicate) continue;
            if (ItemData.Get(Inv[abs].ItemId) is not { Kind: PetFoodKind } food) continue;
            if (food.Damage <= bestValue) continue;
            bestValue = food.Damage;
            best = abs;
        }
        return best;
    }

    private void DismissPet()
    {
        if (Net.I.Pet == null) return;
        Net.I.SendPetDismiss();
    }

    private void OnPetSummoned(PetSheet pet)
    {
        SetPetStatus("", false);
        CombatNotice($"{pet.Name} answers your call.");
        RefreshPetUI();
    }

    private void OnPetGone()
    {
        SetPetStatus("", false);
        RefreshPetUI();
    }

    private void OnPetMode(int mode)
    {
        CombatNotice(mode switch
        {
            PetSheet.ModeAttack => "Familiar Attack Mode",
            PetSheet.ModeLooting => "Familiar Looting Mode",
            _ => "Familiar Defense Mode",
        });
        RefreshPetUI();
    }

    private void OnPetExp(long gained)
    {
        if (gained > 0) CombatNotice($"Familiar awarded {gained} EXP.");
        else if (gained < 0) CombatNotice($"Familiar has lost {-gained} EXP.");
        RefreshPetUI();
    }

    private void OnPetFed(int bagSlot, int itemId, int countLeft, int increase)
    {
        SetPetStatus($"{increase / 100f:0.00}% satisfaction rate increase", false);
        RefreshPetUI();
    }

    private void OnPetFoodRefused(int itemId) =>
        SetPetStatus($"Your familiar would not eat the {ItemData.DisplayName(itemId)}.", true);

    private void OnPetStrike(int targetId, int damage)
    {
        if (damage <= 0 || !_ents.TryGetValue(targetId, out var target)) return;
        CombatNotice($"Familiar on {target.Name} inflicted {damage} damage.");
    }

    private void RefreshPetUI() => ShowPetSheet(Net.I.Pet);

    private void ShowPetSheet(PetSheet? pet)
    {
        if (!GodotObject.IsInstanceValid(_petPanel)) return;
        bool out_ = pet != null && !_selfDead;
        _petAttackBtn.Disabled = !out_;
        _petDefendBtn.Disabled = !out_;
        _petLootBtn.Disabled = !out_;
        _petFeedBtn.Disabled = !out_;
        _petDismissBtn.Disabled = pet == null;

        if (pet == null)
        {
            foreach (var cell in _petBagCells) cell.Set(default);
            var equipped = InventoryConstants.Pet < Inv.Length ? Inv[InventoryConstants.Pet] : default;
            if (equipped.IsLinked && Net.I.PetItems.TryGetValue(equipped.UniqueId, out var info))
            {
                _petNameLbl.Text = info.Name;
                _petLevelLbl.Text = $"Lv {info.Level}";
                _petExpBar.SetFraction(info.ExpPercent / (float)PetSheet.ExpPercentScale, $"{info.ExpPercent / 100f:0.00}%");
                _petSatBar.SetFraction(info.Satisfaction / (float)PetSheet.MaxSatisfaction, $"{info.Satisfaction / 100f:0.00}%");
            }
            else
            {
                _petNameLbl.Text = "Familiar";
                _petLevelLbl.Text = "";
                _petExpBar.SetFraction(0f, "");
                _petSatBar.SetFraction(0f, "");
            }
            _petHpBar.SetFraction(0f, "");
            _petMpBar.SetFraction(0f, "");
            HighlightPetMode(-1);
            if (_petStatus.Text.Length == 0) SetPetStatus(PetNoneText, false);
            return;
        }

        if (_petStatus.Text == PetNoneText) SetPetStatus("", false);
        _petNameLbl.Text = pet.Name;
        _petLevelLbl.Text = $"Lv {pet.Level}";
        _petHpBar.Set(pet.Hp, pet.MaxHp);
        _petMpBar.Set(pet.Mp, pet.MaxMp);
        _petExpBar.SetFraction(pet.ExpFraction, $"{pet.ExpPercent / 100f:0.00}%");
        _petSatBar.SetFraction(pet.SatisfactionFraction, $"{pet.Satisfaction / 100f:0.00}%");
        HighlightPetMode(pet.Mode);
        for (int i = 0; i < _petBagCells.Length && i < pet.Items.Length; i++) _petBagCells[i].Set(pet.Items[i]);
    }

    private bool CanDropOnPetBag(ItemSlotView cell, Variant data)
    {
        if (_selfDead || Net.I.Pet is not { } pet || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (cell.Index < 0 || cell.Index >= pet.Items.Length
            || !d.ContainsKey("invFrom") || d["invFrom"].VariantType != Variant.Type.Int) return false;
        int abs = d["invFrom"].AsInt32();
        return abs >= GridStart && abs < GridStart + GridCount && abs < Inv.Length && !Inv[abs].IsEmpty && !Inv[abs].IsLinked
               && ItemData.Get(Inv[abs].ItemId) is { } item
               && PetBag.Fits(pet.Items, cell.Index, item, ItemData.Get);
    }

    private void DropOnPetBag(ItemSlotView cell, Variant data)
    {
        if (!CanDropOnPetBag(cell, data) || InventoryMoveBusy) return;
        int abs = data.AsGodotDictionary()["invFrom"].AsInt32();
        EnqueuePetMove(ItemMove.InventoryToPet, Inv[abs].ItemId, (byte)(abs - GridStart), (byte)cell.Index, abs, cell.Index);
    }

    private void TakeFromPetBag(int petPos)
    {
        if (_selfDead || Net.I.Pet is not { } pet || petPos < 0 || petPos >= pet.Items.Length || pet.Items[petPos].IsEmpty) return;
        if (InventoryMoveBusy) return;
        int free = Inv.FirstFreeGridSlot();
        if (free < 0)
        {
            SetPetStatus("Your bag is full.", true);
            return;
        }
        EnqueuePetMove(ItemMove.PetToInventory, pet.Items[petPos].ItemId, (byte)petPos, (byte)(free - GridStart), free, petPos);
    }

    private bool FamiliarLoots() =>
        Net.I.Pet is { Mode: PetSheet.ModeLooting } pet && PetBag.Loots(pet.Items, ItemData.Get);

    private void HighlightPetMode(int mode)
    {
        _petAttackBtn.ButtonPressed = mode == PetSheet.ModeAttack;
        _petDefendBtn.ButtonPressed = mode == PetSheet.ModeDefence;
        _petLootBtn.ButtonPressed = mode == PetSheet.ModeLooting;
    }

    private void SetPetStatus(string text, bool warn)
    {
        _petStatus.Text = text;
        _petStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextHi);
    }
}
