using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int PetEggKind = 150;
    private const int PetNameMaxLength = 15;
    private const float PetEggSlotSize = 44f;

    private static readonly Dictionary<int, string> PetHatchFailures = new()
    {
        [1] = "Familiar hatching failed.",
        [2] = "Invalid name.",
        [3] = "This Familiar cannot be incubated.",
        [4] = "Limit exceeded.",
        [Net.PetHatchNameTakenCode] = "There has been a database creation error or the name is already in use. Please incubate again.",
    };

    private CanvasLayer _petHatchLayer = null!;
    private HudWindow _petHatchPanel = null!;
    private HBoxContainer _petHatchEggs = null!;
    private LineEdit _petHatchName = null!;
    private Button _petHatchBtn = null!;
    private Label _petHatchStatus = null!;
    private int _petHatchNpc;
    private int _petHatchSlot = -1;
    private bool _petHatchInFlight;
    private bool _petHatchShown;

    private void PetHatchInit()
    {
        BuildPetHatchPanel();
        Net.I.PetHatchedEvent += OnPetHatched;
        Net.I.PetHatchFailedEvent += OnPetHatchFailed;
    }

    private void PetHatchDispose()
    {
        Net.I.PetHatchedEvent -= OnPetHatched;
        Net.I.PetHatchFailedEvent -= OnPetHatchFailed;
    }

    private void BuildPetHatchPanel()
    {
        _petHatchLayer = new CanvasLayer { Layer = 74 };
        AddChild(_petHatchLayer);

        _petHatchPanel = new HudWindow("pethatch", "Familiar Hatching", bodyMinWidth: 320) { Visible = false };
        _petHatchPanel.Closed += ClosePetHatch;
        _petHatchLayer.AddChild(_petHatchPanel);

        var root = _petHatchPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.Text("Would you like to incubate the egg?", 14, UiTheme.GoldBright));
        root.AddChild(UiTheme.SectionTitle("Egg"));
        _petHatchEggs = new HBoxContainer();
        _petHatchEggs.AddThemeConstantOverride("separation", 6);
        root.AddChild(_petHatchEggs);

        root.AddChild(UiTheme.SectionTitle("Bestow a name to the Familiar"));
        _petHatchName = new LineEdit
        {
            MaxLength = PetNameMaxLength,
            PlaceholderText = "Name",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _petHatchName.TextChanged += _ => RefreshPetHatchUI();
        _petHatchName.TextSubmitted += _ => OnPetHatchPressed();
        root.AddChild(_petHatchName);

        root.AddChild(UiTheme.Rule());
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        root.AddChild(actions);
        _petHatchBtn = UiTheme.ActionButton("Hatch", "Hatch the chosen egg");
        _petHatchBtn.Pressed += OnPetHatchPressed;
        actions.AddChild(_petHatchBtn);
        var close = UiTheme.ActionButton("Close", "");
        close.Pressed += ClosePetHatch;
        actions.AddChild(close);

        _petHatchStatus = HudStyle.Label(12);
        _petHatchStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_petHatchStatus);
    }

    private void OpenPetHatch(int npcId)
    {
        _petHatchNpc = npcId;
        _petHatchInFlight = false;
        _petHatchName.Text = "";
        SetPetHatchStatus("", false);
        _petHatchSlot = FirstEggSlot();
        RebuildPetHatchEggs();
        RefreshPetHatchUI();
        _petHatchPanel.Visible = true;
        _petHatchShown = true;
    }

    private void ClosePetHatch()
    {
        if (!_petHatchShown) return;
        _petHatchShown = false;
        _petHatchPanel.Visible = false;
    }

    private int FirstEggSlot()
    {
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
            if (IsPetEgg(Inv[abs])) return abs;
        return -1;
    }

    private static bool IsPetEgg(ItemSlot slot) =>
        !slot.IsEmpty && ItemData.Get(slot.ItemId) is { Kind: PetEggKind };

    private void RebuildPetHatchEggs()
    {
        foreach (var child in _petHatchEggs.GetChildren())
            child.QueueFree();

        bool any = false;
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
        {
            if (!IsPetEgg(Inv[abs])) continue;
            any = true;
            _petHatchEggs.AddChild(PetEggSlot(abs));
        }

        if (!any)
            _petHatchEggs.AddChild(UiTheme.Text("You have no familiar egg.", 12, UiTheme.TextDim));
    }

    private Control PetEggSlot(int abs)
    {
        int itemId = Inv[abs].ItemId;
        var slot = new PanelContainer
        {
            CustomMinimumSize = new Vector2(PetEggSlotSize, PetEggSlotSize),
            TooltipText = ItemData.DisplayName(itemId),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        slot.AddThemeStyleboxOverride("panel", UiTheme.Slot(abs == _petHatchSlot ? UiTheme.GoldVivid : null));
        var icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = ItemData.Icon(itemId),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        slot.AddChild(icon);
        slot.GuiInput += input =>
        {
            if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            _petHatchSlot = abs;
            RebuildPetHatchEggs();
            RefreshPetHatchUI();
        };
        return slot;
    }

    private void RefreshPetHatchUI()
    {
        bool hasEgg = _petHatchSlot >= 0 && IsPetEgg(Inv[_petHatchSlot]);
        _petHatchBtn.Disabled = _petHatchInFlight || !hasEgg || !IsValidPetName(_petHatchName.Text);
        _petHatchName.Editable = !_petHatchInFlight;
    }

    private static bool IsValidPetName(string name)
    {
        if (name.Length is 0 or > PetNameMaxLength) return false;
        foreach (char c in name)
            if (c <= ' ' || c > '~') return false;
        return true;
    }

    private void OnPetHatchPressed()
    {
        if (_petHatchBtn.Disabled || _petHatchSlot < 0) return;
        _petHatchInFlight = true;
        SetPetHatchStatus("", false);
        Net.I.SendPetHatch(_petHatchNpc, Inv[_petHatchSlot].ItemId, _petHatchSlot - GridStart, _petHatchName.Text);
        RefreshPetHatchUI();
    }

    private void OnPetHatched(int absSlot, PetItemInfo info)
    {
        _petHatchInFlight = false;
        CombatNotice($"{info.Name} hatched from the egg.");
        _petHatchSlot = FirstEggSlot();
        _petHatchName.Text = "";
        if (_petHatchShown)
        {
            RebuildPetHatchEggs();
            SetPetHatchStatus($"{info.Name} hatched. Equip it, then use a Familiar Summon.", false);
        }
        RefreshPetHatchUI();
    }

    private void OnPetHatchFailed(int code)
    {
        _petHatchInFlight = false;
        SetPetHatchStatus(PetHatchFailures.TryGetValue(code, out var text) ? text : PetHatchFailures[1], true);
        RefreshPetHatchUI();
    }

    private void SetPetHatchStatus(string text, bool warn)
    {
        _petHatchStatus.Text = text;
        _petHatchStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextHi);
    }
}
