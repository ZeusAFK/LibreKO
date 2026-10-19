using Godot;

namespace LibreKO.Features.Social;

internal sealed partial class ChatSystem
{
    private readonly IWorldContext context;
    private readonly ChatSession chat;
    private readonly KingSession king;
    private readonly ZoneSession zone;
    private readonly ClanSession clan;
    private readonly SelfSession self;
    private readonly HostilityRules hostility;
    private readonly ZoneAbilitySession zoneAbility;
    private readonly GmFxSession gmFx;
    private readonly IChatRequests chatRequests;
    private readonly IKingRequests kingRequests;

    private HBoxContainer frame = null!;
    private VBoxContainer root = null!;
    private PanelContainer logPanel = null!;
    private Button? peek;
    private RichTextLabel? peekText;
    private bool chatExpanded;
    private const float PeekWidth = 404f;
    private const float PeekHeight = 46f;
    private const float PeekPad = 12f;
    private const float PeekGlyphSize = 26f;
    private const float EdgeMargin = 12f;
    private const int NoticeTop = 60;
    private const float NoticeBarWidth = 520f;
    private const float NoticeBarHeight = 26f;
    private const float NoticeStillInset = 15f;
    private static readonly Vector2 DefaultSize = new(490, 238);
    private static readonly Vector2 MinimumSize = new(330, 170);
    private static readonly Vector2 ResizeGripOffset = new(0, 23);

    private HudLogText scroll = null!;
    private HBoxContainer inputRow = null!;
    private LineEdit input = null!;
    private StyleBoxFlat panelStyle = null!;
    private HudLayout layout = null!;
    private bool active;

    private CanvasLayer noticeLayer = null!;
    private Label noticeLabel = null!;
    private readonly NoticeQueue notices = new();
    private Tween? noticeScroll;
    private Tween? noticeFade;

    internal const byte WhisperChannel = (byte)ChatType.Private;
    private const byte GeneralChannel = (byte)ChatType.General;
    private const byte ClanChannel = (byte)ChatType.Clan;
    private const byte NoahKnightsChannel = (byte)ChatType.NoahKnights;
    private const byte CommanderChannel = (byte)ChatType.Command;

    private byte sendChannel = GeneralChannel;
    private string whisperName = "";
    private string? pendingWhisper;
    private string pendingWhisperTo = "";
    private string lastWhisperFrom = "";

    private static readonly HashSet<string> ReplyCommands = new(StringComparer.OrdinalIgnoreCase) { "r", "reply" };
    private static readonly HashSet<string> WhisperCommands = new(StringComparer.OrdinalIgnoreCase) { "w", "tell", "whisper" };
    private const int NationChatCommandText = 8019;
    private const int NoahChatBlockedText = 40101;
    private static readonly HashSet<string> CommandsOpenToEveryone = new(StringComparer.OrdinalIgnoreCase) { "setlevel" };

    private readonly ChatPrefs prefs = ChatPrefs.Parse(Config.ChatLook);
    private ChatColors colors = ChatColors.Parse(Config.ChatColors);

    internal ChatSystem(IWorldContext context, ChatSession chat, KingSession king, ZoneSession zone, ClanSession clan, SelfSession self,
        HostilityRules hostility, ZoneAbilitySession zoneAbility, GmFxSession gmFx, IChatRequests chatRequests, IKingRequests kingRequests)
    {
        this.context = context;
        this.chat = chat;
        this.king = king;
        this.zone = zone;
        this.clan = clan;
        this.self = self;
        this.hostility = hostility;
        this.zoneAbility = zoneAbility;
        this.gmFx = gmFx;
        this.chatRequests = chatRequests;
        this.kingRequests = kingRequests;
        Bubbles = new SpeechBubbles(context, self, type => colors.ForLine((ChatType)type));
    }

    internal SpeechBubbles Bubbles { get; }

    private IChatHost? host;

    internal void Attach(IChatHost chatHost) => host = chatHost;

    internal void Detach(IChatHost chatHost)
    {
        if (host == chatHost) host = null;
    }

    internal string? SendRefusal() => host?.SendRefusal();

