using Godot;

namespace LibreKO.Features.Windows;

internal sealed class MailFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly MailSession mail;
    private readonly IMailRequests requests;
    private readonly MailIconFeature icon;

    public MailFeature(IWorldContext context, MailSession mail, IMailRequests requests, MailIconFeature icon)
    {
        this.context = context;
        this.mail = mail;
        this.requests = requests;
        this.icon = icon;
    }

    internal event Action? ComposeRequested;

    private const int BodyWidth = 440;
    private const int DeleteConfirmText = 1732;
    private const int ListHeight = 240;
    private const int ListHeightMax = 760;
    private const float ListScreenShare = 0.6f;
    private const int RowIconSize = 16;
    private const int ActionsTopPadding = 8;
    private const int AttachmentRowsVisible = 5;
    private const int AttachmentRowGap = 3;
    private const int AttachmentScrollGutter = 12;
    private const string UnreadGlyph = "●";
    private const string ReadGlyph = "○";

    internal CanvasLayer Layer { get; private set; } = null!;
    internal HudWindow Window { get; private set; } = null!;
    private VBoxContainer list = null!;
    private ScrollContainer listScroll = null!;
    private Label unreadPill = null!;
    private CheckButton unreadOnlyToggle = null!;
    private CheckButton parcelsOnlyToggle = null!;
    private Label fill = null!;

    private Label status = null!;
    internal HudWindow ReadWindow { get; private set; } = null!;
    private Label readSubject = null!;
    private Label readMeta = null!;
    private Label readBody = null!;
    private Label readAttachmentTitle = null!;
    private ScrollContainer readAttachmentScroll = null!;
    private VBoxContainer readAttachments = null!;
    private Button claimBtn = null!;
    private Button deleteBtn = null!;
    private List<MailEntry> mails = [];

    private int selectedId = -1;

    private bool shown;

    internal bool IsOpen => shown;

    public void Build()
    {
        BuildWindow();

        mail.MailListEvent += OnList;
        mail.MailReadEvent += OnRead;
        mail.MailDeleteEvent += OnDeleteResult;
        mail.MailClaimEvent += OnClaimResult;
        mail.MailUnreadEvent += OnUnread;
        icon.Pressed += Toggle;
        OnUnread(mail.MailUnread);
    }

    bool IEscapable.CloseOnEscape()
    {
        if (ReadWindow is { Visible: true }) CloseRead();
        else if (IsOpen) Close();
        else return false;
        return true;
    }

    public void Dispose()
    {
        icon.Pressed -= Toggle;
        mail.MailListEvent -= OnList;
        mail.MailReadEvent -= OnRead;
        mail.MailDeleteEvent -= OnDeleteResult;
        mail.MailClaimEvent -= OnClaimResult;
        mail.MailUnreadEvent -= OnUnread;

        if (GodotObject.IsInstanceValid(Window)) Window.QueueFree();
        if (GodotObject.IsInstanceValid(ReadWindow)) ReadWindow.QueueFree();
    }

    internal void BuildWindow()
    {
        Layer = new CanvasLayer { Layer = UiLayers.HudPopups };
        context.Root.AddChild(Layer);

        Window = new HudWindow("mail", "Mail", new Vector2(180, 110), bodyMinWidth: BodyWidth) { Visible = false };
        Window.Closed += () => shown = false;
        Layer.AddChild(Window);

        var body = Window.Body;
        body.AddThemeConstantOverride("separation", 6);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        body.AddChild(head);
        var compose = UiTheme.SmallButton("New mail", "Write a mail to another character");
        compose.Pressed += () => ComposeRequested?.Invoke();
        head.AddChild(compose);
        var refresh = UiTheme.IconButton(UiIcons.Get("system/refresh"), "Refresh the inbox");
        refresh.Pressed += () => requests.List();
        head.AddChild(refresh);
        unreadOnlyToggle = new CheckButton { Text = "Unread", FocusMode = Control.FocusModeEnum.None };
        unreadOnlyToggle.AddThemeFontSizeOverride("font_size", 12);
        unreadOnlyToggle.Toggled += _ =>
        {
            RenderList();
            Callable.From(Window.ResetSize).CallDeferred();
        };
        head.AddChild(unreadOnlyToggle);
        parcelsOnlyToggle = new CheckButton { Text = "Parcels", FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Only letters with attachments waiting to be claimed" };
        parcelsOnlyToggle.AddThemeFontSizeOverride("font_size", 12);
        parcelsOnlyToggle.Toggled += _ =>
        {
            RenderList();
            Callable.From(Window.ResetSize).CallDeferred();
        };
        head.AddChild(parcelsOnlyToggle);
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        head.AddChild(spacer);
        unreadPill = UiTheme.Text("", 12, UiTheme.GoldBright);
        head.AddChild(unreadPill);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(BodyWidth, ListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        listScroll = scroll;
        body.AddChild(scroll);
        list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 6);
        body.AddChild(foot);
        status = UiTheme.Text("", 12, UiTheme.TextLo);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        foot.AddChild(status);
        fill = UiTheme.Text("", 11, UiTheme.TextDim, HorizontalAlignment.Right);
        fill.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        foot.AddChild(fill);

        BuildReadWindow();
    }

    private void BuildReadWindow()
    {
        ReadWindow = new HudWindow("mailread", "Mail", new Vector2(640, 110), bodyMinWidth: BodyWidth) { Visible = false };
        ReadWindow.Closed += CloseRead;
        Layer.AddChild(ReadWindow);

        var pane = ReadWindow.Body;
        pane.AddThemeConstantOverride("separation", 4);
        readSubject = UiTheme.Text("", 14, UiTheme.GoldBright);
        readSubject.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        pane.AddChild(readSubject);
        readMeta = UiTheme.Text("", 11, UiTheme.TextDim);
        pane.AddChild(readMeta);
        pane.AddChild(new HSeparator());
        readBody = UiTheme.Text("", 13, UiTheme.TextHi);
        readBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        readBody.CustomMinimumSize = new Vector2(BodyWidth, 0);
        pane.AddChild(readBody);
        readAttachmentTitle = UiTheme.SectionTitle("");
        pane.AddChild(readAttachmentTitle);
        readAttachmentScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        UiTheme.ThinScrollbar(readAttachmentScroll.GetVScrollBar());
        pane.AddChild(readAttachmentScroll);
        var gutter = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", AttachmentScrollGutter);
        readAttachmentScroll.AddChild(gutter);
        readAttachments = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        readAttachments.AddThemeConstantOverride("separation", AttachmentRowGap);
        gutter.AddChild(readAttachments);

        var actionsMargin = new MarginContainer();
        actionsMargin.AddThemeConstantOverride("margin_top", ActionsTopPadding);
        pane.AddChild(actionsMargin);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        actionsMargin.AddChild(actions);
        claimBtn = UiTheme.ActionButton("Claim attachments", "Move the attached items and gold into your inventory");
        claimBtn.Pressed += () => { if (selectedId > 0) requests.Claim(selectedId); };
        actions.AddChild(claimBtn);
        deleteBtn = UiTheme.SmallButton("Delete", "Delete this mail");
        deleteBtn.Pressed += AskDelete;
        actions.AddChild(deleteBtn);
    }

    private void AskDelete()
    {
        int mailId = selectedId;
        if (mailId <= 0) return;
        string subject = mails.FirstOrDefault(m => m.Id == mailId)?.Subject ?? "";
        string question = ItemData.Text(DeleteConfirmText, "Are you sure you want to delete?");
        Notice.Confirm(context.Root, subject.Length > 0 ? $"{subject}\n{question}" : question, "Delete", "Cancel",
            () => { if (selectedId == mailId) requests.Delete(mailId); });
    }

    internal void Show()
    {
        shown = true;
        Window.Visible = true;
    }

    internal void Toggle()
    {
        if (shown) { Close(); return; }
        Show();
        FitList();
        status.Text = "";
        requests.List();
    }

    internal void FitList()
    {
        if (!Window.IsInsideTree()) return;
        var height = Mathf.Clamp(Window.GetViewportRect().Size.Y * ListScreenShare, ListHeight, ListHeightMax);
        listScroll.CustomMinimumSize = new Vector2(BodyWidth, height);
        Callable.From(Window.ResetSize).CallDeferred();
    }

    internal void Close()
    {
        shown = false;
        Window.Visible = false;
    }

    internal void CloseRead()
    {
        ReadWindow.Visible = false;
        selectedId = -1;
    }

    internal void OnUnread(int count)
    {
        if (unreadPill == null || !GodotObject.IsInstanceValid(unreadPill)) return;
        unreadPill.Text = count > 0 ? $"{count} unread" : "";
        if (shown && count != mails.Count(m => !m.Read)) requests.List();
    }

    internal void OnList(List<MailEntry> mails)
    {
        this.mails = mails;
        RenderList();
        if (selectedId > 0 && mails.All(m => m.Id != selectedId))
        {
            selectedId = -1;
            ReadWindow.Visible = false;
        }
        else if (selectedId > 0)
        {
            var selected = mails.First(m => m.Id == selectedId);
            RenderAttachments(selected);
            PaintActions(selected);
        }
        Callable.From(Window.ResetSize).CallDeferred();
    }

    private void RenderList()
    {
        UiKit.ClearChildren(list);
        int parcels = MailView.Parcels(mails);
        parcelsOnlyToggle.Text = parcels > 0 ? $"Parcels ({parcels})" : "Parcels";
        fill.Text = MailView.Fill(mails.Count);
        fill.AddThemeColorOverride("font_color", MailView.Full(mails.Count) ? UiTheme.Warning : UiTheme.TextDim);
        if (mails.Count == 0)
        {
            list.AddChild(UiTheme.Text("Your mailbox is empty.", 12, UiTheme.TextLo, HorizontalAlignment.Center));
            return;
        }

        bool unreadOnly = unreadOnlyToggle.ButtonPressed;
        bool parcelsOnly = parcelsOnlyToggle.ButtonPressed;
        int shownCount = 0;
        foreach (var mailEntry in mails)
        {
            if (!MailView.Shows(mailEntry, unreadOnly, parcelsOnly)) continue;
            list.AddChild(BuildRow(mailEntry));
            shownCount++;
        }
        if (shownCount == 0)
            list.AddChild(UiTheme.Text(MailView.EmptyText(unreadOnly, parcelsOnly), 12, UiTheme.TextLo, HorizontalAlignment.Center));
    }

    private Control BuildRow(MailEntry mailEntry)
    {
        var store = mailEntry.Kind == MailKind.Store;
        var panel = UiTheme.RowPanel(mailEntry.Id == selectedId, mailEntry.Read && !store);
        if (store) panel.AddThemeStyleboxOverride("panel", StoreRow(mailEntry.Id == selectedId));
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        var margin = new MarginContainer();
        UiTheme.Margins(margin, 8, 4, 8, 4);
        panel.AddChild(margin);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        margin.AddChild(row);

        row.AddChild(UiTheme.Text(mailEntry.Read ? ReadGlyph : UnreadGlyph, 12, mailEntry.Read ? UiTheme.TextDim : UiTheme.GoldBright));
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        row.AddChild(text);
        text.AddChild(UiTheme.Text(mailEntry.Subject, 13, mailEntry.Read ? UiTheme.TextLo : UiTheme.TextHi));
        text.AddChild(UiTheme.Text($"from {mailEntry.Sender}", 11, UiTheme.TextDim));
        if (store || mailEntry.Attachments == MailAttachmentState.Pending)
        {
            var marker = new TextureRect
            {
                Texture = UiIcons.Get(store ? "system/gem" : "system/gift"),
                CustomMinimumSize = new Vector2(RowIconSize, RowIconSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = store ? UiTheme.Premium : UiTheme.GoldBright,
                TooltipText = store
                    ? mailEntry.Attachments == MailAttachmentState.Pending ? "Power-Up Store items waiting to be claimed" : "From the Power-Up Store"
                    : "Attachments waiting to be claimed",
            };
            row.AddChild(marker);
        }
        row.AddChild(UiTheme.Text(Date(mailEntry.SentAt), 11, UiTheme.TextDim));

        var mailId = mailEntry.Id;
        panel.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            panel.AcceptEvent();
            Callable.From(() => Select(mailId)).CallDeferred();
        };
        return panel;
    }

    private static StyleBoxFlat StoreRow(bool selected)
    {
        var style = UiTheme.Row(selected);
        style.BgColor = selected ? new Color(0.26f, 0.20f, 0.09f, 0.95f) : new Color(0.19f, 0.15f, 0.07f, 0.92f);
        style.BorderColor = selected ? UiTheme.GoldBright : new Color(UiTheme.GoldVivid, 0.75f);
        return style;
    }

    private static string Date(DateTime utc)
    {
        var local = utc.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("HH:mm") : local.ToString("dd MMM");
    }

    internal void Select(int mailId)
    {
        var mailEntry = mails.FirstOrDefault(m => m.Id == mailId);
        if (mailEntry == null) return;
        selectedId = mailId;
        readSubject.Text = mailEntry.Subject;
        readMeta.Text = $"From {mailEntry.Sender}  ·  {mailEntry.SentAt.ToLocalTime():dd MMM yyyy HH:mm}";
        readBody.Text = "";
        RenderAttachments(mailEntry);
        PaintActions(mailEntry);
        ReadWindow.Visible = true;
        ReadWindow.GetParent()?.MoveChild(ReadWindow, ReadWindow.GetParent().GetChildCount() - 1);
        Callable.From(ReadWindow.ResetSize).CallDeferred();
        RenderList();
        requests.Read(mailId);
    }

    internal void RenderAttachments(MailEntry mailEntry)
    {
        UiKit.ClearChildren(readAttachments);
        var count = mailEntry.Items.Count;
        readAttachmentTitle.Visible = readAttachmentScroll.Visible = count > 0;
        if (count == 0) return;
        var claimed = mailEntry.Attachments == MailAttachmentState.Claimed;
        readAttachmentTitle.Text = claimed ? $"Attachments ({count}, claimed)"
            : $"Attachments ({count}) - click one to claim just that";
        for (var index = 0; index < count; index++)
        {
            var attachment = mailEntry.Items[index];
            var displayId = DisplayItemId(attachment);
            var done = claimed || attachment.Remaining == 0;
            var value = !done && attachment.Claimed > 0
                ? $"{attachment.Remaining:n0} of {attachment.Count:n0} left"
                : $"{attachment.Count:n0}";
            var row = QuestRows.ItemRow(context.Tooltips, displayId, UiKit.QuestRewardName(displayId), value, done ? UiTheme.TextDim : UiTheme.GoldBright);
            readAttachments.AddChild(done ? row : ClaimableRow(row, mailEntry.Id, index));
        }
        var rows = Math.Min(count, AttachmentRowsVisible);
        readAttachmentScroll.CustomMinimumSize = new Vector2(0, rows * QuestRows.IconSide + (rows - 1) * AttachmentRowGap);
    }

    private Control ClaimableRow(Control row, int mailId, int index)
    {
        var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = "" };
        var idle = new StyleBoxEmpty();
        var hover = UiTheme.Row(selected: true);
        hover.ContentMarginLeft = hover.ContentMarginRight = hover.ContentMarginTop = hover.ContentMarginBottom = 0;
        frame.AddThemeStyleboxOverride("panel", idle);
        frame.MouseEntered += () => frame.AddThemeStyleboxOverride("panel", hover);
        frame.MouseExited += () => frame.AddThemeStyleboxOverride("panel", idle);
        frame.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            frame.AcceptEvent();
            requests.ClaimAttachment(mailId, index);
        };
        frame.AddChild(row);
        return frame;
    }

    private static int DisplayItemId(MailAttachment attachment) => attachment.Kind switch
    {
        MailAttachmentKind.Gold => QuestData.CoinItemId,
        MailAttachmentKind.Experience => QuestData.ExpItemId,
        MailAttachmentKind.NationalPoints => QuestData.LadderPointItemId,
        _ => attachment.ItemId,
    };

    private void PaintActions(MailEntry mailEntry)
    {
        claimBtn.Visible = mailEntry.Attachments == MailAttachmentState.Pending;
        deleteBtn.Disabled = mailEntry.Attachments == MailAttachmentState.Pending;
        deleteBtn.TooltipText = mailEntry.Attachments == MailAttachmentState.Pending ? "Claim the attachments first" : "Delete this mail";
    }

    internal void OnRead(int mailId, bool ok, string body)
    {
        var mailEntry = mails.FirstOrDefault(m => m.Id == mailId);
        if (ok && mailEntry != null && !mailEntry.Read)
        {
            mailEntry.Read = true;
            RenderList();
        }
        if (mailId != selectedId) return;
        readBody.Text = ok ? body : "This mail is no longer available.";
        Callable.From(ReadWindow.ResetSize).CallDeferred();
    }

    private void OnDeleteResult(int mailId, bool ok, string message)
    {
        status.Text = ok ? "Mail deleted." : message;
        if (ok && mailId == selectedId)
        {
            selectedId = -1;
            ReadWindow.Visible = false;
        }
        requests.List();
    }

    private void OnClaimResult(int mailId, bool ok, string message)
    {
        status.Text = message;
        requests.List();
    }

    internal void ShowStatus(string text) => status.Text = text;
}
