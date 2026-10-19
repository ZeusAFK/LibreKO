using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Features.Social;

namespace LibreKO;

public sealed partial class ChatThreadView : ScrollContainer
{
    private const int FontSize = 13;
    private const int NameFontSize = 12;
    private const int SmallFontSize = 10;
    private const float BottomSlack = 8f;
    private const float NoteWidth = 214f;
    private const float WaveHeight = 26f;
    private const int NoteGap = 8;
    private const float MineRestAlpha = 0.35f;
    private const float TheirRestAlpha = 0.45f;
    private const double NoteRefreshSeconds = 0.1;
    private const int ReactSlotSize = 22;
    private const int ReactIconSize = 14;
    private const int PillOverlap = 4;
    private const int PillInset = 8;
    private const int PillGlyphSize = 15;
    private const int PillCountSize = 11;
    private const int RowGap = 2;
    private static readonly Color MineBubble = new(0.0f, 0.36f, 0.29f, 0.94f);
    private static readonly Color MineEdge = new(0.16f, 0.52f, 0.43f, 0.9f);
    private static readonly Color TheirBubble = new(0.165f, 0.165f, 0.185f, 0.95f);
    private static readonly Color TheirEdge = new(0.32f, 0.31f, 0.34f, 0.8f);
    private static readonly Color MineText = new("e9f5f1");
    private static readonly Color TimeColour = new(0.72f, 0.74f, 0.74f, 0.78f);
    private static readonly Color LoadingTint = new(1f, 1f, 1f, 0.45f);
    private static readonly Color PillFill = new(0.13f, 0.135f, 0.15f, 0.98f);
    private static readonly Color PillEdge = new(0.06f, 0.06f, 0.07f, 0.95f);
    private static readonly Color PillHover = new(0.2f, 0.205f, 0.225f, 0.98f);
    private static readonly Color ReactHover = new(1f, 1f, 1f, 0.12f);

    private sealed class PostRow
    {
        public Control Root = null!;
        public RoomPost Post = null!;
        public Button? React;
        public MarginContainer? PillHolder;
        public PanelContainer? Pill;
        public HBoxContainer? PillGlyphs;
        public Label? PillCount;
    }

    private sealed class NoteRow
    {
        public Button Play = null!;
        public NoteDownloadRing Ring = null!;
        public VoiceWaveformView Wave = null!;
        public Label Clock = null!;
        public RoomPost Post = null!;
    }

    private readonly Func<int> self;
    private readonly VBoxContainer log;
    private readonly Button older;
    private readonly Dictionary<RoomPost, PostRow> rows = new(ReferenceEqualityComparer.Instance);
    private readonly List<NoteRow> notes = [];
    private RoomPost? firstRendered;
    private RoomPost? newest;
    private int rendered;
    private bool stickBottom = true;
    private PostRow? hovered;
    private double noteClock;

    public RoomTimeline? Timeline { get; set; }
    public float BubbleMax { get; set; } = 268f;
    public bool ShowNames { get; init; }
    public RoomPost? Pinned { get; set; }

    public event Action? OlderRequested;
    public event Action<string, Vector2>? NameClicked;
    public event Action<int>? ItemClicked;
    public event Action? ItemHoverEnded;
    public event Action<RoomPost, Control>? ReactRequested;
    public event Action<RoomPost, Control>? ReactionsClicked;
    public event Action? OthersPosted;

    public ChatThreadView(Func<int> self)
    {
        this.self = self;
        HorizontalScrollMode = ScrollMode.Disabled;
        AddThemeStyleboxOverride("panel", UiTheme.Inset());
        UiTheme.ThinScrollbar(GetVScrollBar());
        UiTheme.ThinScrollbarThumb(GetVScrollBar(), true);
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        UiTheme.Margins(margin, 6, 4, 8, 4);
        AddChild(margin);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", RowGap);
        margin.AddChild(content);
        var olderRow = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddChild(olderRow);
        older = UiTheme.SmallButton("Load older messages", "");
        older.Visible = false;
        older.Pressed += () => OlderRequested?.Invoke();
        olderRow.AddChild(older);
        log = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        log.AddThemeConstantOverride("separation", RowGap);
        content.AddChild(log);
        GetVScrollBar().Changed += () => { if (stickBottom) ScrollToEnd(); };
        GetVScrollBar().ValueChanged += _ => stickBottom = AtBottom();
    }

