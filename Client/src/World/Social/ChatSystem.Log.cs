using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

internal sealed partial class ChatSystem
{
    private const int StoreMax = 500;
    private const int PublishedMax = 200;
    private const float BottomSlack = 4f;
    private const float PageScrollShare = 0.85f;
    private const int LineSpacing = 2;
    private const int LogBodyGap = 4;
    private const float PillLift = 4f;
    private const float LogScrollbarBottomInset = 5f;
    private const string PlayerMeta = "p:";
    private const string ItemMeta = "i:";
    private const string TimestampColour = "8d939b";
    private static readonly Color ShadowColour = new(0, 0, 0, 0.85f);

    private readonly record struct ChatEntry(
        ChatCategory Category, byte Type, WhisperSide Side, string Name, int Nation, bool Gm, string Text, string Bbcode)
    {
        public long Seq { get; init; }
        public DateTime Time { get; init; }

        public static ChatEntry Raw(ChatCategory category, string bbcode) =>
            new(category, 0, WhisperSide.None, "", 0, false, "", bbcode);

        public static ChatEntry Plain(ChatCategory category, byte type, string text) =>
            new(category, type, WhisperSide.None, "", 0, false, text, "");

        public static ChatEntry Player(byte type, string name, int nation, bool gm, string text) =>
            new(ChatCategories.Of(type, false), type, WhisperSide.None, name, nation, gm, text, "");

        public static ChatEntry Whisper(WhisperSide side, string name, int nation, bool gm, string text) =>
            new(ChatCategory.Whisper, WhisperChannel, side, name, nation, gm, text, "");
    }

    private readonly LinkedList<ChatEntry> _store = new();
    private readonly Queue<string> _published = new();
    private long _seq;
    private int _missed;
    private int _shown;
    private Button _jumpPill = null!;
    private Control _logBody = null!;
    private int _hoverItem;
    private bool _itemTipPinned;

    internal IReadOnlyList<string> History => _published.ToArray();