    internal event Action<string, Vector2>? NameMenuRequested;
    internal event Action<int>? ItemTipRequested;
    internal event Action? ItemTipDismissed;
    internal event Action? ColorsRequested;
    internal event Action<string>? StatusNotice;
    internal event Action? LayoutChanged;

    internal bool IsActive => active || PluginTyping;

    internal bool PluginTyping { get; private set; }

    internal void NotePluginTyping(bool typing) => PluginTyping = typing;

    internal Control Panel => frame;

    private static bool DocksNearby => !Platform.TouchUi;

    private static readonly Vector2 NearbyColumnWidth = new(NearbyDock.Width + NearbyDock.Gap, 0);

    private Vector2 NearbyColumn => DocksNearby && prefs.NearbyShown ? NearbyColumnWidth : Vector2.Zero;

    private Vector2 FrameDefaultSize => DefaultSize + NearbyColumn;

    private Vector2 FrameMinimumSize => MinimumSize + NearbyColumn;

    private NearbyCard? nearby;

    internal void DockNearby(NearbyCard list)
    {
        nearby = list;
        list.CustomMinimumSize = new Vector2(NearbyDock.Width, 0);
        list.Visible = prefs.NearbyShown;
        frame.AddChild(list);
        frame.MoveChild(list, 0);
        ApplyBackground();
    }

    internal void PreviewNearbyShown(bool shown) => SetNearbyShown(shown, save: false);

    private void SetNearbyShown(bool shown, bool save = true)
    {
        if (prefs.NearbyShown == shown) return;
        prefs.NearbyShown = shown;
        if (save) SavePrefs();
        if (nearby == null || layout == null) return;
        nearby.Visible = shown;
        layout.MinimumSize = FrameMinimumSize;
        Vector2 shift = shown ? -NearbyColumnWidth : NearbyColumnWidth;
        layout.Place(
            new Vector2(Mathf.Max(0, frame.Position.X + shift.X), frame.Position.Y),
            frame.Size - shift);
    }

    internal ChatColors Colors => colors;

    private static string ChanTag(ChatType type) => type switch
    {
        ChatType.Private => "whisper",
        ChatType.Party => "party",
        ChatType.Shout => "shout",
        ChatType.Clan => "clan",
        ChatType.Public or ChatType.WarSystem or ChatType.GameMaster => "GM",
        ChatType.Command => "commander",
        ChatType.Merchant => "trade",
        ChatType.Alliance => "alliance",
        ChatType.SeekingParty => "zone",
        ChatType.ClanOfficer => "officer",
        ChatType.NoahKnights => "noah",
        _ => "",
    };

    internal void SendText(string text) => Submit(text);

    internal void Build()
    {
        var layer = new CanvasLayer { Layer = UiLayers.HudButtons };
        context.Root.AddChild(layer);
        if (PluginHost.Ui.HudHidden(LibreKO.Plugins.HudPart.Chat))
        {
            layer.Visible = false;
            if (PluginHost.Ui.HudReplacement(LibreKO.Plugins.HudPart.Chat) is { } build)
            {
                var pluginLayer = new CanvasLayer { Layer = UiLayers.HudButtons };
                context.Root.AddChild(pluginLayer);
                pluginLayer.AddChild(build());
            }
        }

        frame = new HBoxContainer { Size = FrameDefaultSize };
        frame.AddThemeConstantOverride("separation", (int)NearbyDock.Gap);
        layer.AddChild(frame);
        root = new VBoxContainer { CustomMinimumSize = MinimumSize, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 4);
        frame.AddChild(root);

        root.AddChild(BuildTabBar());
        BuildLogPanel();
        BuildInputRow();
        SetInputRowActive(false);

        if (Platform.TouchUi) BuildChatPeek();
        AttachLayout(frame, Platform.TouchUi ? null : DefaultSpot);
        frame.Resized += () => LayoutChanged?.Invoke();
        ApplyLook();

        BuildNoticeBanner();

        chat.ChatEvent += OnChat;
        king.RoyalOrderEvent += OnRoyalOrder;
        king.KingEventResultEvent += OnKingEventResult;
        chat.ChatTargetEvent += OnChatTarget;
        zone.NoticeEvent += OnNotice;
    }

