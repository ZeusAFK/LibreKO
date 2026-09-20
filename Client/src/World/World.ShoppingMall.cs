using System.Collections.Generic;
using System.Text.Json;
using Godot;
using LibreKO.Network;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _shoppingmallLayer = null!;
    private HudWindow _shoppingmallPanel = null!;
    private bool _shoppingmallShown;

    private Label _shoppingmallStoreStatus = null!;

    private Button _shoppingmallInboxTab = null!;
    private Button _shoppingmallHistoryTab = null!;
    private VBoxContainer _shoppingmallList = null!;
    private bool _shoppingmallHistoryView;
    private readonly List<ShoppingMallLetter> _shoppingmallLetters = new();
    private int _shoppingmallUnread;

    private Label _shoppingmallReadTitle = null!;
    private Label _shoppingmallReadBody = null!;

    private LineEdit _shoppingmallToEdit = null!;
    private LineEdit _shoppingmallSubjectEdit = null!;
    private TextEdit _shoppingmallMsgEdit = null!;
    private OptionButton _shoppingmallGiftPick = null!;
    private Label _shoppingmallStatus = null!;

    private readonly List<PusCatalogEntry> _pusCatalog = new();
    private readonly List<PusBasketEntry> _pusBasket = new();
    private byte _pusSelectedCategory = 1;
    private VBoxContainer _pusItemList = null!;
    private VBoxContainer _pusBasketList = null!;
    private Label _pusSummary = null!;
    private Label _pusWallet = null!;
    private Label _pusBasketStatus = null!;
    private Button _pusBuyButton = null!;
    private Button _pusClearButton = null!;

    private sealed class PusCatalogEntry
    {
        public int Id;
        public int ItemId;
        public string Name = "";
        public string Description = "";
        public int Category;
        public int Price;
        public int PriceType;
    }

    private sealed class PusBasketEntry
    {
        public PusCatalogEntry Item = null!;
        public int Count;
    }

    private void ShoppingMallInit()
    {
        BuildShoppingMallPanel();
        Net.I.ShoppingMallOpenEvent += OnShoppingMallOpen;
        Net.I.ShoppingMallUnreadEvent += OnShoppingMallUnread;
        Net.I.ShoppingMallLetterListEvent += OnShoppingMallLetterList;
        Net.I.ShoppingMallLetterReadEvent += OnShoppingMallLetterRead;
        Net.I.ShoppingMallGiftResultEvent += OnShoppingMallGiftResult;
        Net.I.ShoppingMallSendResultEvent += OnShoppingMallSendResult;
        Net.I.ShoppingMallDeleteEvent += OnShoppingMallDelete;
        Net.I.ShoppingMallBuyResultEvent += OnShoppingMallBuyResult;
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
        Net.I.ShoppingMallBuyResultEvent -= OnShoppingMallBuyResult;
    }

    private void BuildShoppingMallPanel()
    {
        _shoppingmallLayer = new CanvasLayer { Layer = 73 };
        AddChild(_shoppingmallLayer);

        _shoppingmallPanel = new HudWindow("shoppingmall", "Power-Up Store", new Vector2(220, 120)) { Visible = false };
        _shoppingmallPanel.Closed += CloseShoppingMall;
        _shoppingmallLayer.AddChild(_shoppingmallPanel);

        var root = _shoppingmallPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.SectionTitle("Cash Shop"));
        var storeRow = new HBoxContainer();
        storeRow.AddThemeConstantOverride("separation", 8);
        _shoppingmallStoreStatus = HudStyle.Label(13);
        _shoppingmallStoreStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _shoppingmallStoreStatus.Text = "Store closed.";
        storeRow.AddChild(_shoppingmallStoreStatus);
        var openBtn = new Button { Text = "Open Store", FocusMode = Control.FocusModeEnum.None };
        openBtn.AddThemeFontSizeOverride("font_size", 12);
        openBtn.Pressed += () => Net.I.SendShoppingMallOpen();
        storeRow.AddChild(openBtn);
        root.AddChild(storeRow);

        root.AddChild(new HSeparator());

        BuildPusSection(root);

        root.AddChild(new HSeparator());

        root.AddChild(UiTheme.SectionTitle("Gift Letters"));

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
        readBox.AddThemeConstantOverride("separation", 2);
        readPanel.AddChild(readBox);
        _shoppingmallReadTitle = UiTheme.Text("Select a letter to read.", 13, UiTheme.GoldBright);
        readBox.AddChild(_shoppingmallReadTitle);
        _shoppingmallReadBody = UiTheme.Text("", 12, UiTheme.TextLo);
        _shoppingmallReadBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _shoppingmallReadBody.CustomMinimumSize = new Vector2(420, 44);
        readBox.AddChild(_shoppingmallReadBody);
        root.AddChild(readPanel);

        root.AddChild(new HSeparator());

        root.AddChild(UiTheme.SectionTitle("Send a Letter"));
        _shoppingmallToEdit = MakeField(root, "To", 16);
        _shoppingmallSubjectEdit = MakeField(root, "Subject", 31);

        var msgLbl = UiTheme.Text("Message", 12, UiTheme.TextLo);
        root.AddChild(msgLbl);
        _shoppingmallMsgEdit = new TextEdit
        {
            CustomMinimumSize = new Vector2(420, 56),
            PlaceholderText = "Write your message…",
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        root.AddChild(_shoppingmallMsgEdit);

        var giftRow = new HBoxContainer();
        giftRow.AddThemeConstantOverride("separation", 8);
        giftRow.AddChild(UiTheme.Text("Attach", 12, UiTheme.TextLo));
        _shoppingmallGiftPick = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _shoppingmallGiftPick.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        giftRow.AddChild(_shoppingmallGiftPick);
        root.AddChild(giftRow);

        var sendRow = new HBoxContainer();
        sendRow.AddThemeConstantOverride("separation", 8);
        _shoppingmallStatus = HudStyle.Label(12);
        _shoppingmallStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sendRow.AddChild(_shoppingmallStatus);
        var sendBtn = new Button { Text = "Send", FocusMode = Control.FocusModeEnum.None };
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

    public void OpenPowerUpStore() => OpenShoppingMall();

    public void OpenShoppingMall()
    {
        if (_shoppingmallShown) return;
        _shoppingmallShown = true;
        _shoppingmallPanel.Visible = true;
        BootstrapPusCatalog();
        RefreshPusView();        SwitchLetterView(_shoppingmallHistoryView);
        RebuildGiftPicker();
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
        readBtn.Pressed += () => Net.I.SendShoppingMallReadLetter(id);
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

        return row;
    }

    private void OnShoppingMallLetterRead(bool ok, int letterId, string message)
    {
        if (!ok)
        {
            _shoppingmallReadTitle.Text = "That letter is no longer available.";
            _shoppingmallReadBody.Text = "";
            return;
        }
        var subject = "Letter";
        foreach (var l in _shoppingmallLetters)
            if (l.LetterId == letterId) { subject = string.IsNullOrEmpty(l.Subject) ? "Letter" : l.Subject; break; }
        _shoppingmallReadTitle.Text = subject;
        _shoppingmallReadBody.Text = message;
    }

    private void OnShoppingMallGiftResult(bool ok, int letterId, int code)
    {
        if (ok)
        {
            Chat.Info("Gift claimed from your mailbox.");
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
    }

    private void OnShoppingMallOpen(short error, short freeSlot)
    {
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

    private void RebuildGiftPicker()
    {
        _shoppingmallGiftPick.Clear();
        _shoppingmallGiftPick.AddItem("None", 0);
        _shoppingmallGiftPick.SetItemMetadata(0, -1);
        int idx = 1;
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
        {
            if (Inv[abs].IsEmpty) continue;
            string name = ItemData.DisplayName(Inv[abs].ItemId);
            if (Inv[abs].Count > 1) name += $" x{Inv[abs].Count}";
            _shoppingmallGiftPick.AddItem(name, idx);
            _shoppingmallGiftPick.SetItemMetadata(idx, abs);
            idx++;
        }
        _shoppingmallGiftPick.Selected = 0;
    }

    private void SendComposedLetter()
    {
        string to = _shoppingmallToEdit.Text.Trim();
        string subject = _shoppingmallSubjectEdit.Text.Trim();
        string message = _shoppingmallMsgEdit.Text.Trim();

        if (to.Length == 0) { SetShoppingMallStatus("Enter a recipient.", true); return; }
        if (subject.Length == 0) { SetShoppingMallStatus("Enter a subject.", true); return; }
        if (message.Length == 0) { SetShoppingMallStatus("Write a message.", true); return; }

        int sel = _shoppingmallGiftPick.Selected;
        int absSlot = sel > 0 ? (int)_shoppingmallGiftPick.GetItemMetadata(sel) : -1;

        if (absSlot >= 0 && absSlot < Inv.Length && !Inv[absSlot].IsEmpty)
        {
            int cost = Net.ShoppingMallGiftCost;
            if (Sheet.Gold < cost) { SetShoppingMallStatus($"Sending a gift costs {cost:n0} gold.", true); return; }
            byte srcPos = (byte)(absSlot - GridStart);
            Net.I.SendShoppingMallGiftLetter(to, subject, message, Inv[absSlot].ItemId, srcPos);
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
            RebuildGiftPicker();
            return;
        }
        SetShoppingMallStatus(code switch
        {
            -6  => "You can't mail yourself.",
            -32 => "That item can't be mailed.",
            _   => "Couldn't send (check the name / your gold).",
        }, true);
    }

    private void OnShoppingMallBuyResult(bool ok, int totalCost)
    {
        if (ok)
        {
            if (totalCost > 0)
                Sheet.AdjustKnightCash(-totalCost);

            UpdateStatusHud();
            UpdatePusWallet();
            _pusBasketStatus.Text = $"Purchase complete: {totalCost:n0} KC spent.";
            _pusBasketStatus.AddThemeColorOverride("font_color", UiTheme.Good);
            Chat.Info($"Power-Up Store purchase complete: {totalCost:n0} KC.");
            return;
        }

        _pusBasketStatus.Text = "Purchase failed. Check your KC balance and try again.";
        _pusBasketStatus.AddThemeColorOverride("font_color", UiTheme.Bad);
    }

    private void SetShoppingMallStatus(string text, bool warn)
    {
        _shoppingmallStatus.Text = text;
        _shoppingmallStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.Good);
    }

    private void BuildPusSection(VBoxContainer root)
    {
        var pusRoot = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        pusRoot.AddThemeConstantOverride("separation", 8);
        root.AddChild(pusRoot);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 6);
        var categories = new[]
        {
            new { Id = (byte)1, Name = "Items" },
            new { Id = (byte)2, Name = "Premium" },
            new { Id = (byte)3, Name = "Utility" },
            new { Id = (byte)4, Name = "Gear" },
            new { Id = (byte)5, Name = "KC" },
        };

        foreach (var cat in categories)
        {
            var tab = new Button { Text = cat.Name, ToggleMode = true, FocusMode = Control.FocusModeEnum.None };
            tab.Pressed += () =>
            {
                _pusSelectedCategory = cat.Id;
                RefreshPusView();
            };
            tabs.AddChild(tab);
        }
        pusRoot.AddChild(tabs);

        var content = new HBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        pusRoot.AddChild(content);

        var listScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(260, 220),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _pusItemList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _pusItemList.AddThemeConstantOverride("separation", 4);
        listScroll.AddChild(_pusItemList);
        content.AddChild(listScroll);

        var basketPanel = new VBoxContainer { CustomMinimumSize = new Vector2(150, 220) };
        basketPanel.AddThemeConstantOverride("separation", 6);
        basketPanel.AddChild(UiTheme.Text("Basket", 12, UiTheme.TextHi));
        _pusWallet = UiTheme.Text("KC Balance 0", 12, UiTheme.GoldBright);
        basketPanel.AddChild(_pusWallet);
        _pusBasketList = new VBoxContainer();
        _pusBasketList.AddThemeConstantOverride("separation", 4);
        basketPanel.AddChild(_pusBasketList);

        _pusSummary = UiTheme.Text("Total 0 KC", 12, UiTheme.GoldBright);
        basketPanel.AddChild(_pusSummary);
        _pusBasketStatus = UiTheme.Text("", 11, UiTheme.TextLo);
        _pusBasketStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        basketPanel.AddChild(_pusBasketStatus);

        var basketButtons = new HBoxContainer();
        basketButtons.AddThemeConstantOverride("separation", 6);
        _pusClearButton = new Button { Text = "Clear", FocusMode = Control.FocusModeEnum.None };
        _pusClearButton.Pressed += ClearPusBasket;
        basketButtons.AddChild(_pusClearButton);
        _pusBuyButton = new Button { Text = "Buy", FocusMode = Control.FocusModeEnum.None };
        _pusBuyButton.Pressed += BuyPusBasket;
        basketButtons.AddChild(_pusBuyButton);
        basketPanel.AddChild(basketButtons);

        content.AddChild(basketPanel);
    }

    private sealed class PusSeedItem
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public string ItemTitle { get; set; } = "";
        public int Price { get; set; }
        public int SendType { get; set; }
        public int BuyCount { get; set; }
        public string ItemDesc { get; set; } = "";
        public int Category { get; set; }
        public int PriceType { get; set; }
    }

    private void BootstrapPusCatalog()
    {
        if (_pusCatalog.Count > 0) return;

        var seedPath = ProjectSettings.GlobalizePath("res://../Server/LibreKO.Game/Seed/Data/PusItems.json");
        using var file = Godot.FileAccess.Open(seedPath, Godot.FileAccess.ModeFlags.Read);

        if (file != null)
        {
            var json = file.GetAsText();
            var rows = JsonSerializer.Deserialize<List<PusSeedItem>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (rows != null)
            {
                foreach (var row in rows)
                {
                    _pusCatalog.Add(new PusCatalogEntry
                    {
                        Id = row.Id,
                        ItemId = row.ItemId,
                        Name = row.ItemName,
                        Description = string.IsNullOrWhiteSpace(row.ItemDesc) ? row.ItemTitle : row.ItemDesc,
                        Category = row.Category,
                        Price = row.Price,
                        PriceType = row.PriceType,
                    });
                }
                return;
            }
        }

        _pusCatalog.Add(new PusCatalogEntry
        {
            Id = 1, ItemId = 800079000, Name = "HP Scroll 60%", Description = "HP recovery", Category = 1, Price = 499, PriceType = 0,
        });
    }

    private void RefreshPusView()
    {
        UpdatePusWallet();
        foreach (var node in _pusItemList.GetChildren()) node.QueueFree();
        foreach (var node in _pusBasketList.GetChildren()) node.QueueFree();

        var rows = _pusCatalog.Where(i => i.Category == _pusSelectedCategory).ToList();
        if (rows.Count == 0)
        {
            _pusItemList.AddChild(UiTheme.Text("No items in this category.", 12, UiTheme.TextLo));
        }
        else
        {
            foreach (var item in rows)
            {
                var row = UiTheme.RowPanel();
                var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8); row.AddChild(hb);

                var icon = new TextureRect
                {
                    Texture = ItemData.Icon(item.ItemId),
                    CustomMinimumSize = new Vector2(30, 30),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                hb.AddChild(icon);

                var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                info.AddThemeConstantOverride("separation", -2);
                info.AddChild(UiTheme.Text(item.Name, 12, UiTheme.TextHi));
                info.AddChild(UiTheme.Text(item.Description, 11, UiTheme.TextLo));
                info.AddChild(UiTheme.Text($"{item.Price:n0} {(item.PriceType == 1 ? "TL" : "KC")}", 11, UiTheme.GoldBright));
                hb.AddChild(info);

                var add = new Button { Text = "+", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(28, 28) };
                add.Pressed += () => AddToPusBasket(item);
                hb.AddChild(add);

                _pusItemList.AddChild(row);
            }
        }

        if (_pusBasket.Count == 0)
        {
            _pusBasketStatus.Text = "Basket empty.";
            _pusSummary.Text = "Total 0 KC";
        }
        else
        {
            int total = 0;
            foreach (var basket in _pusBasket)
            {
                total += basket.Item.Price * basket.Count;
                var row = UiTheme.RowPanel();
                var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 6); row.AddChild(hb);
                hb.AddChild(UiTheme.Text(basket.Item.Name, 11, UiTheme.TextHi));
                var qty = UiTheme.Text($"x{basket.Count}", 11, UiTheme.TextLo);
                qty.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                hb.AddChild(qty);
                var remove = new Button { Text = "-", FocusMode = Control.FocusModeEnum.None };
                remove.Pressed += () => RemoveFromPusBasket(basket.Item);
                hb.AddChild(remove);
                _pusBasketList.AddChild(row);
            }
            _pusSummary.Text = $"Total {total:n0} {( _pusBasket[0].Item.PriceType == 1 ? "TL" : "KC")}";
            _pusBasketStatus.Text = "Ready to buy.";
        }

        _pusBuyButton.Disabled = _pusBasket.Count == 0;
        _pusClearButton.Disabled = _pusBasket.Count == 0;
    }

    private void UpdatePusWallet()
    {
        if (_pusWallet == null || !IsInstanceValid(_pusWallet)) return;
        _pusWallet.Text = $"KC Balance {Sheet.KnightCash:n0}";
    }

    private void AddToPusBasket(PusCatalogEntry item)
    {
        var existing = _pusBasket.FirstOrDefault(x => x.Item.Id == item.Id);
        if (existing != null) existing.Count++;
        else _pusBasket.Add(new PusBasketEntry { Item = item, Count = 1 });
        RefreshPusView();
    }

    private void RemoveFromPusBasket(PusCatalogEntry item)
    {
        var existing = _pusBasket.FirstOrDefault(x => x.Item.Id == item.Id);
        if (existing == null) return;
        if (existing.Count > 1) existing.Count--;
        else _pusBasket.Remove(existing);
        RefreshPusView();
    }

    private void ClearPusBasket()
    {
        _pusBasket.Clear();
        RefreshPusView();
    }

    private void BuyPusBasket()
    {
        if (_pusBasket.Count == 0)
        {
            _pusBasketStatus.Text = "Basket is empty.";
            _pusBasketStatus.AddThemeColorOverride("font_color", UiTheme.Bad);
            return;
        }

        var item = _pusBasket[0].Item;
        int total = 0;
        foreach (var entry in _pusBasket)
        {
            total += entry.Item.Price * entry.Count;
            if (entry.Item.PriceType != item.PriceType)
            {
                _pusBasketStatus.Text = "Basket mixes KC and TL items. Split purchases by currency type.";
                _pusBasketStatus.AddThemeColorOverride("font_color", UiTheme.Bad);
                return;
            }
        }

        if (item.PriceType == 1)
        {
            _pusBasketStatus.Text = "TL-based PUS items are not purchased from this client flow yet.";
            _pusBasketStatus.AddThemeColorOverride("font_color", UiTheme.Bad);
            return;
        }

        var sent = new List<(int itemId, int count, int priceType, int totalCost)>();
        foreach (var entry in _pusBasket)
        {
            int lineTotal = entry.Item.Price * entry.Count;
            sent.Add((entry.Item.ItemId, entry.Count, entry.Item.PriceType, lineTotal));
            Net.I.SendPowerUpStoreBuy(entry.Item.ItemId, entry.Count, entry.Item.PriceType, lineTotal);
        }

        _pusBasketStatus.Text = $"Purchase request sent for {total:n0} KC. Server validation pending.";
        _pusBasketStatus.AddThemeColorOverride("font_color", UiTheme.Good);
        Chat.Info($"Power-Up Store purchase request sent: {total:n0} KC.");
        _pusBasket.Clear();
        RefreshPusView();
    }
}