    private void BuildLogPanel()
    {
        _logPanel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _panelStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, ChatPrefs.Backgrounds[ChatPrefs.DefaultBackground]) };
        foreach (var s in new[] { "left", "right", "top", "bottom" }) _panelStyle.Set($"content_margin_{s}", 7f);
        _panelStyle.SetCornerRadiusAll(6);
        _panelStyle.SetBorderWidthAll(1);
        _panelStyle.BorderColor = new Color(UiTheme.Edge, 0.25f);
        _logPanel.AddThemeStyleboxOverride("panel", _panelStyle);
        _root.AddChild(_logPanel);

        _scroll = new HudLogText
        {
            ScrollbarOnLeft = false,
            ScrollbarBottomInset = Platform.TouchUi ? HudLogText.DefaultScrollbarBottomInset : LogScrollbarBottomInset,
            BbcodeEnabled = true,
            ScrollActive = true,
            ScrollFollowing = true,
            FitContent = false,
            SelectionEnabled = true,
            MetaUnderlined = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 110),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _scroll.AddThemeConstantOverride("line_separation", LineSpacing);
        _scroll.AddThemeColorOverride("font_shadow_color", ShadowColour);
        _scroll.AddThemeConstantOverride("shadow_offset_x", 1);
        _scroll.AddThemeConstantOverride("shadow_offset_y", 1);
        _scroll.MetaClicked += OnMetaClicked;
        _scroll.MetaHoverStarted += meta => _hoverItem = ItemOf(meta);
        _scroll.MetaHoverEnded += _ => LeaveItemLink();
        _scroll.GuiInput += OnLogInput;
        _logBody = new Control { CustomMinimumSize = _scroll.CustomMinimumSize };
        _logPanel.AddChild(_logBody);
        _logBody.AddChild(_scroll);
        _scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _scroll.GetVScrollBar().ValueChanged += _ => { if (AtBottom()) HideJump(); };

        BuildJumpPill();
    }

    private void BuildJumpPill()
    {
        _jumpPill = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            Visible = false,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1f, AnchorBottom = 1f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Begin,
            OffsetBottom = -PillLift,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        _jumpPill.AddThemeFontSizeOverride("font_size", 11);
        _jumpPill.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        _jumpPill.AddThemeColorOverride("font_hover_color", Godot.Colors.White);
        _jumpPill.AddThemeStyleboxOverride("normal", PillStyle(new Color(0.095f, 0.082f, 0.05f, 0.96f), new Color("#c7984b")));
        _jumpPill.AddThemeStyleboxOverride("hover", PillStyle(new Color(0.16f, 0.13f, 0.07f, 0.98f), UiTheme.GoldBright));
        _jumpPill.AddThemeStyleboxOverride("pressed", PillStyle(new Color(0.16f, 0.13f, 0.07f, 0.98f), UiTheme.GoldBright));
        _jumpPill.Pressed += JumpToLatest;
        _scroll.AddChild(_jumpPill);
    }

    private static StyleBoxFlat PillStyle(Color background, Color edge)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = edge };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(9);
        style.ContentMarginLeft = style.ContentMarginRight = 9;
        style.ContentMarginTop = style.ContentMarginBottom = 2;
        return style;
    }

    private bool AtBottom()
    {
        var bar = _scroll.GetVScrollBar();
        return bar.Value >= bar.MaxValue - bar.Page - BottomSlack;
    }

    private void JumpToLatest()
    {
        HideJump();
        _scroll.ScrollToLine(Math.Max(0, _scroll.GetLineCount() - 1));
    }

    private void HideJump()
    {
        _missed = 0;
        _jumpPill.Visible = false;
    }

    private void NoteMissed()
    {
        _missed++;
        _jumpPill.Text = _missed == 1 ? "1 new message ↓" : $"{_missed} new messages ↓";
        _jumpPill.Visible = true;
    }

    private void ScrollLog(int pages)
    {
        var bar = _scroll.GetVScrollBar();
        bar.Value = Mathf.Clamp(bar.Value + pages * bar.Page * PageScrollShare, 0, bar.MaxValue - bar.Page);
    }

    private ChatCategory CurrentFilter => _prefs.Filter(ChatTabs.All[_selectedTab]);

    private void AddEntry(ChatEntry entry)
    {
        entry = entry with { Seq = ++_seq, Time = DateTime.Now };
        bool atBottom = AtBottom();
        string bbcode = Render(entry);
        Publish(bbcode);

        _store.AddLast(entry);
        if (_store.Count > StoreMax)
        {
            var dropped = _store.First!.Value;
            _store.RemoveFirst();
            if ((dropped.Category & CurrentFilter) != 0 && _shown > 0)
            {
                for (int lines = Paragraphs(Render(dropped)); lines > 0; lines--)
                    _scroll.RemoveParagraph(0, true);
                _shown--;
            }
        }

        if ((entry.Category & CurrentFilter) != 0)
        {
            if (_shown > 0) _scroll.AppendText("\n");
            _scroll.AppendText(bbcode);
            _shown++;
            if (!atBottom) NoteMissed();
        }
        MarkUnread(entry.Category);
    }

    private void Publish(string bbcode)
    {
        _published.Enqueue(bbcode);
        while (_published.Count > PublishedMax) _published.Dequeue();
        PluginHost.Game.RaiseChatLine(bbcode);
        UpdateChatPeek(bbcode);
    }

    private static int Paragraphs(string bbcode)
    {
        int count = 1;
        foreach (char c in bbcode) if (c == '\n') count++;
        return count;
    }

    private void RebuildLog()
    {
        var filter = CurrentFilter;
        var text = new StringBuilder();
        _shown = 0;
        foreach (var entry in _store)
        {
            if ((entry.Category & filter) == 0) continue;
            if (_shown > 0) text.Append('\n');
            text.Append(Render(entry));
            _shown++;
        }
        _scroll.Clear();
        _scroll.AppendText(text.ToString());
        HideJump();
        Callable.From(() => _scroll.ScrollToLine(Math.Max(0, _scroll.GetLineCount() - 1))).CallDeferred();
    }

    private string Render(ChatEntry entry)
    {
        var line = new StringBuilder();
        if (entry.Bbcode.Length > 0)
        {
            if (_prefs.Timestamps) line.Append($"[color=#{TimestampColour}]{entry.Time:HH:mm}[/color] ");
            line.Append(entry.Bbcode);
            return line.ToString();
        }

        string hex = _colors.ForLine(entry.Type, entry.Side, entry.Nation).ToHtml(false);
        if (_prefs.Timestamps) line.Append($"[color=#{hex}]{entry.Time:HH:mm}[/color] ");
        if (entry.Name.Length == 0)
        {
            if (entry.Type is ChatType.Clan or ChatType.Shout) line.Append($"[color=#{hex}][lb]{ChanTag(entry.Type)}[rb][/color] ");
            line.Append(RenderText(entry.Text, hex));
            return line.ToString();
        }

        string tag = entry.Side switch
        {
            WhisperSide.Sent => "to",
            WhisperSide.Received or WhisperSide.ReceivedFromFriend => "from",
            _ => ChanTag(entry.Type),
        };
        if (tag.Length > 0) line.Append($"[color=#{hex}][lb]{tag}[rb][/color] ");
        line.Append(NameLink(entry.Name, hex));
        line.Append($"[color=#{hex}]: [/color]");
        line.Append(RenderText(entry.Text, hex));
        return line.ToString();
    }

    private static string NameLink(string name, string colour)
    {
        string coloured = $"[color=#{colour}]{BbCode.Esc(name)}[/color]";
        return name.IndexOfAny(new[] { '[', ']' }) >= 0 ? coloured : $"[url={PlayerMeta}{name}]{coloured}[/url]";
    }

    private static string RenderText(string text, string colour)
    {
        var line = new StringBuilder();
        foreach (var segment in ChatItemLink.Split(text))
        {
            if (segment.ItemId == 0)
            {
                line.Append($"[color=#{colour}]{BbCode.Esc(segment.Text)}[/color]");
                continue;
            }
            if (ItemData.Get(segment.ItemId) == null) continue;
            string grade = ItemGrade.Tint(segment.ItemId).ToHtml(false);
            line.Append($"[url={ItemMeta}{segment.ItemId}][color=#{grade}][lb]{BbCode.Esc(ItemData.DisplayName(segment.ItemId))}[rb][/color][/url]");
        }
        return line.ToString();
    }

    internal static string PlainText(string text)
    {
        var plain = new StringBuilder();
        foreach (var segment in ChatItemLink.Split(text))
            plain.Append(segment.ItemId == 0 ? segment.Text
                : ItemData.Get(segment.ItemId) == null ? "" : $"[{ItemData.DisplayName(segment.ItemId)}]");
        return plain.ToString();
    }

    private static int ItemOf(Variant meta)
    {
        string text = meta.AsString();
        return text.StartsWith(ItemMeta, StringComparison.Ordinal)
               && int.TryParse(text[ItemMeta.Length..], out int id) ? id : 0;
    }

    private void OnMetaClicked(Variant meta)
    {
        string text = meta.AsString();
        if (text.StartsWith(PlayerMeta, StringComparison.Ordinal))
        {
            string name = text[PlayerMeta.Length..];
            if (!IsSelf(name)) NameMenu?.Invoke(name, _scroll.GetGlobalMousePosition());
            return;
        }
        if (ItemOf(meta) is int id and > 0) PinItemTip(id);
    }

    private void OnLogInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } && _hoverItem > 0)
        {
            PinItemTip(_hoverItem);
            _scroll.AcceptEvent();
        }
    }

    private void PinItemTip(int itemId)
    {
        _itemTipPinned = true;
        ItemTipShow?.Invoke(itemId);
    }

    private void LeaveItemLink()
    {
        _hoverItem = 0;
        if (!_itemTipPinned) return;
        _itemTipPinned = false;
        ItemTipHide?.Invoke();
    }

    private void ClearLog()
    {
        _store.Clear();
        _scroll.Clear();
        _shown = 0;
        HideJump();
    }
}
