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
    private bool _petShown;

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

        _petPanel = new HudWindow("pet", Localization.Loc.Tr("Familiar"), new Vector2(90, 140), 280) { Visible = false };
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

        _petHpBar = PetBar(root, Localization.Loc.Tr("HP"), UiTheme.Hp);
        _petMpBar = PetBar(root, Localization.Loc.Tr("MP"), UiTheme.Mp);
        _petExpBar = PetBar(root, Localization.Loc.Tr("EXP"), UiTheme.Gold);
        _petSatBar = PetBar(root, Localization.Loc.Tr("Satisfaction"), UiTheme.Good);

        root.AddChild(UiTheme.Rule());
        root.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("Mode")));
        var modes = new HBoxContainer();
        modes.AddThemeConstantOverride("separation", 6);
        root.AddChild(modes);
        _petAttackBtn = PetModeButton(modes, Localization.Loc.Tr("Attack"), PetSheet.ModeAttack);
        _petDefendBtn = PetModeButton(modes, Localization.Loc.Tr("Defend"), PetSheet.ModeDefence);
        _petLootBtn = PetModeButton(modes, Localization.Loc.Tr("Loot"), PetSheet.ModeLooting);

        root.AddChild(UiTheme.Rule());
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        root.AddChild(actions);
        _petFeedBtn = UiTheme.ActionButton(Localization.Loc.Tr("Feed"), Localization.Loc.Tr("Give your familiar the most filling food in your bag"));
        _petFeedBtn.Pressed += FeedPet;
        actions.AddChild(_petFeedBtn);
        _petDismissBtn = UiTheme.ActionButton(Localization.Loc.Tr("Dismiss"), Localization.Loc.Tr("Send your familiar away"));
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
        if (Net.I.Pet is not { } pet) return;
        if (pet.Satisfaction >= PetSheet.MaxSatisfaction)
        {
            SetPetStatus(Localization.Loc.Tr("Your familiar is already full."), false);
            return;
        }

        int slot = BestPetFoodSlot();
        if (slot < 0)
        {
            SetPetStatus(Localization.Loc.Tr("You have no familiar food."), true);
            return;
        }
        Net.I.SendPetFeed(slot - GridStart, Inv[slot].ItemId);
    }

    private int BestPetFoodSlot()
    {
        int best = -1, bestValue = -1;
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
        {
            if (Inv[abs].IsEmpty || Inv[abs].Count <= 0) continue;
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
        CombatNotice($"{pet.Name} {Localization.Loc.Tr("answers your call.")}");
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
            PetSheet.ModeAttack => Localization.Loc.Tr("Familiar Attack Mode"),
            PetSheet.ModeLooting => Localization.Loc.Tr("Familiar Looting Mode"),
            _ => Localization.Loc.Tr("Familiar Defense Mode"),
        });
        RefreshPetUI();
    }

    private void OnPetExp(long gained)
    {
        if (gained > 0) CombatNotice($"{Localization.Loc.Tr("Familiar awarded")} {gained} {Localization.Loc.Tr("EXP")}.");
        else if (gained < 0) CombatNotice($"{Localization.Loc.Tr("Familiar has lost")} {-gained} {Localization.Loc.Tr("EXP")}.");
        RefreshPetUI();
    }

    private void OnPetFed(int bagSlot, int itemId, int countLeft, int increase)
    {
        SetPetStatus($"{increase / 100f:0.00}% {Localization.Loc.Tr("satisfaction rate increase")}", false);
        RefreshPetUI();
    }

    private void OnPetFoodRefused(int itemId) =>
        SetPetStatus($"{Localization.Loc.Tr("Your familiar would not eat the")} {ItemData.DisplayName(itemId)}.", true);

    private void OnPetStrike(int targetId, int damage)
    {
        if (damage <= 0 || !_ents.TryGetValue(targetId, out var target)) return;
        CombatNotice($"{Localization.Loc.Tr("Familiar on")} {target.Name} {Localization.Loc.Tr("inflicted")} {damage} {Localization.Loc.Tr("damage.")}");
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
            var equipped = InventoryConstants.Pet < Inv.Length ? Inv[InventoryConstants.Pet] : default;
            if (equipped.IsLinked && Net.I.PetItems.TryGetValue(equipped.UniqueId, out var info))
            {
                _petNameLbl.Text = info.Name;
                _petLevelLbl.Text = $"{Localization.Loc.Tr("Lv")} {info.Level}";
                _petExpBar.SetFraction(info.ExpPercent / (float)PetSheet.ExpPercentScale, $"{info.ExpPercent / 100f:0.00}%");
                _petSatBar.SetFraction(info.Satisfaction / (float)PetSheet.MaxSatisfaction, $"{info.Satisfaction / 100f:0.00}%");
            }
            else
            {
                _petNameLbl.Text = Localization.Loc.Tr("Familiar");
                _petLevelLbl.Text = "";
                _petExpBar.SetFraction(0f, "");
                _petSatBar.SetFraction(0f, "");
            }
            _petHpBar.SetFraction(0f, "");
            _petMpBar.SetFraction(0f, "");
            HighlightPetMode(-1);
            if (_petStatus.Text.Length == 0) SetPetStatus(Localization.Loc.Tr(PetNoneText), false);
            return;
        }

        if (_petStatus.Text == Localization.Loc.Tr(PetNoneText)) SetPetStatus("", false);
        _petNameLbl.Text = pet.Name;
        _petLevelLbl.Text = $"{Localization.Loc.Tr("Lv")} {pet.Level}";
        _petHpBar.Set(pet.Hp, pet.MaxHp);
        _petMpBar.Set(pet.Mp, pet.MaxMp);
        _petExpBar.SetFraction(pet.ExpFraction, $"{pet.ExpPercent / 100f:0.00}%");
        _petSatBar.SetFraction(pet.SatisfactionFraction, $"{pet.Satisfaction / 100f:0.00}%");
        HighlightPetMode(pet.Mode);
    }

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