    internal void DetachNetwork()
    {
        chat.ChatEvent -= OnChat;
        king.RoyalOrderEvent -= OnRoyalOrder;
        king.KingEventResultEvent -= OnKingEventResult;
        chat.ChatTargetEvent -= OnChatTarget;
        zone.NoticeEvent -= OnNotice;
    }

    internal void Dispose() => DetachNetwork();

    private Vector2 DefaultSpot()
    {
        float height = frame.IsInsideTree() ? frame.GetViewportRect().Size.Y : FrameDefaultSize.Y + EdgeMargin * 2;
        return new Vector2(EdgeMargin, height - EdgeMargin - Mathf.Max(frame.Size.Y, FrameMinimumSize.Y));
    }

    internal void AttachLayout(Control target, Func<Vector2>? floating = null, bool persist = true)
    {
        target.Modulate = Godot.Colors.White;
        layout = HudLayout.Attach(
            target, persist ? "hud_chat" : "uilab_actual_chat", floating != null ? moveTool : null, floating,
            resizable: !Platform.TouchUi,
            defaultSize: FrameDefaultSize,
            minimumSize: Platform.TouchUi ? Vector2.Zero : FrameMinimumSize,
            persist: persist,
            resizeCorner: HudLayout.Corner.TopRight,
            resizeGripOffset: ResizeGripOffset,
            anchor: floating == null ? HudPlacement.ChatAnchor : null,
            anchorMargin: HudPlacement.ChatMargin,
            moveGripOverlay: false);
        layout.Locked = prefs.Locked;
        layout.Placed += () => LayoutChanged?.Invoke();
    }

    private void BuildNoticeBanner()
    {
        noticeLayer = new CanvasLayer { Layer = UiLayers.ChatOverlays, Visible = false };
        context.Root.AddChild(noticeLayer);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = NoticeTop,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", QuestProgressToastFeature.ToastStyle());
        noticeLayer.AddChild(panel);
        NoticePanel = panel;

        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 6);
        panel.AddChild(rows);
        rows.AddChild(QuestProgressToastFeature.ToastRule());
        var bar = new Control
        {
            CustomMinimumSize = new Vector2(NoticeBarWidth, NoticeBarHeight),
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rows.AddChild(bar);
        noticeLabel = UiTheme.Text("", 16, UiTheme.GoldBright);
        noticeLabel.VerticalAlignment = VerticalAlignment.Center;
        noticeLabel.AddThemeConstantOverride("font_embolden", 1);
        noticeLabel.AddThemeConstantOverride("outline_size", 4);
        noticeLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        bar.AddChild(noticeLabel);
        rows.AddChild(QuestProgressToastFeature.ToastRule());
    }

    internal Control NoticePanel { get; private set; } = null!;

    internal void ShowNoticePreview(string msg) => OnNotice(msg);

    private const int DragonAlarmId = 7035;

    private void ShowServerNotice(ChatLine line)
    {
        string text = ChatSender.Flatten(line.Type, line.Message);
        if (line.Type == ChatType.InfoNotice)
        {
            StatusNotice?.Invoke(text);
            return;
        }
        if (line.Type == ChatType.AlarmNotice && line.CharId == DragonAlarmId) Audio.PlayUi(Sfx.DragonRoar);
        if (notices.Enqueue(text)) ShowNotice(text);
    }

    private void OnNotice(string msg)
    {
        AddEntry(ChatEntry.Plain(ChatCategory.Notice, NoticeType, msg));
        if (notices.Enqueue(msg)) ShowNotice(msg);
    }

    private const byte NoticeType = (byte)ChatType.WarSystem;

