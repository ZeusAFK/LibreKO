using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int PetEggKind = 150;
    private const int PetFamiliarKind = 151;
    private const int PetScrollKind = 171;
    private const int PetScrollEffect = 253;
    private const int PetNameMaxLength = 15;
    private const float PetPickSlotSize = 44f;
    private const int PetHatchTab = 0;
    private const int PetTransformTab = 1;
    private const int NoPetPick = -1;
    private const string PetTransformFailed = "The familiar could not be transformed.";

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
    private ServiceTabs _petHatchTabs = null!;
    private VBoxContainer _petHatchPage = null!;
    private VBoxContainer _petTransformPage = null!;
    private HBoxContainer _petHatchEggs = null!;
    private HBoxContainer _petTransformPets = null!;
    private HBoxContainer _petTransformScrolls = null!;
    private Label _petHatchEggPick = null!;
    private Label _petTransformPetPick = null!;
    private Label _petTransformScrollPick = null!;
    private LineEdit _petHatchName = null!;
    private Button _petHatchBtn = null!;
    private Label _petHatchStatus = null!;
    private int _petHatchNpc;
    private int _petHatchSlot = NoPetPick;
    private int _petTransformSlot = NoPetPick;
    private int _petScrollSlot = NoPetPick;
    private bool _petHatchInFlight;
    private bool _petHatchShown;

    private void PetHatchInit()
    {
        BuildPetHatchPanel();
        Net.I.PetHatchedEvent += OnPetHatched;
        Net.I.PetHatchFailedEvent += OnPetHatchFailed;
        Net.I.PetTransformedEvent += OnPetTransformed;
        Net.I.PetTransformFailedEvent += OnPetTransformFailed;
    }

    private void PetHatchDispose()
    {
        Net.I.PetHatchedEvent -= OnPetHatched;
        Net.I.PetHatchFailedEvent -= OnPetHatchFailed;
        Net.I.PetTransformedEvent -= OnPetTransformed;
        Net.I.PetTransformFailedEvent -= OnPetTransformFailed;
    }

    private void BuildPetHatchPanel()
    {
        _petHatchLayer = new CanvasLayer { Layer = 74 };
        AddChild(_petHatchLayer);

        _petHatchPanel = new HudWindow("pethatch", "Familiar Hatching and Transform", bodyMinWidth: 320) { Visible = false };
        _petHatchPanel.Closed += ClosePetHatch;
        _petHatchLayer.AddChild(_petHatchPanel);

        var root = _petHatchPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        _petHatchTabs = new ServiceTabs();
        _petHatchTabs.SetTabs(new[] { "Hatch", "Transform" }, PetHatchTab);
        _petHatchTabs.Selected += _ => ShowPetHatchTab();
        root.AddChild(_petHatchTabs);

        _petHatchPage = PetHatchPage(root);
        _petHatchPage.AddChild(UiTheme.Text("Would you like to incubate the egg?", 14, UiTheme.GoldBright));
        _petHatchPage.AddChild(UiTheme.SectionTitle("Egg"));
        _petHatchEggs = PetPickRow(_petHatchPage, out _petHatchEggPick);
        _petHatchPage.AddChild(UiTheme.SectionTitle("Bestow a name to the Familiar"));
        _petHatchName = new LineEdit
        {
            MaxLength = PetNameMaxLength,
            PlaceholderText = "Name",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _petHatchName.TextChanged += _ => RefreshPetHatchUI();
        _petHatchName.TextSubmitted += _ => OnPetHatchPressed();
        _petHatchPage.AddChild(_petHatchName);

        _petTransformPage = PetHatchPage(root);
        _petTransformPage.AddChild(UiTheme.Text("Would you like to transform a familiar?", 14, UiTheme.GoldBright));
        _petTransformPage.AddChild(UiTheme.SectionTitle("Familiar"));
        _petTransformPets = PetPickRow(_petTransformPage, out _petTransformPetPick);
        _petTransformPage.AddChild(UiTheme.SectionTitle("Transformation scroll"));
        _petTransformScrolls = PetPickRow(_petTransformPage, out _petTransformScrollPick);
        var note = UiTheme.Text("The scroll is used up and decides the familiar's new form.", 12, UiTheme.TextDim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _petTransformPage.AddChild(note);

        root.AddChild(UiTheme.Rule());
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        root.AddChild(actions);
        _petHatchBtn = UiTheme.ActionButton("Hatch", "");
        _petHatchBtn.Pressed += OnPetHatchPressed;
        actions.AddChild(_petHatchBtn);
        var close = UiTheme.ActionButton("Close", "");
        close.Pressed += ClosePetHatch;
        actions.AddChild(close);

        _petHatchStatus = HudStyle.Label(12);
        _petHatchStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_petHatchStatus);
    }

    private static VBoxContainer PetHatchPage(VBoxContainer root)
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 8);
        root.AddChild(page);
        return page;
    }

    private static HBoxContainer PetPickRow(VBoxContainer page, out Label pick)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        page.AddChild(row);
        pick = UiTheme.Text("", 12, UiTheme.TextHi);
        page.AddChild(pick);
        return row;
    }

    private void OpenPetHatch(int npcId)
    {
        _petHatchNpc = npcId;
        _petHatchInFlight = false;
        _petHatchName.Text = "";
        SetPetHatchStatus("", false);
        _petHatchSlot = _petTransformSlot = _petScrollSlot = NoPetPick;
        RebuildPetPicks();
        bool transform = _petHatchSlot < 0 && _petTransformSlot >= 0;
        _petHatchTabs.Select(transform ? PetTransformTab : PetHatchTab, notify: false);
        ShowPetHatchTab();
        _petHatchPanel.Visible = true;
        _petHatchShown = true;
    }

    private void ClosePetHatch()
    {
        if (!_petHatchShown) return;
        _petHatchShown = false;
        _petHatchPanel.Visible = false;
        HideItemTooltip();
    }

    private bool PetTransforming => _petHatchTabs.Current == PetTransformTab;

    private void ShowPetHatchTab()
    {
        _petHatchPage.Visible = !PetTransforming;
        _petTransformPage.Visible = PetTransforming;
        _petHatchBtn.Text = PetTransforming ? "Transform" : "Hatch";
        _petHatchBtn.TooltipText = PetTransforming
            ? "Transform the chosen familiar with the chosen scroll"
            : "Hatch the chosen egg";
        SetPetHatchStatus("", false);
        RefreshPetHatchUI();
    }

    private int FirstPetPick(Func<ItemSlot, bool> matches)
    {
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
            if (matches(Inv[abs])) return abs;
        return NoPetPick;
    }

    private static bool IsPetEgg(ItemSlot slot) =>
        !slot.IsEmpty && ItemData.Get(slot.ItemId) is { Kind: PetEggKind };

    private static bool IsFamiliarItem(ItemSlot slot) =>
        !slot.IsEmpty && slot.UniqueId != 0 && ItemData.Get(slot.ItemId) is { Kind: PetFamiliarKind };

    private static bool IsTransformScroll(ItemSlot slot) =>
        !slot.IsEmpty && ItemData.Get(slot.ItemId) is { Kind: PetScrollKind, Effect2: PetScrollEffect };

    private void RebuildPetPicks()
    {
        HideItemTooltip();
        _petHatchSlot = KeepPetPick(_petHatchSlot, IsPetEgg);
        _petTransformSlot = KeepPetPick(_petTransformSlot, IsFamiliarItem);
        _petScrollSlot = KeepPetPick(_petScrollSlot, IsTransformScroll);
        FillPetPicks(_petHatchEggs, IsPetEgg, "You have no familiar egg.", abs => _petHatchSlot = abs);
        FillPetPicks(_petTransformPets, IsFamiliarItem, "You have no familiar in your bag.", abs => _petTransformSlot = abs);
        FillPetPicks(_petTransformScrolls, IsTransformScroll, "You have no transformation scroll.", abs => _petScrollSlot = abs);
        RefreshPetPickLooks();
    }

    private int KeepPetPick(int abs, Func<ItemSlot, bool> matches) =>
        abs >= 0 && abs < Inv.Length && matches(Inv[abs]) ? abs : FirstPetPick(matches);

    private void FillPetPicks(HBoxContainer row, Func<ItemSlot, bool> matches, string empty, Action<int> pick)
    {
        foreach (var child in row.GetChildren())
            child.QueueFree();

        bool any = false;
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
        {
            if (!matches(Inv[abs])) continue;
            any = true;
            var cell = new ItemSlotView(PetPickSlotSize) { Index = abs };
            cell.Set(Inv[abs]);
            cell.Clicked += c =>
            {
                pick(c.Index);
                RefreshPetPickLooks();
                RefreshPetHatchUI();
            };
            cell.Hovered += c => ShowItemTooltip(c.Index, Inv[c.Index]);
            cell.Unhovered += _ => HideItemTooltip();
            row.AddChild(cell);
        }

        if (!any)
            row.AddChild(UiTheme.Text(empty, 12, UiTheme.TextDim));
    }

    private void RefreshPetPickLooks()
    {
        PetPickLooks(_petHatchEggs, _petHatchSlot);
        PetPickLooks(_petTransformPets, _petTransformSlot);
        PetPickLooks(_petTransformScrolls, _petScrollSlot);
        _petHatchEggPick.Text = _petHatchSlot >= 0 ? ItemData.DisplayName(Inv[_petHatchSlot].ItemId) : "";
        _petTransformPetPick.Text = _petTransformSlot >= 0 ? FamiliarCaption(Inv[_petTransformSlot]) : "";
        _petTransformScrollPick.Text = _petScrollSlot >= 0 ? ItemData.DisplayName(Inv[_petScrollSlot].ItemId) : "";
    }

    private static string FamiliarCaption(ItemSlot slot)
    {
        string kind = ItemData.DisplayName(slot.ItemId);
        return Net.I.PetItems.TryGetValue(slot.UniqueId, out var info) ? $"{info.Name} ({kind}), level {info.Level}" : kind;
    }

    private static void PetPickLooks(HBoxContainer row, int selected)
    {
        foreach (var child in row.GetChildren())
            if (child is ItemSlotView cell)
                cell.Look = cell.Index == selected ? SlotLook.Selected : SlotLook.Normal;
    }

    private void RefreshPetHatchUI()
    {
        bool ready = PetTransforming
            ? _petTransformSlot >= 0 && IsFamiliarItem(Inv[_petTransformSlot])
              && _petScrollSlot >= 0 && IsTransformScroll(Inv[_petScrollSlot])
            : _petHatchSlot >= 0 && IsPetEgg(Inv[_petHatchSlot]) && IsValidPetName(_petHatchName.Text);
        _petHatchBtn.Disabled = _petHatchInFlight || !ready;
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
        if (_petHatchBtn.Disabled) return;
        _petHatchInFlight = true;
        SetPetHatchStatus("", false);
        if (PetTransforming)
            Net.I.SendPetTransform(_petHatchNpc, Inv[_petTransformSlot].ItemId, _petTransformSlot - GridStart,
                Inv[_petScrollSlot].ItemId, _petScrollSlot - GridStart);
        else
            Net.I.SendPetHatch(_petHatchNpc, Inv[_petHatchSlot].ItemId, _petHatchSlot - GridStart, _petHatchName.Text);
        RefreshPetHatchUI();
    }

    private void OnPetHatched(int absSlot, PetItemInfo info)
    {
        _petHatchInFlight = false;
        CombatNotice($"{info.Name} hatched from the egg.");
        _petHatchName.Text = "";
        if (_petHatchShown)
        {
            RebuildPetPicks();
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

    private void OnPetTransformed(int absSlot, PetItemInfo info)
    {
        _petHatchInFlight = false;
        string form = ItemData.DisplayName(Inv[absSlot].ItemId);
        CombatNotice($"{info.Name} transformed into {form}.");
        if (_petHatchShown)
        {
            RebuildPetPicks();
            SetPetHatchStatus($"{info.Name} transformed into {form}.", false);
        }
        RefreshPetHatchUI();
    }

    private void OnPetTransformFailed(int _)
    {
        _petHatchInFlight = false;
        SetPetHatchStatus(PetTransformFailed, true);
        RefreshPetHatchUI();
    }

    private void SetPetHatchStatus(string text, bool warn)
    {
        _petHatchStatus.Text = text;
        _petHatchStatus.Visible = text.Length > 0;
        _petHatchStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextHi);
    }
}
