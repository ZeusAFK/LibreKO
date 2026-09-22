using System.Collections.Generic;
using Godot;
using LibreKO.Network;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _shoppingmallLayer = null!;
    private HudWindow _shoppingmallPanel = null!;
    private bool _shoppingmallShown;

    private Label? _shoppingmallStoreStatus;

    private Button _shoppingmallInboxTab = null!;
    private Button _shoppingmallHistoryTab = null!;
    private VBoxContainer _shoppingmallList = null!;
    private bool _shoppingmallHistoryView;
    private readonly List<ShoppingMallLetter> _shoppingmallLetters = new();
    private int _shoppingmallUnread;

    // Top-Right HUD button
    private CanvasLayer _mailboxTopRightLayer = null!;
    private Button _mailboxTopRightButton = null!;
    private TextureRect _mailboxTopRightIcon = null!;
    private PanelContainer _mailboxTopRightBadge = null!;
    private Label _mailboxTopRightCount = null!;
    private Tween? _mailboxTopRightBlink;
    private const float MailboxIconSize = 20f;
    private const float MailboxPadX = 9f;
    private const float MailboxGap = 6f;

    // Reader elements
    private Label _shoppingmallReadTitle = null!;
    private Label _shoppingmallReadSender = null!;
    private Label _shoppingmallReadBody = null!;
    private HBoxContainer _mailboxReadAttachBox = null!;
    private MailboxItemSocket _mailboxReadSocket = null!;
    private Label _mailboxReadItemName = null!;
    private Label _mailboxReadCoins = null!;
    private Button _mailboxReadClaimBtn = null!;
    private Button _mailboxReadDeleteBtn = null!;
    private ShoppingMallLetter? _selectedLetter;

    // Compose elements
    private LineEdit _shoppingmallToEdit = null!;
    private LineEdit _shoppingmallSubjectEdit = null!;
    private TextEdit _shoppingmallMsgEdit = null!;
    private MailboxItemSocket _mailboxComposeSocket = null!;
    private Label _mailboxAttachNameLabel = null!;
    private Label _mailboxAttachFeeLabel = null!;
    private Button _mailboxClearAttachBtn = null!;
    private HBoxContainer _mailboxQtyRow = null!;
    private SpinBox _mailboxQtySpin = null!;
    private Label _mailboxQtyTotal = null!;
    private Label _shoppingmallStatus = null!;
    private int _attachedInvSlot = -1;
    private int _attachedCount = 1;

    private void ShoppingMallInit()
    {
        BuildTopRightMailbox();
        BuildShoppingMallPanel();
        Net.I.ShoppingMallOpenEvent += OnShoppingMallOpen;
        Net.I.ShoppingMallUnreadEvent += OnShoppingMallUnread;
        Net.I.ShoppingMallLetterListEvent += OnShoppingMallLetterList;
        Net.I.ShoppingMallLetterReadEvent += OnShoppingMallLetterRead;
        Net.I.ShoppingMallGiftResultEvent += OnShoppingMallGiftResult;
        Net.I.ShoppingMallSendResultEvent += OnShoppingMallSendResult;
        Net.I.ShoppingMallDeleteEvent += OnShoppingMallDelete;
        Net.I.SendShoppingMallUnread();
    }

    private void ShoppingMallDispose()
    {
        Net.I.ShoppingMallOpenEvent -= OnShoppingMallOpen;
        Net.I.ShoppingMallUnreadEvent -= OnShoppingMallUnread;
        Net.I.ShoppingMallLetterListEvent -= OnShoppingMallLetterList;
        Net.I.ShoppingMallLetterReadEvent -= OnShoppingMallLetterRead;
        Net.I.ShoppingMallGiftResultEvent -= OnShoppingMallGiftResult;
        Net.I.ShoppingMallSendResultEvent -= OnShoppingMallSendResult;
        Net.I.ShoppingMallDeleteEvent -= OnShoppingMallDelete;
    }

    private void BuildShoppingMallPanel()
    {
        _shoppingmallLayer = new CanvasLayer { Layer = 73 };
        AddChild(_shoppingmallLayer);

        _shoppingmallPanel = new HudWindow("shoppingmall", "Mailbox", new Vector2(220, 120)) { Visible = false };
        _shoppingmallPanel.Closed += CloseShoppingMall;
        _shoppingmallLayer.AddChild(_shoppingmallPanel);

        var root = _shoppingmallPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.SectionTitle("Letters"));

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 6);
        _shoppingmallInboxTab = MakeTab("Inbox", () => SwitchLetterView(false));
        _shoppingmallHistoryTab = MakeTab("History", () => SwitchLetterView(true));
        tabs.AddChild(_shoppingmallInboxTab);
        tabs.AddChild(_shoppingmallHistoryTab);
        var refresh = new Button { Text = "Refresh", FocusMode = Control.FocusModeEnum.None };
        refresh.AddThemeFontSizeOverride("font_size", 12);
        refresh.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        refresh.Pressed += RequestLetterList;
        tabs.AddChild(refresh);
        root.AddChild(tabs);

        var listScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(420, 220),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        root.AddChild(listScroll);
        _shoppingmallList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _shoppingmallList.AddThemeConstantOverride("separation", 3);
        listScroll.AddChild(_shoppingmallList);

        var readPanel = UiTheme.Section();
        var readBox = new VBoxContainer();
        readBox.AddThemeConstantOverride("separation", 4);
        readPanel.AddChild(readBox);

        _shoppingmallReadTitle = UiTheme.Text("Select a letter to read.", 13, UiTheme.GoldBright);
        readBox.AddChild(_shoppingmallReadTitle);

        _shoppingmallReadSender = UiTheme.Text("", 11, UiTheme.TextLo);
        readBox.AddChild(_shoppingmallReadSender);

        _shoppingmallReadBody = UiTheme.Text("", 12, UiTheme.TextHi);
        _shoppingmallReadBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _shoppingmallReadBody.CustomMinimumSize = new Vector2(420, 36);
        readBox.AddChild(_shoppingmallReadBody);

        _mailboxReadAttachBox = new HBoxContainer();
        _mailboxReadAttachBox.AddThemeConstantOverride("separation", 10);
        _mailboxReadAttachBox.Visible = false;

        _mailboxReadSocket = new MailboxItemSocket("Item", 44f, canDrop: false);
        _mailboxReadSocket.Hovered += () => { if (_mailboxReadSocket.HasItem) ShowItemTooltip(-1, _mailboxReadSocket.Item); };
        _mailboxReadSocket.Unhovered += HideItemTooltip;
        _mailboxReadAttachBox.AddChild(_mailboxReadSocket);

        var readAttachDetails = new VBoxContainer();
        readAttachDetails.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        readAttachDetails.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        readAttachDetails.AddThemeConstantOverride("separation", 2);

        _mailboxReadItemName = UiTheme.Text("", 12, UiTheme.GoldBright);
        readAttachDetails.AddChild(_mailboxReadItemName);

        _mailboxReadCoins = UiTheme.Text("", 11, UiTheme.Gold);
        readAttachDetails.AddChild(_mailboxReadCoins);

        var readBtnRow = new HBoxContainer();
        readBtnRow.AddThemeConstantOverride("separation", 8);

        _mailboxReadClaimBtn = new Button { Text = "Claim Attached Item", FocusMode = Control.FocusModeEnum.None };
        _mailboxReadClaimBtn.AddThemeFontSizeOverride("font_size", 12);
        _mailboxReadClaimBtn.Pressed += ClaimCurrentLetterGift;
        readBtnRow.AddChild(_mailboxReadClaimBtn);

        _mailboxReadDeleteBtn = new Button { Text = "Delete Letter", FocusMode = Control.FocusModeEnum.None };
        _mailboxReadDeleteBtn.AddThemeFontSizeOverride("font_size", 12);
        _mailboxReadDeleteBtn.Pressed += DeleteCurrentLetter;
        readBtnRow.AddChild(_mailboxReadDeleteBtn);

        readAttachDetails.AddChild(readBtnRow);
        _mailboxReadAttachBox.AddChild(readAttachDetails);
        readBox.AddChild(_mailboxReadAttachBox);

        root.AddChild(readPanel);

        root.AddChild(new HSeparator());

        root.AddChild(UiTheme.SectionTitle("Send a Letter"));
        _shoppingmallToEdit = MakeField(root, "To", 16);
        _shoppingmallSubjectEdit = MakeField(root, "Subject", 31);

        var msgLbl = UiTheme.Text("Message", 12, UiTheme.TextLo);
        root.AddChild(msgLbl);
        _shoppingmallMsgEdit = new TextEdit
        {
            CustomMinimumSize = new Vector2(420, 52),
            PlaceholderText = "Write your message…",
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        root.AddChild(_shoppingmallMsgEdit);

        // Attachment Section with Drag-and-Drop Slot
        var attachBox = new HBoxContainer();
        attachBox.AddThemeConstantOverride("separation", 12);
        attachBox.Alignment = BoxContainer.AlignmentMode.Begin;

        _mailboxComposeSocket = new MailboxItemSocket("Drag\nItem", 44f, canDrop: true);
        _mailboxComposeSocket.DroppedInventorySlot += AttachItemFromInventory;
        _mailboxComposeSocket.Cleared += () => SetAttachedSlot(-1);
        _mailboxComposeSocket.Hovered += () =>
        {
            if (_attachedInvSlot >= 0 && _attachedInvSlot < Inv.Length && !Inv[_attachedInvSlot].IsEmpty)
                ShowItemTooltip(_attachedInvSlot, Inv[_attachedInvSlot]);
        };
        _mailboxComposeSocket.Unhovered += HideItemTooltip;
        attachBox.AddChild(_mailboxComposeSocket);

        var attachMeta = new VBoxContainer();
        attachMeta.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        attachMeta.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        attachMeta.AddThemeConstantOverride("separation", 4);

        var attachTitleRow = new HBoxContainer();
        attachTitleRow.AddThemeConstantOverride("separation", 8);
        _mailboxAttachNameLabel = UiTheme.Text("Drop item from inventory here to send with letter", 12, UiTheme.TextLo);
        _mailboxAttachNameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        attachTitleRow.AddChild(_mailboxAttachNameLabel);

        _mailboxClearAttachBtn = new Button { Text = "Clear [×]", FocusMode = Control.FocusModeEnum.None, Visible = false };
        _mailboxClearAttachBtn.AddThemeFontSizeOverride("font_size", 11);
        _mailboxClearAttachBtn.Pressed += () => SetAttachedSlot(-1);
        attachTitleRow.AddChild(_mailboxClearAttachBtn);
        attachMeta.AddChild(attachTitleRow);

        // Quantity controls for stackable items
        _mailboxQtyRow = new HBoxContainer();
        _mailboxQtyRow.AddThemeConstantOverride("separation", 6);
        _mailboxQtyRow.Visible = false;

        var qtyLbl = UiTheme.Text("Quantity to send:", 11, UiTheme.TextLo);
        _mailboxQtyRow.AddChild(qtyLbl);

        var decBtn = new Button { Text = "-", CustomMinimumSize = new Vector2(24, 20), FocusMode = Control.FocusModeEnum.None };
        decBtn.AddThemeFontSizeOverride("font_size", 11);
        decBtn.Pressed += () => ChangeAttachQuantity(-1);
        _mailboxQtyRow.AddChild(decBtn);

        _mailboxQtySpin = UiTheme.NumberBox(1, 9999, 1, 65);
        _mailboxQtySpin.ValueChanged += val => OnAttachQuantityChanged((int)val);
        _mailboxQtyRow.AddChild(_mailboxQtySpin);

        var incBtn = new Button { Text = "+", CustomMinimumSize = new Vector2(24, 20), FocusMode = Control.FocusModeEnum.None };
        incBtn.AddThemeFontSizeOverride("font_size", 11);
        incBtn.Pressed += () => ChangeAttachQuantity(1);
        _mailboxQtyRow.AddChild(incBtn);

        var maxBtn = new Button { Text = "Max", CustomMinimumSize = new Vector2(36, 20), FocusMode = Control.FocusModeEnum.None };
        maxBtn.AddThemeFontSizeOverride("font_size", 11);
        maxBtn.Pressed += SetAttachQuantityMax;
        _mailboxQtyRow.AddChild(maxBtn);

        _mailboxQtyTotal = UiTheme.Text("/ 1", 11, UiTheme.TextDim);
        _mailboxQtyRow.AddChild(_mailboxQtyTotal);

        attachMeta.AddChild(_mailboxQtyRow);

        _mailboxAttachFeeLabel = UiTheme.Text("Sending fee: 1,000 Noahs (10,000 Noahs with item)", 11, UiTheme.TextDim);
        attachMeta.AddChild(_mailboxAttachFeeLabel);

        attachBox.AddChild(attachMeta);
        root.AddChild(attachBox);

        var sendRow = new HBoxContainer();
        sendRow.AddThemeConstantOverride("separation", 8);
        _shoppingmallStatus = HudStyle.Label(12);
        _shoppingmallStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sendRow.AddChild(_shoppingmallStatus);
        var sendBtn = new Button { Text = "Send Letter", FocusMode = Control.FocusModeEnum.None };
        sendBtn.AddThemeFontSizeOverride("font_size", 12);
        sendBtn.Pressed += SendComposedLetter;
        sendRow.AddChild(sendBtn);
        root.AddChild(sendRow);
    }

    private Button MakeTab(string text, System.Action onPressed)
    {
        var b = new Button { Text = text, ToggleMode = true, FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 12);
        b.Pressed += () => onPressed();
        return b;
    }

    private LineEdit MakeField(VBoxContainer parent, string label, int maxLen)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var lbl = UiTheme.Text(label, 12, UiTheme.TextLo);
        lbl.CustomMinimumSize = new Vector2(60, 0);
        row.AddChild(lbl);
        var edit = new LineEdit { MaxLength = maxLen };
        edit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(edit);
        parent.AddChild(row);
        return edit;
    }

    public void ToggleShoppingMall()
    {
        if (_shoppingmallShown) CloseShoppingMall();
        else OpenShoppingMall();
    }

    public void OpenShoppingMall()
    {
        if (_shoppingmallShown) return;
        _shoppingmallShown = true;
        _shoppingmallPanel.Visible = true;

        SwitchLetterView(_shoppingmallHistoryView);
        RequestLetterList();
        Net.I.SendShoppingMallUnread();
    }

    public void CloseShoppingMall()
    {
        if (!_shoppingmallShown) return;
        _shoppingmallShown = false;
        _shoppingmallPanel.Visible = false;
        Net.I.SendShoppingMallClose();
    }

    private void SwitchLetterView(bool history)
    {
        _shoppingmallHistoryView = history;
        _shoppingmallInboxTab.ButtonPressed = !history;
        _shoppingmallHistoryTab.ButtonPressed = history;
        if (_shoppingmallShown) RequestLetterList();
    }

    private void RequestLetterList()
    {
        if (_shoppingmallHistoryView) Net.I.SendShoppingMallLetterHistory();
        else Net.I.SendShoppingMallLetterList();
    }

    private void OnShoppingMallLetterList(List<ShoppingMallLetter> letters, bool history)
    {
        if (!_shoppingmallShown) return;
        if (history != _shoppingmallHistoryView) return;
        _shoppingmallLetters.Clear();
        _shoppingmallLetters.AddRange(letters);
        RebuildLetterList();
    }

    private void RebuildLetterList()
    {
        foreach (var c in _shoppingmallList.GetChildren()) c.QueueFree();
        if (_shoppingmallLetters.Count == 0)
        {
            var empty = HudStyle.Label(13);
            empty.Text = _shoppingmallHistoryView ? "No past letters." : "Your mailbox is empty.";
            _shoppingmallList.AddChild(empty);
            return;
        }
        foreach (var letter in _shoppingmallLetters)
            _shoppingmallList.AddChild(BuildLetterRow(letter));
    }

    private Control BuildLetterRow(ShoppingMallLetter letter)
    {
        var row = UiTheme.RowPanel();
        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 8);
        row.AddChild(hb);

        if (letter.HasGift && letter.ItemId != 0)
        {
            hb.AddChild(new TextureRect
            {
                Texture = ItemData.Icon(letter.ItemId),
                CustomMinimumSize = new Vector2(30, 30),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        else
        {
            var glyph = UiTheme.Text(letter.HasGift ? "$" : "✉", 18, UiTheme.Gold, HorizontalAlignment.Center);
            glyph.CustomMinimumSize = new Vector2(30, 30);
            hb.AddChild(glyph);
        }

        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", -2);
        var subject = UiTheme.Text("", 13, letter.Status == 1 ? UiTheme.TextHi : UiTheme.TextLo);
        subject.Text = string.IsNullOrEmpty(letter.Subject) ? "(no subject)" : letter.Subject;
        info.AddChild(subject);
        string gift = letter.HasGift
            ? (letter.ItemId != 0
                ? $"  ·  {ItemData.DisplayName(letter.ItemId)}" + (letter.Count > 1 ? $" x{letter.Count}" : "")
                : "") + (letter.Coins > 0 ? $"  ·  {letter.Coins:n0} gold" : "")
            : "";
        var meta = UiTheme.Text($"from {letter.Sender}{gift}   ({letter.DaysLeft}d left)", 11, UiTheme.TextDim);
        info.AddChild(meta);
        hb.AddChild(info);

        int id = letter.LetterId;
        var readBtn = new Button { Text = "Read", FocusMode = Control.FocusModeEnum.None };
        readBtn.AddThemeFontSizeOverride("font_size", 11);
        readBtn.Pressed += () =>
        {
            SelectLetter(letter);
            Net.I.SendShoppingMallReadLetter(id);
        };
        hb.AddChild(readBtn);

        if (letter.HasGift)
        {
            var getBtn = new Button { Text = "Get", FocusMode = Control.FocusModeEnum.None };
            getBtn.AddThemeFontSizeOverride("font_size", 11);
            getBtn.Pressed += () => Net.I.SendShoppingMallGetGift(id);
            hb.AddChild(getBtn);
        }

        var delBtn = new Button { Text = "×", FocusMode = Control.FocusModeEnum.None, TooltipText = "Delete" };
        delBtn.AddThemeFontSizeOverride("font_size", 13);
        delBtn.Pressed += () => Net.I.SendShoppingMallDelete(new[] { id });
        hb.AddChild(delBtn);

        row.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                SelectLetter(letter);
                Net.I.SendShoppingMallReadLetter(id);
            }
        };

        return row;
    }

    private void SelectLetter(ShoppingMallLetter letter, string? message = null)
    {
        _selectedLetter = letter;
        _shoppingmallReadTitle.Text = string.IsNullOrEmpty(letter.Subject) ? "(no subject)" : letter.Subject;
        _shoppingmallReadSender.Text = $"From: {letter.Sender}   ({letter.DaysLeft} days remaining)";
        if (message != null) _shoppingmallReadBody.Text = message;

        if (letter.HasGift && letter.ItemId != 0)
        {
            _mailboxReadAttachBox.Visible = true;
            _mailboxReadSocket.Set(new ItemSlot { ItemId = letter.ItemId, Count = (short)letter.Count, Durability = 100 });
            _mailboxReadSocket.Visible = true;
            _mailboxReadItemName.Text = ItemData.DisplayName(letter.ItemId) + (letter.Count > 1 ? $" x{letter.Count}" : "");
            _mailboxReadCoins.Text = letter.Coins > 0 ? $"Noahs: {letter.Coins:n0}" : "";
            _mailboxReadClaimBtn.Visible = true;
            _mailboxReadClaimBtn.Text = letter.Coins > 0 ? $"Claim Item & {letter.Coins:n0} Noahs" : "Claim Attached Item";
        }
        else if (letter.Coins > 0)
        {
            _mailboxReadAttachBox.Visible = true;
            _mailboxReadSocket.Clear();
            _mailboxReadSocket.Visible = false;
            _mailboxReadItemName.Text = "";
            _mailboxReadCoins.Text = $"Noahs: {letter.Coins:n0}";
            _mailboxReadClaimBtn.Visible = true;
            _mailboxReadClaimBtn.Text = $"Claim {letter.Coins:n0} Noahs";
        }
        else
        {
            _mailboxReadAttachBox.Visible = false;
            _mailboxReadSocket.Clear();
            _mailboxReadClaimBtn.Visible = false;
        }

        _mailboxReadDeleteBtn.Visible = true;
    }

    private void ClaimCurrentLetterGift()
    {
        if (_selectedLetter is { HasGift: true } sel)
        {
            Net.I.SendShoppingMallGetGift(sel.LetterId);
        }
    }

    private void DeleteCurrentLetter()
    {
        if (_selectedLetter is { } sel)
        {
            Net.I.SendShoppingMallDelete(new[] { sel.LetterId });
            _selectedLetter = null;
            _shoppingmallReadTitle.Text = "Select a letter to read.";
            _shoppingmallReadSender.Text = "";
            _shoppingmallReadBody.Text = "";
            _mailboxReadAttachBox.Visible = false;
            _mailboxReadDeleteBtn.Visible = false;
        }
    }

    private void OnShoppingMallLetterRead(bool ok, int letterId, string message)
    {
        if (!ok)
        {
            _shoppingmallReadTitle.Text = "That letter is no longer available.";
            _shoppingmallReadBody.Text = "";
            _mailboxReadAttachBox.Visible = false;
            return;
        }
        ShoppingMallLetter? target = null;
        foreach (var l in _shoppingmallLetters)
        {
            if (l.LetterId == letterId) { target = l; break; }
        }
        if (target is { } found)
        {
            SelectLetter(found, message);
        }
        else
        {
            _shoppingmallReadTitle.Text = "Letter";
            _shoppingmallReadBody.Text = message;
            _mailboxReadAttachBox.Visible = false;
        }
    }

    private void OnShoppingMallGiftResult(bool ok, int letterId, int code)
    {
        if (ok)
        {
            Chat.Info("Gift claimed from your mailbox.");
            if (_selectedLetter is { } sel && sel.LetterId == letterId)
            {
                _mailboxReadClaimBtn.Visible = false;
                _mailboxReadCoins.Text = "Attachment claimed.";
            }
            RequestLetterList();
            Net.I.SendShoppingMallUnread();
        }
        else
        {
            SetShoppingMallStatus(code switch
            {
                -2 => "That gift was already claimed.",
                _  => "Couldn't claim the gift (bags full or too heavy).",
            }, true);
        }
    }

    private void OnShoppingMallDelete(List<int> deletedIds, bool overflow)
    {
        if (overflow)
        {
            SetShoppingMallStatus("Delete up to 5 letters at a time.", true);
            return;
        }
        if (deletedIds.Count > 0)
        {
            RequestLetterList();
            Net.I.SendShoppingMallUnread();
        }
    }

    private void OnShoppingMallUnread(int count)
    {
        _shoppingmallUnread = count;
        _shoppingmallInboxTab.Text = count > 0 ? $"Inbox ({count})" : "Inbox";
        UpdateTopRightMailboxBadge(count);
    }

    private void OnShoppingMallOpen(short error, short freeSlot)
    {
        if (_shoppingmallStoreStatus == null) return;
        if (error == 1)
        {
            _shoppingmallStoreStatus.Text = "Store open — browse cash items on the website.";
            _shoppingmallStoreStatus.AddThemeColorOverride("font_color", UiTheme.Good);
        }
        else
        {
            string reason = error switch
            {
                -2 => "You can't shop while dead.",
                -3 => "Close your trade first.",
                -4 => "Close your stall first.",
                -5 => "The store is closed in this zone.",
                -8 => "Make a free inventory slot first.",
                _  => "The store couldn't open.",
            };
            _shoppingmallStoreStatus.Text = reason;
            _shoppingmallStoreStatus.AddThemeColorOverride("font_color", UiTheme.Bad);
        }
    }

    private void AttachItemFromInventory(int absSlot)
    {
        if (absSlot < GridStart || absSlot >= GridStart + GridCount || absSlot >= Inv.Length || Inv[absSlot].IsEmpty)
        {
            SetShoppingMallStatus("Invalid inventory item.", true);
            return;
        }
        SetAttachedSlot(absSlot);
    }

    private void SetAttachedSlot(int absSlot, int count = -1)
    {
        _attachedInvSlot = absSlot;
        if (absSlot >= 0 && absSlot < Inv.Length && !Inv[absSlot].IsEmpty)
        {
            var item = Inv[absSlot];
            int max = item.Count;
            _attachedCount = count > 0 ? Mathf.Clamp(count, 1, max) : max;

            _mailboxComposeSocket.Set(item, _attachedCount);
            _mailboxAttachFeeLabel.Text = $"Sending fee: {Net.ShoppingMallGiftCost:n0} Noahs";
            _mailboxClearAttachBtn.Visible = true;

            if (max > 1)
            {
                _mailboxQtyRow.Visible = true;
                _mailboxQtySpin.MaxValue = max;
                _mailboxQtySpin.Value = _attachedCount;
                _mailboxQtyTotal.Text = $"/ {max:n0}";
                _mailboxAttachNameLabel.Text = $"{ItemData.DisplayName(item.ItemId)} x{_attachedCount:n0} (in bag: {max:n0})";
            }
            else
            {
                _mailboxQtyRow.Visible = false;
                _mailboxAttachNameLabel.Text = ItemData.DisplayName(item.ItemId);
            }
        }
        else
        {
            _attachedInvSlot = -1;
            _attachedCount = 1;
            _mailboxComposeSocket.Clear();
            _mailboxAttachNameLabel.Text = "Drop item from inventory here to send with letter";
            _mailboxAttachFeeLabel.Text = $"Sending fee: {Net.ShoppingMallLetterCost:n0} Noahs (10,000 Noahs with item)";
            _mailboxClearAttachBtn.Visible = false;
            _mailboxQtyRow.Visible = false;
            HideItemTooltip();
        }
    }

    private void OnAttachQuantityChanged(int val)
    {
        if (_attachedInvSlot < 0 || _attachedInvSlot >= Inv.Length || Inv[_attachedInvSlot].IsEmpty) return;
        int max = Inv[_attachedInvSlot].Count;
        _attachedCount = Mathf.Clamp(val, 1, max);
        _mailboxComposeSocket.SetCount(_attachedCount);
        string name = ItemData.DisplayName(Inv[_attachedInvSlot].ItemId);
        _mailboxAttachNameLabel.Text = $"{name} x{_attachedCount:n0} (in bag: {max:n0})";
    }

    private void ChangeAttachQuantity(int delta)
    {
        if (_mailboxQtySpin == null) return;
        _mailboxQtySpin.Value += delta;
    }

    private void SetAttachQuantityMax()
    {
        if (_mailboxQtySpin == null || _attachedInvSlot < 0 || _attachedInvSlot >= Inv.Length || Inv[_attachedInvSlot].IsEmpty) return;
        _mailboxQtySpin.Value = Inv[_attachedInvSlot].Count;
    }

    private void SendComposedLetter()
    {
        string to = _shoppingmallToEdit.Text.Trim();
        string subject = _shoppingmallSubjectEdit.Text.Trim();
        string message = _shoppingmallMsgEdit.Text.Trim();

        if (to.Length == 0) { SetShoppingMallStatus("Enter a recipient.", true); return; }
        if (subject.Length == 0) { SetShoppingMallStatus("Enter a subject.", true); return; }
        if (message.Length == 0) { SetShoppingMallStatus("Write a message.", true); return; }

        int absSlot = _attachedInvSlot;
        if (absSlot >= 0 && absSlot < Inv.Length && !Inv[absSlot].IsEmpty)
        {
            int cost = Net.ShoppingMallGiftCost;
            if (Sheet.Gold < cost) { SetShoppingMallStatus($"Sending an item costs {cost:n0} gold.", true); return; }
            byte srcPos = (byte)(absSlot - GridStart);
            Net.I.SendShoppingMallGiftLetter(to, subject, message, Inv[absSlot].ItemId, srcPos, _attachedCount);
        }
        else
        {
            int cost = Net.ShoppingMallLetterCost;
            if (Sheet.Gold < cost) { SetShoppingMallStatus($"Sending a letter costs {cost:n0} gold.", true); return; }
            Net.I.SendShoppingMallTextLetter(to, subject, message);
        }
        SetShoppingMallStatus("Sending…", false);
    }

    private void OnShoppingMallSendResult(bool ok, int code)
    {
        if (ok)
        {
            SetShoppingMallStatus("Letter sent.", false);
            Chat.Info("Your letter was delivered.");
            _shoppingmallToEdit.Text = "";
            _shoppingmallSubjectEdit.Text = "";
            _shoppingmallMsgEdit.Text = "";
            SetAttachedSlot(-1);
            return;
        }
        SetShoppingMallStatus(code switch
        {
            -6  => "You can't mail yourself.",
            -32 => "That item can't be mailed.",
            _   => "Couldn't send (check the name / your gold).",
        }, true);
    }

    private void SetShoppingMallStatus(string text, bool warn)
    {
        _shoppingmallStatus.Text = text;
        _shoppingmallStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.Good);
    }

    private void BuildTopRightMailbox()
    {
        _mailboxTopRightLayer = new CanvasLayer { Layer = 66 };
        AddChild(_mailboxTopRightLayer);

        _mailboxTopRightButton = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Mailbox (F11)",
        };
        var flat = new StyleBoxEmpty();
        _mailboxTopRightButton.AddThemeStyleboxOverride("normal", flat);
        _mailboxTopRightButton.AddThemeStyleboxOverride("hover", flat);
        _mailboxTopRightButton.AddThemeStyleboxOverride("pressed", flat);
        _mailboxTopRightButton.Pressed += ToggleShoppingMall;
        _mailboxTopRightButton.MouseEntered += () => _mailboxTopRightIcon.SelfModulate = UiTheme.GoldBright;
        _mailboxTopRightButton.MouseExited += () => _mailboxTopRightIcon.SelfModulate = _shoppingmallUnread > 0 ? UiTheme.GoldBright : UiTheme.Gold;
        _mailboxTopRightLayer.AddChild(_mailboxTopRightButton);

        _mailboxTopRightIcon = UiIcons.Image("system/mail",
            new Vector2(MailboxIconSize, MailboxIconSize), UiTheme.Gold);
        _mailboxTopRightIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
        _mailboxTopRightIcon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _mailboxTopRightButton.AddChild(_mailboxTopRightIcon);

        var badge = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        badge.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        badge.AddChild(new BadgeDisc { MouseFilter = Control.MouseFilterEnum.Ignore });
        badge.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        badge.OffsetLeft = -13f;
        badge.OffsetTop = -13f;
        badge.OffsetRight = 2;
        badge.OffsetBottom = 2;
        _mailboxTopRightButton.AddChild(badge);

        _mailboxTopRightCount = UiTheme.Text("",
            Platform.TouchUi ? 17 : 9,
            UiTheme.Self, HorizontalAlignment.Center);
        _mailboxTopRightCount.VerticalAlignment = VerticalAlignment.Center;
        _mailboxTopRightCount.MouseFilter = Control.MouseFilterEnum.Ignore;
        badge.AddChild(_mailboxTopRightCount);
        _mailboxTopRightBadge = badge;
        _mailboxTopRightBadge.Visible = false;

        if (_premiumChip != null) _premiumChip.Resized += PlaceTopRightMailbox;
        if (_attendanceGift != null) _attendanceGift.Resized += PlaceTopRightMailbox;
        if (_trophy != null) _trophy.Resized += PlaceTopRightMailbox;
        _mailboxTopRightButton.Resized += PlaceTopRightMailbox;
        Callable.From(PlaceTopRightMailbox).CallDeferred();
    }

    private void PlaceTopRightMailbox()
    {
        if (_mailboxTopRightButton == null || !GodotObject.IsInstanceValid(_mailboxTopRightButton)) return;
        if (Platform.TouchUi) return;
        if (_premiumChip == null) return;

        float giftWidth = _attendanceGift?.Size.X ?? 0f;
        float trophyWidth = _trophy?.Size.X ?? 0f;
        _mailboxTopRightButton.CustomMinimumSize = new Vector2(
            MailboxIconSize + MailboxPadX * 2f,
            Mathf.Max(_premiumChip.Size.Y, MailboxIconSize));

        float offsetFromRight = HudAnchor.Edge + MiniMap.SquareSize + StatusHudGap
            + _premiumChip.Size.X + AttendanceGiftGap + giftWidth + TrophyGap + trophyWidth + MailboxGap;

        HudAnchor.Pin(_mailboxTopRightButton, HudAnchor.Spot.TopRight, new Vector2(
            offsetFromRight,
            HudAnchor.Edge));
    }

    private void UpdateTopRightMailboxBadge(int count)
    {
        if (_mailboxTopRightButton == null || !GodotObject.IsInstanceValid(_mailboxTopRightButton)) return;

        bool waiting = count > 0;
        _mailboxTopRightBadge.Visible = waiting;
        _mailboxTopRightCount.Text = count > 9 ? "9+" : count.ToString();
        _mailboxTopRightIcon.SelfModulate = waiting ? UiTheme.GoldBright : UiTheme.Gold;
        _mailboxTopRightButton.TooltipText = waiting
            ? $"Mailbox — {count} new letter{(count == 1 ? "" : "s")} (F11)"
            : "Mailbox (F11)";

        if (_mailboxTopRightBlink != null && _mailboxTopRightBlink.IsValid())
            _mailboxTopRightBlink.Kill();
        _mailboxTopRightBlink = null;
        _mailboxTopRightButton.Modulate = Colors.White;
        if (!waiting) return;

        var blink = _mailboxTopRightButton.CreateTween().SetLoops();
        blink.TweenProperty(_mailboxTopRightButton, "modulate:a", 0.3f, 0.6f);
        blink.TweenProperty(_mailboxTopRightButton, "modulate:a", 1f, 0.6f);
        _mailboxTopRightBlink = blink;
    }

    private sealed partial class MailboxItemSocket : PanelContainer
    {
        public event System.Action<int>? DroppedInventorySlot;
        public event System.Action? Cleared;
        public event System.Action? Hovered;
        public event System.Action? Unhovered;

        private readonly Label _placeholder;
        private readonly TextureRect _icon;
        private readonly Label _count;
        private readonly UpgradeBadge _plus;
        private readonly bool _canDrop;
        private ItemSlot _item;

        public bool HasItem => !_item.IsEmpty;
        public ItemSlot Item => _item;

        public MailboxItemSocket(string placeholder, float size = 44f, bool canDrop = false)
        {
            _canDrop = canDrop;
            CustomMinimumSize = new Vector2(size, size);
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            SizeFlagsVertical = SizeFlags.ShrinkCenter;
            AddThemeStyleboxOverride("panel", UiTheme.Slot());

            _placeholder = UiTheme.Text(placeholder, 10, UiTheme.TextDim, HorizontalAlignment.Center);
            _placeholder.SetAnchorsPreset(LayoutPreset.FullRect);
            _placeholder.VerticalAlignment = VerticalAlignment.Center;
            _placeholder.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _placeholder.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(_placeholder);

            _icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _icon.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_icon);

            _count = HudStyle.Label(11, HorizontalAlignment.Right);
            _count.SetAnchorsPreset(LayoutPreset.BottomRight);
            _count.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(_count);

            _plus = UpgradeBadge.Attach(this);

            MouseEntered += () => { if (!_item.IsEmpty) Hovered?.Invoke(); };
            MouseExited += () => Unhovered?.Invoke();
        }

        public void Set(ItemSlot item, int overrideCount = -1)
        {
            _item = item;
            if (item.IsEmpty)
            {
                Clear();
                return;
            }
            _icon.Texture = ItemData.Icon(item.ItemId);
            _placeholder.Visible = false;
            int count = overrideCount > 0 ? overrideCount : item.Count;
            _count.Text = count > 1 ? count.ToString() : "";
            _plus.Set(item.ItemId);
            AddThemeStyleboxOverride("panel", UiTheme.Slot(UiTheme.Gold));
        }

        public void SetCount(int count)
        {
            _count.Text = count > 1 ? count.ToString() : "";
        }

        public void Clear()
        {
            _item = default;
            _icon.Texture = null;
            _placeholder.Visible = true;
            _count.Text = "";
            _plus.Clear();
            AddThemeStyleboxOverride("panel", UiTheme.Slot());
            Unhovered?.Invoke();
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (!_canDrop) return false;
            if (data.VariantType != Variant.Type.Dictionary) return false;
            var d = data.AsGodotDictionary();
            return d.ContainsKey("invFrom");
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            if (!_canDrop) return;
            var d = data.AsGodotDictionary();
            if (d.TryGetValue("invFrom", out var v))
            {
                DroppedInventorySlot?.Invoke(v.AsInt32());
            }
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }
                or InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left })
            {
                Cleared?.Invoke();
            }
        }
    }
}
