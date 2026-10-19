using System.Text;
using Godot;

namespace LibreKO.Features.Social;

internal sealed partial class ChatSystem
{
    private const int StoreMax = 500;
    private const int PublishedMax = 200;
    private const int TrimBlock = 60;
    private const float BottomSlack = 4f;
    private const float PageScrollShare = 0.85f;
    private const int LineSpacing = 2;
    private const int LogBodyGap = 4;
    private const float PillLift = 4f;
    private const float LogScrollbarBottomInset = 5f;
    private const string PlayerMeta = "p:";
    private const string ItemMeta = "i:";
    private const string TimestampColour = "8d939b";
    private const string RankSymbolSize = "48x12";
    private static readonly Color ShadowColour = new(0, 0, 0, 0.85f);

    private readonly record struct ChatEntry(
        ChatCategory Category, ChatType Type, WhisperSide Side, string Name, int Nation, bool Gm, string Text, string Bbcode)
    {
        public long Seq { get; init; }
        public DateTime Time { get; init; }
        public int Rank { get; init; } = NationRankAura.Unranked;
        public bool Masked { get; init; }

        public static ChatEntry Raw(ChatCategory category, string bbcode) =>
            new(category, default, WhisperSide.None, "", 0, false, "", bbcode);

        public static ChatEntry Plain(ChatCategory category, byte type, string text) =>
            new(category, (ChatType)type, WhisperSide.None, "", 0, false, text, "");

        public static ChatEntry Player(ChatType type, string name, int nation, bool gm, string text) =>
            new(ChatCategories.Of(type, false), type, WhisperSide.None, name, nation, gm, text, "");

        public static ChatEntry Whisper(WhisperSide side, string name, int nation, bool gm, string text) =>
            new(ChatCategory.Whisper, (ChatType)WhisperChannel, side, name, nation, gm, text, "");
    }

    private readonly LinkedList<ChatEntry> store = new();
    private readonly Queue<GameChatLine> published = new();
    private long seq;
    private int missed;
    private int shownLines;
    private readonly StringBuilder pendingLog = new();
    private int pendingShown;
    private int pendingTrim;
    private bool logFlushQueued;
    private bool pendingAtBottom;
    private Button jumpPill = null!;
    private Control logBody = null!;
    private int hoverItem;
    private bool itemTipPinned;

    internal IReadOnlyList<string> History => published.Select(line => line.BbCode).ToArray();
    internal IReadOnlyList<GameChatLine> Lines => published.ToArray();

