using Godot;

namespace LibreKO.Features.Windows;

internal sealed class PetHatchFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly PetSession pet;
    private readonly SelfSession self;
    private readonly IPetRequests petRequests;

    public PetHatchFeature(IWorldContext context, PetSession pet, SelfSession self, IPetRequests petRequests)
    {
        this.context = context;
        this.pet = pet;
        this.self = self;
        this.petRequests = petRequests;
    }

    private const int PetEggKind = 150;
    private const int PetFamiliarKind = 151;
    private const int PetScrollKind = 171;
    private const int PetScrollEffect = 253;
    private const int PetNameMaxLength = PetSheet.NameMaxLength;
    private const float PetPickSlotSize = 44f;
    private const int Tab = 0;
    private const int PetTransformTab = 1;
    private const int PetRenameTab = 2;
    private const int NoPetPick = -1;
    private const string PetTransformFailed = "The familiar could not be transformed.";
    private const string PetRenameFailed = "The familiar could not be renamed.";
    private const string NotSent = "Not connected. Please try again after reconnecting.";

    private static readonly Dictionary<int, int> Failures = new()
    {
        [1] = PetLogText.HatchFailed,
        [2] = PetLogText.InvalidName,
        [3] = PetLogText.CannotIncubate,
        [4] = PetLogText.LimitExceeded,
        [PetWire.NameTakenCode] = PetLogText.NameTaken,
    };

    private static readonly Dictionary<int, int> RenameFailures = new()
    {
        [1] = PetLogText.InvalidItem,
        [2] = PetLogText.InvalidName,
        [3] = PetLogText.NotFamiliarItem,
        [4] = PetLogText.RenameNameTaken,
    };

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private ServiceTabs tabs = null!;
    private VBoxContainer page = null!;
    private VBoxContainer petTransformPage = null!;
    private VBoxContainer petRenamePage = null!;
    private HBoxContainer eggs = null!;
    private HBoxContainer petTransformPets = null!;
    private HBoxContainer petTransformScrolls = null!;
    private HBoxContainer petRenamePets = null!;
    private HBoxContainer petRenameCertificates = null!;
    private Label eggPick = null!;
    private Label petTransformPetPick = null!;
    private Label petTransformScrollPick = null!;
    private Label petRenamePetPick = null!;
    private Label petRenameCertificatePick = null!;

    internal LineEdit NameInput { get; private set; } = null!;
    internal LineEdit RenameInput { get; private set; } = null!;
    private Button btn = null!;
    private Label status = null!;
    private int npc;
    private int slot = NoPetPick;

    private int petTransformSlot = NoPetPick;
    private int petScrollSlot = NoPetPick;
    private int petRenameSlot = NoPetPick;
    private int petCertificateSlot = NoPetPick;
    private bool inFlight;
    private bool shown;

    public bool IsOpen => shown;

    public void Build()
    {
        BuildPanel();
        pet.PetHatchedEvent += OnPetHatched;
        pet.PetHatchFailedEvent += OnFailed;
        pet.PetTransformedEvent += OnPetTransformed;
        pet.PetTransformFailedEvent += OnPetTransformFailed;
        pet.PetRenamedEvent += OnPetRenamed;
        pet.PetRenameFailedEvent += OnPetRenameFailed;
        pet.PetResetEvent += Reset;
    }

    public void Dispose()
    {
        pet.PetHatchedEvent -= OnPetHatched;
        pet.PetHatchFailedEvent -= OnFailed;
        pet.PetTransformedEvent -= OnPetTransformed;
        pet.PetTransformFailedEvent -= OnPetTransformFailed;
        pet.PetRenamedEvent -= OnPetRenamed;
        pet.PetRenameFailedEvent -= OnPetRenameFailed;
        pet.PetResetEvent -= Reset;
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("pethatch", "Familiar Hatching and Transform", bodyMinWidth: 320) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 8);

        tabs = new ServiceTabs();
        tabs.SetTabs(new[] { "Hatch", "Transform", "Rename" }, Tab);
        tabs.Selected += _ => ShowTab();
        root.AddChild(tabs);

        page = Page(root);
        page.AddChild(UiTheme.Text("Would you like to incubate the egg?", 14, UiTheme.GoldBright));
        page.AddChild(UiTheme.SectionTitle("Egg"));
        eggs = PetPickRow(page, out eggPick);
        page.AddChild(UiTheme.SectionTitle("Bestow a name to the Familiar"));
        NameInput = new LineEdit
        {
            MaxLength = PetNameMaxLength,
            PlaceholderText = "Name",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        NameInput.TextChanged += _ => RefreshUI();
        NameInput.TextSubmitted += _ => OnPressed();
        page.AddChild(NameInput);

        petTransformPage = Page(root);
        petTransformPage.AddChild(UiTheme.Text("Would you like to transform a familiar?", 14, UiTheme.GoldBright));
        petTransformPage.AddChild(UiTheme.SectionTitle("Familiar"));
        petTransformPets = PetPickRow(petTransformPage, out petTransformPetPick);
        petTransformPage.AddChild(UiTheme.SectionTitle("Transformation scroll"));
        petTransformScrolls = PetPickRow(petTransformPage, out petTransformScrollPick);
        var note = UiTheme.Text("The scroll is used up and decides the familiar's new form.", 12, UiTheme.TextDim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        petTransformPage.AddChild(note);

        petRenamePage = Page(root);
        petRenamePage.AddChild(UiTheme.Text("Would you like to rename the Familiar?", 14, UiTheme.GoldBright));
        petRenamePage.AddChild(UiTheme.SectionTitle("Familiar"));
        petRenamePets = PetPickRow(petRenamePage, out petRenamePetPick);
        petRenamePage.AddChild(UiTheme.SectionTitle("Certificate of Familiar Name Change"));
        petRenameCertificates = PetPickRow(petRenamePage, out petRenameCertificatePick);
        petRenamePage.AddChild(UiTheme.SectionTitle("New name"));
        RenameInput = new LineEdit
        {
            MaxLength = PetNameMaxLength,
            PlaceholderText = "Name",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        RenameInput.TextChanged += _ => RefreshUI();
        RenameInput.TextSubmitted += _ => OnPressed();
        petRenamePage.AddChild(RenameInput);
        var renameNote = UiTheme.Text("The certificate is used up.", 12, UiTheme.TextDim);
        renameNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        petRenamePage.AddChild(renameNote);

        root.AddChild(UiTheme.Rule());
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        root.AddChild(actions);
        btn = UiTheme.ActionButton("Hatch", "");
        btn.Pressed += OnPressed;
        actions.AddChild(btn);
        var close = UiTheme.ActionButton("Close", "");
        close.Pressed += Close;
        actions.AddChild(close);

        status = HudStyle.Label(12);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(status);
    }

    private static VBoxContainer Page(VBoxContainer root)
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

    internal void Open(int npcId, bool rename = false)
    {
        if (inFlight)
        {
            Panel.Visible = true;
            shown = true;
            RefreshUI();
            return;
        }
        npc = npcId;
        NameInput.Text = "";
        RenameInput.Text = "";
        SetStatus("", false);
        slot = petTransformSlot = petScrollSlot = petRenameSlot = petCertificateSlot = NoPetPick;
        RebuildPetPicks();
        int opening = rename ? PetRenameTab
            : slot < 0 && petTransformSlot >= 0 ? PetTransformTab
            : Tab;
        tabs.Select(opening, notify: false);
        ShowTab();
        Panel.Visible = true;
        shown = true;
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        context.Tooltips.Hide();
    }

    private void ShowTab()
    {
        page.Visible = tabs.Current == Tab;
        petTransformPage.Visible = tabs.Current == PetTransformTab;
        petRenamePage.Visible = tabs.Current == PetRenameTab;
        (btn.Text, btn.TooltipText) = tabs.Current switch
        {
            PetTransformTab => ("Transform", "Transform the chosen familiar with the chosen scroll"),
            PetRenameTab => ("Rename", "Give the chosen familiar the new name"),
            _ => ("Hatch", "Hatch the chosen egg"),
        };
        SetStatus("", false);
        RefreshUI();
    }

    private int FirstPetPick(Func<ItemSlot, bool> matches)
    {
        for (int abs = Inventory.GridStart; abs < Inventory.GridStart + Inventory.GridCount && abs < self.Inventory.Length; abs++)
            if (matches(self.Inventory[abs])) return abs;
        return NoPetPick;
    }

    private static bool IsPetEgg(ItemSlot itemSlot) =>
        PetPickAllowed(itemSlot) && !itemSlot.IsLinked && ItemData.Get(itemSlot.ItemId) is { Kind: PetEggKind };

    private static bool IsFamiliarItem(ItemSlot itemSlot) =>
        PetPickAllowed(itemSlot) && itemSlot.UniqueId != 0 && ItemData.Get(itemSlot.ItemId) is { Kind: PetFamiliarKind };

    private static bool IsTransformScroll(ItemSlot itemSlot) =>
        PetPickAllowed(itemSlot) && ItemData.Get(itemSlot.ItemId) is { Kind: PetScrollKind, Effect2: PetScrollEffect };

    private static bool IsNameCertificate(ItemSlot itemSlot) =>
        PetPickAllowed(itemSlot) && itemSlot.ItemId == PetWire.NameCertificate;

    private static bool PetPickAllowed(ItemSlot itemSlot) => !itemSlot.IsEmpty && itemSlot.Count > 0
    && itemSlot.State is not (ItemFlag.Sealed or ItemFlag.Duplicate or ItemFlag.Rented);

    private void RebuildPetPicks()
    {
        context.Tooltips.Hide();
        slot = KeepPetPick(slot, IsPetEgg);
        petTransformSlot = KeepPetPick(petTransformSlot, IsFamiliarItem);
        petScrollSlot = KeepPetPick(petScrollSlot, IsTransformScroll);
        petRenameSlot = KeepPetPick(petRenameSlot, IsFamiliarItem);
        petCertificateSlot = KeepPetPick(petCertificateSlot, IsNameCertificate);
        FillPetPicks(eggs, IsPetEgg, "You have no familiar egg.", abs => slot = abs);
        FillPetPicks(petTransformPets, IsFamiliarItem, "You have no familiar in your bag.", abs => petTransformSlot = abs);
        FillPetPicks(petTransformScrolls, IsTransformScroll, "You have no transformation scroll.", abs => petScrollSlot = abs);
        FillPetPicks(petRenamePets, IsFamiliarItem, "You have no familiar in your bag.", abs => petRenameSlot = abs);
        FillPetPicks(petRenameCertificates, IsNameCertificate, "You have no Certificate of Familiar Name Change.", abs => petCertificateSlot = abs);
        RefreshPetPickLooks();
    }

    private int KeepPetPick(int abs, Func<ItemSlot, bool> matches) =>
        abs >= 0 && abs < self.Inventory.Length && matches(self.Inventory[abs]) ? abs : FirstPetPick(matches);

    private void FillPetPicks(HBoxContainer row, Func<ItemSlot, bool> matches, string empty, Action<int> pick)
    {
        foreach (var child in row.GetChildren())
            child.QueueFree();

        bool any = false;
        for (int abs = Inventory.GridStart; abs < Inventory.GridStart + Inventory.GridCount && abs < self.Inventory.Length; abs++)
        {
            if (!matches(self.Inventory[abs])) continue;
            any = true;
            var cell = new ItemSlotView(PetPickSlotSize) { Index = abs, Veiled = true };
            cell.Set(self.Inventory[abs]);
            cell.Clicked += c =>
            {
                pick(c.Index);
                RefreshPetPickLooks();
                RefreshUI();
            };
            cell.Hovered += c => context.Tooltips.Show(c.Index, self.Inventory[c.Index]);
            cell.Unhovered += _ => context.Tooltips.Hide();
            row.AddChild(cell);
        }

        if (!any)
            row.AddChild(UiTheme.Text(empty, 12, UiTheme.TextDim));
    }

    private void RefreshPetPickLooks()
    {
        PetPickLooks(eggs, slot);
        PetPickLooks(petTransformPets, petTransformSlot);
        PetPickLooks(petTransformScrolls, petScrollSlot);
        PetPickLooks(petRenamePets, petRenameSlot);
        PetPickLooks(petRenameCertificates, petCertificateSlot);
        eggPick.Text = slot >= 0 ? ItemData.DisplayName(self.Inventory[slot].ItemId) : "";
        petTransformPetPick.Text = petTransformSlot >= 0 ? FamiliarCaption(self.Inventory[petTransformSlot]) : "";
        petTransformScrollPick.Text = petScrollSlot >= 0 ? ItemData.DisplayName(self.Inventory[petScrollSlot].ItemId) : "";
        petRenamePetPick.Text = petRenameSlot >= 0 ? FamiliarCaption(self.Inventory[petRenameSlot]) : "";
        petRenameCertificatePick.Text = petCertificateSlot >= 0 ? ItemData.DisplayName(self.Inventory[petCertificateSlot].ItemId) : "";
    }

    private string FamiliarCaption(ItemSlot itemSlot)
    {
        string kind = ItemData.DisplayName(itemSlot.ItemId);
        return pet.Items.TryGetValue(itemSlot.UniqueId, out var info) ? $"{info.Name} ({kind}), level {info.Level}" : kind;
    }

    private static void PetPickLooks(HBoxContainer row, int selected)
    {
        foreach (var child in row.GetChildren())
            if (child is ItemSlotView cell)
                cell.Look = cell.Index == selected ? SlotLook.Selected : SlotLook.Normal;
    }

    internal void RefreshUI()
    {
        bool ready = tabs.Current switch
        {
            PetTransformTab => petTransformSlot >= 0 && IsFamiliarItem(self.Inventory[petTransformSlot])
                && petScrollSlot >= 0 && IsTransformScroll(self.Inventory[petScrollSlot]),
            PetRenameTab => petRenameSlot >= 0 && IsFamiliarItem(self.Inventory[petRenameSlot])
                && petCertificateSlot >= 0 && IsNameCertificate(self.Inventory[petCertificateSlot])
                && IsValidPetName(RenameInput.Text),
            _ => slot >= 0 && IsPetEgg(self.Inventory[slot]) && IsValidPetName(NameInput.Text),
        };
        btn.Disabled = inFlight || context.Self.Dead || !ready;
        NameInput.Editable = !inFlight;
        RenameInput.Editable = !inFlight;
    }

    private static bool IsValidPetName(string name)
    {
        if (name.Length is 0 or > PetNameMaxLength) return false;
        foreach (char c in name)
            if (c <= ' ' || c > '~') return false;
        return true;
    }

    private void OnPressed()
    {
        RefreshUI();
        if (btn.Disabled) return;
        SetStatus("", false);
        inFlight = tabs.Current switch
        {
            PetTransformTab => petRequests.Transform(npc, self.Inventory[petTransformSlot].ItemId, petTransformSlot - Inventory.GridStart,
                self.Inventory[petTransformSlot].UniqueId, self.Inventory[petScrollSlot].ItemId, petScrollSlot - Inventory.GridStart),
            PetRenameTab => petRequests.Rename(npc, self.Inventory[petRenameSlot].ItemId, petRenameSlot - Inventory.GridStart,
                self.Inventory[petRenameSlot].UniqueId, self.Inventory[petCertificateSlot].ItemId, petCertificateSlot - Inventory.GridStart,
                RenameInput.Text),
            _ => petRequests.Hatch(npc, self.Inventory[slot].ItemId, slot - Inventory.GridStart, NameInput.Text),
        };
        if (!inFlight) SetStatus(NotSent, true);
        RefreshUI();
    }

    private void Reset()
    {
        inFlight = false;
        Close();
        slot = petTransformSlot = petScrollSlot = petRenameSlot = petCertificateSlot = NoPetPick;
        RefreshUI();
    }

    private void OnPetHatched(int absSlot, PetItemInfo info)
    {
        inFlight = false;
        context.Log.Notice($"{info.Name} hatched from the egg.");
        NameInput.Text = "";
        if (shown)
        {
            RebuildPetPicks();
            SetStatus($"{info.Name} hatched. Equip it, then use a Familiar Summon.", false);
        }
        RefreshUI();
    }

    private void OnFailed(int code)
    {
        inFlight = false;
        SetStatus(GameText.System(Failures.TryGetValue(code, out int text) ? text : PetLogText.HatchFailed, "Familiar hatching failed."), true);
        RefreshUI();
    }

    private void OnPetTransformed(int absSlot, PetItemInfo info)
    {
        inFlight = false;
        string form = ItemData.DisplayName(self.Inventory[absSlot].ItemId);
        context.Log.Notice($"{info.Name} transformed into {form}.");
        if (shown)
        {
            RebuildPetPicks();
            SetStatus($"{info.Name} transformed into {form}.", false);
        }
        RefreshUI();
    }

    private void OnPetTransformFailed(int _)
    {
        inFlight = false;
        SetStatus(PetTransformFailed, true);
        RefreshUI();
    }

    private void OnPetRenamed(int absSlot, PetItemInfo info)
    {
        inFlight = false;
        context.Log.Notice($"Your familiar is now called {info.Name}.");
        RenameInput.Text = "";
        if (shown)
        {
            RebuildPetPicks();
            SetStatus($"Your familiar is now called {info.Name}.", false);
        }
        RefreshUI();
    }

    private void OnPetRenameFailed(int code)
    {
        inFlight = false;
        SetStatus(GameText.System(RenameFailures.TryGetValue(code, out int text) ? text : PetLogText.InvalidItem, PetRenameFailed), true);
        RefreshUI();
    }

    private void SetStatus(string text, bool warn)
    {
        status.Text = text;
        status.Visible = text.Length > 0;
        status.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextHi);
    }
}
