using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _petLayer = null!;
    private HudWindow _petPanel = null!;
    private Label _petSatLabel = null!, _petStatus = null!;
    private ProgressBar _petSatBar = null!;
    private Button _petAttackBtn = null!, _petDefendBtn = null!, _petLootBtn = null!, _petFeedBtn = null!;
    private bool _petShown;
    private bool _petAlive;
    private byte _petMode = Net.PetModeDefence;
    private int _petSatisfaction = Net.PetMaxSatisfaction;

    private void PetInit()
    {
        BuildPetPanel();
        Net.I.PetSatisfactionEvent += OnPetSatisfaction;
        Net.I.PetModeEvent += OnPetMode;
        Net.I.PetFedEvent += OnPetFed;
        Net.I.PetDeathEvent += OnPetDeath;
    }

    private void PetDispose()
    {
        Net.I.PetSatisfactionEvent -= OnPetSatisfaction;
        Net.I.PetModeEvent -= OnPetMode;
        Net.I.PetFedEvent -= OnPetFed;
        Net.I.PetDeathEvent -= OnPetDeath;
    }

    private void BuildPetPanel()
    {
        _petLayer = new CanvasLayer { Layer = 75 };
        AddChild(_petLayer);

        _petPanel = new HudWindow("pet", "Pet", new Vector2(90, 140)) { Visible = false };
        _petPanel.Closed += ClosePet;
        _petLayer.AddChild(_petPanel);

        var root = _petPanel.Body;
        root.AddThemeConstantOverride("separation", 8);
        root.CustomMinimumSize = new Vector2(240, 0);

        root.AddChild(UiTheme.SectionTitle("Satisfaction"));

        _petSatBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = Net.PetMaxSatisfaction,
            Value = Net.PetMaxSatisfaction,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 14),
        };
        root.AddChild(_petSatBar);

        _petSatLabel = HudStyle.Label(12, HorizontalAlignment.Right);
        root.AddChild(_petSatLabel);

        root.AddChild(new HSeparator());
        root.AddChild(UiTheme.SectionTitle("Mode"));

        var modes = new HBoxContainer();
        modes.AddThemeConstantOverride("separation", 6);
        _petAttackBtn = MakeModeButton("Attack", Net.PetModeAttack);
        _petDefendBtn = MakeModeButton("Defend", Net.PetModeDefence);
        _petLootBtn   = MakeModeButton("Loot",   Net.PetModeLooting);
        modes.AddChild(_petAttackBtn);
        modes.AddChild(_petDefendBtn);
        modes.AddChild(_petLootBtn);
        root.AddChild(modes);

        root.AddChild(new HSeparator());

        _petFeedBtn = new Button { Text = "Feed", FocusMode = Control.FocusModeEnum.None };
        _petFeedBtn.Pressed += FeedPet;
        root.AddChild(_petFeedBtn);

        _petStatus = HudStyle.Label(13);
        root.AddChild(_petStatus);

        HighlightPetMode();
    }

    private Button MakeModeButton(string text, byte mode)
    {
        var btn = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            ToggleMode = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        btn.Pressed += () => RequestPetMode(mode);
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

    private void RequestPetMode(byte mode)
    {
        if (_selfDead) { SetPetStatus("You can't command a pet while dead.", true); HighlightPetMode(); return; }
        if (!_petAlive) { SetPetStatus("No pet summoned.", true); HighlightPetMode(); return; }
        _petMode = mode;
        HighlightPetMode();
        Net.I.SendPetMode(mode);
    }

    private void OnPetMode(byte mode)
    {
        _petAlive = true;
        _petMode = mode;
        HighlightPetMode();
        SetPetStatus(mode switch
        {
            Net.PetModeAttack  => "Pet will attack.",
            Net.PetModeLooting => "Pet will loot.",
            _                  => "Pet will defend.",
        }, false);
    }

    private void HighlightPetMode()
    {
        _petAttackBtn.ButtonPressed = _petMode == Net.PetModeAttack;
        _petDefendBtn.ButtonPressed = _petMode == Net.PetModeDefence;
        _petLootBtn.ButtonPressed   = _petMode == Net.PetModeLooting;
    }

    private void FeedPet()
    {
        if (_selfDead) { SetPetStatus("You can't feed a pet while dead.", true); return; }
        if (!_petAlive) { SetPetStatus("No pet summoned.", true); return; }
        if (_petSatisfaction >= Net.PetMaxSatisfaction) { SetPetStatus("Your pet is already full.", false); return; }

        int slot = FindPetFoodSlot(out int foodId);
        if (slot < 0) { SetPetStatus("No pet food in your bags.", true); return; }

        Inv.Consume(slot, 1);
        Net.I.MirrorInventorySlot(slot, Inv[slot]);
        if (CharTabOpen()) RefreshInventoryUI();

        Net.I.SendPetFeed((byte)(slot - GridStart), foodId);
    }

    private int FindPetFoodSlot(out int foodId)
    {
        foodId = 0;
        int best = -1, bestRank = -1;
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
        {
            if (Inv[abs].IsEmpty || Inv[abs].Count <= 0) continue;
            int rank = Inv[abs].ItemId switch
            {
                Net.PetFood100 => 2,
                Net.PetFood50  => 1,
                Net.PetFood20  => 0,
                _              => -1,
            };
            if (rank > bestRank) { bestRank = rank; best = abs; foodId = Inv[abs].ItemId; }
        }
        return best;
    }

    private void OnPetFed(int itemId, int oldSatisfaction)
    {
        _petAlive = true;
        SetPetStatus($"Fed {ItemData.DisplayName(itemId)}.", false);
    }

    private void OnPetSatisfaction(int satisfaction, int nid)
    {
        _petAlive = true;
        _petSatisfaction = satisfaction;
        if (_petShown) RefreshPetUI();
    }

    private void OnPetDeath(int nid)
    {
        _petAlive = false;
        _petSatisfaction = 0;
        _petMode = Net.PetModeDefence;
        if (_petShown) RefreshPetUI();
        SetPetStatus("Your pet starved and ran away.", true);
        Chat.Info("Your pet's satisfaction reached zero — it has left.");
    }

    private void RefreshPetUI()
    {
        _petSatBar.Value = Mathf.Clamp(_petSatisfaction, 0, Net.PetMaxSatisfaction);
        int pct = Mathf.RoundToInt(_petSatisfaction * 100f / Net.PetMaxSatisfaction);
        _petSatLabel.Text = _petAlive ? $"{_petSatisfaction:n0} / {Net.PetMaxSatisfaction:n0}  ({pct}%)" : "No pet summoned";

        bool commandable = _petAlive && !_selfDead;
        _petAttackBtn.Disabled = !commandable;
        _petDefendBtn.Disabled = !commandable;
        _petLootBtn.Disabled = !commandable;
        _petFeedBtn.Disabled = !commandable;
        HighlightPetMode();
    }

    private void SetPetStatus(string text, bool warn)
    {
        _petStatus.Text = text;
        _petStatus.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }
}
