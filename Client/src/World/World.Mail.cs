using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _mailLayer = null!;
    private HudWindow _mailPanel = null!;
    private VBoxContainer _mailList = null!;
    private LineEdit _mailToEdit = null!, _mailSubjectEdit = null!, _mailGoldEdit = null!, _mailItemEdit = null!;
    private TextEdit _mailBodyEdit = null!;
    private Label _mailStatus = null!;
    private bool _mailShown;

    private void MailInit()
    {
        _mailLayer = new CanvasLayer { Layer = 74 };
        AddChild(_mailLayer);

        _mailPanel = new HudWindow("mail", "Mail", new Vector2(180, 110)) { Visible = false };
        _mailPanel.Closed += CloseMail;
        _mailLayer.AddChild(_mailPanel);
        var root = _mailPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var inboxHead = new HBoxContainer(); inboxHead.AddThemeConstantOverride("separation", 6);
        var inboxTitle = UiTheme.SectionTitle("Inbox");
        inboxTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        inboxHead.AddChild(inboxTitle);
        var refreshBtn = new Button { Text = "Refresh", FocusMode = Control.FocusModeEnum.None };
        refreshBtn.Pressed += () => Net.I.SendMailList();
        inboxHead.AddChild(refreshBtn);
        root.AddChild(inboxHead);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, 220), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _mailList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _mailList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_mailList);

        root.AddChild(new HSeparator());

        root.AddChild(UiTheme.SectionTitle("Compose"));

        _mailToEdit = ComposeRow(root, "To:", "recipient name", 220);
        _mailSubjectEdit = ComposeRow(root, "Subject:", "subject", 220);
        _mailSubjectEdit.MaxLength = Net.MailSubjectMax;

        var bodyLbl = HudStyle.Label(13); bodyLbl.Text = "Message:";
        root.AddChild(bodyLbl);
        _mailBodyEdit = new TextEdit
        {
            CustomMinimumSize = new Vector2(360, 80),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "message body (max 512 chars)",
        };
        root.AddChild(_mailBodyEdit);

        _mailGoldEdit = ComposeRow(root, "Gold:", "0", 120);
        _mailItemEdit = ComposeRow(root, "Item id:", "0 (optional)", 120);

        var sendBtn = new Button { Text = "Send Mail", FocusMode = Control.FocusModeEnum.None };
        sendBtn.Pressed += OnMailSendPressed;
        root.AddChild(sendBtn);

        _mailStatus = HudStyle.Label(13);
        root.AddChild(_mailStatus);

        Net.I.MailListEvent += OnMailList;
        Net.I.MailReadEvent += OnMailRead;
        Net.I.MailSendEvent += OnMailSend;
        Net.I.MailDeleteEvent += OnMailDelete;
    }

    private void MailDispose()
    {
        Net.I.MailListEvent -= OnMailList;
        Net.I.MailReadEvent -= OnMailRead;
        Net.I.MailSendEvent -= OnMailSend;
        Net.I.MailDeleteEvent -= OnMailDelete;
    }

    private static LineEdit ComposeRow(VBoxContainer parent, string label, string placeholder, int width)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
        var lbl = HudStyle.Label(13); lbl.Text = label; lbl.CustomMinimumSize = new Vector2(70, 0);
        row.AddChild(lbl);
        var edit = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(width, 0) };
        row.AddChild(edit);
        parent.AddChild(row);
        return edit;
    }

    private void ToggleMail()
    {
        if (_mailShown) { CloseMail(); return; }
        _mailPanel.Visible = true;
        _mailShown = true;
        SetMailStatus("", false);
        Net.I.SendMailList();
    }

    private void CloseMail()
    {
        if (!_mailShown) return;
        _mailShown = false;
        _mailPanel.Visible = false;
    }

    private void OnMailSendPressed()
    {
        string to = _mailToEdit.Text.Trim();
        string subject = _mailSubjectEdit.Text.Trim();
        string body = _mailBodyEdit.Text.Trim();
        if (to.Length < 2) { SetMailStatus("Enter a recipient name.", true); return; }
        if (subject.Length == 0) { SetMailStatus("Enter a subject.", true); return; }
        if (subject.Length > Net.MailSubjectMax) subject = subject[..Net.MailSubjectMax];
        if (body.Length > Net.MailBodyMax) body = body[..Net.MailBodyMax];
        int gold = ParseMailInt(_mailGoldEdit.Text);
        int itemId = ParseMailInt(_mailItemEdit.Text);
        Net.I.SendMailSend(to, subject, body, gold, itemId);
        SetMailStatus("Sending…", false);
    }

    private static int ParseMailInt(string text)
    {
        return int.TryParse(text.Trim(), out int v) && v > 0 ? v : 0;
    }

    private void OnMailList(List<MailEntry> list)
    {
        foreach (var c in _mailList.GetChildren()) c.QueueFree();
        if (list.Count == 0)
        {
            var e = HudStyle.Label(13); e.Text = "Your inbox is empty.";
            _mailList.AddChild(e);
            return;
        }
        foreach (var m in list)
        {
            int id = m.Id;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var vb = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            vb.AddThemeConstantOverride("separation", 1);
            row.AddChild(vb);

            var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8);
            string subjText = (m.Read ? "" : "● ") + (string.IsNullOrEmpty(m.Subject) ? "(no subject)" : m.Subject);
            var subj = UiTheme.Text(subjText, 13, m.Read ? UiTheme.TextLo : UiTheme.TextHi);
            subj.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            head.AddChild(subj);
            var from = UiTheme.Text("from " + m.Sender, 12, UiTheme.TextLo);
            head.AddChild(from);
            vb.AddChild(head);

            if (m.Gold > 0 || m.ItemId > 0)
            {
                string att = m.Gold > 0 ? $"Gold {m.Gold}" : "";
                if (m.ItemId > 0) att += (att.Length > 0 ? "   " : "") + $"Item {m.ItemId}";
                vb.AddChild(UiTheme.Text("Attached: " + att, 11, UiTheme.Gold));
            }

            var btnRow = new HBoxContainer(); btnRow.AddThemeConstantOverride("separation", 6);
            btnRow.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            var readBtn = new Button { Text = "Read", FocusMode = Control.FocusModeEnum.None };
            readBtn.Pressed += () => Net.I.SendMailRead(id);
            btnRow.AddChild(readBtn);
            var delBtn = new Button { Text = "Delete", FocusMode = Control.FocusModeEnum.None };
            delBtn.Pressed += () => Net.I.SendMailDelete(id);
            btnRow.AddChild(delBtn);
            vb.AddChild(btnRow);

            _mailList.AddChild(row);
        }
    }

    private void OnMailRead(int mailId, bool ok)
    {
        SetMailStatus(ok ? "Mail read." : "Couldn't open that mail.", !ok);
        if (ok) Net.I.SendMailList();
    }

    private void OnMailSend(bool ok)
    {
        if (ok)
        {
            SetMailStatus("Mail sent.", false);
            _mailToEdit.Text = "";
            _mailSubjectEdit.Text = "";
            _mailBodyEdit.Text = "";
            _mailGoldEdit.Text = "";
            _mailItemEdit.Text = "";
        }
        else SetMailStatus("Couldn't send (unknown recipient or bad input).", true);
    }

    private void OnMailDelete(int mailId, bool ok)
    {
        SetMailStatus(ok ? "Mail deleted." : "Couldn't delete that mail.", !ok);
        if (ok) Net.I.SendMailList();
    }

    private void SetMailStatus(string text, bool warn)
    {
        _mailStatus.Text = text;
        _mailStatus.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }
}