    public bool StickBottom
    {
        get => stickBottom;
        set => stickBottom = value;
    }

    public void Clear()
    {
        foreach (var child in log.GetChildren())
        {
            log.RemoveChild(child);
            child.QueueFree();
        }
        rows.Clear();
        notes.Clear();
        hovered = null;
        firstRendered = null;
        rendered = 0;
    }

    public void Reset()
    {
        Clear();
        newest = null;
    }

    public void Render()
    {
        if (Timeline is not { } timeline) return;
        var posts = timeline.Posts;
        if (posts.Count == 0 || (firstRendered != null && !ReferenceEquals(posts[0], firstRendered)) || rendered > posts.Count)
            Clear();
        older.Visible = timeline.MoreHistory;
        int selfId = self();
        bool fromMe = false;
        for (int i = rendered; i < posts.Count; i++)
        {
            var post = posts[i];
            if (i == 0 || posts[i - 1].Time.Date != post.Time.Date) log.AddChild(DayChip(post.Time));
            bool mine = post.SenderId != 0 && post.SenderId == selfId;
            fromMe |= mine;
            log.AddChild(BuildPost(post, timeline.StartsGroup(i), mine));
        }
        firstRendered = posts.Count > 0 ? posts[0] : null;
        rendered = posts.Count;
        if (fromMe) stickBottom = true;
        if (stickBottom) ScrollToEnd();
        var newest = posts.Count > 0 ? posts[^1] : null;
        if (newest != null && this.newest != null && !ReferenceEquals(newest, this.newest)
            && newest.SenderId != selfId && newest.Kind != RoomPostKind.Notice)
            OthersPosted?.Invoke();
        this.newest = newest;
    }

    public void Refresh(RoomPost post)
    {
        if (!rows.TryGetValue(post, out var row)) return;
        bool atBottom = AtBottom();
        RefreshReactions(row);
        if (atBottom) stickBottom = true;
    }

    public bool Shows(RoomPost post) => rows.ContainsKey(post);

    public void ScrollToEnd() => ScrollVertical = (int)GetVScrollBar().MaxValue;

    public bool AtBottom()
    {
        var bar = GetVScrollBar();
        if (bar.MaxValue <= bar.Page) return true;
        return bar.Value + bar.Page >= bar.MaxValue - BottomSlack;
    }

    public void ShowReactButton(RoomPost post)
    {
        if (rows.TryGetValue(post, out var row)) Hover(row);
    }

    internal Control? ReactButtonOf(RoomPost post) => rows.TryGetValue(post, out var row) ? row.React : null;

