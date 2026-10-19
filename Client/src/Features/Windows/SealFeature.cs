using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class SealFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly ItemUpgradeSession itemUpgrade;
    private readonly ItemsSession items;
    private readonly SelfSession self;
    private readonly IItemUpgradeRequests itemUpgradeRequests;

    public SealFeature(IWorldContext context, ItemUpgradeSession itemUpgrade, ItemsSession items, SelfSession self, IItemUpgradeRequests itemUpgradeRequests)
    {
        this.context = context;
        this.itemUpgrade = itemUpgrade;
        this.items = items;
        this.self = self;
        this.itemUpgradeRequests = itemUpgradeRequests;
    }

    private const int Fee = 1_000_000;
    private const int CodeLength = 4;
    private const int StoneItem = 810_890_000;
    private const int ConfirmText = 15110;
    private const int UnsealConfirmText = 15111;
    private const int UnbindStonesText = 4613;
    private const int ShortFeeText = 15114;
    private const int WrongCodeText = 15107;
    private const int PremiumOnlyText = 15105;
    private const int FailedText = 15115;
    private const int UnsealFailedText = 15116;
    private const int BindFailedText = 19006;
    private const int UnbindNotSealableText = 19000;
    private const int UnbindMissingStonesText = 19005;
    private const int UnbindFailedText = 19002;
    private const string CodePrompt = "Enter your VIP vault password";
    private const string ItemChangedText = "The item in the socket changed. Check it and confirm again.";
    private const string CodeChangedText = CodePrompt + " and confirm again.";

    internal enum Mode { Secret, Bind }

    private static string WindowTitle(Mode mode) =>
        mode == Mode.Secret ? "Item Seal / Unseal" : "Item Bind / Release";

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private WarehouseCell socket = null!;
    private BagCompanion? companion;
    private Label headline = null!;
    private Label prompt = null!;
    private VBoxContainer codeRow = null!;
    private LineEdit codeField = null!;
    internal PanelContainer Pad { get; private set; } = null!;
    private Label gold = null!;
    private Button confirm = null!;
    private Mode mode = Mode.Secret;
    private int slot = -1;
    private string code = "";
    private bool shown;

    public bool IsOpen => shown;

    private bool waiting;
    private Notice? notice;

    public void Build()
    {
        BuildWindow();
        itemUpgrade.ItemSealEvent += OnItem;
        items.InventorySlotEvent += OnInventorySlot;
    }

    public void Dispose()
    {
        itemUpgrade.ItemSealEvent -= OnItem;
        items.InventorySlotEvent -= OnInventorySlot;
    }

    private void OnInventorySlot(int abs, ItemSlot item)
    {
        if (shown) RefreshWindow();
    }

    private static ItemSealType ActionFor(Mode mode, ItemFlag state) => mode switch
    {
        Mode.Secret => state == ItemFlag.Sealed ? ItemSealType.Unseal : ItemSealType.Seal,
        _ => state == ItemFlag.Bound ? ItemSealType.Unbind : ItemSealType.Bind,
    };

    internal void Open(Mode mode)
    {
        this.mode = mode;
        slot = -1;
        code = "";
        DismissNotice();
        Pad.Visible = false;
        Panel.Title = WindowTitle(mode);
        Panel.Visible = true;
        shown = true;
        companion ??= new BagCompanion(TakeFromBag, FitAt, _ => "", Close);
        context.BagCompanions.Attach(companion);
        RefreshWindow();
    }

    private bool TakeFromBag(int abs)
    {
        if (!self.Inventory.InMainBag(abs)) return false;
        TakeSocket(abs - Inventory.GridStart);
        return true;
    }

    private BagFit FitAt(int abs) =>
        abs == slot ? BagFit.Staged : SocketRefusal(self.Inventory[abs]) == null ? BagFit.Normal : BagFit.Unfit;

    public void Close()
    {
        if (!shown) return;
        shown = false;
        slot = -1;
        code = "";
        Panel.Visible = false;
        Pad.Visible = false;
        DismissNotice();
        context.Tooltips.Hide();
        if (companion != null) context.BagCompanions.Detach(companion);
    }

    private void RefreshWindow()
    {
        if (slot >= 0 && (slot >= self.Inventory.Length || self.Inventory[slot].IsEmpty))
            slot = -1;

        var held = slot >= 0 ? self.Inventory[slot] : default;
        socket.Set(held);

        gold.Text = $"{self.Sheet.Gold:n0}";
        var action = ActionFor(mode, held.State);
        bool needsCode = !held.IsEmpty && NeedsCode(action);
        codeRow.Visible = needsCode;
        codeField.Text = new string('*', code.Length);

        headline.Text = held.IsEmpty
            ? "Place an item in the socket."
            : action switch
            {
                ItemSealType.Seal => "This item will be sealed.",
                ItemSealType.Unseal => "The seal on this item will be lifted.",
                ItemSealType.Bind => "This item will be bound to you.",
                _ => "The binding on this item will be released.",
            };

        prompt.Text = held.IsEmpty ? "" : action switch
        {
            ItemSealType.Seal => $"{Fee:n0} gold",
            ItemSealType.Unbind => UnbindCostLine(held),
            _ => "",
        };

        confirm.Disabled = held.IsEmpty || waiting || (needsCode && code.Length != CodeLength);
        context.BagCompanions.RefreshFit();
    }

    private static bool NeedsCode(ItemSealType action) => action != ItemSealType.Bind;

    private string UnbindCostLine(ItemSlot held)
    {
        int stones = UnbindStonesFor(held.ItemId);
        if (stones <= 0) return "Free of charge";
        string cost = TextTemplate.Fill(
            ItemData.Text(UnbindStonesText, "Number of seal item needed to unbound: %d"), stones);
        return $"{cost} (you have {CarriedStones():n0})";
    }

    private static int UnbindStonesFor(int itemId) =>
        ItemData.Get(itemId) is { } def ? ItemData.UnbindStones(def, ItemData.ExtFor(itemId)) : 0;

    private int CarriedStones()
    {
        int total = 0;
        for (int abs = Inventory.GridStart; abs < self.Inventory.Length; abs++)
            if (self.Inventory[abs].ItemId == StoneItem) total += Mathf.Max(1, (int)self.Inventory[abs].Count);
        return total;
    }

    private const int TextCannot = 15101;
    private const int TextBindStatus = 19017;

    internal void TakeSocket(int bagIndex)
    {
        if (waiting || notice != null) return;
        int abs = Inventory.GridStart + bagIndex;
        if (abs >= self.Inventory.Length || self.Inventory[abs].IsEmpty) return;
        if (SocketRefusal(self.Inventory[abs]) is { } refusal)
        {
            context.Log.Notice(refusal);
            return;
        }
        slot = abs;
        RefreshWindow();
    }

    private static string? SocketRefusal(ItemSlot itemSlot) => ItemData.ShownFlag(itemSlot) switch
    {
        ItemFlag.CharacterSeal or ItemFlag.Duplicate =>
            ItemData.Text(GameText.DuplicateSerial, "Items with duplicated serial number cannot be upgraded."),
        ItemFlag.NotBound => ItemData.Text(TextBindStatus, "You cannot register an item on seal/bind status."),
        _ when (ItemData.Get(itemSlot.ItemId)?.Countable ?? 0) != 0 || itemSlot.ExpiresAt != 0 =>
            ItemData.Text(TextCannot, "Item cannot be sealed."),
        _ => null,
    };

    private void ClearSocket()
    {
        if (waiting || notice != null) return;
        slot = -1;
        RefreshWindow();
    }

    private void AskConfirm()
    {
        if (waiting || notice != null || confirm.Disabled) return;
        if (this.slot < 0 || this.slot >= self.Inventory.Length || self.Inventory[this.slot].IsEmpty) return;

        var action = ActionFor(mode, self.Inventory[this.slot].State);
        string question = action switch
        {
            ItemSealType.Seal => ItemData.Text(ConfirmText,
                $"It costs {Fee:n0} gold and the item can't be traded, upgraded or destroyed until unsealed. "
                + "Would you like to seal your item now?"),
            ItemSealType.Unseal => ItemData.Text(UnsealConfirmText, "Would you like to lift the seal?"),
            ItemSealType.Bind => "Bind this item to you?",
            _ => "Release the binding?",
        };
        int slot = this.slot;
        var item = self.Inventory[slot];
        notice = Notice.Confirm(context.Root, question, "Confirm", "Cancel",
            () =>
            {
                notice = null;
                Send(slot, item);
            },
            () => notice = null,
            WindowTitle(mode));
        Audio.PlayUi(Sfx.MsgBoxPop);
    }

    private void DismissNotice()
    {
        if (notice != null && GodotObject.IsInstanceValid(notice)) notice.Close();
        notice = null;
    }

    private void Send(int slot, ItemSlot item)
    {
        if (waiting || !shown) return;
        if (this.slot != slot || !self.Inventory.Holds(slot, item))
        {
            context.Log.Notice(ItemChangedText);
            RefreshWindow();
            return;
        }
        if (mode == Mode.Secret && code.Length != CodeLength)
        {
            context.Log.Notice(CodeChangedText);
            RefreshWindow();
            return;
        }

        waiting = true;
        RefreshWindow();
        itemUpgradeRequests.ItemSeal(
            ActionFor(mode, item.State),
            item.ItemId,
            (byte)(slot - Inventory.GridStart),
            code);
    }

    private void OnItem(ItemSealType sealType, ItemSealResult result, int itemId, int srcPos)
    {
        waiting = false;
        if (sealType == ItemSealType.Bind && FinishBindBeforeEquip(result, srcPos)) return;
        int abs = srcPos >= 0 ? Inventory.GridStart + srcPos : slot;

        if (result != ItemSealResult.Succeeded)
        {
            context.Log.Add(Refusal(sealType, result), CombatLogKind.Status, UiKit.WhRefusalTint);
            if (result == ItemSealResult.CodeLockedOut)
                context.Log.Notice("The VIP vault password stays locked until you log in again.");
            code = "";
            RefreshWindow();
            return;
        }

        if (self.Inventory.Holds(abs, itemId)) ApplyFlag(abs, sealType);
        context.Log.Notice(sealType switch
        {
            ItemSealType.Seal => "Item sealed.",
            ItemSealType.Unseal => "Seal lifted.",
            ItemSealType.Bind => "Item bound to you.",
            _ => "Binding released.",
        });
        code = "";
        RefreshWindow();
    }

    private void ApplyFlag(int absSlot, ItemSealType sealType)
    {
        if (absSlot < 0 || absSlot >= self.Inventory.Length || self.Inventory[absSlot].IsEmpty) return;

        var itemSlot = self.Inventory[absSlot];
        itemSlot.Flag = (byte)(sealType switch
        {
            ItemSealType.Seal => ItemFlag.Sealed,
            ItemSealType.Unseal => ItemFlag.Unsealed,
            ItemSealType.Bind => ItemFlag.Bound,
            _ => ItemFlag.NotBound,
        });
        self.SetInventorySlot(absSlot, itemSlot);

        if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
    }

    private static string Refusal(ItemSealType sealType, ItemSealResult result)
    {
        bool secret = sealType is ItemSealType.Seal or ItemSealType.Unseal;
        return (sealType, result) switch
        {
            (_, ItemSealResult.TooSoon) =>
                ItemData.Text(CharacterSeal.TooSoonText, "Please try again. You may not repeat this function instantly."),
            (ItemSealType.Bind, _) => ItemData.Text(BindFailedText, "Item restoration failed."),
            (_, ItemSealResult.NoCodeSet) => ItemData.Text(CharacterSeal.NoVaultPasswordText,
                "There is no password set for the VIP Vault. Visit the Inn Hostess to set the VIP Vault password."),
            (_, ItemSealResult.CodeLockedOut) => ItemData.Text(CharacterSeal.VaultLockedText,
                "You have inserted wrong Password more than 3 times."),
            (_, ItemSealResult.WrongVaultPassword) => ItemData.Text(CharacterSeal.WrongVaultPasswordText, "Wrong VIP Key Password"),
            (ItemSealType.Unbind, ItemSealResult.WrongCode) =>
                ItemData.Text(CharacterSeal.WrongVaultPasswordText, "Wrong VIP Key Password"),
            (_, ItemSealResult.WrongCode) => ItemData.Text(WrongCodeText, "Wrong Security Code"),
            (_, ItemSealResult.MissingMaterial) when secret => ItemData.Text(PremiumOnlyText,
                "Item Sealing/Unsealing functions are available to Gold and Platinum Premium users ONLY."),
            (ItemSealType.Unbind, ItemSealResult.MissingMaterial) =>
                ItemData.Text(UnbindMissingStonesText, "Insufficient items to perform the seal."),
            (ItemSealType.Seal, ItemSealResult.NeedCoins) =>
                ItemData.Text(ShortFeeText, "You are short on the seal lifting fee."),
            (ItemSealType.Unbind, ItemSealResult.Failed or ItemSealResult.NotSealable) =>
                ItemData.Text(UnbindNotSealableText, "This item cannot be sealed."),
            (ItemSealType.Seal, _) => ItemData.Text(FailedText, "Seal failed."),
            (ItemSealType.Unseal, _) => ItemData.Text(UnsealFailedText, "Seal lift failed."),
            _ => ItemData.Text(UnbindFailedText, "Sealing the item failed."),
        };
    }

    internal void BuildWindow()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("seal", WindowTitle(Mode.Secret))
        {
            Visible = false,
        };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(304, 0) };
        root.AddThemeConstantOverride("separation", 9);
        Panel.Body.AddChild(root);

        var stage = UiTheme.Section();
        root.AddChild(stage);
        var stageBox = new VBoxContainer();
        stageBox.AddThemeConstantOverride("separation", 7);
        stage.AddChild(stageBox);

        var socketRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        socketRow.AddThemeConstantOverride("separation", 10);
        stageBox.AddChild(socketRow);

        socket = new WarehouseCell(0);
        socket.CustomMinimumSize = new Vector2(52, 52);
        socket.OnActivate += _ => ClearSocket();
        socket.OnHover += context.Tooltips.Hover;
        socket.OnHoverEnd += context.Tooltips.Hide;
        socketRow.AddChild(socket);

        var socketText = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        socketText.AddThemeConstantOverride("separation", 2);
        socketRow.AddChild(socketText);
        headline = UiTheme.Text("", 13, UiTheme.TextHi);
        headline.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        headline.CustomMinimumSize = new Vector2(214, 0);
        socketText.AddChild(headline);
        prompt = UiTheme.Text("", 11, UiTheme.TextDim);
        prompt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        prompt.CustomMinimumSize = new Vector2(214, 0);
        socketText.AddChild(prompt);

        codeRow = new VBoxContainer();
        codeRow.AddThemeConstantOverride("separation", 4);
        stageBox.AddChild(codeRow);
        codeRow.AddChild(UiTheme.Text(
            CodePrompt, 11, UiTheme.TextDim, HorizontalAlignment.Center));

        codeField = new LineEdit
        {
            Editable = false,
            Alignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        codeField.AddThemeFontSizeOverride("font_size", 15);
        codeField.GuiInput += OnCodeFieldInput;
        codeRow.AddChild(codeField);

        BuildKeypad();
        codeRow.AddChild(Pad);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 10);
        root.AddChild(buttons);
        confirm = UiTheme.SmallButton("Confirm", "");
        confirm.CustomMinimumSize = new Vector2(96, 28);
        confirm.Pressed += AskConfirm;
        buttons.AddChild(confirm);
        var cancel = UiTheme.SmallButton("Cancel", "");
        cancel.CustomMinimumSize = new Vector2(96, 28);
        cancel.Pressed += Close;
        buttons.AddChild(cancel);

        var bagSection = UiTheme.Section();
        root.AddChild(bagSection);
        var bagBox = new VBoxContainer();
        bagBox.AddThemeConstantOverride("separation", 6);
        bagSection.AddChild(bagBox);

        var footer = new HBoxContainer();
        bagBox.AddChild(footer);
        var goldLabel = UiTheme.Text("Gold", 12, UiTheme.TextLo);
        goldLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(goldLabel);
        gold = UiTheme.Text("", 12, UiTheme.Gold, HorizontalAlignment.Right);
        footer.AddChild(gold);
    }

    private void BuildKeypad()
    {
        Pad = new PanelContainer { Visible = false };
        Pad.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 8);
        Pad.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        margin.AddChild(box);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(grid);

        foreach (int digit in KeypadOrder())
            grid.AddChild(Key(digit.ToString(), () => PushDigit(digit)));

        grid.AddChild(Key("⌫", () =>
        {
            if (code.Length > 0) code = code[..^1];
            RefreshWindow();
        }));
        grid.AddChild(Key("C", () => { code = ""; RefreshWindow(); }));

        var done = UiTheme.SmallButton("Done", "");
        done.CustomMinimumSize = new Vector2(0, 30);
        done.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        done.Pressed += () => Pad.Visible = false;
        box.AddChild(done);
    }

    private static Button Key(string label, System.Action onPressed)
    {
        var key = UiTheme.SmallButton(label, "");
        key.CustomMinimumSize = new Vector2(52, 32);
        key.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        key.Pressed += () => onPressed();
        return key;
    }

    internal static int[] KeypadOrder()
    {
        var order = new int[10];
        for (int i = 0; i < 10; i++) order[i] = i;
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = (int)(GD.Randi() % (uint)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    internal void PushDigit(int digit)
    {
        if (code.Length >= CodeLength) return;
        code += (char)('0' + digit);
        RefreshWindow();
        if (code.Length == CodeLength) Pad.Visible = false;
    }

    private void OnCodeFieldInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            return;

        Pad.Visible = !Pad.Visible;
        codeField.AcceptEvent();
    }

    private const int TextBindOnEquipTitle = 19003;
    private const int TextBindOnEquipAsk = 19004;

    private Notice? bindAsk;
    private System.Action? equipAfterBind;

    internal bool AskBindBeforeEquip(int bagAbs, System.Action equip)
    {
        var item = self.Inventory[bagAbs];
        if (ItemData.ShownFlag(item) != ItemFlag.NotBound) return false;
        if (equipAfterBind != null || (bindAsk != null && GodotObject.IsInstanceValid(bindAsk))) return true;
        bindAsk = Notice.Confirm(context.Root,
            ItemData.Text(TextBindOnEquipAsk, "Once you equip the item, the item will be bound to you and will not be tradable. Would you like to continue?"),
            "Yes", "No",
            () =>
            {
                if (self.Inventory[bagAbs].ItemId != item.ItemId) return;
                equipAfterBind = equip;
                itemUpgradeRequests.ItemBind(item.ItemId, (byte)(bagAbs - Inventory.GridStart));
            },
            title: ItemData.Text(TextBindOnEquipTitle, "Item Restoration."));
        return true;
    }

    private bool FinishBindBeforeEquip(ItemSealResult result, int srcPos)
    {
        if (equipAfterBind is not { } equip) return false;
        equipAfterBind = null;
        if (result != ItemSealResult.Succeeded)
        {
            context.Log.Add(Refusal(ItemSealType.Bind, result), CombatLogKind.Status, UiKit.WhRefusalTint);
            return true;
        }
        if (srcPos >= 0) ApplyFlag(Inventory.GridStart + srcPos, ItemSealType.Bind);
        equip();
        return true;
    }
}
