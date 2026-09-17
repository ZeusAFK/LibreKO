using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int SealFee = 1_000_000;
    private const int SealCodeLength = 8;
    private const int SealBagCells = 28;

    internal enum SealMode { Secret, Bind }

    private CanvasLayer _sealLayer = null!;
    private HudWindow _sealPanel = null!;
    private WarehouseCell _sealSocket = null!;
    private readonly WarehouseCell[] _sealBagCells = new WarehouseCell[SealBagCells];
    private Label _sealHeadline = null!;
    private Label _sealPrompt = null!;
    private VBoxContainer _sealCodeRow = null!;
    private LineEdit _sealCodeField = null!;
    private PanelContainer _sealPad = null!;
    private Label _sealGold = null!;
    private Button _sealConfirm = null!;

    private PanelContainer _sealAskPanel = null!;
    private Label _sealAskText = null!;

    private SealMode _sealMode = SealMode.Secret;
    private int _sealSlot = -1;
    private string _sealCode = "";
    private bool _sealShown;

    private void SealInit()
    {
        BuildSealWindow();
        Net.I.ItemSealEvent += OnItemSeal;
        Net.I.InventorySlotEvent += OnSealInventorySlot;
    }

    private void SealDispose()
    {
        Net.I.ItemSealEvent -= OnItemSeal;
        Net.I.InventorySlotEvent -= OnSealInventorySlot;
    }

    private void OnSealInventorySlot(int abs, ItemSlot item)
    {
        if (_sealShown) RefreshSealWindow();
    }

    private static ItemSealType SealActionFor(SealMode mode, ItemFlag state) => mode switch
    {
        SealMode.Secret => state == ItemFlag.Sealed ? ItemSealType.Unseal : ItemSealType.Seal,
        _ => state == ItemFlag.Bound ? ItemSealType.Unbind : ItemSealType.Bind,
    };

    private void OpenSealWindow(SealMode mode)
    {
        _sealMode = mode;
        _sealSlot = -1;
        _sealCode = "";
        _sealPad.Visible = false;
        _sealPanel.Title = mode == SealMode.Secret ? "Item Seal / Unseal" : "Item Bind / Release";
        _sealPanel.Visible = true;
        _sealShown = true;
        RefreshSealWindow();
    }

    private void CloseSealWindow()
    {
        if (!_sealShown) return;
        _sealShown = false;
        _sealSlot = -1;
        _sealCode = "";
        _sealPanel.Visible = false;
        _sealPad.Visible = false;
        _sealAskPanel.Visible = false;
        HideItemTooltip();
    }

    private void RefreshSealWindow()
    {
        if (_sealSlot >= 0 && (_sealSlot >= Inv.Length || Inv[_sealSlot].IsEmpty))
            _sealSlot = -1;

        var held = _sealSlot >= 0 ? Inv[_sealSlot] : default;
        _sealSocket.Set(held);

        for (int i = 0; i < SealBagCells; i++)
        {
            int abs = GridStart + i;
            _sealBagCells[i].Set(abs < Inv.Length ? Inv[abs] : default);
        }

        _sealGold.Text = $"{Sheet.Gold:n0}";
        _sealCodeRow.Visible = _sealMode == SealMode.Secret;
        _sealCodeField.Text = new string('*', _sealCode.Length);

        var action = SealActionFor(_sealMode, held.State);
        _sealHeadline.Text = held.IsEmpty
            ? "Place an item in the socket."
            : action switch
            {
                ItemSealType.Seal => "This item will be sealed.",
                ItemSealType.Unseal => "The seal on this item will be lifted.",
                ItemSealType.Bind => "This item will be bound to you.",
                _ => "The binding on this item will be released.",
            };

        _sealPrompt.Text = held.IsEmpty || action != ItemSealType.Seal
            ? ""
            : $"{SealFee:n0} coins";

        _sealConfirm.Disabled = held.IsEmpty
            || (_sealMode == SealMode.Secret && _sealCode.Length != SealCodeLength);
    }

    private void TakeSealSocket(int bagIndex)
    {
        int abs = GridStart + bagIndex;
        if (abs >= Inv.Length || Inv[abs].IsEmpty) return;
        _sealSlot = abs;
        RefreshSealWindow();
    }

    private void ClearSealSocket()
    {
        _sealSlot = -1;
        RefreshSealWindow();
    }

    private void AskSealConfirm()
    {
        if (_sealSlot < 0 || _sealSlot >= Inv.Length || Inv[_sealSlot].IsEmpty) return;

        var action = SealActionFor(_sealMode, Inv[_sealSlot].State);
        _sealAskText.Text = action switch
        {
            ItemSealType.Seal =>
                $"It costs {SealFee:n0} coins and the item cannot be traded, upgraded or destroyed "
                + "until unsealed. Seal it now?",
            ItemSealType.Unseal => "Lift the seal?",
            ItemSealType.Bind => "Bind this item to you?",
            _ => "Release the binding?",
        };
        _sealAskPanel.Visible = true;
        Audio.PlayUi(Sfx.MsgBoxPop);
    }

    private void SendSeal()
    {
        _sealAskPanel.Visible = false;
        if (_sealSlot < 0 || _sealSlot >= Inv.Length || Inv[_sealSlot].IsEmpty) return;

        var slot = Inv[_sealSlot];
        Net.I.SendItemSeal(
            SealActionFor(_sealMode, slot.State),
            slot.ItemId,
            (byte)(_sealSlot - GridStart),
            _sealCode);
    }

    private void OnItemSeal(ItemSealType sealType, ItemSealResult result, int itemId, int srcPos)
    {
        int abs = srcPos >= 0 ? GridStart + srcPos : _sealSlot;

        if (result != ItemSealResult.Succeeded)
        {
            CombatNotice(SealRefusal(sealType, result));
            _sealCode = "";
            RefreshSealWindow();
            return;
        }

        ApplySealFlag(abs, sealType);
        CombatNotice(sealType switch
        {
            ItemSealType.Seal => "Item sealed.",
            ItemSealType.Unseal => "Seal lifted.",
            ItemSealType.Bind => "Item bound to you.",
            _ => "Binding released.",
        });
        _sealCode = "";
        RefreshSealWindow();
    }

    private void ApplySealFlag(int absSlot, ItemSealType sealType)
    {
        if (absSlot < 0 || absSlot >= Inv.Length || Inv[absSlot].IsEmpty) return;

        var slot = Inv[absSlot];
        slot.Flag = (byte)(sealType switch
        {
            ItemSealType.Seal => ItemFlag.Sealed,
            ItemSealType.Unseal => ItemFlag.Unsealed,
            ItemSealType.Bind => ItemFlag.Bound,
            _ => ItemFlag.NotBound,
        });
        Inv[absSlot] = slot;
        Net.I.MirrorInventorySlot(absSlot, slot);

        if (CharTabOpen()) RefreshInventoryUI();
    }

    private static string SealRefusal(ItemSealType sealType, ItemSealResult result) => result switch
    {
        ItemSealResult.NeedCoins => "You are short on the seal fee.",
        ItemSealResult.WrongCode => "Wrong secret answer.",
        ItemSealResult.MissingMaterial => "Insufficient items to perform the seal.",
        ItemSealResult.TooSoon => "Please try again in a moment.",
        ItemSealResult.NoCodeSet => "No secret answer is set for this account yet.",
        ItemSealResult.CodeLockedOut => "Too many wrong answers. Contact support to reset it.",
        _ => sealType switch
        {
            ItemSealType.Seal => "Seal failed.",
            ItemSealType.Unseal => "Seal lift failed.",
            ItemSealType.Bind => "This item cannot be bound.",
            _ => "Item restoration failed.",
        },
    };

    private void BuildSealWindow()
    {
        _sealLayer = new CanvasLayer { Layer = 75 };
        AddChild(_sealLayer);

        _sealPanel = new HudWindow("seal", "Item Seal / Unseal", new Vector2(210, 110))
        {
            Visible = false,
        };
        _sealPanel.Closed += CloseSealWindow;
        _sealLayer.AddChild(_sealPanel);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(304, 0) };
        root.AddThemeConstantOverride("separation", 9);
        _sealPanel.Body.AddChild(root);

        var stage = UiTheme.Section();
        root.AddChild(stage);
        var stageBox = new VBoxContainer();
        stageBox.AddThemeConstantOverride("separation", 7);
        stage.AddChild(stageBox);

        var socketRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        socketRow.AddThemeConstantOverride("separation", 10);
        stageBox.AddChild(socketRow);

        _sealSocket = new WarehouseCell(0);
        _sealSocket.CustomMinimumSize = new Vector2(52, 52);
        _sealSocket.OnActivate += _ => ClearSealSocket();
        _sealSocket.OnHover += HoverWarehouseCell;
        _sealSocket.OnHoverEnd += HideItemTooltip;
        socketRow.AddChild(_sealSocket);

        var socketText = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        socketText.AddThemeConstantOverride("separation", 2);
        socketRow.AddChild(socketText);
        _sealHeadline = UiTheme.Text("", 13, UiTheme.TextHi);
        _sealHeadline.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _sealHeadline.CustomMinimumSize = new Vector2(214, 0);
        socketText.AddChild(_sealHeadline);
        _sealPrompt = UiTheme.Text("", 11, UiTheme.TextDim);
        _sealPrompt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _sealPrompt.CustomMinimumSize = new Vector2(214, 0);
        socketText.AddChild(_sealPrompt);

        _sealCodeRow = new VBoxContainer();
        _sealCodeRow.AddThemeConstantOverride("separation", 4);
        stageBox.AddChild(_sealCodeRow);
        _sealCodeRow.AddChild(UiTheme.Text(
            "Enter your secret answer", 11, UiTheme.TextDim, HorizontalAlignment.Center));

        _sealCodeField = new LineEdit
        {
            Editable = false,
            Alignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _sealCodeField.AddThemeFontSizeOverride("font_size", 15);
        _sealCodeField.GuiInput += OnSealCodeFieldInput;
        _sealCodeRow.AddChild(_sealCodeField);

        BuildSealKeypad();
        _sealCodeRow.AddChild(_sealPad);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 10);
        root.AddChild(buttons);
        _sealConfirm = UiTheme.SmallButton("Confirm", "");
        _sealConfirm.CustomMinimumSize = new Vector2(96, 28);
        _sealConfirm.Pressed += AskSealConfirm;
        buttons.AddChild(_sealConfirm);
        var cancel = UiTheme.SmallButton("Cancel", "");
        cancel.CustomMinimumSize = new Vector2(96, 28);
        cancel.Pressed += CloseSealWindow;
        buttons.AddChild(cancel);

        var bagSection = UiTheme.Section();
        root.AddChild(bagSection);
        var bagBox = new VBoxContainer();
        bagBox.AddThemeConstantOverride("separation", 6);
        bagSection.AddChild(bagBox);

        var grid = new GridContainer { Columns = 7 };
        grid.AddThemeConstantOverride("h_separation", 3);
        grid.AddThemeConstantOverride("v_separation", 3);
        bagBox.AddChild(grid);
        for (int i = 0; i < SealBagCells; i++)
        {
            int index = i;
            var cell = new WarehouseCell(GridStart + i, bag: true);
            cell.OnActivate += _ => TakeSealSocket(index);
            cell.OnHover += HoverWarehouseCell;
            cell.OnHoverEnd += HideItemTooltip;
            _sealBagCells[i] = cell;
            grid.AddChild(cell);
        }

        var footer = new HBoxContainer();
        bagBox.AddChild(footer);
        var goldLabel = UiTheme.Text("Coins", 12, UiTheme.TextLo);
        goldLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(goldLabel);
        _sealGold = UiTheme.Text("", 12, UiTheme.Gold, HorizontalAlignment.Right);
        footer.AddChild(_sealGold);

        BuildSealAskPanel();
    }

    private void BuildSealKeypad()
    {
        _sealPad = new PanelContainer { Visible = false };
        _sealPad.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 8);
        _sealPad.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        margin.AddChild(box);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(grid);

        foreach (int digit in SealKeypadOrder())
            grid.AddChild(SealKey(digit.ToString(), () => PushSealDigit(digit)));

        grid.AddChild(SealKey("⌫", () =>
        {
            if (_sealCode.Length > 0) _sealCode = _sealCode[..^1];
            RefreshSealWindow();
        }));
        grid.AddChild(SealKey("C", () => { _sealCode = ""; RefreshSealWindow(); }));

        var done = UiTheme.SmallButton("Done", "");
        done.CustomMinimumSize = new Vector2(0, 30);
        done.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        done.Pressed += () => _sealPad.Visible = false;
        box.AddChild(done);
    }

    private static Button SealKey(string label, System.Action onPressed)
    {
        var key = UiTheme.SmallButton(label, "");
        key.CustomMinimumSize = new Vector2(52, 32);
        key.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        key.Pressed += () => onPressed();
        return key;
    }

    private static int[] SealKeypadOrder()
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

    private void PushSealDigit(int digit)
    {
        if (_sealCode.Length >= SealCodeLength) return;
        _sealCode += (char)('0' + digit);
        RefreshSealWindow();
        if (_sealCode.Length == SealCodeLength) _sealPad.Visible = false;
    }

    private void OnSealCodeFieldInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            return;

        _sealPad.Visible = !_sealPad.Visible;
        _sealCodeField.AcceptEvent();
    }

    private void BuildSealAskPanel()
    {
        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        centre.MouseFilter = Control.MouseFilterEnum.Ignore;
        _sealLayer.AddChild(centre);

        _sealAskPanel = new PanelContainer { Visible = false };
        _sealAskPanel.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        centre.AddChild(_sealAskPanel);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 16, 13, 16, 13);
        _sealAskPanel.AddChild(margin);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(292, 0) };
        box.AddThemeConstantOverride("separation", 12);
        margin.AddChild(box);

        _sealAskText = UiTheme.Text("", 12, UiTheme.TextHi);
        _sealAskText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _sealAskText.CustomMinimumSize = new Vector2(292, 0);
        box.AddChild(_sealAskText);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 10);
        box.AddChild(buttons);
        var yes = UiTheme.SmallButton("Confirm", "");
        yes.CustomMinimumSize = new Vector2(96, 28);
        yes.Pressed += SendSeal;
        buttons.AddChild(yes);
        var no = UiTheme.SmallButton("Cancel", "");
        no.CustomMinimumSize = new Vector2(96, 28);
        no.Pressed += () => _sealAskPanel.Visible = false;
        buttons.AddChild(no);
    }
}