    private void ShowNotice(string msg)
    {
        noticeLabel.Text = msg;
        noticeLabel.Size = new Vector2(noticeLabel.GetMinimumSize().X, NoticeBarHeight);
        noticeLabel.Position = new Vector2(NoticeStillInset, 0);
        if (!noticeLabel.IsInsideTree())
        {
            noticeLayer.Visible = true;
            noticeLabel.Connect(Node.SignalName.TreeEntered, Callable.From(() => ShowNotice(msg)), (uint)GodotObject.ConnectFlags.OneShot);
            return;
        }

        FadeNotice(visible: true);
        noticeScroll?.Kill();
        float width = noticeLabel.Size.X;
        float seconds = NoticeMarquee.PassSeconds(NoticeBarWidth, width);
        noticeScroll = noticeLabel.CreateTween();
        for (int pass = 0; pass < NoticeMarquee.Passes; pass++)
            noticeScroll.TweenProperty(noticeLabel, "position:x", -width, seconds).From(NoticeBarWidth);
        noticeScroll.TweenCallback(Callable.From(NextNotice));
    }

    private void NextNotice()
    {
        if (!GodotObject.IsInstanceValid(noticeLayer)) return;
        if (notices.Advance() is { } next) ShowNotice(next);
        else FadeNotice(visible: false);
    }

    private void FadeNotice(bool visible)
    {
        noticeFade?.Kill();
        if (visible && !noticeLayer.Visible) NoticePanel.Modulate = Godot.Colors.Transparent;
        if (visible) noticeLayer.Visible = true;
        noticeFade = NoticePanel.CreateTween();
        noticeFade.TweenProperty(NoticePanel, "modulate:a", visible ? 1f : 0f, NoticeMarquee.FadeSeconds);
        if (!visible) noticeFade.TweenCallback(Callable.From(() => noticeLayer.Visible = false));
    }

    internal void Open()
    {
        if (PluginHost.Ui.HudHidden(HudPart.Chat))
        {
            PluginHost.Game.RaiseChatInputRequested();
            return;
        }
        active = true;
        SetInputRowActive(true);
        UpdateInputColour();
        input.GrabFocus();
        input.CaretColumn = input.Text.Length;
        UpdateCounter();
    }

    internal void OpenWith(string text)
    {
        Open();
        ShowTyped(text);
    }

    internal void Close()
    {
        active = false;
        input.ReleaseFocus();
        input.Clear();
        lastInputText = "";
        linkDraft.Clear();
        ApplyInputLimit();
        history.StopBrowsing();
        SetInputRowActive(false);
        UpdateCounter();
    }

    private void Suspend()
    {
        if (!active || settingsMenu.Visible || EmoticonsOpen || input.HasFocus()) return;
        if (input.Text.Length == 0)
        {
            Close();
            return;
        }
        active = false;
        SetInputRowActive(false);
        UpdateCounter();
    }

    private void SetInputRowActive(bool active)
    {
        bool clickToType = !Platform.TouchUi;
        inputRow.Modulate = active ? Godot.Colors.White : new Color(1, 1, 1, clickToType ? IdleInputAlpha : 0);
        inputRow.MouseFilter = active || clickToType ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
        input.MouseFilter = active || clickToType ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        input.Editable = active;
        input.FocusMode = active ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        emoticonButton.Visible = active || !clickToType;
        ApplyInputStyle();
    }

    internal void SetPreviewState(bool isInputActive, byte channel)
    {
        active = isInputActive;
        SetChannel(channel);
        SetInputRowActive(isInputActive);
        if (isInputActive) UpdateInputColour();
    }

    internal void AttachPreviewLayout(Control target) =>
        AttachLayout(target, Platform.TouchUi ? null : DefaultSpot, persist: false);

    internal void PreviewLine(ChatLine line) => OnChat(line);

    internal void PreviewSent(string name, string text) => AppendWhisperEcho(name, text);

    internal void PreviewTyped(string text, int itemId, string tail)
    {
        Open();
        ShowTyped(text);
        if (itemId > 0) InsertItemLink(itemId);
        if (tail.Length > 0) ShowTyped(input.Text + tail);
    }

    internal void PreviewScrolledUp(IEnumerable<ChatLine> fresh)
    {
        scroll.ScrollToLine(0);
        foreach (var line in fresh) OnChat(line);
    }

    internal void SetChannel(byte channel)
    {
        sendChannel = channel;
        UpdateInputColour();
    }

    private const char GmCommandPrefix = '+';

