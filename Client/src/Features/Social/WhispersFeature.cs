using Godot;

namespace LibreKO.Features.Social;

internal sealed class WhispersFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ChatSession chat;
    private readonly RoomMediaSession roomMedia;
    private readonly SelfSession self;
    private readonly ZoneAbilitySession zoneAbility;
    private readonly IChatRequests requests;
    private readonly IRoomMediaRequests roomMediaRequests;
    private readonly ChatReactionsFeature reactions;
    private readonly PlayerMenuFeature playerMenu;
    private readonly FriendsFeature friends;

    public WhispersFeature(IWorldContext context, ChatSession chat, RoomMediaSession roomMedia, SelfSession self, ZoneAbilitySession zoneAbility, IChatRequests chatRequests, IRoomMediaRequests roomMediaRequests, ChatReactionsFeature reactions, PlayerMenuFeature playerMenu, FriendsFeature friends)
    {
        this.context = context;
        this.chat = chat;
        this.roomMedia = roomMedia;
        this.self = self;
        this.zoneAbility = zoneAbility;
        requests = chatRequests;
        this.roomMediaRequests = roomMediaRequests;
        this.reactions = reactions;
        this.playerMenu = playerMenu;
        this.friends = friends;
    }

    internal IEscapable Panel => new ClosablePanel(AnyWhisperExpanded, MinimizeAllWhispers);

    internal const float BlinkDim = 0.42f;
    internal const float BlinkStep = 0.32f;
    private const float WhisperAlphaStep = 0.05f;
    private PopupPanel? whisperOptions;
    private HSlider whisperAlphaSlider = null!;
    private readonly Button[] whisperSizeButtons = new Button[3];
    private static readonly Color WhisperBlue = new("2f80d4");
    private static readonly Color WhisperBlueEdge = new("5aa6e8");
    private static readonly Color WhisperBlueText = new("eaf3fd");
    private static readonly Vector2 WhisperFirstPos = new(430, 300);
    private static readonly Vector2 WhisperCascade = new(28, 26);

    internal sealed class Whisper
    {
        public string Name = "";
        public HudWindow Window = null!;
        public ChatThreadView Thread = null!;
        public ChatComposer Composer = null!;
        public readonly List<Button> HeaderIcons = [];
        public Button? Block;
        public Tween? Blink;
        public bool StartCollapsed;

        public LineEdit Input => Composer.Input;
    }

    private CanvasLayer whisperLayer = null!;

    internal Dictionary<string, Whisper> Windows { get; } = new(StringComparer.OrdinalIgnoreCase);

    private int whisperOpened;
    private string whisperTyping = "";

    public void Build()
    {
        friends.WhisperRequested += OpenWhisperWith;
        whisperLayer = new CanvasLayer { Layer = UiLayers.Whispers };
        whisperLayer.SetMeta(Audio.QuietMeta, true);
        context.Root.AddChild(whisperLayer);

        chat.ChatEvent += OnWhisper;
        roomMedia.DirectTimelineEvent += OnDirectTimeline;
        roomMedia.DirectPostedEvent += OnDirectPosted;
        context.Chat.WhisperEcho += OnWhisperEcho;
        context.Chat.WhisperNotice += OnWhisperNotice;
        context.Chat.WhisperOpened += OnWhisperOpened;
        Config.WhisperLookChanged += OnWhisperLookChanged;

        foreach (var open in roomMedia.OpenWhispers)
        {
            var whisper = GetOrCreateWhisper(open.Name, open.Minimized, new Vector2(open.X, open.Y));
            if (open.Unread) StartWhisperBlink(whisper);
        }
    }

    public void Dispose()
    {
        friends.WhisperRequested -= OpenWhisperWith;
        roomMedia.KeepOpenWhispers(Windows.Values
            .Where(whisper => GodotObject.IsInstanceValid(whisper.Window) && whisper.Window.Visible)
            .Select(whisper => new OpenWhisper(whisper.Name, whisper.Window.Position.X, whisper.Window.Position.Y, Folded(whisper),
                whisper.Blink != null && whisper.Blink.IsValid())));
        chat.ChatEvent -= OnWhisper;
        roomMedia.DirectTimelineEvent -= OnDirectTimeline;
        roomMedia.DirectPostedEvent -= OnDirectPosted;
        context.Chat.WhisperEcho -= OnWhisperEcho;
        context.Chat.WhisperNotice -= OnWhisperNotice;
        context.Chat.WhisperOpened -= OnWhisperOpened;
        Config.WhisperLookChanged -= OnWhisperLookChanged;
        if (VoiceChat.I is { } voice && voice.Recording && voice.RecordingTarget.Length > 0) voice.StopRecording(send: false);
    }

    private static StyleBoxFlat WhisperPanelStyle()
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.105f, 0.108f, 0.116f, 0.78f),
            BorderColor = new Color(0.62f, 0.58f, 0.46f, 0.72f),
            ShadowColor = new Color(0, 0, 0, 0.42f),
            ShadowSize = 6,
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(2);
        sb.SetContentMarginAll(0);
        return sb;
    }

    internal Whisper GetOrCreateWhisper(string name, bool minimized, Vector2? at = null)
    {
        if (Windows.TryGetValue(name, out var existing)) return existing;

        var whisper = new Whisper { Name = name, StartCollapsed = minimized };
        var box = WhisperLook.BoxOf(Config.WhisperSize);
        var offset = at ?? WhisperFirstPos + WhisperCascade * (whisperOpened++ % 6);

        whisper.Window = new HudWindow($"whisper_{name}", name, offset, box.Width, persistLayout: false, minimizable: true)
        {
            Visible = true,
        };
        whisper.Window.AddThemeStyleboxOverride("panel", WhisperPanelStyle());
        whisper.Window.SetHeaderAccent(WhisperBlue, WhisperBlueEdge, WhisperBlueText);
        AddWhisperHeaderIcon(whisper, "system/user", "Player menu",
            () => playerMenu.ShowByName(name, context.Root.GetViewport().GetMousePosition()));
        AddWhisperHeaderIcon(whisper, "system/arrange", "Window size and transparency", OpenWhisperOptions);
        whisper.Block = AddWhisperHeaderIcon(whisper, "system/lock", "", ToggleWhisperBlock);
        RefreshWhisperBlock(whisper);
        whisper.Window.Closed += () => CloseWhisper(name);
        whisper.Window.MinimizedChanged += isMinimized =>
        {
            foreach (var icon in whisper.HeaderIcons) icon.Visible = !isMinimized;
            if (isMinimized) return;
            StopWhisperBlink(whisper);
            whisper.Thread.StickBottom = true;
            whisper.Thread.ScrollToEnd();
            whisper.Input.CallDeferred(Control.MethodName.GrabFocus);
        };
        whisperLayer.AddChild(whisper.Window);

        var root = whisper.Window.Body;
        root.AddThemeConstantOverride("separation", 6);

        var timeline = roomMedia.DirectTimeline(name);
        whisper.Thread = new ChatThreadView(() => context.Self.CharId)
        {
            ShowNames = false,
            BubbleMax = box.Wrap,
            Timeline = timeline,
            CustomMinimumSize = WhisperLogSize(),
        };
        whisper.Thread.OlderRequested += () => roomMediaRequests.DirectHistory(name, timeline.OldestId);
        whisper.Thread.OthersPosted += () => { if (whisper.Window.Minimized) StartWhisperBlink(whisper); };
        reactions.WireThread(whisper.Thread);
        root.AddChild(whisper.Thread);

        whisper.Composer = new ChatComposer(name);
        whisper.Composer.Submitted += wire => context.Chat.SendWhisper(name, wire);
        whisper.Composer.Notice += text => AddWhisperPost(whisper, RoomPost.Notice(text, DateTime.Now));
        whisper.Input.FocusEntered += () => whisperTyping = name;
        root.AddChild(whisper.Composer);

        Windows[name] = whisper;
        whisper.Thread.Render();
        whisper.Thread.ScrollToEnd();
        if (!timeline.HistoryLoaded) roomMediaRequests.DirectHistory(name, 0);
        if (minimized) whisper.Window.SetMinimized(true);
        whisper.Window.Modulate = WhisperRestModulate();
        context.Plugins.Whispers();
        return whisper;
    }

    private Button? AddWhisperHeaderIcon(Whisper whisper, string icon, string tooltip, Action pressed)
    {
        var button = whisper.Window.AddHeaderIcon(icon, tooltip, pressed);
        if (button != null) whisper.HeaderIcons.Add(button);
        return button;
    }

    private static Vector2 WhisperLogSize()
    {
        var box = WhisperLook.BoxOf(Config.WhisperSize);
        return new Vector2(box.Width, box.Height);
    }

    private static Color WhisperRestModulate() => new(1f, 1f, 1f, Config.WhisperAlpha);

    private void OnWhisperLookChanged()
    {
        var box = WhisperLook.BoxOf(Config.WhisperSize);
        foreach (var whisper in Windows.Values)
        {
            whisper.Window.Body.CustomMinimumSize = new Vector2(box.Width, 0);
            whisper.Thread.CustomMinimumSize = WhisperLogSize();
            whisper.Thread.BubbleMax = box.Wrap;
            whisper.Thread.Reset();
            whisper.Thread.Render();
            whisper.Window.ResetSize();
            if (whisper.Blink == null) whisper.Window.Modulate = WhisperRestModulate();
            whisper.Thread.StickBottom = true;
            whisper.Thread.ScrollToEnd();
        }
        RefreshWhisperOptions();
        context.Plugins.Whispers();
    }

    private void OpenWhisperOptions()
    {
        if (whisperOptions == null) BuildWhisperOptions();
        RefreshWhisperOptions();
        UiKit.PopupAtCursor(context.Root, whisperOptions!, context.Root.GetViewport().GetMousePosition());
    }

    private void BuildWhisperOptions()
    {
        whisperOptions = new PopupPanel();
        whisperLayer.AddChild(whisperOptions);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        whisperOptions.AddChild(column);
        column.AddChild(UiTheme.Text("Window size", 12, UiTheme.TextLo));
        var sizes = new HBoxContainer();
        sizes.AddThemeConstantOverride("separation", 4);
        column.AddChild(sizes);
        var group = new ButtonGroup();
        string[] labels = { "Small", "Medium", "Large" };
        for (int i = 0; i < whisperSizeButtons.Length; i++)
        {
            var size = (WhisperSize)i;
            var button = new Button { Text = labels[i], ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => Config.SetWhisperLook(size, Config.WhisperAlpha);
            whisperSizeButtons[i] = button;
            sizes.AddChild(button);
        }
        column.AddChild(UiTheme.Text("Transparency", 12, UiTheme.TextLo));
        whisperAlphaSlider = new HSlider
        {
            MinValue = WhisperLook.MinAlpha, MaxValue = WhisperLook.MaxAlpha, Step = WhisperAlphaStep,
            CustomMinimumSize = new Vector2(180, 18),
        };
        whisperAlphaSlider.ValueChanged += value => Config.SetWhisperLook(Config.WhisperSize, (float)value);
        column.AddChild(whisperAlphaSlider);
    }

    private void RefreshWhisperOptions()
    {
        if (whisperOptions == null) return;
        for (int i = 0; i < whisperSizeButtons.Length; i++)
            whisperSizeButtons[i].SetPressedNoSignal((int)Config.WhisperSize == i);
        whisperAlphaSlider.SetValueNoSignal(Config.WhisperAlpha);
    }

    internal void ToggleWhisperBlock()
    {
        requests.Block(!chat.WhispersBlocked);
        foreach (var whisper in Windows.Values) RefreshWhisperBlock(whisper);
        context.Plugins.Whispers();
    }

    private const string WhisperLockedIcon = "system/lock";
    private const string WhisperUnlockedIcon = "system/unlock";

    private void RefreshWhisperBlock(Whisper whisper)
    {
        if (whisper.Block == null) return;
        bool blocked = chat.WhispersBlocked;
        var colour = blocked ? UiTheme.Bad : UiTheme.Good;
        whisper.Block.Icon = UiIcons.Get(blocked ? WhisperLockedIcon : WhisperUnlockedIcon);
        whisper.Block.AddThemeColorOverride("icon_normal_color", colour);
        whisper.Block.AddThemeColorOverride("icon_hover_color", colour.Lightened(0.25f));
        whisper.Block.AddThemeColorOverride("icon_pressed_color", colour.Darkened(0.15f));
        whisper.Block.TooltipText = blocked ? "Private messages are blocked - click to allow them" : "Block private messages";
    }

    internal void OpenWhisperWith(string name)
    {
        if (name.Length == 0) return;
        var whisper = GetOrCreateWhisper(name, minimized: false);
        whisper.Window.Visible = true;
        whisper.Window.SetMinimized(false);
        RaiseWhisper(whisper);
        StopWhisperBlink(whisper);
        whisper.Thread.StickBottom = true;
        whisper.Thread.ScrollToEnd();
        if (whisper.Input.IsVisibleInTree()) whisper.Input.GrabFocus();
    }

    internal void CloseWhisper(string name)
    {
        if (!Windows.Remove(name, out var whisper)) return;
        themedFolded.Remove(name);
        StopWhisperBlink(whisper);
        if (VoiceChat.I is { } voice && voice.RecordingFor(name)) voice.StopRecording(send: false);
        whisper.Window.QueueFree();
        context.Plugins.Whispers();
    }

    internal void FocusWhisperAt(Vector2 screenPos)
    {
        var shown = Windows.Values
            .Where(whisper => GodotObject.IsInstanceValid(whisper.Window) && whisper.Window.Visible)
            .OrderBy(whisper => whisper.Window.GetIndex())
            .ToList();
        int hit = WindowStack.TopmostAt(shown.Select(whisper => whisper.Window.GetGlobalRect()).ToList(), screenPos);
        if (hit == WindowStack.None || shown[hit].Window.Minimized) return;
        RaiseWhisper(shown[hit]);
        if (!shown[hit].Input.HasFocus()) shown[hit].Input.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void RaiseWhisper(Whisper whisper) =>
        whisperLayer.MoveChild(whisper.Window, whisperLayer.GetChildCount() - 1);

    private int FocusedWhisperIndex()
    {
        if (context.Root.GetViewport().GuiGetFocusOwner() is not LineEdit focused) return WindowStack.None;
        foreach (var whisper in Windows.Values)
            if (whisper.Input == focused && GodotObject.IsInstanceValid(whisper.Window)) return whisper.Window.GetIndex();
        return WindowStack.None;
    }

    private void OnWhisper(ChatLine line)
    {
        if (line.Type != (ChatType)ChatSystem.WhisperChannel || line.Name.Length == 0) return;
        if (string.Equals(line.Name, self.LastEnter.Name, StringComparison.OrdinalIgnoreCase)) return;

        var whisper = ShowIncomingWhisper(line.Name);
        AddWhisperPost(whisper, RoomPost.Line(line.CharId, line.Name, ChatSystem.ReadableText(line, self.Nation, zoneAbility.CurrentZoneAbility.CanTalk), DateTime.Now));
        if (whisper.Window.Minimized) StartWhisperBlink(whisper);
    }

    private Whisper ShowIncomingWhisper(string name)
    {
        bool isNew = !Windows.ContainsKey(name);
        var whisper = GetOrCreateWhisper(name, minimized: isNew);
        whisper.Window.Visible = true;
        if (WindowStack.RaisesIncoming(FocusedWhisperIndex(), whisper.Window.GetIndex())) RaiseWhisper(whisper);
        return whisper;
    }

    private void OnDirectTimeline(string partner)
    {
        if (Windows.TryGetValue(partner, out var whisper)) whisper.Thread.Render();
    }

    private void OnDirectPosted(string partner, RoomPost post)
    {
        bool theirs = post.SenderId != context.Self.CharId;
        if (!Windows.TryGetValue(partner, out var whisper))
        {
            if (!theirs || post.Kind != RoomPostKind.VoiceNote) return;
            whisper = ShowIncomingWhisper(partner);
        }
        context.Plugins.WhisperLine(partner, post);
        whisper.Thread.Render();
        if (theirs && whisper.Window.Minimized) StartWhisperBlink(whisper);
    }

    private void OnWhisperEcho(string name, string message)
    {
        var whisper = GetOrCreateWhisper(name, minimized: false);
        whisper.Window.Visible = true;
        whisper.Thread.StickBottom = true;
        AddWhisperPost(whisper, RoomPost.Line(context.Self.CharId, self.LastEnter.Name ?? "", message, DateTime.Now));
    }

    private void OnWhisperOpened(string name) => OpenWhisperWith(name);

    internal void OnWhisperNotice(string name, string text) => WhisperNoticeTo(name, text);

    internal void WhisperNoticeTo(string name, string text)
    {
        if (name.Length == 0 || !Windows.TryGetValue(name, out var whisper))
        {
            context.Log.Notice(text);
            return;
        }
        AddWhisperPost(whisper, RoomPost.Notice(text, DateTime.Now));
    }

    private void AddWhisperPost(Whisper whisper, RoomPost post)
    {
        roomMedia.RecordWhisper(whisper.Name, post);
        context.Plugins.WhisperLine(whisper.Name, post);
        whisper.Thread.Render();
    }

    internal bool InsertWhisperItemLink(int itemId)
    {
        if (!Windows.TryGetValue(whisperTyping, out var whisper)) return false;
        if (!GodotObject.IsInstanceValid(whisper.Window) || !whisper.Window.Visible || whisper.Window.Minimized) return false;
        return whisper.Composer.InsertItemLink(itemId);
    }

    private void StartWhisperBlink(Whisper whisper)
    {
        if (whisper.Blink != null && whisper.Blink.IsValid()) return;
        var tween = whisper.Window.CreateTween().SetLoops();
        tween.TweenProperty(whisper.Window, "modulate:a", BlinkDim * Config.WhisperAlpha, BlinkStep);
        tween.TweenProperty(whisper.Window, "modulate:a", Config.WhisperAlpha, BlinkStep);
        whisper.Blink = tween;
    }

    private static void StopWhisperBlink(Whisper whisper)
    {
        if (whisper.Blink != null && whisper.Blink.IsValid()) whisper.Blink.Kill();
        whisper.Blink = null;
        whisper.Window.Modulate = WhisperRestModulate();
    }

    internal bool AnyWhisperExpanded()
    {
        foreach (var whisper in Windows.Values)
            if (IsWhisperExpanded(whisper)) return true;
        return false;
    }

    internal void MinimizeAllWhispers()
    {
        foreach (var whisper in Windows.Values.ToList())
            if (IsWhisperExpanded(whisper)) Fold(whisper);
    }

    private readonly Dictionary<string, bool> themedFolded = new(StringComparer.OrdinalIgnoreCase);

    internal void ReportThemedFold(string name, bool folded) => themedFolded[name] = folded;

    private bool Folded(Whisper whisper) =>
        whisper.Window.PluginReplaced
            ? themedFolded.GetValueOrDefault(whisper.Name, whisper.StartCollapsed)
            : whisper.Window.Minimized;

    private void Fold(Whisper whisper)
    {
        if (whisper.Window.PluginReplaced) context.Plugins.WhisperCollapse(whisper.Name);
        else whisper.Window.SetMinimized(true);
    }

    private bool IsWhisperExpanded(Whisper whisper) =>
        GodotObject.IsInstanceValid(whisper.Window) && whisper.Window.Visible && !Folded(whisper);

    internal bool WhisperInputHasFocus()
    {
        if (context.Root.GetViewport().GuiGetFocusOwner() is not LineEdit focused) return false;
        foreach (var whisper in Windows.Values)
            if (whisper.Input == focused) return true;
        return false;
    }

    internal bool TryMinimizeFocusedWhisper()
    {
        var focus = context.Root.GetViewport().GuiGetFocusOwner();
        for (Node? n = focus; n != null; n = n.GetParent())
        {
            if (n is not HudWindow window) continue;
            foreach (var whisper in Windows.Values)
            {
                if (whisper.Window != window || Folded(whisper)) continue;
                Fold(whisper);
                focus?.ReleaseFocus();
                return true;
            }
            return false;
        }
        return false;
    }

    private void OnPostChanged(RoomPost post)
    {
        foreach (var whisper in Windows.Values)
            if (GodotObject.IsInstanceValid(whisper.Thread)) whisper.Thread.Refresh(post);
    }
}