    internal Control? PillOf(RoomPost post) => rows.TryGetValue(post, out var row) ? row.Pill : null;

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        TrackHover();
        noteClock += delta;
        if (noteClock < NoteRefreshSeconds) return;
        noteClock = 0;
        foreach (var note in notes) RefreshNote(note);
    }

    private void TrackHover()
    {
        var mouse = GetGlobalMousePosition();
        if (!GetGlobalRect().HasPoint(mouse))
        {
            Hover(null);
            return;
        }
        if (hovered != null && IsInstanceValid(hovered.Root) && hovered.Root.GetGlobalRect().HasPoint(mouse)) return;
        PostRow? found = null;
        foreach (var row in rows.Values)
        {
            if (row.React == null || !row.Root.GetGlobalRect().HasPoint(mouse)) continue;
            found = row;
            break;
        }
        Hover(found);
    }

    private void Hover(PostRow? row)
    {
        if (ReferenceEquals(row, hovered)) return;
        if (hovered?.React is { } old && IsInstanceValid(old)) old.Visible = ReferenceEquals(hovered.Post, Pinned);
        hovered = row;
        if (row?.React is { } now) now.Visible = row.Post.Id > 0;
    }

    public void Unpin()
    {
        var pinned = Pinned;
        Pinned = null;
        if (pinned != null && rows.TryGetValue(pinned, out var row) && row.React != null && !ReferenceEquals(row, hovered))
            row.React.Visible = false;
    }

    private static Control DayChip(DateTime time)
    {
        var today = DateTime.Now.Date;
        string label = time.Date == today ? "Today" : time.Date == today.AddDays(-1) ? "Yesterday" : time.ToString("ddd d MMM");
        var row = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var pill = UiTheme.Pill(label, UiTheme.TextLo, SmallFontSize);
        pill.CustomMinimumSize = new Vector2(0, 18);
        row.AddChild(pill);
        var spaced = new MarginContainer();
        UiTheme.Margins(spaced, 0, 4, 0, 4);
        spaced.AddChild(row);
        return spaced;
    }

    private Control BuildPost(RoomPost post, bool groupStart, bool mine)
    {
        if (post.Kind == RoomPostKind.Notice) return BuildNotice(post);

        var spaced = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        UiTheme.Margins(spaced, 0, groupStart ? 4 : 0, 0, 0);
        var line = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = mine ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Begin,
        };
        line.AddThemeConstantOverride("separation", 2);
        spaced.AddChild(line);

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 0);
        var bubble = new PanelContainer { SizeFlagsHorizontal = mine ? SizeFlags.ShrinkEnd : SizeFlags.ShrinkBegin };
        bubble.AddThemeStyleboxOverride("panel", BubbleStyle(mine, groupStart));
        stack.AddChild(bubble);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 1);
        bubble.AddChild(column);

        var row = new PostRow { Root = spaced, Post = post };
        var slot = ReactSlot(row);
        if (mine) line.AddChild(slot);
        line.AddChild(stack);
        if (!mine) line.AddChild(slot);

        if (!mine && groupStart && ShowNames) column.AddChild(NameLabel(post));
        if (post.Kind == RoomPostKind.VoiceNote) column.AddChild(BuildNote(post, mine));
        else
        {
            column.AddChild(BuildText(post.Text, mine));
            column.AddChild(TimeLabel(post));
        }
        BuildPill(row, stack);
        rows[post] = row;
        RefreshReactions(row);
        return spaced;
    }

    private Control BuildNotice(RoomPost post)
    {
        var center = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        chip.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        float width = GetThemeDefaultFont().GetStringSize(post.Text, HorizontalAlignment.Left, -1, SmallFontSize + 1).X;
        bool wraps = width > BubbleMax;
        if (WhisperLinks.Linkify(post.Text) is var linked && linked != BbCode.Esc(post.Text))
        {
            var rich = new RichTextLabel
            {
                BbcodeEnabled = true,
                FitContent = true,
                ScrollActive = false,
                AutowrapMode = wraps ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
                CustomMinimumSize = new Vector2(wraps ? BubbleMax : width + 2, 0),
                Text = $"[center][color=#{UiTheme.TextLo.ToHtml(false)}]{linked}[/color][/center]",
            };
            rich.AddThemeFontSizeOverride("normal_font_size", SmallFontSize + 1);
            rich.MetaClicked += meta =>
            {
                if (WhisperLinks.IsWebLink(meta.AsString())) OS.ShellOpen(meta.AsString());
            };
            chip.AddChild(rich);
        }
        else
        {
            var notice = UiTheme.Text(post.Text, SmallFontSize + 1, UiTheme.TextLo, HorizontalAlignment.Center);
            if (wraps)
            {
                notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                notice.CustomMinimumSize = new Vector2(BubbleMax, 0);
            }
            chip.AddChild(notice);
        }
        var spacedNotice = new MarginContainer();
        UiTheme.Margins(spacedNotice, 0, 3, 0, 3);
        spacedNotice.AddChild(chip);
        center.AddChild(spacedNotice);
        return center;
    }

    private Label NameLabel(RoomPost post)
    {
        var name = UiTheme.Text(post.Name, NameFontSize, new Color(RoomNameColours.Of(post.SenderId)));
        name.AddThemeConstantOverride("font_embolden", 1);
        name.MouseFilter = MouseFilterEnum.Stop;
        name.MouseDefaultCursorShape = CursorShape.PointingHand;
        string who = post.Name;
        name.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true } click && click.ButtonIndex is MouseButton.Left or MouseButton.Right)
                NameClicked?.Invoke(who, GetGlobalMousePosition());
        };
        return name;
    }

    private Control ReactSlot(PostRow row)
    {
        var slot = new CenterContainer
        {
            CustomMinimumSize = new Vector2(ReactSlotSize, ReactSlotSize),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var react = new Button
        {
            Icon = UiIcons.Get("system/smiley"),
            Flat = true,
            FocusMode = FocusModeEnum.None,
            ExpandIcon = true,
            IconAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(ReactSlotSize, ReactSlotSize),
            TooltipText = "React",
            Visible = false,
        };
        react.AddThemeConstantOverride("icon_max_width", ReactIconSize);
        react.AddThemeColorOverride("icon_normal_color", UiTheme.TextLo);
        react.AddThemeColorOverride("icon_hover_color", UiTheme.TextHi);
        react.AddThemeColorOverride("icon_pressed_color", UiTheme.TextHi);
        var hover = new StyleBoxFlat { BgColor = ReactHover };
        hover.SetCornerRadiusAll(ReactSlotSize / 2);
        react.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        react.AddThemeStyleboxOverride("hover", hover);
        react.AddThemeStyleboxOverride("pressed", hover);
        Audio.HookButton(react);
        react.Pressed += () =>
        {
            if (row.Post.Id <= 0) return;
            Pinned = row.Post;
            ReactRequested?.Invoke(row.Post, react);
        };
        slot.AddChild(react);
        row.React = react;
        return slot;
    }

    private void BuildPill(PostRow row, VBoxContainer stack)
    {
        var holder = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        holder.AddThemeConstantOverride("margin_top", -PillOverlap);
        holder.AddThemeConstantOverride("margin_left", PillInset);
        stack.AddChild(holder);
        var align = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        holder.AddChild(align);
        var pill = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        pill.AddThemeStyleboxOverride("panel", PillStyle(PillFill));
        align.AddChild(pill);
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 2);
        pill.AddChild(line);
        var glyphs = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        glyphs.AddThemeConstantOverride("separation", 1);
        line.AddChild(glyphs);
        var count = UiTheme.Text("", PillCountSize, UiTheme.TextLo);
        count.MouseFilter = MouseFilterEnum.Ignore;
        line.AddChild(count);
        pill.MouseEntered += () => pill.AddThemeStyleboxOverride("panel", PillStyle(PillHover));
        pill.MouseExited += () => pill.AddThemeStyleboxOverride("panel", PillStyle(PillFill));
        pill.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            pill.AcceptEvent();
            ReactionsClicked?.Invoke(row.Post, pill);
        };
        row.PillHolder = holder;
        row.Pill = pill;
        row.PillGlyphs = glyphs;
        row.PillCount = count;
    }

    private static StyleBoxFlat PillStyle(Color fill)
    {
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = PillEdge };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(10);
        style.ContentMarginLeft = 5;
        style.ContentMarginRight = 6;
        style.ContentMarginTop = 0;
        style.ContentMarginBottom = 0;
        return style;
    }

    private void RefreshReactions(PostRow row)
    {
        if (row.React != null && ReferenceEquals(row, hovered)) row.React.Visible = row.Post.Id > 0;
        if (row.PillHolder == null || row.PillGlyphs == null || row.PillCount == null) return;
        var reactions = row.Post.Reactions;
        row.PillHolder.Visible = reactions.Total > 0;
        foreach (var child in row.PillGlyphs.GetChildren())
        {
            row.PillGlyphs.RemoveChild(child);
            child.QueueFree();
        }
        var tally = reactions.Tally();
        for (int i = 0; i < tally.Count && i < ReactionRules.PillMax; i++)
            row.PillGlyphs.AddChild(Glyph(tally[i].Emoji, PillGlyphSize));
        row.PillCount.Text = ReactionRules.Count(reactions.Total);
        row.PillCount.Visible = row.PillCount.Text.Length > 0;
        if (row.Pill != null) row.Pill.TooltipText = ReactionRules.Title(reactions.Total);
    }

    public static Control Glyph(string value, int size)
    {
        var parts = Emoticons.Split(value);
        if (parts.Count == 1 && parts[0].Emoticon > 0 && Emoticons.Get(parts[0].Emoticon) is { } info)
            return new TextureRect
            {
                Texture = EmoticonPicker.Icon(GD.Load<Texture2D>(Emoticons.SheetPath), info),
                CustomMinimumSize = new Vector2(size + 2, size + 2),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
        var label = UiTheme.Text(value, size, Colors.White);
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return label;
    }

    private static StyleBoxFlat BubbleStyle(bool mine, bool groupStart)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = mine ? MineBubble : TheirBubble,
            BorderColor = mine ? MineEdge : TheirEdge,
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(8);
        if (groupStart)
        {
            if (mine) sb.CornerRadiusTopRight = 2;
            else sb.CornerRadiusTopLeft = 2;
        }
        sb.ContentMarginLeft = sb.ContentMarginRight = 9;
        sb.ContentMarginTop = 5;
        sb.ContentMarginBottom = 3;
        return sb;
    }

    private Control BuildText(string text, bool mine)
    {
        var colour = mine ? MineText : UiTheme.TextHi;
        string plain = ChatSystem.PlainText(text);
        float width = GetThemeDefaultFont().GetStringSize(plain, HorizontalAlignment.Left, -1, FontSize).X;
        bool wraps = width > BubbleMax;
        var rich = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            MetaUnderlined = false,
            AutowrapMode = wraps ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            CustomMinimumSize = wraps ? new Vector2(BubbleMax, 0) : Vector2.Zero,
            Text = ChatSystem.RenderText(text, colour.ToHtml(false)),
        };
        rich.AddThemeFontSizeOverride("normal_font_size", FontSize);
        rich.MetaClicked += meta =>
        {
            if (ChatSystem.ItemOf(meta) is int itemId and > 0) ItemClicked?.Invoke(itemId);
        };
        rich.MetaHoverEnded += _ => ItemHoverEnded?.Invoke();
        return rich;
    }

    private static Label TimeLabel(RoomPost post) =>
        UiTheme.Text(post.Time.ToString("HH:mm"), SmallFontSize, TimeColour, HorizontalAlignment.Right);

    private Control BuildNote(RoomPost post, bool mine)
    {
        var note = new VBoxContainer { CustomMinimumSize = new Vector2(Math.Min(NoteWidth, BubbleMax), 0) };
        note.AddThemeConstantOverride("separation", 1);

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", NoteGap);
        note.AddChild(line);
        var play = UiTheme.IconButton(UiIcons.Get("system/play"), "Play the voice note");
        play.Pressed += () => VoiceChat.I?.ToggleNote(post.Id);
        line.AddChild(play);
        var ring = new NoteDownloadRing { Visible = false };
        ring.SetAnchorsPreset(LayoutPreset.FullRect);
        play.AddChild(ring);
        var wave = new VoiceWaveformView
        {
            Samples = post.Waveform,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, WaveHeight),
            Played = mine ? MineText : UiTheme.Gold,
            Rest = mine ? new Color(MineText, MineRestAlpha) : new Color(UiTheme.TextLo, TheirRestAlpha),
        };
        wave.Seeked += fraction => VoiceChat.I?.SeekNote(post.Id, fraction);
        line.AddChild(wave);

        var foot = new HBoxContainer();
        note.AddChild(foot);
        foot.AddChild(new Control { CustomMinimumSize = new Vector2(play.CustomMinimumSize.X + NoteGap, 0) });
        var clock = UiTheme.Text(VoiceNoteRules.Clock(post.Seconds), SmallFontSize, mine ? MineText : UiTheme.TextLo);
        clock.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foot.AddChild(clock);
        foot.AddChild(TimeLabel(post));

        var row = new NoteRow { Play = play, Ring = ring, Wave = wave, Clock = clock, Post = post };
        notes.Add(row);
        RefreshNote(row);
        return note;
    }

    public void RefreshNotes()
    {
        foreach (var note in notes) RefreshNote(note);
    }

    private static void RefreshNote(NoteRow row)
    {
        var voice = VoiceChat.I;
        long id = row.Post.Id;
        var state = voice?.StateOf(id) ?? VoiceChat.NoteState.Idle;
        bool loading = state == VoiceChat.NoteState.Loading;
        row.Play.Icon = UiIcons.Get(state == VoiceChat.NoteState.Playing ? "system/pause" : "system/play");
        row.Play.Disabled = state == VoiceChat.NoteState.Missing;
        row.Play.SelfModulate = loading ? LoadingTint : Colors.White;
        row.Play.TooltipText = state switch
        {
            VoiceChat.NoteState.Loading => "Downloading...",
            VoiceChat.NoteState.Missing => RoomMediaResults.Text(RoomMediaResult.NotFound),
            VoiceChat.NoteState.Playing => "Pause",
            _ => "Play the voice note",
        };
        row.Ring.Visible = loading;
        if (loading) row.Ring.Fraction = voice?.LoadProgress(id) ?? 0f;
        float progress = voice?.NoteProgress(id) ?? 0f;
        row.Wave.Progress = progress;
        row.Clock.Text = state == VoiceChat.NoteState.Playing
            ? VoiceNoteRules.Clock(row.Post.Seconds * progress)
            : VoiceNoteRules.Clock(row.Post.Seconds);
    }
}