    private void Submit(string text)
    {
        string visible = text.TrimEnd();
        if (visible.Length == 0) { Close(); return; }
        history.Push(visible);
        string wire = linkDraft.ToWire(visible);

        if (visible[0] == '/' && host?.RunLocalCommand(visible.Substring(1).Trim()) == true) { Close(); return; }
        if (visible[0] == '/' && RunSlashCommand(visible.Substring(1).Trim())) { Close(); return; }

        if (visible[0] != GmCommandPrefix && SendRefusal() is { } refusal)
        {
            Info(refusal);
            Close();
            return;
        }

        var (chan, body, target) = Parse(wire);

        if (target.Length > 0)
        {
            pendingWhisper = body.Length > 0 ? body : null;
            pendingWhisperTo = target;
            chatRequests.Target(target);
        }
        else if (chan == WhisperChannel)
        {
            if (whisperName.Length == 0)
                Info("No whisper target. Use @name message to start a whisper.");
            else if (body.Length > 0)
            {
                chatRequests.Send(body, WhisperChannel);
                AppendWhisperEcho(whisperName, body);
            }
        }
        else if (body.Length > 0)
        {
            if (chan == CommanderChannel && clan.MyClan.Fame != ClanRanks.CommandCaptain)
                Info("Only war captains can use commander chat.");
            else if (chan == NoahKnightsChannel && host?.NoahKnight != true)
                Info(ItemData.Text(NoahChatBlockedText, "Currently the Noah Knights chat is blocked"));
            else
                chatRequests.Send(body, chan);
        }

        Close();
    }

    internal void RunCommand(string command)
    {
        if (host?.RunLocalCommand(command) == true) return;
        RunSlashCommand(command);
    }

    private bool RunSlashCommand(string command)
    {
        int sp = command.IndexOf(' ');
        string word = sp < 0 ? command : command[..sp];
        string args = sp < 0 ? "" : command[(sp + 1)..].Trim();

        if (ReplyCommands.Contains(word))
        {
            if (lastWhisperFrom.Length == 0) Info("No one has whispered you yet.");
            else if (args.Length == 0) WhisperOpened?.Invoke(lastWhisperFrom);
            else SendWhisper(lastWhisperFrom, args);
            return true;
        }

        if (WhisperCommands.Contains(word))
        {
            int gap = args.IndexOf(' ');
            string name = gap < 0 ? args : args[..gap];
            string message = gap < 0 ? "" : args[(gap + 1)..].Trim();
            if (name.Length == 0) Info($"Usage: /{word} <name> <message>");
            else if (message.Length == 0) WhisperOpened?.Invoke(name);
            else SendWhisper(name, message);
            return true;
        }

        if (string.Equals(word, ItemData.Text(NationChatCommandText, "nation_chat"), StringComparison.OrdinalIgnoreCase))
        {
            NationChatRequested?.Invoke();
            return true;
        }

        if (Gestures.CommandAction(word, ItemData.Text) is { } gesture)
        {
            GestureRequested?.Invoke(gesture);
            return true;
        }

        if (KingCommands.Parse(word, args, ItemData.Text) is { } kingCommand)
        {
            kingRequests.Command(kingCommand);
            return true;
        }

        if (self.IsGm || CommandsOpenToEveryone.Contains(word)) return false;
        Info($"Unknown command: /{word}");
        return true;
    }

    internal event Action? NationChatRequested;
    internal event Action<int>? GestureRequested;

    private const string RoyalOrderColour = "ff6994";

    private void OnRoyalOrder(byte kind, string message)
    {
        string line = ItemData.Text(KingCommands.RoyalOrderLineText, "%s : %s");
        int first = line.IndexOf("%s", StringComparison.Ordinal);
        if (first >= 0) line = line[..first] + ItemData.Text(KingCommands.KingTitleText, "King") + line[(first + 2)..];
        line = line.Replace("%s", message);
        AddEntry(ChatEntry.Raw(ChatCategory.Notice, $"[color=#{RoyalOrderColour}]{BbCode.Esc(line)}[/color]"));
    }

    private void OnKingEventResult(KingEventSubOpcode kingEvent, short result)
    {
        int text = KingCommands.ResultText(kingEvent, result);
        if (text != 0) Info(ItemData.Text(text, ""));
    }