    private void BuildLogPanel()
    {
        logPanel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        panelStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, ChatPrefs.Backgrounds[ChatPrefs.DefaultBackground]) };
        foreach (var s in new[] { "left", "right", "top", "bottom" }) panelStyle.Set($"content_margin_{s}", 7f);
        panelStyle.SetCornerRadiusAll(6);
        panelStyle.SetBorderWidthAll(1);
        panelStyle.BorderColor = new Color(UiTheme.Edge, 0.25f);
        logPanel.AddThemeStyleboxOverride("panel", panelStyle);
        root.AddChild(logPanel);

        scroll = new HudLogText
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
        scroll.AddThemeConstantOverride("line_separation", LineSpacing);
        scroll.AddThemeColorOverride("font_shadow_color", ShadowColour);
        scroll.AddThemeConstantOverride("shadow_offset_x", 1);
        scroll.AddThemeConstantOverride("shadow_offset_y", 1);
        scroll.MetaClicked += OnMetaClicked;
        scroll.MetaHoverStarted += meta => hoverItem = ItemOf(meta);
        scroll.MetaHoverEnded += _ => LeaveItemLink();
        scroll.GuiInput += OnLogInput;
        logBody = new Control { CustomMinimumSize = scroll.CustomMinimumSize };
        logPanel.AddChild(logBody);
        logBody.AddChild(scroll);
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        scroll.GetVScrollBar().ValueChanged += _ => { if (AtBottom()) HideJump(); };

        BuildJumpPill();
    }

    private void BuildJumpPill()
    {
        jumpPill = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            Visible = false,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1f, AnchorBottom = 1f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Begin,
            OffsetBottom = -PillLift,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        jumpPill.AddThemeFontSizeOverride("font_size", 11);
        jumpPill.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        jumpPill.AddThemeColorOverride("font_hover_color", Godot.Colors.White);
        jumpPill.AddThemeStyleboxOverride("normal", PillStyle(new Color(0.095f, 0.082f, 0.05f, 0.96f), new Color("#c7984b")));
        jumpPill.AddThemeStyleboxOverride("hover", PillStyle(new Color(0.16f, 0.13f, 0.07f, 0.98f), UiTheme.GoldBright));
        jumpPill.AddThemeStyleboxOverride("pressed", PillStyle(new Color(0.16f, 0.13f, 0.07f, 0.98f), UiTheme.GoldBright));
        jumpPill.Pressed += JumpToLatest;
        scroll.AddChild(jumpPill);
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
        var bar = scroll.GetVScrollBar();
        return bar.Value >= bar.MaxValue - bar.Page - BottomSlack;
    }

    private void JumpToLatest()
    {
        HideJump();
        scroll.ScrollToLine(Math.Max(0, scroll.GetLineCount() - 1));
    }

    private void HideJump()
    {
        missed = 0;
        jumpPill.Visible = false;
    }

    private void NoteMissed(int count)
    {
        missed += count;
        jumpPill.Text = missed == 1 ? "1 new message ↓" : $"{missed} new messages ↓";
        jumpPill.Visible = true;
    }

    private void ScrollLog(int pages)
    {
        var bar = scroll.GetVScrollBar();
        bar.Value = Mathf.Clamp(bar.Value + pages * bar.Page * PageScrollShare, 0, bar.MaxValue - bar.Page);
    }

    private ChatCategory CurrentFilter => prefs.Filter(ChatTabs.All[selectedTab]);

    private void AddEntry(ChatEntry entry)
    {
        entry = entry with { Seq = ++seq, Time = DateTime.Now };
        string bbcode = Render(entry);
        Publish(new GameChatLine(bbcode, entry.Category));

        store.AddLast(entry);
        if (store.Count > StoreMax)
        {
            var dropped = store.First!.Value;
            store.RemoveFirst();
            if ((dropped.Category & CurrentFilter) != 0 && shownLines > 0)
            {
                pendingTrim += Paragraphs(Render(dropped));
                shownLines--;
            }
        }

        if ((entry.Category & CurrentFilter) != 0)
        {
            if (shownLines > 0) pendingLog.Append('\n');
            if (pendingShown == 0) pendingAtBottom = !scroll.IsInsideTree() || AtBottom();
            pendingLog.Append(bbcode);
            shownLines++;
            pendingShown++;
            QueueLogFlush();
        }
        MarkUnread(entry.Category);
    }

    private void QueueLogFlush()
    {
        if (logFlushQueued) return;
        logFlushQueued = true;
        Callable.From(FlushLog).CallDeferred();
    }

    private void FlushLog()
    {
        logFlushQueued = false;
        if (!GodotObject.IsInstanceValid(scroll)) return;
        if (pendingTrim >= TrimBlock && !TrimLog(pendingTrim))
        {
            RebuildLog();
            return;
        }
        if (pendingLog.Length == 0) return;
        scroll.AppendText(pendingLog.ToString());
        if (!pendingAtBottom) NoteMissed(pendingShown);
        ClearPendingLog(keepTrim: true);
    }

    private bool TrimLog(int paragraphs)
    {
        if (paragraphs >= scroll.GetParagraphCount()) return false;
        for (; paragraphs > 0; paragraphs--)
            if (!scroll.RemoveParagraph(0, true)) return false;
        pendingTrim = 0;
        return true;
    }

    private void ClearPendingLog(bool keepTrim)
    {
        pendingLog.Clear();
        pendingShown = 0;
        if (!keepTrim) pendingTrim = 0;
    }

    private void Publish(GameChatLine line)
    {
        published.Enqueue(line);
        while (published.Count > PublishedMax) published.Dequeue();
        PluginHost.Game.RaiseChatLine(line);
        UpdateChatPeek(line.BbCode);
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
        ClearPendingLog(keepTrim: false);
        shownLines = 0;
        foreach (var entry in store)
        {
            if ((entry.Category & filter) == 0) continue;
            if (shownLines > 0) text.Append('\n');
            text.Append(Render(entry));
            shownLines++;
        }
        scroll.Clear();
        scroll.AppendText(text.ToString());
        HideJump();
        Callable.From(() => scroll.ScrollToLine(Math.Max(0, scroll.GetLineCount() - 1))).CallDeferred();
    }

    private string Render(ChatEntry entry)
    {
        var line = new StringBuilder();
        if (entry.Bbcode.Length > 0)
        {
            if (prefs.Timestamps) line.Append($"[color=#{TimestampColour}]{entry.Time:HH:mm}[/color] ");
            line.Append(entry.Bbcode);
            return line.ToString();
        }

        string hex = colors.ForLine(entry.Type, entry.Side, entry.Nation).ToHtml(false);
        if (prefs.Timestamps) line.Append($"[color=#{hex}]{entry.Time:HH:mm}[/color] ");
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
        if (RankSymbol.TexturePath(entry.Rank) is { } symbol) line.Append($"[img={RankSymbolSize}]{symbol}[/img] ");
        line.Append(entry.Masked ? $"[color=#{hex}]{BbCode.Esc(entry.Name)}[/color]" : NameLink(entry.Name, hex));
        line.Append($"[color=#{hex}]: [/color]");
        line.Append(RenderText(entry.Text, hex));
        return line.ToString();
    }

    private static string NameLink(string name, string colour)
    {
        string coloured = $"[color=#{colour}]{BbCode.Esc(name)}[/color]";
        return name.IndexOfAny(new[] { '[', ']' }) >= 0 ? coloured : $"[url={PlayerMeta}{name}]{coloured}[/url]";
    }

    internal static string RenderText(string text, string colour)
    {
        var line = new StringBuilder();
        foreach (var segment in ChatItemLink.Split(text))
        {
            if (segment.ItemId == 0)
            {
                foreach (var part in Emoticons.Split(segment.Text))
                    line.Append(part.Emoticon > 0 && Emoticons.Get(part.Emoticon) is { } emoticon
                        ? EmoticonPicker.InlineImage(emoticon)
                        : $"[color=#{colour}]{BbCode.Esc(part.Text)}[/color]");
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

    internal static int ItemOf(Variant meta)
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
            if (!IsSelf(name)) NameMenuRequested?.Invoke(name, scroll.GetGlobalMousePosition());
            return;
        }
        if (ItemOf(meta) is int id and > 0) PinItemTip(id);
    }

    private void OnLogInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } && hoverItem > 0)
        {
            PinItemTip(hoverItem);
            scroll.AcceptEvent();
        }
    }

    private void PinItemTip(int itemId)
    {
        itemTipPinned = true;
        ItemTipRequested?.Invoke(itemId);
    }

    private void LeaveItemLink()
    {
        hoverItem = 0;
        if (!itemTipPinned) return;
        itemTipPinned = false;
        ItemTipDismissed?.Invoke();
    }

    private void ClearLog()
    {
        store.Clear();
        scroll.Clear();
        ClearPendingLog(keepTrim: false);
        shownLines = 0;
        HideJump();
    }
}
