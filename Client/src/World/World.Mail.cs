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
    private const int MailActionsTopPadding = 8;
    private const int MailLayerIndex = 71;
    private const string MailUnreadGlyph = "●";
    private const string MailReadGlyph = "○";

    private CanvasLayer _mailLayer = null!;
    private HudWindow _mailWindow = null!;
    private VBoxContainer _mailList = null!;
    private Label _mailUnreadPill = null!;
    private CheckButton _mailUnreadOnly = null!;
    private Label _mailStatus = null!;
    private HudWindow _mailReadWindow = null!;
    private Label _mailReadSubject = null!;
    private Label _mailReadMeta = null!;
    private Label _mailReadBody = null!;
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
        _mailReadAttachments = new VBoxContainer();
        _mailReadAttachments.AddThemeConstantOverride("separation", 3);
        pane.AddChild(_mailReadAttachments);

        var actionsMargin = new MarginContainer();
        actionsMargin.AddThemeConstantOverride("margin_top", MailActionsTopPadding);
        pane.AddChild(actionsMargin);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        actionsMargin.AddChild(actions);
        _mailClaimBtn = UiTheme.ActionButton("Claim attachments", "Move the attached items and coins into your inventory");
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
        _mailWindow.Visible = true;
        _mailStatus.Text = "";
        Net.I.SendMailList();
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
            PaintMailActions(mails.First(m => m.Id == _mailSelectedId));
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
        var panel = UiTheme.RowPanel(mail.Id == _mailSelectedId, mail.Read);
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
        if (mail.Attachments == MailAttachmentState.Pending)
        {
            var gift = new TextureRect
            {
                Texture = UiIcons.Get("system/gift"),
                CustomMinimumSize = new Vector2(16, 16),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = UiTheme.GoldBright,
                TooltipText = "Attachments waiting to be claimed",
            };
            row.AddChild(gift);
        }
        row.AddChild(UiTheme.Text(MailDate(mail.SentAt), 11, UiTheme.TextDim));

        var mailId = mail.Id;
        panel.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                SelectMail(mailId);
        };
        return panel;
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
        if (mail.Items.Count == 0) return;
        _mailReadAttachments.AddChild(UiTheme.SectionTitle(mail.Attachments == MailAttachmentState.Claimed ? "Attachments (claimed)" : "Attachments"));
        foreach (var attachment in mail.Items)
        {
            var displayId = MailDisplayItemId(attachment);
            _mailReadAttachments.AddChild(QuestItemRow(displayId, QuestRewardName(displayId), $"{attachment.Count:n0}",
                mail.Attachments == MailAttachmentState.Claimed ? UiTheme.TextDim : UiTheme.GoldBright));
        }
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
        _mailStatus.Text = message;
        Net.I.SendMailList();
    }
}