    internal event Action<string, string>? WhisperEcho;
    internal event Action<string, string>? WhisperNotice;
    internal event Action<string>? WhisperOpened;

    internal void SendWhisper(string target, string message)
    {
        if (target.Length == 0 || message.Length == 0) return;
        pendingWhisper = message;
        pendingWhisperTo = target;
        chatRequests.Target(target);
    }

    private (byte chan, string body, string target) Parse(string text)
    {
        char c = text[0];
        string rest = text.Substring(1).TrimStart();
        switch (c)
        {
            case '@':
            {
                int sp = rest.IndexOf(' ');
                string name = sp < 0 ? rest : rest.Substring(0, sp);
                string msg = sp < 0 ? "" : rest.Substring(sp + 1);
                return (WhisperChannel, msg, name);
            }
            case '/': return (GeneralChannel, "+" + text.Substring(1), "");
            case '+': return (GeneralChannel, text, "");
        }
        byte prefixed = ChatPrefixes.ChannelFor(text, ChatPrefixes.NotChat);
        return prefixed == ChatPrefixes.NotChat ? (sendChannel, text, "") : (prefixed, rest, "");
    }

    private void OnChat(ChatLine line)
    {
        if (line.Type == (ChatType)WhisperChannel)
        {
            if (line.Name.Length == 0 || IsSelf(line.Name)) return;
            lastWhisperFrom = line.Name;
            var side = host?.IsFriend(line.Name) == true ? WhisperSide.ReceivedFromFriend : WhisperSide.Received;
            AddEntry(SenderShown(ChatEntry.Whisper(side, line.Name, line.Nation, line.IsGm, ReadableText(line)), line));
            return;
        }

        if (line.Type is ChatType.MarqueeNotice or ChatType.AlarmNotice or ChatType.InfoNotice && line.Name.Length == 0)
        {
            ShowServerNotice(line);
            return;
        }

        if (line.CharId < 0 && line.Name.Length == 0)
        {
            var category = ChatCategories.Of(line.Type, false);
            string notice = ChatSender.Flatten(line.Type, line.Message);
            if (category == ChatCategory.Notice) StatusNotice?.Invoke(notice);
            else AddEntry(ChatEntry.Plain(category, (byte)line.Type, notice));
            return;
        }

        string text = ChatSender.Flatten(line.Type, ReadableText(line));
        var entry = SenderShown(ChatEntry.Player(line.Type, line.Name, line.Nation, line.IsGm, text), line);
        AddEntry(entry);
        Bubbles.Show(line.CharId, line.Type, PlainText(entry.Text));
    }

    private ChatEntry SenderShown(ChatEntry entry, ChatLine line)
    {
        int siege = hostility.SiegeChatMask(line.CharId, line.IsGm);
        if (siege != 0)
            return entry with
            {
                Name = ItemData.Text(siege, SiegeWar.Fallback(siege)),
                Text = SiegeWar.Scramble(entry.Text, count => (int)(GD.Randi() % (uint)count)),
                Masked = true,
            };
        int mask = ChatSender.MaskText(self.LastEnter.Zone, self.IsGm, self.Nation, self.MyCharId,
            line.Type, line.Nation, line.CharId);
        return mask == 0
            ? entry with { Rank = RankOf(line.CharId) }
            : entry with { Name = ItemData.Text(mask, entry.Name), Masked = true };
    }

    private int RankOf(int charId) => host?.PersonalRankOf(charId) ?? NationRankAura.Unranked;

    private bool IsSelf(string name) =>
        string.Equals(name, self.LastEnter.Name, StringComparison.OrdinalIgnoreCase);

    private static bool Understandable(ChatLine line, int ownNation, bool zoneCanTalk)
        => line.IsGm
        || line.Nation == 0
        || line.Nation == ownNation
        || zoneCanTalk
        || line.Type is not (ChatType.General or ChatType.Private or ChatType.Shout or ChatType.GameMaster
            or ChatType.Merchant);

    internal static string ReadableText(ChatLine line, int ownNation, bool zoneCanTalk) =>
        Understandable(line, ownNation, zoneCanTalk) ? line.Message : Garble(line.Message);

