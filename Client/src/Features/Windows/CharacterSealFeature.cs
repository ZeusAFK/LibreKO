using Godot;

namespace LibreKO.Features.Windows;

internal sealed class CharacterSealFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly CharacterSealSession characterSeal;
    private readonly ItemsSession items;
    private readonly PetSession pet;
    private readonly SelfSession self;
    private readonly ICharacterSealRequests requests;

    public CharacterSealFeature(IWorldContext context, CharacterSealSession characterSeal, ItemsSession items, PetSession pet, SelfSession self, ICharacterSealRequests requests)
    {
        this.context = context;
        this.characterSeal = characterSeal;
        this.items = items;
        this.pet = pet;
        this.self = self;
        this.requests = requests;
    }

    internal const string Title = "Character Seal and Unseal";
    private const int BodyWidth = 380;
    private const float RowHeight = 36f;
    private const float KeySize = 40f;
    private const int KeyColumns = 5;
    private const float SealSheetCellSize = 40f;
    private const int SealSheetEquipColumns = 3;
    private const int SealSheetBagColumns = 7;
    private static readonly Color GoodColour = new(0.4f, 0.55f, 1f);
    internal static readonly Color BadColour = new("ff3b3b");
    private static readonly Color Lilac = new("9b9bff");
    private static readonly Color SealSheetKarusColour = new("ff6060");
    private static readonly Color SealSheetElmoradColour = new("807fff");

    internal CanvasLayer Layer { get; set; } = null!;
    internal HudWindow CsWindow { get; private set; } = null!;
    internal VBoxContainer CsSealingPage { get; private set; } = null!;
    internal VBoxContainer CsSelectPage { get; private set; } = null!;
    internal VBoxContainer CsCodePage { get; private set; } = null!;
    internal Label CsSelectTitle { get; private set; } = null!;
    internal Label CsSelectPrompt { get; private set; } = null!;
    internal Label CsCodeStars { get; private set; } = null!;
    private Button csSelectOk = null!;
    private readonly Button[] csSlotButtons = new Button[CharacterRecordWire.SlotCount];
    private GridContainer csKeys = null!;
    internal HudWindow CsTrans { get; private set; } = null!;
    internal WarehouseCell CsSocket { get; private set; } = null!;
    internal Label CsTransHeadline { get; private set; } = null!;
    internal Label CsTransGold { get; private set; } = null!;
    private BagCompanion? csCompanion;
    internal HudWindow CsSheet { get; private set; } = null!;
    private Label csSheetName = null!;
    private Label csSheetLevel = null!;
    private Label csSheetRace = null!;
    private Label csSheetClass = null!;
    private Label csSheetRebirth = null!;
    private Label csSheetLoyalty = null!;
    private Label csSheetGold = null!;
    private Label csSheetAchievements = null!;
    private readonly Label[] csSheetStats = new Label[CharacterSealWire.StatCount];
    private readonly Label[] csSheetSkills = new Label[CharacterSealWire.SkillTrees + 1];
    private readonly Label[] csSheetSkillNames = new Label[CharacterSealWire.SkillTrees - 1];
    private readonly ItemSlotView[] csSheetCells = new ItemSlotView[CharacterSealWire.SheetItems];

    internal CharacterSeal.Mode Mode { get; set; } = CharacterSeal.Mode.Seal;
    internal CharacterSummary[] Summaries { get; set; } = [];
    private int csBagSlot = -1;
    internal int Picked { get; set; } = -1;
    internal string Code { get; set; } = "";
    private bool csTransShown;
    private ulong csSheetAskedAt;
    internal int SheetId { get; set; }

    public void Build()
    {
        Layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(Layer);
        BuildWindow();
        BuildTrans();
        BuildSealedSheet();
        characterSeal.CharacterSealListEvent += OnList;
        characterSeal.CharacterSealEvent += OnSealed;
        characterSeal.CharacterUnsealEvent += OnUnsealed;
        characterSeal.SealedCharacterSheetEvent += OnSealedSheet;
        characterSeal.SealedAchievementsEvent += OnSealedAchievements;
        items.InventorySlotEvent += OnSlot;
    }

    public void Dispose()
    {
        characterSeal.CharacterSealListEvent -= OnList;
        characterSeal.CharacterSealEvent -= OnSealed;
        characterSeal.CharacterUnsealEvent -= OnUnsealed;
        characterSeal.SealedCharacterSheetEvent -= OnSealedSheet;
        characterSeal.SealedAchievementsEvent -= OnSealedAchievements;
        items.InventorySlotEvent -= OnSlot;
    }

    private static string SealText(int id, string fallback = "") => ItemData.Text(id, fallback);

    internal void Log(int textId, Color colour, string fallback = "") =>
        context.Log.Add(SealText(textId, fallback), CombatLogKind.Status, colour);

    internal void Open()
    {
        context.NpcDialog.Close();
        ShowPage(CsSealingPage, Title);
    }

    internal void ShowPage(Control page, string title)
    {
        CsSealingPage.Visible = page == CsSealingPage;
        CsSelectPage.Visible = page == CsSelectPage;
        CsCodePage.Visible = page == CsCodePage;
        CsWindow.Title = title;
        CsWindow.Visible = true;
        CsWindow.ResetSize();
    }

    private void Close()
    {
        CsWindow.Visible = false;
        Code = "";
        Picked = -1;
    }

    internal void BuildWindow()
    {
        CsWindow = new HudWindow("char_seal", Title, BodyWidth) { Visible = false };
        CsWindow.Closed += Close;
        Layer.AddChild(CsWindow);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        CsWindow.Body.AddChild(body);

        CsSealingPage = new VBoxContainer();
        CsSealingPage.AddThemeConstantOverride("separation", 10);
        body.AddChild(CsSealingPage);
        string help = SealText(CharacterSeal.HelpText);
        foreach (var mode in new[] { CharacterSeal.Mode.Seal, CharacterSeal.Mode.Unseal })
        {
            var (title, text) = CharacterSeal.HelpPanel(help, mode);
            CsSealingPage.AddChild(UiTheme.Text(title, 13, UiTheme.GoldBright, HorizontalAlignment.Center));
            var panel = UiTheme.Section();
            var label = UiTheme.Text(text, 12, UiTheme.TextHi);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(BodyWidth - 30, 64);
            panel.AddChild(label);
            CsSealingPage.AddChild(panel);
        }
        CsSealingPage.AddChild(Buttons(
            ("Seal", () => OpenTrans(CharacterSeal.Mode.Seal)),
            ("Unseal", () => OpenTrans(CharacterSeal.Mode.Unseal))));

        CsSelectPage = new VBoxContainer();
        CsSelectPage.AddThemeConstantOverride("separation", 8);
        body.AddChild(CsSelectPage);
        CsSelectTitle = UiTheme.Text("", 13, UiTheme.GoldBright, HorizontalAlignment.Center);
        CsSelectPage.AddChild(CsSelectTitle);
        var slots = UiTheme.Section();
        var slotBox = new VBoxContainer();
        slotBox.AddThemeConstantOverride("separation", 6);
        slots.AddChild(slotBox);
        for (int i = 0; i < csSlotButtons.Length; i++)
        {
            int index = i;
            var button = new Button
            {
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
                ClipText = true,
                CustomMinimumSize = new Vector2(0, RowHeight),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            button.AddThemeFontSizeOverride("font_size", 13);
            button.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
            button.AddThemeColorOverride("font_disabled_color", UiTheme.TextLo);
            button.AddThemeStyleboxOverride("hover", UiTheme.ListRow(true));
            button.AddThemeStyleboxOverride("pressed", UiTheme.ListRow(true));
            button.AddThemeStyleboxOverride("disabled", UiTheme.ListRow(false));
            button.Pressed += () => PickSlot(index);
            csSlotButtons[i] = button;
            slotBox.AddChild(button);
        }
        CsSelectPage.AddChild(slots);
        CsSelectPrompt = UiTheme.Text("", 12, UiTheme.TextDim, HorizontalAlignment.Center);
        CsSelectPage.AddChild(CsSelectPrompt);
        var selectButtons = Buttons(("Seal", ConfirmSlot), ("Cancel", Close));
        csSelectOk = (Button)selectButtons.GetChild(0);
        CsSelectPage.AddChild(selectButtons);

        CsCodePage = new VBoxContainer();
        CsCodePage.AddThemeConstantOverride("separation", 8);
        body.AddChild(CsCodePage);
        CsCodePage.AddChild(UiTheme.Text("To Seal, Please Enter the Code", 13, UiTheme.GoldBright, HorizontalAlignment.Center));
        CsCodeStars = UiTheme.Text("", 18, UiTheme.TextHi, HorizontalAlignment.Center);
        var starsPanel = UiTheme.Section();
        starsPanel.CustomMinimumSize = new Vector2(0, 40);
        starsPanel.AddChild(CsCodeStars);
        CsCodePage.AddChild(starsPanel);
        csKeys = new GridContainer { Columns = KeyColumns, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        csKeys.AddThemeConstantOverride("h_separation", 6);
        csKeys.AddThemeConstantOverride("v_separation", 6);
        CsCodePage.AddChild(csKeys);
        CsCodePage.AddChild(Buttons(("Shuffle", ShuffleKeys), ("Delete", PopDigit)));
        CsCodePage.AddChild(Buttons(("OK", SendCode), ("Cancel", Close)));
    }

    private static HBoxContainer Buttons(params (string Label, Action Pressed)[] buttons)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        foreach (var (label, pressed) in buttons)
        {
            var button = UiTheme.SmallButton(label, "");
            button.CustomMinimumSize = new Vector2(120, 28);
            button.Pressed += pressed;
            row.AddChild(button);
        }
        return row;
    }

    internal void ShuffleKeys()
    {
        foreach (var child in csKeys.GetChildren()) child.QueueFree();
        foreach (int digit in SealFeature.KeypadOrder())
        {
            var key = UiTheme.SmallButton(digit.ToString(), "");
            key.CustomMinimumSize = new Vector2(KeySize, KeySize);
            key.Pressed += () => PushDigit(digit);
            csKeys.AddChild(key);
        }
    }

    private void PushDigit(int digit)
    {
        if (Code.Length >= CharacterSeal.MaxCodeDigits) return;
        Code += (char)('0' + digit);
        CsCodeStars.Text = new string('*', Code.Length);
    }

    private void PopDigit()
    {
        if (Code.Length > 0) Code = Code[..^1];
        CsCodeStars.Text = new string('*', Code.Length);
    }

    private void OpenTrans(CharacterSeal.Mode mode)
    {
        Mode = mode;
        csBagSlot = -1;
        CsWindow.Visible = false;
        CsTrans.Visible = true;
        csTransShown = true;
        csCompanion ??= new BagCompanion(TakeFromBag, FitAt, _ => "", CloseTrans);
        context.BagCompanions.Attach(csCompanion);
        RefreshTrans();
    }

    private void CloseTrans()
    {
        if (!csTransShown) return;
        csTransShown = false;
        CsTrans.Visible = false;
        context.Tooltips.Hide();
        if (csCompanion != null) context.BagCompanions.Detach(csCompanion);
    }

    private bool TakeFromBag(int abs)
    {
        if (!self.Inventory.InMainBag(abs)) return false;
        if (self.Inventory[abs].IsEmpty) return true;
        if (self.Inventory[abs].State == ItemFlag.Duplicate)
        {
            Log(CharacterSeal.DuplicateSerialText, Lilac);
            return true;
        }
        if (CharacterSeal.Fits(Mode, ItemData.Get(self.Inventory[abs].ItemId)?.Kind ?? 0))
        {
            csBagSlot = abs;
            RefreshTrans();
        }
        return true;
    }

    private BagFit FitAt(int abs) =>
        abs == csBagSlot ? BagFit.Staged
        : CharacterSeal.Fits(Mode, ItemData.Get(self.Inventory[abs].ItemId)?.Kind ?? 0) ? BagFit.Normal
        : BagFit.Unfit;

    private void OnSlot(int abs, ItemSlot item)
    {
        if (csTransShown) RefreshTrans();
    }

    private void RefreshTrans()
    {
        if (csBagSlot >= 0 && (csBagSlot >= self.Inventory.Length || self.Inventory[csBagSlot].IsEmpty)) csBagSlot = -1;
        var held = csBagSlot >= 0 ? self.Inventory[csBagSlot] : default;
        CsSocket.Set(held);
        CsTransHeadline.Text = held.IsEmpty
            ? Mode == CharacterSeal.Mode.Seal ? "Place a Character Seal Scroll in the slot." : "Place a Cypher Ring in the slot."
            : ItemData.Get(held.ItemId)?.Name ?? "";
        CsTransGold.Text = $"{self.Sheet.Gold:n0}";
        context.BagCompanions.RefreshFit();
    }

    private void ConfirmTrans()
    {
        if (csBagSlot < 0)
        {
            RefreshTrans();
            return;
        }
        int ask = Mode == CharacterSeal.Mode.Seal ? CharacterSeal.SealAskText : CharacterSeal.UnsealAskText;
        Notice.Confirm(context.Root, SealText(ask), "OK", "Cancel", () => requests.List(), title: Title);
    }

    internal void BuildTrans()
    {
        CsTrans = new HudWindow("char_seal_trans", "Character Seal/Unseal", BodyWidth) { Visible = false };
        CsTrans.Closed += CloseTrans;
        Layer.AddChild(CsTrans);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        CsTrans.Body.AddChild(body);

        var stage = UiTheme.Section();
        body.AddChild(stage);
        var stageBox = new VBoxContainer();
        stageBox.AddThemeConstantOverride("separation", 8);
        stage.AddChild(stageBox);
        CsSocket = new WarehouseCell(0) { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        CsSocket.CustomMinimumSize = new Vector2(52, 52);
        CsSocket.OnActivate += _ => { csBagSlot = -1; RefreshTrans(); };
        CsSocket.OnHover += context.Tooltips.Hover;
        CsSocket.OnHoverEnd += context.Tooltips.Hide;
        stageBox.AddChild(CsSocket);
        CsTransHeadline = UiTheme.Text("", 13, UiTheme.TextHi, HorizontalAlignment.Center);
        stageBox.AddChild(CsTransHeadline);
        var notice = UiTheme.Text("You cannot unseal the character that was sealed from enemy nation.", 11, UiTheme.TextDim, HorizontalAlignment.Center);
        notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        notice.CustomMinimumSize = new Vector2(BodyWidth - 30, 0);
        stageBox.AddChild(notice);

        body.AddChild(Buttons(("OK", ConfirmTrans), ("Cancel", CloseTrans)));

        var footer = new HBoxContainer();
        var goldLabel = UiTheme.Text("Gold", 12, UiTheme.TextLo);
        goldLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(goldLabel);
        CsTransGold = UiTheme.Text("", 12, UiTheme.Gold, HorizontalAlignment.Right);
        footer.AddChild(CsTransGold);
        body.AddChild(footer);
    }

    private void OnList(CharacterSealList list)
    {
        if (!list.Ok)
        {
            if (list.Code == CharacterSeal.CodeTooSoon) Log(CharacterSeal.TooSoonText, BadColour);
            return;
        }
        if (csBagSlot < 0) return;
        Summaries = list.Slots;
        Picked = -1;
        CloseTrans();
        bool seal = Mode == CharacterSeal.Mode.Seal;
        CsSelectTitle.Text = seal ? "Select Character Slot" : "Select Slot to Unseal Character";
        CsSelectPrompt.Text = seal ? ">> Select Character to Seal <<" : "";
        csSelectOk.Text = seal ? "Seal" : "Unseal";
        RefreshSlots();
        ShowPage(CsSelectPage, seal ? "Character Seal" : "Unseal Character");
    }

    internal void RefreshSlots()
    {
        string empty = SealText(CharacterSeal.EmptySlotText, "Empty Slot");
        for (int i = 0; i < csSlotButtons.Length; i++)
        {
            var slot = i < Summaries.Length ? Summaries[i] : new CharacterSummary();
            var button = csSlotButtons[i];
            button.Text = CharacterSeal.SlotLine(slot, SealedClassName(slot.Class), empty);
            button.Disabled = !CharacterSeal.SlotPickable(Mode, slot, self.LastEnter.Name);
            button.AddThemeStyleboxOverride("normal", UiTheme.ListRow(i == Picked));
            button.AddThemeColorOverride("font_color", i == Picked ? UiTheme.GoldBright : UiTheme.TextHi);
        }
    }

    internal static string SealedClassName(int cls) =>
        ItemData.Text(EquipRules.ClassNameTextId(cls % 100), CharacterClassCatalog.DisplayName(cls));

    private void PickSlot(int index)
    {
        Picked = index;
        RefreshSlots();
    }

    private void ConfirmSlot()
    {
        if (Picked < 0 || Picked >= Summaries.Length)
        {
            Log(CharacterSeal.InvalidCharacterText, BadColour, "Invalid character");
            return;
        }
        if (csBagSlot < 0 || csBagSlot >= self.Inventory.Length || self.Inventory[csBagSlot].IsEmpty) return;
        if (Mode == CharacterSeal.Mode.Unseal)
        {
            requests.CharacterUnseal((byte)(csBagSlot - Inventory.GridStart), self.Inventory[csBagSlot].ItemId, (byte)Picked);
            Close();
            return;
        }
        Code = "";
        CsCodeStars.Text = "";
        ShuffleKeys();
        ShowPage(CsCodePage, "Character Seal");
    }

    private void SendCode()
    {
        if (Code.Length == 0 || Picked < 0 || Picked >= Summaries.Length) return;
        if (csBagSlot < 0 || csBagSlot >= self.Inventory.Length || self.Inventory[csBagSlot].IsEmpty) return;
        requests.Send((byte)(csBagSlot - Inventory.GridStart), self.Inventory[csBagSlot].ItemId, Summaries[Picked].Name, Code);
        Close();
    }

    private void OnSealed(CharacterSealOutcome outcome)
    {
        Close();
        if (outcome.Result != CharacterSealResult.Succeeded)
        {
            int text = CharacterSeal.SealRefusalText(outcome.Result, outcome.Code);
            string line = text == CharacterSeal.VoteWarningText
                ? TextTemplate.Fill(SealText(text), SealText(CharacterSeal.JuraidText, "Juraid"))
                : SealText(text);
            context.Log.Add(line, CombatLogKind.Status, BadColour);
            if (text == CharacterSeal.VaultLockedText)
                Notice.Show(context.Root, SealText(CharacterSeal.VaultLockedText), Title);
            return;
        }
        int abs = Inventory.GridStart + outcome.BagPos;
        if (abs >= Inventory.GridStart && abs < self.Inventory.Length)
        {
            var ring = self.Inventory[abs];
            ring.ItemId = outcome.ItemId;
            ring.Count = 1;
            ring.UniqueId = outcome.UniqueId;
            self.SetInventorySlot(abs, ring);
            if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
        }
        Log(CharacterSeal.SealedText, GoodColour);
    }

    private void OnUnsealed(CharacterUnsealOutcome outcome)
    {
        Close();
        if (outcome.Result != CharacterSealResult.Succeeded)
        {
            Log(CharacterSeal.UnsealFailedText, BadColour);
            return;
        }
        int abs = Inventory.GridStart + outcome.BagPos;
        if (abs >= Inventory.GridStart && abs < self.Inventory.Length)
        {
            self.SetInventorySlot(abs, default);
            if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
        }
        Log(CharacterSeal.UnsealedText, GoodColour);
    }

    internal static bool IsCypherRing(ItemSlot item) =>
        !item.IsEmpty && ItemData.Get(item.ItemId)?.Kind == CharacterSeal.RingKind;

    internal bool ShowSealedSheet(ItemSlot item)
    {
        if (!IsCypherRing(item) || item.UniqueId == 0) return false;
        if (SheetId == item.UniqueId && CsSheet.Visible) return true;
        ulong now = Time.GetTicksMsec();
        if (csSheetAskedAt != 0 && now - csSheetAskedAt < CharacterSeal.SheetRequestCooldownMs) return true;
        csSheetAskedAt = now;
        SheetId = item.UniqueId;
        requests.SealedCharacterSheet(item.UniqueId);
        return true;
    }

    internal void BuildSealedSheet()
    {
        CsSheet = new HudWindow("char_seal_sheet", "Cypher Ring") { Visible = false };
        CsSheet.Closed += () => { CsSheet.Visible = false; context.Tooltips.Hide(); };
        Layer.AddChild(CsSheet);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        CsSheet.Body.AddChild(body);

        csSheetName = UiTheme.Text("", 15, UiTheme.TextHi, HorizontalAlignment.Center);
        body.AddChild(csSheetName);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        body.AddChild(top);

        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 4);
        top.AddChild(left);
        var idPanel = UiTheme.Section();
        var idBox = new VBoxContainer();
        idBox.AddThemeConstantOverride("separation", 2);
        idPanel.AddChild(idBox);
        left.AddChild(idPanel);
        csSheetLevel = SheetRow(idBox, "Lv");
        csSheetRebirth = SheetRow(idBox, "RB:");
        csSheetRace = SheetRow(idBox, SealText(CharacterSeal.RaceLabelText, "Race :"));
        csSheetClass = SheetRow(idBox, SealText(CharacterSeal.JobLabelText, "Job :"));
        var achievements = UiTheme.SmallButton("Achievements", "");
        achievements.Pressed += AskSealedAchievements;
        left.AddChild(achievements);

        var statPanel = UiTheme.Section();
        var statGrid = new GridContainer { Columns = 4 };
        statGrid.AddThemeConstantOverride("h_separation", 8);
        statGrid.AddThemeConstantOverride("v_separation", 3);
        statPanel.AddChild(statGrid);
        left.AddChild(statPanel);
        string[] statNames = ["STR :", "HP :", "DEX :", "INT :", "MP :"];
        for (int i = 0; i < CharacterSealWire.StatCount; i++)
        {
            statGrid.AddChild(UiTheme.Text(statNames[i], 12, UiTheme.TextLo));
            csSheetStats[i] = UiTheme.Text("", 12, UiTheme.TextHi);
            statGrid.AddChild(csSheetStats[i]);
            if (i == 0)
            {
                statGrid.AddChild(UiTheme.Text("Point :", 12, UiTheme.TextLo));
                csSheetSkills[0] = UiTheme.Text("", 12, UiTheme.TextHi);
                statGrid.AddChild(csSheetSkills[0]);
            }
            else if (i <= csSheetSkillNames.Length)
            {
                csSheetSkillNames[i - 1] = UiTheme.Text("", 12, UiTheme.TextLo);
                statGrid.AddChild(csSheetSkillNames[i - 1]);
                csSheetSkills[i] = UiTheme.Text("", 12, UiTheme.TextHi);
                statGrid.AddChild(csSheetSkills[i]);
            }
            else
            {
                statGrid.AddChild(UiTheme.Text("Master:", 12, UiTheme.TextLo));
                csSheetSkills[i] = UiTheme.Text("", 12, UiTheme.TextHi);
                statGrid.AddChild(csSheetSkills[i]);
            }
        }
        var wealth = UiTheme.Section();
        var wealthBox = new VBoxContainer();
        wealthBox.AddThemeConstantOverride("separation", 2);
        wealth.AddChild(wealthBox);
        left.AddChild(wealth);
        csSheetLoyalty = SheetRow(wealthBox, "CONT");
        csSheetGold = SheetRow(wealthBox, "Gold");

        var equip = new GridContainer { Columns = SealSheetEquipColumns };
        equip.AddThemeConstantOverride("h_separation", 4);
        equip.AddThemeConstantOverride("v_separation", 4);
        var equipPanel = UiTheme.Section();
        equipPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        equipPanel.AddChild(equip);
        top.AddChild(equipPanel);
        var bag = new GridContainer { Columns = SealSheetBagColumns, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        bag.AddThemeConstantOverride("h_separation", 4);
        bag.AddThemeConstantOverride("v_separation", 4);
        var bagPanel = UiTheme.Section();
        bagPanel.AddChild(bag);
        body.AddChild(bagPanel);
        for (int i = 0; i < csSheetCells.Length; i++)
        {
            var cell = new ItemSlotView(SealSheetCellSize) { Index = i };
            cell.Hovered += view => context.Tooltips.Show(-1, view.Item);
            cell.Unhovered += _ => context.Tooltips.Hide();
            csSheetCells[i] = cell;
            (i < CharacterSealWire.EquipItems ? equip : bag).AddChild(cell);
        }

        csSheetAchievements = UiTheme.Text("", 11, UiTheme.TextDim);
        csSheetAchievements.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        csSheetAchievements.Visible = false;
        body.AddChild(csSheetAchievements);
    }

    private static Label SheetRow(VBoxContainer box, string label)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var name = UiTheme.Text(label, 12, UiTheme.TextLo);
        name.CustomMinimumSize = new Vector2(52, 0);
        row.AddChild(name);
        var value = UiTheme.Text("", 12, UiTheme.TextHi);
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(value);
        box.AddChild(row);
        return value;
    }

    private static readonly string[][] SealSheetSkillNames =
    [
        ["Attack:", "Defense:", "Passion:"],
        ["Archery:", "Dagger:", "Explore:"],
        ["Flame:", "Glacier:", "Lightning:"],
        ["Recovery:", "Unsuitable:", "Holy Spirit:"],
        ["Attack:", "Defense:", "Devil:"],
    ];

    internal void OnSealedSheet(SealedCharacterSheet? sheet)
    {
        if (sheet == null)
        {
            Log(CharacterSeal.SheetFailedText, BadColour, "Failed.");
            return;
        }
        csSheetName.Text = sheet.Name;
        csSheetName.AddThemeColorOverride("font_color",
            sheet.Nation == Nations.Karus ? SealSheetKarusColour : SealSheetElmoradColour);
        csSheetLevel.Text = sheet.Level.ToString();
        csSheetRebirth.Text = pet.Items.TryGetValue(SheetId, out var ring) ? ring.Tail.ToString() : "0";
        csSheetRace.Text = StarterStats.RaceName(sheet.Race);
        csSheetClass.Text = SealedClassName(sheet.Class);
        for (int i = 0; i < CharacterSealWire.StatCount; i++)
            csSheetStats[i].Text = CharacterSeal.StatLine(sheet.Stats[i], sheet.StatBonus[i]);
        csSheetSkills[0].Text = sheet.FreePoints.ToString();
        int family = Math.Clamp(CharacterClassCatalog.Family(sheet.Class), 1, SealSheetSkillNames.Length) - 1;
        for (int i = 0; i < csSheetSkillNames.Length; i++)
            csSheetSkillNames[i].Text = SealSheetSkillNames[family][i];
        for (int i = 0; i < CharacterSealWire.SkillTrees; i++)
            csSheetSkills[i + 1].Text = sheet.SkillTrees[i].ToString();
        csSheetLoyalty.Text = $"{sheet.Loyalty:n0}";
        csSheetGold.Text = $"{sheet.Gold:n0}";
        for (int i = 0; i < csSheetCells.Length; i++) csSheetCells[i].Set(sheet.Items[i]);
        csSheetAchievements.Visible = false;
        CsSheet.Visible = true;
        CsSheet.ResetSize();
    }

    private void AskSealedAchievements()
    {
        if (csSheetName.Text.Length > 0) requests.SealedAchievements(csSheetName.Text);
    }

    private void OnSealedAchievements(int[] ids)
    {
        if (!CsSheet.Visible) return;
        var names = new System.Collections.Generic.List<string>();
        foreach (int id in ids)
            if (AchievementData.Get(id) is { } info && info.Name.Length > 0) names.Add(info.Name);
        csSheetAchievements.Text = names.Count > 0 ? string.Join(", ", names) : "No achievements.";
        csSheetAchievements.Visible = true;
        CsSheet.ResetSize();
    }
}
