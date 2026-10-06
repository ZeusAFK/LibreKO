using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int MailBodyWidth = 440;
    private const int MailListHeight = 240;
    private const int MailListHeightMax = 760;
    private const float MailListScreenShare = 0.6f;
    private const int MailRowIconSize = 16;
    private const int MailActionsTopPadding = 8;
    private const int MailAttachmentRowsVisible = 5;
    private const int MailAttachmentRowGap = 3;
    private const int MailAttachmentScrollGutter = 12;
    private const int MailLayerIndex = 71;
    private const string MailUnreadGlyph = "●";
    private const string MailReadGlyph = "○";

    private CanvasLayer _mailLayer = null!;
    private HudWindow _mailWindow = null!;
    private VBoxContainer _mailList = null!;
    private ScrollContainer _mailListScroll = null!;
    private Label _mailUnreadPill = null!;
    private CheckButton _mailUnreadOnly = null!;
    private Label _mailStatus = null!;
    private HudWindow _mailReadWindow = null!;
    private Label _mailReadSubject = null!;
    private Label _mailReadMeta = null!;
    private Label _mailReadBody = null!;
    private Label _mailReadAttachmentTitle = null!;
    private ScrollContainer _mailReadAttachmentScroll = null!;
    private VBoxContainer _mailReadAttachments = null!;
    private Button _mailClaimBtn = null!;
    private Button _mailDeleteBtn = null!;

    private List<MailEntry> _mails = [];
    private int _mailSelectedId = -1;
    private bool _mailShown;

    private void MailInit()
    {
        BuildMailWindow();
        BuildMailComposeWindow();

        Net.I.MailListEvent += OnMailList;
        Net.I.MailReadEvent += OnMailRead;
        Net.I.MailSendEvent += OnMailSendResult;
        Net.I.MailDeleteEvent += OnMailDeleteResult;
        Net.I.MailClaimEvent += OnMailClaimResult;
        Net.I.MailUnreadEvent += OnMailUnread;
        Net.I.FriendListEvent += OnMailComposeFriends;
        Net.I.ClanMembersEvent += OnMailComposeClanMembers;
        OnMailUnread(Net.I.MailUnread);
    }

    private void MailDispose()
    {
        Net.I.MailListEvent -= OnMailList;
        Net.I.MailReadEvent -= OnMailRead;
        Net.I.MailSendEvent -= OnMailSendResult;
        Net.I.MailDeleteEvent -= OnMailDeleteResult;
        Net.I.MailClaimEvent -= OnMailClaimResult;
        Net.I.MailUnreadEvent -= OnMailUnread;
        Net.I.FriendListEvent -= OnMailComposeFriends;
        Net.I.ClanMembersEvent -= OnMailComposeClanMembers;

        if (IsInstanceValid(_mailWindow)) _mailWindow.QueueFree();
        if (IsInstanceValid(_mailComposeWindow)) _mailComposeWindow.QueueFree();
        if (IsInstanceValid(_mailReadWindow)) _mailReadWindow.QueueFree();
    }

    private void BuildMailWindow()
    {
        _mailLayer = new CanvasLayer { Layer = MailLayerIndex };
        AddChild(_mailLayer);

        _mailWindow = new HudWindow("mail", "Mail", new Vector2(180, 110), bodyMinWidth: MailBodyWidth) { Visible = false };
        _mailWindow.Closed += () => _mailShown = false;
        _mailLayer.AddChild(_mailWindow);

        var body = _mailWindow.Body;
        body.AddThemeConstantOverride("separation", 6);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        body.AddChild(head);
        var compose = UiTheme.SmallButton("New mail", "Write a mail to another character");
        compose.Pressed += OpenMailCompose;
        head.AddChild(compose);
        var refresh = UiTheme.IconButton(UiIcons.Get("system/refresh"), "Refresh the inbox");
        refresh.Pressed += () => Net.I.SendMailList();
        head.AddChild(refresh);
        _mailUnreadOnly = new CheckButton { Text = "Unread", FocusMode = Control.FocusModeEnum.None };
        _mailUnreadOnly.AddThemeFontSizeOverride("font_size", 12);
        _mailUnreadOnly.Toggled += _ =>
        {
            RenderMailList();
            Callable.From(_mailWindow.ResetSize).CallDeferred();
        };
        head.AddChild(_mailUnreadOnly);
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        head.AddChild(spacer);
        _mailUnreadPill = UiTheme.Text("", 12, UiTheme.GoldBright);
        head.AddChild(_mailUnreadPill);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(MailBodyWidth, MailListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _mailListScroll = scroll;
        body.AddChild(scroll);
        _mailList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _mailList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_mailList);

        _mailStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        _mailStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_mailStatus);

        BuildMailReadWindow();
    }

    private void BuildMailReadWindow()
    {
        _mailReadWindow = new HudWindow("mailread", "Mail", new Vector2(640, 110), bodyMinWidth: MailBodyWidth) { Visible = false };
        _mailReadWindow.Closed += () => _mailSelectedId = -1;
        _mailLayer.AddChild(_mailReadWindow);

        var pane = _mailReadWindow.Body;
        pane.AddThemeConstantOverride("separation", 4);
        _mailReadSubject = UiTheme.Text("", 14, UiTheme.GoldBright);
        _mailReadSubject.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        pane.AddChild(_mailReadSubject);
        _mailReadMeta = UiTheme.Text("", 11, UiTheme.TextDim);
        pane.AddChild(_mailReadMeta);
        pane.AddChild(new HSeparator());
        _mailReadBody = UiTheme.Text("", 13, UiTheme.TextHi);
        _mailReadBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _mailReadBody.CustomMinimumSize = new Vector2(MailBodyWidth, 0);
        pane.AddChild(_mailReadBody);
        _mailReadAttachmentTitle = UiTheme.SectionTitle("");
        pane.AddChild(_mailReadAttachmentTitle);
        _mailReadAttachmentScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        UiTheme.ThinScrollbar(_mailReadAttachmentScroll.GetVScrollBar());
        pane.AddChild(_mailReadAttachmentScroll);
        var gutter = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", MailAttachmentScrollGutter);
        _mailReadAttachmentScroll.AddChild(gutter);
        _mailReadAttachments = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _mailReadAttachments.AddThemeConstantOverride("separation", MailAttachmentRowGap);
        gutter.AddChild(_mailReadAttachments);

        var actionsMargin = new MarginContainer();
        actionsMargin.AddThemeConstantOverride("margin_top", MailActionsTopPadding);
        pane.AddChild(actionsMargin);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        actionsMargin.AddChild(actions);
        _mailClaimBtn = UiTheme.ActionButton("Claim attachments", "Move the attached items and gold into your inventory");
        _mailClaimBtn.Pressed += () => { if (_mailSelectedId > 0) Net.I.SendMailClaim(_mailSelectedId); };
        actions.AddChild(_mailClaimBtn);
        _mailDeleteBtn = UiTheme.SmallButton("Delete", "Delete this mail");
        _mailDeleteBtn.Pressed += () => { if (_mailSelectedId > 0) Net.I.SendMailDelete(_mailSelectedId); };
        actions.AddChild(_mailDeleteBtn);
    }

    private void ToggleMail()
    {
        if (_mailShown) { CloseMail(); return; }
        _mailShown = true;
        FitMailList();
        _mailWindow.Visible = true;
        _mailStatus.Text = "";
        Net.I.SendMailList();
    }

    private void FitMailList()
    {
        if (!_mailWindow.IsInsideTree()) return;
        var height = Mathf.Clamp(_mailWindow.GetViewportRect().Size.Y * MailListScreenShare, MailListHeight, MailListHeightMax);
        _mailListScroll.CustomMinimumSize = new Vector2(MailBodyWidth, height);
        Callable.From(_mailWindow.ResetSize).CallDeferred();
    }

    private void CloseMail()
    {
        _mailShown = false;
        _mailWindow.Visible = false;
    }

    private void OnMailUnread(int count)
    {
        if (_mailUnreadPill == null || !IsInstanceValid(_mailUnreadPill)) return;
        _mailUnreadPill.Text = count > 0 ? $"{count} unread" : "";
        if (_mailShown) Net.I.SendMailList();
    }

    private void OnMailList(List<MailEntry> mails)
    {
        _mails = mails;
        RenderMailList();
        if (_mailSelectedId > 0 && mails.All(m => m.Id != _mailSelectedId))
        {
            _mailSelectedId = -1;
            _mailReadWindow.Visible = false;
        }
        else if (_mailSelectedId > 0)
        {
            var selected = mails.First(m => m.Id == _mailSelectedId);
            RenderMailAttachments(selected);
            PaintMailActions(selected);
        }
        Callable.From(_mailWindow.ResetSize).CallDeferred();
    }

    private void RenderMailList()
    {
        ClearChildren(_mailList);
        if (_mails.Count == 0)
        {
            _mailList.AddChild(UiTheme.Text("Your mailbox is empty.", 12, UiTheme.TextLo, HorizontalAlignment.Center));
            return;
        }

        bool unreadOnly = _mailUnreadOnly.ButtonPressed;
        int shown = 0;
        foreach (var mail in _mails)
        {
            if (unreadOnly && mail.Read) continue;
            _mailList.AddChild(BuildMailRow(mail));
            shown++;
        }
        if (shown == 0)
            _mailList.AddChild(UiTheme.Text("No unread mail.", 12, UiTheme.TextLo, HorizontalAlignment.Center));
    }

    private Control BuildMailRow(MailEntry mail)
    {
        var store = mail.Kind == MailKind.Store;
        var panel = UiTheme.RowPanel(mail.Id == _mailSelectedId, mail.Read && !store);
        if (store) panel.AddThemeStyleboxOverride("panel", MailStoreRow(mail.Id == _mailSelectedId));
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        var margin = new MarginContainer();
        UiTheme.Margins(margin, 8, 4, 8, 4);
        panel.AddChild(margin);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        margin.AddChild(row);

        row.AddChild(UiTheme.Text(mail.Read ? MailReadGlyph : MailUnreadGlyph, 12, mail.Read ? UiTheme.TextDim : UiTheme.GoldBright));
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        row.AddChild(text);
        text.AddChild(UiTheme.Text(mail.Subject, 13, mail.Read ? UiTheme.TextLo : UiTheme.TextHi));
        text.AddChild(UiTheme.Text($"from {mail.Sender}", 11, UiTheme.TextDim));
        if (store || mail.Attachments == MailAttachmentState.Pending)
        {
            var marker = new TextureRect
            {
                Texture = UiIcons.Get(store ? "system/gem" : "system/gift"),
                CustomMinimumSize = new Vector2(MailRowIconSize, MailRowIconSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = store ? UiTheme.Premium : UiTheme.GoldBright,
                TooltipText = store
                    ? mail.Attachments == MailAttachmentState.Pending ? "Power-Up Store items waiting to be claimed" : "From the Power-Up Store"
                    : "Attachments waiting to be claimed",
            };
            row.AddChild(marker);
        }
        row.AddChild(UiTheme.Text(MailDate(mail.SentAt), 11, UiTheme.TextDim));

        var mailId = mail.Id;
        panel.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            panel.AcceptEvent();
            Callable.From(() => SelectMail(mailId)).CallDeferred();
        };
        return panel;
    }

    private static StyleBoxFlat MailStoreRow(bool selected)
    {
        var style = UiTheme.Row(selected);
        style.BgColor = selected ? new Color(0.26f, 0.20f, 0.09f, 0.95f) : new Color(0.19f, 0.15f, 0.07f, 0.92f);
        style.BorderColor = selected ? UiTheme.GoldBright : new Color(UiTheme.GoldVivid, 0.75f);
        return style;
    }

    private static string MailDate(DateTime utc)
    {
        var local = utc.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("HH:mm") : local.ToString("dd MMM");
    }

    private void SelectMail(int mailId)
    {
        var mail = _mails.FirstOrDefault(m => m.Id == mailId);
        if (mail == null) return;
        _mailSelectedId = mailId;
        _mailReadSubject.Text = mail.Subject;
        _mailReadMeta.Text = $"From {mail.Sender}  ·  {mail.SentAt.ToLocalTime():dd MMM yyyy HH:mm}";
        _mailReadBody.Text = "";
        RenderMailAttachments(mail);
        PaintMailActions(mail);
        _mailReadWindow.Visible = true;
        _mailReadWindow.GetParent()?.MoveChild(_mailReadWindow, _mailReadWindow.GetParent().GetChildCount() - 1);
        Callable.From(_mailReadWindow.ResetSize).CallDeferred();
        RenderMailList();
        Net.I?.SendMailRead(mailId);
    }

    private void RenderMailAttachments(MailEntry mail)
    {
        ClearChildren(_mailReadAttachments);
        var count = mail.Items.Count;
        _mailReadAttachmentTitle.Visible = _mailReadAttachmentScroll.Visible = count > 0;
        if (count == 0) return;
        var claimed = mail.Attachments == MailAttachmentState.Claimed;
        _mailReadAttachmentTitle.Text = claimed ? $"Attachments ({count}, claimed)"
            : $"Attachments ({count}) - click one to claim just that";
        for (var index = 0; index < count; index++)
        {
            var attachment = mail.Items[index];
            var displayId = MailDisplayItemId(attachment);
            var done = claimed || attachment.Remaining == 0;
            var value = !done && attachment.Claimed > 0
                ? $"{attachment.Remaining:n0} of {attachment.Count:n0} left"
                : $"{attachment.Count:n0}";
            var row = QuestItemRow(displayId, QuestRewardName(displayId), value, done ? UiTheme.TextDim : UiTheme.GoldBright);
            _mailReadAttachments.AddChild(done ? row : MailClaimableRow(row, mail.Id, index));
        }
        var rows = Math.Min(count, MailAttachmentRowsVisible);
        _mailReadAttachmentScroll.CustomMinimumSize = new Vector2(0, rows * QuestRowIconSide + (rows - 1) * MailAttachmentRowGap);
    }

    private Control MailClaimableRow(Control row, int mailId, int index)
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
            Net.I.SendMailClaimAttachment(mailId, index);
        };
        frame.AddChild(row);
        return frame;
    }

    private static int MailDisplayItemId(MailAttachment attachment) => attachment.Kind switch
    {
        MailAttachmentKind.Gold => QuestData.CoinItemId,
        MailAttachmentKind.Experience => QuestData.ExpItemId,
        MailAttachmentKind.NationalPoints => QuestData.LadderPointItemId,
        _ => attachment.ItemId,
    };

    private void PaintMailActions(MailEntry mail)
    {
        _mailClaimBtn.Visible = mail.Attachments == MailAttachmentState.Pending;
        _mailDeleteBtn.Disabled = mail.Attachments == MailAttachmentState.Pending;
        _mailDeleteBtn.TooltipText = mail.Attachments == MailAttachmentState.Pending ? "Claim the attachments first" : "Delete this mail";
    }

    private void OnMailRead(int mailId, bool ok, string body)
    {
        if (mailId != _mailSelectedId) return;
        _mailReadBody.Text = ok ? body : "This mail is no longer available.";
        var mail = _mails.FirstOrDefault(m => m.Id == mailId);
        if (mail != null && !mail.Read)
        {
            mail.Read = true;
            RenderMailList();
        }
        Callable.From(_mailReadWindow.ResetSize).CallDeferred();
    }

    private void OnMailDeleteResult(int mailId, bool ok, string message)
    {
        _mailStatus.Text = ok ? "Mail deleted." : message;
        if (ok && mailId == _mailSelectedId)
        {
            _mailSelectedId = -1;
            _mailReadWindow.Visible = false;
        }
        Net.I.SendMailList();
    }

    private void OnMailClaimResult(int mailId, bool ok, string message)
    {
        _mailStatus.Text = ok ? "" : message;
        Net.I.SendMailList();
    }
}