    private string ReadableText(ChatLine line) => ReadableText(line, self.Nation, zoneAbility.CurrentZoneAbility.CanTalk);

    private static string Garble(string message)
    {
        var scrambled = new char[message.Length];
        for (int i = 0; i < scrambled.Length; i++)
            scrambled[i] = (char)(GD.Randi() % 10 + 33);
        return new string(scrambled);
    }

    internal const int TextWhisperFailed = 1101;
    internal const int TextWhisperConnected = 1102;
    private const int TextOwnWhispersBlocked = 7635;
    private const int TextTargetBlocksWhispers = 7636;
    private const int TextNoCrossNationWhisper = 44060;

    private void OnChatTarget(ChatTargetResult result, string name)
    {
        switch (result)
        {
            case ChatTargetResult.Connected:
                whisperName = name;
                WhisperOpened?.Invoke(name);
                if (pendingWhisper is { } msg)
                {
                    chatRequests.Send(msg, WhisperChannel);
                    AppendWhisperEcho(name, msg);
                    pendingWhisper = null;
                }
                break;
            case ChatTargetResult.TargetNotFound:
                WhisperNotice?.Invoke(pendingWhisperTo, ItemData.Text(TextWhisperFailed, "Failed to connect for a private chat"));
                pendingWhisper = null;
                break;
            case ChatTargetResult.TargetBlocked:
                WhisperNotice?.Invoke(pendingWhisperTo, ItemData.Text(TextTargetBlocksWhispers, "This user has blocked private messages."));
                pendingWhisper = null;
                break;
            case ChatTargetResult.CrossNationZone:
                WhisperNotice?.Invoke(pendingWhisperTo, ItemData.Text(TextNoCrossNationWhisper, "You cannot whisper between nations in this zone."));
                pendingWhisper = null;
                break;
            case ChatTargetResult.SenderBlocked:
                WhisperNotice?.Invoke(pendingWhisperTo, ItemData.Text(TextOwnWhispersBlocked, "Blocked Private messages."));
                pendingWhisper = null;
                break;
        }
    }

    private void AppendWhisperEcho(string name, string msg)
    {
        AddEntry(ChatEntry.Whisper(WhisperSide.Sent, name, 0, false, msg));
        WhisperEcho?.Invoke(name, msg);
    }

    private void BuildChatPeek()
    {
        peek = new Button
        {
            Name = "chat_peek",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(PeekWidth, PeekHeight),
        };
        var skin = new StyleBoxFlat
        {
            BgColor = new Color(0.015f, 0.018f, 0.024f, 0.30f),
            BorderColor = new Color(UiTheme.Edge, 0.18f),
        };
        skin.SetCornerRadiusAll(8);
        skin.SetBorderWidthAll(1);
        foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            peek.AddThemeStyleboxOverride(state, skin);

        var bubble = new ChatBubbleGlyph
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(PeekPad, (PeekHeight - PeekGlyphSize) * 0.5f),
            Size = Vector2.One * PeekGlyphSize,
        };
        peek.AddChild(bubble);

        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        centre.OffsetLeft = PeekPad * 2f + PeekGlyphSize;
        centre.OffsetRight = -PeekPad;
        peek.AddChild(centre);

        peekText = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = false,
            FitContent = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(PeekWidth - PeekPad * 3f - PeekGlyphSize, 0f),
        };
        peekText.AddThemeFontSizeOverride("normal_font_size", 15);
        centre.AddChild(peekText);

        peek.Pressed += () => SetChatExpanded(!chatExpanded);
        root.AddChild(peek);
        root.MoveChild(peek, 0);
        SetChatExpanded(false);
    }

    private void SetChatExpanded(bool expanded)
    {
        chatExpanded = expanded;
        if (peek == null) return;
        tabBar.Visible = expanded;
        logPanel.Visible = expanded;
        peek.Visible = !expanded;
    }

    private void UpdateChatPeek(string bbcode)
    {
        if (peekText == null) return;
        peekText.Text = bbcode;
    }

    private void Info(string msg) => StatusNotice?.Invoke(msg);

    internal void AppendShout(string message) => AddEntry(ChatEntry.Plain(ChatCategory.Shout, (byte)ChatType.Shout, message));
}
