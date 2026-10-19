using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class MailComposeFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly FriendsSession friends;
    private readonly MailSession mail;
    private readonly SelfSession self;
    private readonly IClanRequests clanRequests;
    private readonly IFriendsRequests friendsRequests;
    private readonly IMailRequests requests;
    private readonly MailFeature mailWindow;

    public MailComposeFeature(IWorldContext context, ClanSession clan, FriendsSession friends, MailSession mail, SelfSession self, IClanRequests clanRequests, IFriendsRequests friendsRequests, IMailRequests requests, MailFeature mailWindow)
    {
        this.context = context;
        this.clan = clan;
        this.friends = friends;
        this.mail = mail;
        this.self = self;
        this.clanRequests = clanRequests;
        this.friendsRequests = friendsRequests;
        this.requests = requests;
        this.mailWindow = mailWindow;
    }

    public void Build()
    {
        BuildComposeWindow();
        mailWindow.ComposeRequested += OpenCompose;
        mail.MailSendEvent += OnSendResult;
        friends.FriendListEvent += OnComposeFriends;
        clan.ClanMembersEvent += OnComposeClanMembers;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (!ComposeShown) return false;
        HideCompose();
        return true;
    }

    public void Dispose()
    {
        mailWindow.ComposeRequested -= OpenCompose;
        mail.MailSendEvent -= OnSendResult;
        friends.FriendListEvent -= OnComposeFriends;
        clan.ClanMembersEvent -= OnComposeClanMembers;
        if (GodotObject.IsInstanceValid(ComposeWindow)) ComposeWindow.QueueFree();
    }

    private const int ComposeWidth = 400;
    private const int ComposeBodyHeight = 110;
    private const int ComposeGap = 10;
    private const int AttachRowGap = 8;
    private const int AttachAreaHeight = (int)(MailWire.ItemAttachmentsMax * QuestRows.IconSide + (MailWire.ItemAttachmentsMax - 1) * AttachRowGap) + 20;
    private const int RecipientSuggestions = 8;
    private const int UntradeableRace = 20;
    private const int NoTradeItemMin = 900000001;
    private const int NoTradeItemMax = 1_000_000_000;
    private const long GoldMax = int.MaxValue;
    private const int BodyWarnRemaining = 40;
    private const double SuggestDebounceSeconds = 0.25;
    private const string SendingText = "Sending…";

    internal HudWindow ComposeWindow { get; private set; } = null!;
    internal LineEdit Recipient { get; private set; } = null!;
    private VBoxContainer recipientSuggestions = null!;
    private Godot.Timer recipientDebounce = null!;
    internal LineEdit SubjectInput { get; private set; } = null!;
    internal TextEdit BodyInput { get; private set; } = null!;
    private Label bodyRemaining = null!;
    internal MoneyEdit Gold { get; private set; } = null!;
    private Label attachTitle = null!;
    internal DropZone AttachmentDrop { get; private set; } = null!;
    private VBoxContainer attachRows = null!;
    private Label composeStatus = null!;
    private Button sendBtn = null!;
    internal SortedSet<string> Contacts { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal List<(int Slot, int Count)> Attachments { get; } = [];
    internal bool ComposeShown { get; set; }
    private bool sending;
    private bool sentDraftOpen;

    internal void BuildComposeWindow()
    {
        ComposeWindow = new HudWindow("mailcompose", "New mail", new Vector2(640, 110), bodyMinWidth: ComposeWidth) { Visible = false };
        ComposeWindow.Closed += HideCompose;
        mailWindow.Layer.AddChild(ComposeWindow);

        var body = ComposeWindow.Body;
        body.AddThemeConstantOverride("separation", 6);

        body.AddChild(UiTheme.Text("To", 12, UiTheme.TextLo));
        Recipient = new LineEdit { PlaceholderText = "character name", MaxLength = 20 };
        Recipient.TextChanged += _ => recipientDebounce.Start();
        Recipient.FocusEntered += () => recipientDebounce.Start();
        Recipient.FocusExited += () => Callable.From(() => { if (!Recipient.HasFocus()) recipientSuggestions.Visible = false; }).CallDeferred();
        body.AddChild(Recipient);
        recipientDebounce = new Godot.Timer { WaitTime = SuggestDebounceSeconds, OneShot = true };
        recipientDebounce.Timeout += RefreshRecipientSuggestions;
        Recipient.AddChild(recipientDebounce);
        recipientSuggestions = new VBoxContainer { Visible = false };
        recipientSuggestions.AddThemeConstantOverride("separation", 2);
        body.AddChild(recipientSuggestions);

        body.AddChild(UiTheme.Text("Subject", 12, UiTheme.TextLo));
        SubjectInput = new LineEdit { PlaceholderText = "subject", MaxLength = MailWire.SubjectMax };
        body.AddChild(SubjectInput);

        var messageHead = new HBoxContainer();
        body.AddChild(messageHead);
        var messageLabel = UiTheme.Text("Message", 12, UiTheme.TextLo);
        messageLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        messageHead.AddChild(messageLabel);
        bodyRemaining = UiTheme.Text(MailWire.BodyMax.ToString(), 11, UiTheme.TextDim);
        messageHead.AddChild(bodyRemaining);
        BodyInput = new TextEdit
        {
            CustomMinimumSize = new Vector2(ComposeWidth, ComposeBodyHeight),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "write your message",
        };
        var inputBox = new StyleBoxFlat { BgColor = new Color(0.04f, 0.03f, 0.02f, 0.9f), BorderColor = new Color(UiTheme.Edge, 0.7f) };
        inputBox.SetBorderWidthAll(1);
        inputBox.SetCornerRadiusAll(4);
        inputBox.SetContentMarginAll(6);
        var inputFocus = (StyleBoxFlat)inputBox.Duplicate();
        inputFocus.BorderColor = new Color(UiTheme.Gold, 0.9f);
        BodyInput.AddThemeStyleboxOverride("normal", inputBox);
        BodyInput.AddThemeStyleboxOverride("focus", inputFocus);
        BodyInput.AddThemeStyleboxOverride("read_only", inputBox);
        BodyInput.AddThemeColorOverride("font_color", UiTheme.TextHi);
        BodyInput.AddThemeColorOverride("font_placeholder_color", new Color(UiTheme.TextLo, 0.6f));
        BodyInput.AddThemeColorOverride("caret_color", UiTheme.Gold);
        BodyInput.TextChanged += OnBodyChanged;
        body.AddChild(BodyInput);

        var goldRow = new HBoxContainer();
        goldRow.AddThemeConstantOverride("separation", 8);
        body.AddChild(ComposeGapAbove(goldRow));
        goldRow.AddChild(new TextureRect
        {
            Texture = UiIcons.Get("system/coins"),
            CustomMinimumSize = new Vector2(16, 16),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Modulate = UiTheme.GoldBright,
        });
        goldRow.AddChild(UiTheme.Text("Gold", 12, UiTheme.TextLo));
        Gold = new MoneyEdit(GoldMax, 160);
        goldRow.AddChild(Gold);

        attachTitle = UiTheme.Text("", 13, UiTheme.Gold);
        body.AddChild(ComposeGapAbove(attachTitle));
        AttachmentDrop = new DropZone
        {
            OnDropItem = AttachItem,
            CanAccept = CanAttachSlot,
        };
        body.AddChild(AttachmentDrop);
        attachRows = AttachmentDrop.Rows;

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        body.AddChild(ComposeGapAbove(actions));
        sendBtn = UiTheme.ActionButton("Send", "Send the mail");
        sendBtn.Pressed += SendComposed;
        actions.AddChild(sendBtn);
        var cancel = UiTheme.SmallButton("Cancel", "Discard this mail");
        cancel.Pressed += CloseCompose;
        actions.AddChild(cancel);

        composeStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        composeStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(composeStatus);

        RenderAttachments();
    }

    private static MarginContainer ComposeGapAbove(Control content)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", ComposeGap);
        margin.AddChild(content);
        return margin;
    }

    internal void OnBodyChanged()
    {
        if (BodyInput.Text.Length > MailWire.BodyMax)
        {
            BodyInput.Text = BodyInput.Text[..MailWire.BodyMax];
            BodyInput.SetCaretLine(BodyInput.GetLineCount() - 1);
            BodyInput.SetCaretColumn(BodyInput.GetLine(BodyInput.GetLineCount() - 1).Length);
        }
        var remaining = MailWire.BodyMax - BodyInput.Text.Length;
        bodyRemaining.Text = remaining.ToString();
        bodyRemaining.AddThemeColorOverride("font_color", remaining <= 0 ? UiTheme.Bad : remaining < BodyWarnRemaining ? UiTheme.Warning : UiTheme.TextDim);
    }

    internal void OpenCompose()
    {
        ComposeShown = true;
        ComposeWindow.Visible = true;
        ComposeWindow.GetParent()?.MoveChild(ComposeWindow, ComposeWindow.GetParent().GetChildCount() - 1);
        composeStatus.Text = sending ? SendingText : "";
        sendBtn.Disabled = sending;
        friendsRequests.FriendListRequest();
        clanRequests.MembersRequest();
        Recipient.GrabFocus();
    }

    internal void OpenComposeTo(string recipient)
    {
        OpenCompose();
        Recipient.Text = recipient;
    }

    internal void HideCompose()
    {
        ComposeShown = false;
        sentDraftOpen = false;
        ComposeWindow.Visible = false;
        recipientSuggestions.Visible = false;
    }

    private void CloseCompose()
    {
        HideCompose();
        ResetCompose();
    }

    private void ResetCompose()
    {
        Recipient.Text = "";
        SubjectInput.Text = "";
        BodyInput.Text = "";
        Gold.Value = 0;
        Attachments.Clear();
        OnBodyChanged();
        RenderAttachments();
    }

    private void OnComposeFriends(List<FriendEntry> friendEntries)
    {
        foreach (var friend in friendEntries)
            if (!string.IsNullOrEmpty(friend.Name)) Contacts.Add(friend.Name);
    }

    private void OnComposeClanMembers(List<ClanMember> members)
    {
        foreach (var member in members)
            if (!string.IsNullOrEmpty(member.Name)) Contacts.Add(member.Name);
    }

    private void RefreshRecipientSuggestions()
    {
        if (!ComposeShown) return;
        var typed = Recipient.Text.Trim();
        var me = self.LastEnter.Name ?? "";
        var matches = Contacts
            .Where(name => !string.Equals(name, me, StringComparison.OrdinalIgnoreCase))
            .Where(name => typed.Length == 0 || name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Where(name => !string.Equals(name, typed, StringComparison.OrdinalIgnoreCase))
            .Take(RecipientSuggestions)
            .ToList();

        UiKit.ClearChildren(recipientSuggestions);
        recipientSuggestions.Visible = matches.Count > 0 && Recipient.HasFocus();
        foreach (var name in matches)
        {
            var pick = new Button
            {
                Text = name,
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
            };
            pick.AddThemeFontSizeOverride("font_size", 12);
            pick.AddThemeColorOverride("font_color", UiTheme.TextLo);
            pick.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
            pick.AddThemeStyleboxOverride("hover", UiTheme.Row(selected: true));
            var chosen = name;
            pick.Pressed += () =>
            {
                Recipient.Text = chosen;
                Recipient.CaretColumn = chosen.Length;
                recipientSuggestions.Visible = false;
                SubjectInput.GrabFocus();
            };
            recipientSuggestions.AddChild(pick);
        }
        Callable.From(ComposeWindow.ResetSize).CallDeferred();
    }

    private static bool ItemTradable(ItemSlot slot)
    {
        if (slot.IsEmpty) return false;
        var data = ItemData.Get(slot.ItemId);
        if (data == null || data.Race == UntradeableRace) return false;
        if (slot.ItemId >= NoTradeItemMin && slot.ItemId < NoTradeItemMax) return false;
        return slot.State is not (ItemFlag.Rented or ItemFlag.CharacterSeal or ItemFlag.Duplicate or ItemFlag.Sealed or ItemFlag.Bound);
    }

    private bool CanAttachSlot(int abs) =>
        self.Inventory.IsGridSlot(abs) && abs < self.Inventory.Length
        && Attachments.Count < MailWire.ItemAttachmentsMax
        && !Attachments.Any(a => a.Slot == abs);

    private void AttachItem(int abs)
    {
        if (!CanAttachSlot(abs)) return;
        var slot = self.Inventory[abs];
        if (!ItemTradable(slot))
        {
            composeStatus.Text = $"{ItemData.DisplayName(slot.ItemId)} cannot be traded, so it cannot be mailed.";
            return;
        }
        composeStatus.Text = "";
        Attachments.Add((abs, slot.Count));
        RenderAttachments();
    }

    internal void RenderAttachments()
    {
        attachTitle.Text = $"Attachments ({Attachments.Count} / {MailWire.ItemAttachmentsMax})";
        UiKit.ClearChildren(attachRows);
        AttachmentDrop.SetHintVisible(Attachments.Count < MailWire.ItemAttachmentsMax);
        for (var i = 0; i < Attachments.Count; i++)
        {
            var index = i;
            var (abs, count) = Attachments[i];
            var slot = self.Inventory[abs];
            var stackable = (ItemData.Get(slot.ItemId)?.Countable ?? 0) != 0;
            var row = UiKit.ValueRow(ItemData.DisplayName(slot.ItemId), stackable && slot.Count == 1 ? "1" : "", UiTheme.GoldBright);
            var nameLabel = row.GetChild<Label>(0);
            nameLabel.AutowrapMode = TextServer.AutowrapMode.Off;
            nameLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            var icon = new TextureRect
            {
                Texture = ItemIcons.Get(slot.ItemId),
                CustomMinimumSize = new Vector2(QuestRows.IconSide, QuestRows.IconSide),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            UpgradeBadge.Show(icon, slot.ItemId);
            row.AddChild(icon);
            row.MoveChild(icon, 0);
            if (slot.Count > 1)
            {
                var spin = UiTheme.NumberBox(1, slot.Count, 1, 72, 11);
                spin.Value = count;
                spin.ValueChanged += value => Attachments[index] = (abs, (int)value);
                row.AddChild(spin);
            }
            var remove = UiTheme.IconButton(UiIcons.Get("system/close"), "Remove");
            remove.Pressed += () => { Attachments.RemoveAt(index); RenderAttachments(); };
            row.AddChild(remove);
            row.MouseFilter = Control.MouseFilterEnum.Pass;
            row.MouseEntered += () => context.Tooltips.Show(abs, self.Inventory[abs]);
            row.MouseExited += context.Tooltips.Hide;
            attachRows.AddChild(row);
        }
        Callable.From(ComposeWindow.ResetSize).CallDeferred();
    }

    private void SendComposed()
    {
        if (sending) return;
        var to = Recipient.Text.Trim();
        var subject = SubjectInput.Text.Trim();
        if (to.Length == 0 || subject.Length == 0)
        {
            composeStatus.Text = "A recipient and a subject are required.";
            return;
        }

        var goldAmount = (int)Math.Clamp(Gold.Value, 0, GoldMax);
        if (goldAmount > self.Sheet.Gold)
        {
            composeStatus.Text = "You do not carry that much gold.";
            return;
        }

        var items = Attachments
            .Where(a => ItemTradable(self.Inventory[a.Slot]))
            .Select(a => new MailItemPick((byte)a.Slot, (ushort)Math.Clamp(a.Count, 1, self.Inventory[a.Slot].Count)))
            .ToList();

        sendBtn.Disabled = true;
        sending = true;
        sentDraftOpen = true;
        composeStatus.Text = SendingText;
        requests.Send(to, subject, BodyInput.Text, goldAmount, items);
    }

    private void OnSendResult(bool ok, string message)
    {
        sending = false;
        if (ok)
        {
            mailWindow.ShowStatus(message);
            if (sentDraftOpen) CloseCompose();
            else
            {
                sendBtn.Disabled = false;
                composeStatus.Text = "";
            }
            requests.List();
            return;
        }
        sendBtn.Disabled = false;
        composeStatus.Text = message;
    }

    internal partial class DropZone : PanelContainer
    {
        public Action<int>? OnDropItem;
        public Func<int, bool>? CanAccept;
        public VBoxContainer Rows { get; }
        private readonly Label hint;
        private readonly StyleBox idle;
        private readonly StyleBox hot;

        public DropZone()
        {
            idle = UiTheme.Inset();
            var hotStyle = (StyleBoxFlat)UiTheme.Inset();
            hotStyle.BorderColor = UiTheme.GoldBright;
            hotStyle.SetBorderWidthAll(1);
            hot = hotStyle;
            AddThemeStyleboxOverride("panel", idle);
            MouseFilter = MouseFilterEnum.Stop;
            CustomMinimumSize = new Vector2(ComposeWidth, AttachAreaHeight);

            var stack = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Pass,
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
            };
            stack.AddThemeConstantOverride("separation", AttachRowGap);
            AddChild(stack);
            Rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
            Rows.AddThemeConstantOverride("separation", AttachRowGap);
            stack.AddChild(Rows);
            hint = UiTheme.Text("Drag items here from your inventory", 12, UiTheme.TextLo, HorizontalAlignment.Center);
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.CustomMinimumSize = new Vector2(0, QuestRows.IconSide);
            hint.MouseFilter = MouseFilterEnum.Ignore;
            stack.AddChild(hint);
            MouseExited += () => AddThemeStyleboxOverride("panel", idle);
        }

        public void SetHintVisible(bool visible) => hint.Visible = visible;

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (data.VariantType != Variant.Type.Dictionary) return false;
            var d = data.AsGodotDictionary();
            if (!d.ContainsKey("invFrom")) return false;
            var ok = CanAccept?.Invoke(d["invFrom"].AsInt32()) ?? false;
            AddThemeStyleboxOverride("panel", ok ? hot : idle);
            return ok;
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            AddThemeStyleboxOverride("panel", idle);
            OnDropItem?.Invoke(data.AsGodotDictionary()["invFrom"].AsInt32());
        }
    }
}
