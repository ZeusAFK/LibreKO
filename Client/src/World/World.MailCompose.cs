using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int MailComposeWidth = 400;
    private const int MailComposeBodyHeight = 110;
    private const int MailComposeGap = 10;
    private const int MailAttachRowGap = 8;
    private const int MailAttachAreaHeight = (int)(Net.MailItemAttachmentsMax * QuestRowIconSide + (Net.MailItemAttachmentsMax - 1) * MailAttachRowGap) + 20;
    private const int MailRecipientSuggestions = 8;
    private const int MailUntradeableRace = 20;
    private const int MailNoTradeItemMin = 900000001;
    private const int MailNoTradeItemMax = 1_000_000_000;
    private const long MailGoldMax = int.MaxValue;
    private const int MailBodyWarnRemaining = 40;
    private const double MailSuggestDebounceSeconds = 0.25;

    private HudWindow _mailComposeWindow = null!;
    private LineEdit _mailTo = null!;
    private VBoxContainer _mailToSuggest = null!;
    private Godot.Timer _mailToDebounce = null!;
    private LineEdit _mailSubject = null!;
    private TextEdit _mailBody = null!;
    private Label _mailBodyRemaining = null!;
    private MoneyEdit _mailGold = null!;
    private Label _mailAttachTitle = null!;
    private MailDropZone _mailDropZone = null!;
    private VBoxContainer _mailAttachRows = null!;
    private Label _mailComposeStatus = null!;
    private Button _mailSendBtn = null!;

    private readonly SortedSet<string> _mailContacts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(int Slot, int Count)> _mailAttachments = [];
    private bool _mailComposeShown;

    private void BuildMailComposeWindow()
    {
        _mailComposeWindow = new HudWindow("mailcompose", "New mail", new Vector2(640, 110), bodyMinWidth: MailComposeWidth) { Visible = false };
        _mailComposeWindow.Closed += () => _mailComposeShown = false;
        _mailLayer.AddChild(_mailComposeWindow);

        var body = _mailComposeWindow.Body;
        body.AddThemeConstantOverride("separation", 6);

        body.AddChild(UiTheme.Text("To", 12, UiTheme.TextLo));
        _mailTo = new LineEdit { PlaceholderText = "character name", MaxLength = 20 };
        _mailTo.TextChanged += _ => _mailToDebounce.Start();
        _mailTo.FocusEntered += () => _mailToDebounce.Start();
        _mailTo.FocusExited += () => Callable.From(() => { if (!_mailTo.HasFocus()) _mailToSuggest.Visible = false; }).CallDeferred();
        body.AddChild(_mailTo);
        _mailToDebounce = new Godot.Timer { WaitTime = MailSuggestDebounceSeconds, OneShot = true };
        _mailToDebounce.Timeout += RefreshMailRecipientSuggestions;
        _mailTo.AddChild(_mailToDebounce);
        _mailToSuggest = new VBoxContainer { Visible = false };
        _mailToSuggest.AddThemeConstantOverride("separation", 2);
        body.AddChild(_mailToSuggest);

        body.AddChild(UiTheme.Text("Subject", 12, UiTheme.TextLo));
        _mailSubject = new LineEdit { PlaceholderText = "subject", MaxLength = Net.MailSubjectMax };
        body.AddChild(_mailSubject);

        var messageHead = new HBoxContainer();
        body.AddChild(messageHead);
        var messageLabel = UiTheme.Text("Message", 12, UiTheme.TextLo);
        messageLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        messageHead.AddChild(messageLabel);
        _mailBodyRemaining = UiTheme.Text(Net.MailBodyMax.ToString(), 11, UiTheme.TextDim);
        messageHead.AddChild(_mailBodyRemaining);
        _mailBody = new TextEdit
        {
            CustomMinimumSize = new Vector2(MailComposeWidth, MailComposeBodyHeight),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "write your message",
        };
        var inputBox = new StyleBoxFlat { BgColor = new Color(0.04f, 0.03f, 0.02f, 0.9f), BorderColor = new Color(UiTheme.Edge, 0.7f) };
        inputBox.SetBorderWidthAll(1);
        inputBox.SetCornerRadiusAll(4);
        inputBox.SetContentMarginAll(6);
        var inputFocus = (StyleBoxFlat)inputBox.Duplicate();
        inputFocus.BorderColor = new Color(UiTheme.Gold, 0.9f);
        _mailBody.AddThemeStyleboxOverride("normal", inputBox);
        _mailBody.AddThemeStyleboxOverride("focus", inputFocus);
        _mailBody.AddThemeStyleboxOverride("read_only", inputBox);
        _mailBody.AddThemeColorOverride("font_color", UiTheme.TextHi);
        _mailBody.AddThemeColorOverride("font_placeholder_color", new Color(UiTheme.TextLo, 0.6f));
        _mailBody.AddThemeColorOverride("caret_color", UiTheme.Gold);
        _mailBody.TextChanged += OnMailBodyChanged;
        body.AddChild(_mailBody);

        var goldRow = new HBoxContainer();
        goldRow.AddThemeConstantOverride("separation", 8);
        body.AddChild(MailComposeGapAbove(goldRow));
        goldRow.AddChild(new TextureRect
        {
            Texture = UiIcons.Get("system/coins"),
            CustomMinimumSize = new Vector2(16, 16),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Modulate = UiTheme.GoldBright,
        });
        goldRow.AddChild(UiTheme.Text("Coins", 12, UiTheme.TextLo));
        _mailGold = new MoneyEdit(MailGoldMax, 160);
        goldRow.AddChild(_mailGold);

        _mailAttachTitle = UiTheme.Text("", 13, UiTheme.Gold);
        body.AddChild(MailComposeGapAbove(_mailAttachTitle));
        _mailDropZone = new MailDropZone
        {
            OnDropItem = AttachMailItem,
            CanAccept = CanAttachMailSlot,
        };
        body.AddChild(_mailDropZone);
        _mailAttachRows = _mailDropZone.Rows;

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        body.AddChild(MailComposeGapAbove(actions));
        _mailSendBtn = UiTheme.ActionButton("Send", "Send the mail");
        _mailSendBtn.Pressed += SendComposedMail;
        actions.AddChild(_mailSendBtn);
        var cancel = UiTheme.SmallButton("Cancel", "Discard this mail");
        cancel.Pressed += CloseMailCompose;
        actions.AddChild(cancel);

        _mailComposeStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        _mailComposeStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_mailComposeStatus);

        RenderMailAttachments();
    }

    private static MarginContainer MailComposeGapAbove(Control content)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", MailComposeGap);
        margin.AddChild(content);
        return margin;
    }

    private void OnMailBodyChanged()
    {
        if (_mailBody.Text.Length > Net.MailBodyMax)
        {
            _mailBody.Text = _mailBody.Text[..Net.MailBodyMax];
            _mailBody.SetCaretLine(_mailBody.GetLineCount() - 1);
            _mailBody.SetCaretColumn(_mailBody.GetLine(_mailBody.GetLineCount() - 1).Length);
        }
        var remaining = Net.MailBodyMax - _mailBody.Text.Length;
        _mailBodyRemaining.Text = remaining.ToString();
        _mailBodyRemaining.AddThemeColorOverride("font_color", remaining <= 0 ? UiTheme.Bad : remaining < MailBodyWarnRemaining ? UiTheme.Warning : UiTheme.TextDim);
    }

    private void OpenMailCompose()
    {
        _mailComposeShown = true;
        _mailComposeWindow.Visible = true;
        _mailComposeWindow.GetParent()?.MoveChild(_mailComposeWindow, _mailComposeWindow.GetParent().GetChildCount() - 1);
        _mailComposeStatus.Text = "";
        _mailSendBtn.Disabled = false;
        Net.I.SendFriendListRequest();
        Net.I.SendClanMembersRequest();
        _mailTo.GrabFocus();
    }

    private void CloseMailCompose()
    {
        _mailComposeShown = false;
        _mailComposeWindow.Visible = false;
        _mailToSuggest.Visible = false;
        ResetMailCompose();
    }

    private void ResetMailCompose()
    {
        _mailTo.Text = "";
        _mailSubject.Text = "";
        _mailBody.Text = "";
        _mailGold.Value = 0;
        _mailAttachments.Clear();
        OnMailBodyChanged();
        RenderMailAttachments();
    }

    private void OnMailComposeFriends(List<FriendEntry> friends)
    {
        foreach (var friend in friends)
            if (!string.IsNullOrEmpty(friend.Name)) _mailContacts.Add(friend.Name);
    }

    private void OnMailComposeClanMembers(List<ClanMember> members)
    {
        foreach (var member in members)
            if (!string.IsNullOrEmpty(member.Name)) _mailContacts.Add(member.Name);
    }

    private void RefreshMailRecipientSuggestions()
    {
        if (!_mailComposeShown) return;
        var typed = _mailTo.Text.Trim();
        var me = Net.I?.LastEnter.Name ?? "";
        var matches = _mailContacts
            .Where(name => !string.Equals(name, me, StringComparison.OrdinalIgnoreCase))
            .Where(name => typed.Length == 0 || name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Where(name => !string.Equals(name, typed, StringComparison.OrdinalIgnoreCase))
            .Take(MailRecipientSuggestions)
            .ToList();

        ClearChildren(_mailToSuggest);
        _mailToSuggest.Visible = matches.Count > 0 && _mailTo.HasFocus();
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
                _mailTo.Text = chosen;
                _mailTo.CaretColumn = chosen.Length;
                _mailToSuggest.Visible = false;
                _mailSubject.GrabFocus();
            };
            _mailToSuggest.AddChild(pick);
        }
        Callable.From(_mailComposeWindow.ResetSize).CallDeferred();
    }

    private static bool MailItemTradable(ItemSlot slot)
    {
        if (slot.IsEmpty) return false;
        var data = ItemData.Get(slot.ItemId);
        if (data == null || data.Race == MailUntradeableRace) return false;
        if (slot.ItemId >= MailNoTradeItemMin && slot.ItemId < MailNoTradeItemMax) return false;
        return slot.State is not (ItemFlag.Rented or ItemFlag.CharacterSeal or ItemFlag.Duplicate or ItemFlag.Sealed or ItemFlag.Bound);
    }

    private bool CanAttachMailSlot(int abs) =>
        Inv.IsGridSlot(abs) && abs < Inv.Length
        && _mailAttachments.Count < Net.MailItemAttachmentsMax
        && !_mailAttachments.Any(a => a.Slot == abs);

    private void AttachMailItem(int abs)
    {
        if (!CanAttachMailSlot(abs)) return;
        var slot = Inv[abs];
        if (!MailItemTradable(slot))
        {
            _mailComposeStatus.Text = $"{ItemData.DisplayName(slot.ItemId)} cannot be traded, so it cannot be mailed.";
            return;
        }
        _mailComposeStatus.Text = "";
        _mailAttachments.Add((abs, slot.Count));
        RenderMailAttachments();
    }

    private void RenderMailAttachments()
    {
        _mailAttachTitle.Text = $"Attachments ({_mailAttachments.Count} / {Net.MailItemAttachmentsMax})";
        ClearChildren(_mailAttachRows);
        _mailDropZone.SetHintVisible(_mailAttachments.Count < Net.MailItemAttachmentsMax);
        for (var i = 0; i < _mailAttachments.Count; i++)
        {
            var index = i;
            var (abs, count) = _mailAttachments[i];
            var slot = Inv[abs];
            var stackable = (ItemData.Get(slot.ItemId)?.Countable ?? 0) != 0;
            var row = QuestValueRow(ItemData.DisplayName(slot.ItemId), stackable && slot.Count == 1 ? "1" : "", UiTheme.GoldBright);
            var nameLabel = row.GetChild<Label>(0);
            nameLabel.AutowrapMode = TextServer.AutowrapMode.Off;
            nameLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            var icon = new TextureRect
            {
                Texture = ItemData.Icon(slot.ItemId),
                CustomMinimumSize = new Vector2(QuestRowIconSide, QuestRowIconSide),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            row.AddChild(icon);
            row.MoveChild(icon, 0);
            if (slot.Count > 1)
            {
                var spin = UiTheme.NumberBox(1, slot.Count, 1, 72, 11);
                spin.Value = count;
                spin.ValueChanged += value => _mailAttachments[index] = (abs, (int)value);
                row.AddChild(spin);
            }
            var remove = UiTheme.IconButton(UiIcons.Get("system/close"), "Remove");
            remove.Pressed += () => { _mailAttachments.RemoveAt(index); RenderMailAttachments(); };
            row.AddChild(remove);
            row.MouseFilter = Control.MouseFilterEnum.Pass;
            row.MouseEntered += () => ShowItemTooltip(abs, Inv[abs]);
            row.MouseExited += HideItemTooltip;
            _mailAttachRows.AddChild(row);
        }
        Callable.From(_mailComposeWindow.ResetSize).CallDeferred();
    }

    private void SendComposedMail()
    {
        var to = _mailTo.Text.Trim();
        var subject = _mailSubject.Text.Trim();
        if (to.Length == 0 || subject.Length == 0)
        {
            _mailComposeStatus.Text = "A recipient and a subject are required.";
            return;
        }

        var gold = (int)Math.Clamp(_mailGold.Value, 0, MailGoldMax);
        if (gold > Sheet.Gold)
        {
            _mailComposeStatus.Text = "You do not carry that much gold.";
            return;
        }

        var items = _mailAttachments
            .Where(a => MailItemTradable(Inv[a.Slot]))
            .Select(a => new MailItemPick((byte)a.Slot, (ushort)Math.Clamp(a.Count, 1, Inv[a.Slot].Count)))
            .ToList();

        _mailSendBtn.Disabled = true;
        _mailComposeStatus.Text = "Sending…";
        Net.I.SendMailSend(to, subject, _mailBody.Text, gold, items);
    }

    private void OnMailSendResult(bool ok, string message)
    {
        if (ok)
        {
            _mailStatus.Text = message;
            CloseMailCompose();
            Net.I.SendMailList();
            return;
        }
        _mailSendBtn.Disabled = false;
        _mailComposeStatus.Text = message;
    }

    private partial class MailDropZone : PanelContainer
    {
        public Action<int>? OnDropItem;
        public Func<int, bool>? CanAccept;
        public VBoxContainer Rows { get; }
        private readonly Label _hint;
        private readonly StyleBox _idle;
        private readonly StyleBox _hot;

        public MailDropZone()
        {
            _idle = UiTheme.Inset();
            var hot = (StyleBoxFlat)UiTheme.Inset();
            hot.BorderColor = UiTheme.GoldBright;
            hot.SetBorderWidthAll(1);
            _hot = hot;
            AddThemeStyleboxOverride("panel", _idle);
            MouseFilter = MouseFilterEnum.Stop;
            CustomMinimumSize = new Vector2(MailComposeWidth, MailAttachAreaHeight);

            var stack = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Pass,
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
            };
            stack.AddThemeConstantOverride("separation", MailAttachRowGap);
            AddChild(stack);
            Rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
            Rows.AddThemeConstantOverride("separation", MailAttachRowGap);
            stack.AddChild(Rows);
            _hint = UiTheme.Text("Drag items here from your inventory", 12, UiTheme.TextLo, HorizontalAlignment.Center);
            _hint.VerticalAlignment = VerticalAlignment.Center;
            _hint.CustomMinimumSize = new Vector2(0, QuestRowIconSide);
            _hint.MouseFilter = MouseFilterEnum.Ignore;
            stack.AddChild(_hint);
            MouseExited += () => AddThemeStyleboxOverride("panel", _idle);
        }

        public void SetHintVisible(bool visible) => _hint.Visible = visible;

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (data.VariantType != Variant.Type.Dictionary) return false;
            var d = data.AsGodotDictionary();
            if (!d.ContainsKey("invFrom")) return false;
            var ok = CanAccept?.Invoke(d["invFrom"].AsInt32()) ?? false;
            AddThemeStyleboxOverride("panel", ok ? _hot : _idle);
            return ok;
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            AddThemeStyleboxOverride("panel", _idle);
            OnDropItem?.Invoke(data.AsGodotDictionary()["invFrom"].AsInt32());
        }
    }
}
