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
    private const int NoPetPick = -1;
    private const string PetTransformFailed = "The familiar could not be transformed.";
    private const string NotSent = "Not connected. Please try again after reconnecting.";

    private static readonly Dictionary<int, int> Failures = new()
    {
        [1] = PetLogText.HatchFailed,
        [2] = PetLogText.InvalidName,
        [3] = PetLogText.CannotIncubate,
        [4] = PetLogText.LimitExceeded,
        [PetWire.NameTakenCode] = PetLogText.NameTaken,
    };

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private ServiceTabs tabs = null!;
    private VBoxContainer page = null!;
    private VBoxContainer petTransformPage = null!;
    private HBoxContainer eggs = null!;
    private HBoxContainer petTransformPets = null!;
    private HBoxContainer petTransformScrolls = null!;
    private Label eggPick = null!;
    private Label petTransformPetPick = null!;
    private Label petTransformScrollPick = null!;

    internal LineEdit NameInput { get; private set; } = null!;
    private Button btn = null!;
    private Label status = null!;
    private int npc;
    private int slot = NoPetPick;

    private int petTransformSlot = NoPetPick;
    private int petScrollSlot = NoPetPick;
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
        pet.PetResetEvent += Reset;
    }

    public void Dispose()
    {
        pet.PetHatchedEvent -= OnPetHatched;
        pet.PetHatchFailedEvent -= OnFailed;
        pet.PetTransformedEvent -= OnPetTransformed;
        pet.PetTransformFailedEvent -= OnPetTransformFailed;
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
        tabs.SetTabs(new[] { "Hatch", "Transform" }, Tab);
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

    internal void Open(int npcId)
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
        SetStatus("", false);
        slot = petTransformSlot = petScrollSlot = NoPetPick;
        RebuildPetPicks();
        bool transform = slot < 0 && petTransformSlot >= 0;
        tabs.Select(transform ? PetTransformTab : Tab, notify: false);
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

    private bool PetTransforming => tabs.Current == PetTransformTab;

    private void ShowTab()
    {
        page.Visible = !PetTransforming;
        petTransformPage.Visible = PetTransforming;
        btn.Text = PetTransforming ? "Transform" : "Hatch";
        btn.TooltipText = PetTransforming
            ? "Transform the chosen familiar with the chosen scroll"
            : "Hatch the chosen egg";
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

    private static bool PetPickAllowed(ItemSlot itemSlot) => !itemSlot.IsEmpty && itemSlot.Count > 0
    && itemSlot.State is not (ItemFlag.Sealed or ItemFlag.Duplicate or ItemFlag.Rented);

    private void RebuildPetPicks()
    {
        context.Tooltips.Hide();
        slot = KeepPetPick(slot, IsPetEgg);
        petTransformSlot = KeepPetPick(petTransformSlot, IsFamiliarItem);
        petScrollSlot = KeepPetPick(petScrollSlot, IsTransformScroll);
        FillPetPicks(eggs, IsPetEgg, "You have no familiar egg.", abs => slot = abs);
        FillPetPicks(petTransformPets, IsFamiliarItem, "You have no familiar in your bag.", abs => petTransformSlot = abs);
        FillPetPicks(petTransformScrolls, IsTransformScroll, "You have no transformation scroll.", abs => petScrollSlot = abs);
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
        eggPick.Text = slot >= 0 ? ItemData.DisplayName(self.Inventory[slot].ItemId) : "";
        petTransformPetPick.Text = petTransformSlot >= 0 ? FamiliarCaption(self.Inventory[petTransformSlot]) : "";
        petTransformScrollPick.Text = petScrollSlot >= 0 ? ItemData.DisplayName(self.Inventory[petScrollSlot].ItemId) : "";
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
        bool ready = PetTransforming
            ? petTransformSlot >= 0 && IsFamiliarItem(self.Inventory[petTransformSlot])
              && petScrollSlot >= 0 && IsTransformScroll(self.Inventory[petScrollSlot])
            : slot >= 0 && IsPetEgg(self.Inventory[slot]) && IsValidPetName(NameInput.Text);
        btn.Disabled = inFlight || context.Self.Dead || !ready;
        NameInput.Editable = !inFlight;
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
        inFlight = PetTransforming
            ? petRequests.Transform(npc, self.Inventory[petTransformSlot].ItemId, petTransformSlot - Inventory.GridStart,
                self.Inventory[petTransformSlot].UniqueId, self.Inventory[petScrollSlot].ItemId, petScrollSlot - Inventory.GridStart)
            : petRequests.Hatch(npc, self.Inventory[slot].ItemId, slot - Inventory.GridStart, NameInput.Text);
        if (!inFlight) SetStatus(NotSent, true);
        RefreshUI();
    }

    private void Reset()
    {
        inFlight = false;
        Close();
        slot = petTransformSlot = petScrollSlot = NoPetPick;
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

    private void SetStatus(string text, bool warn)
    {
        status.Text = text;
        status.Visible = text.Length > 0;
        status.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextHi);
    }
}
