using Godot;

namespace LibreKO.Features.Social;

internal sealed partial class ChatRoomFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly ChatSession chat;
    private readonly NationChatSession nation;
    private readonly RoomMediaSession roomMedia;
    private readonly SelfSession self;
    private readonly INationChatRequests nationRequests;
    private readonly IRoomMediaRequests roomMediaRequests;
    private readonly NationChannelsFeature nationChannels;
    private readonly WhispersFeature whispers;
    private readonly ChatReactionsFeature reactions;
    private readonly PlayerMenuFeature playerMenu;

    public ChatRoomFeature(IWorldContext context, ChatSession chat, NationChatSession nationChat, RoomMediaSession roomMedia, SelfSession self, INationChatRequests nationChatRequests, IRoomMediaRequests roomMediaRequests,
        NationChannelsFeature nationChannels, WhispersFeature whispers, ChatReactionsFeature reactions, PlayerMenuFeature playerMenu)
    {
        this.context = context;
        this.chat = chat;
        nation = nationChat;
        this.roomMedia = roomMedia;
        this.self = self;
        nationRequests = nationChatRequests;
        this.roomMediaRequests = roomMediaRequests;
        this.nationChannels = nationChannels;
        this.whispers = whispers;
        this.reactions = reactions;
        this.playerMenu = playerMenu;
    }

    private const int Width = 380;
    private const float LogHeight = 330f;
    private const float BubbleMax = 268f;
    private const double TickSeconds = 0.1;
    private static readonly Vector2 DefaultPos = new(1110, 170);

    private CanvasLayer roomLayer = null!;
    internal HudWindow RoomWindow { get; private set; } = null!;
    private Button? roomChannelsButton;
    private Tween? roomBlink;
    internal ChatThreadView RoomThread { get; private set; } = null!;
    internal ChatComposer RoomComposer { get; private set; } = null!;
    internal Godot.Timer RoomTick { get; private set; } = null!;

    public void Build()
    {
        roomLayer = new CanvasLayer { Layer = UiLayers.ChatOverlays };
        roomLayer.SetMeta(Audio.QuietMeta, true);
        context.Root.AddChild(roomLayer);
        BuildWindow();
        RoomTick = new Godot.Timer { WaitTime = TickSeconds, ProcessCallback = Godot.Timer.TimerProcessCallback.Idle };
        RoomTick.Timeout += ChatRoomTick;
        roomLayer.AddChild(RoomTick);

        roomMedia.RoomTimelineEvent += Render;
        nation.RoomChatEvent += OnLine;
        roomMedia.RoomPostedEvent += OnPosted;
        roomMedia.RoomMediaResultEvent += OnRoomMediaResult;
        nation.NationChatStateEvent += Sync;
        nation.NationMembersEvent += OnMembers;
        roomMedia.VoiceRosterEvent += RefreshVoice;
        roomMedia.VoiceSilenceEvent += OnSilence;
        roomMedia.ChatPostChangedEvent += OnPostChanged;
        nation.RoomChatNoticeEvent += OnRoomNotice;
        nation.NationChatResultEvent += OnNationResult;
        if (VoiceChat.I is { } voice)
        {
            voice.StateChanged += RefreshVoice;
            voice.NoteRecorded += SendRecordedNote;
            voice.NoteNotice += NoticeFor;
        }
        Sync();
    }

    bool IEscapable.CloseOnEscape()
    {
        if (Expanded) RoomWindow.SetMinimized(true);
        else return false;
        return true;
    }

    public void Dispose()
    {
        roomMedia.RoomTimelineEvent -= Render;
        nation.RoomChatEvent -= OnLine;
        roomMedia.RoomPostedEvent -= OnPosted;
        roomMedia.RoomMediaResultEvent -= OnRoomMediaResult;
        nation.NationChatStateEvent -= Sync;
        nation.NationMembersEvent -= OnMembers;
        roomMedia.VoiceRosterEvent -= RefreshVoice;
        roomMedia.VoiceSilenceEvent -= OnSilence;
        roomMedia.ChatPostChangedEvent -= OnPostChanged;
        nation.RoomChatNoticeEvent -= OnRoomNotice;
        nation.NationChatResultEvent -= OnNationResult;
        if (VoiceChat.I is { } voice)
        {
            voice.StateChanged -= RefreshVoice;
            voice.NoteRecorded -= SendRecordedNote;
            voice.NoteNotice -= NoticeFor;
            if (voice.Recording) voice.StopRecording(send: false);
        }
    }

    private bool Shown => GodotObject.IsInstanceValid(RoomWindow) && RoomWindow.Visible;

    internal bool Expanded => Shown && !RoomWindow.Minimized;

    private void BuildWindow()
    {
        RoomWindow = new HudWindow("chat_room", "Chat room", DefaultPos, Width, minimizable: true) { Visible = false };
        RoomWindow.Closed += Hide;
        RoomWindow.MinimizedChanged += minimized =>
        {
            RefreshHeader();
            if (minimized) return;
            StopBlink();
            RoomThread.StickBottom = true;
            RoomThread.ScrollToEnd();
        };
        RoomWindow.AddHeaderIcon("system/door", "Leave the room", LeaveFromPanel);
        roomChannelsButton = RoomWindow.AddHeaderIcon("system/chat-circle", "Channel list", OpenChannelListFromRoom);
        roomLayer.AddChild(RoomWindow);

        var body = RoomWindow.Body;
        body.AddThemeConstantOverride("separation", 6);

        var main = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        main.AddThemeConstantOverride("separation", (int)ColumnGap);
        body.AddChild(main);
        BuildMembersColumn(main);
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", StackGap);
        main.AddChild(right);
        RoomThread = new ChatThreadView(() => context.Self.CharId)
        {
            ShowNames = true,
            BubbleMax = BubbleMax,
            Timeline = roomMedia.RoomTimeline,
            CustomMinimumSize = new Vector2(Width, LogHeight),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        RoomThread.OlderRequested += () => roomMediaRequests.RoomHistory(roomMedia.RoomTimeline.OldestId);
        RoomThread.OthersPosted += StartBlink;
        reactions.WireThread(RoomThread);
        right.AddChild(RoomThread);

        BuildVoiceRow(right);
        RoomComposer = new ChatComposer("");
        RoomComposer.Submitted += SendText;
        RoomComposer.Notice += ChatRoomNotice;
        right.AddChild(RoomComposer);
        AlignColumns();
    }

    private static ProgressBar Meter(Color tint) => ChatComposer.Meter(tint);

    private void Sync()
    {
        if (!GodotObject.IsInstanceValid(RoomWindow)) return;
        var state = nation.NationChat;
        if (!state.InRoom)
        {
            if (RoomWindow.Visible) RoomWindow.Visible = false;
            RoomTick.Stop();
            RoomThread.Reset();
            StopBlink();
            return;
        }
        if (roomMedia.ChatRoomPanelShown && !RoomWindow.Visible) Show();
        RefreshHeader();
        RefreshVoice();
    }

    internal void Show()
    {
        if (!nation.NationChat.InRoom) return;
        roomMedia.ShowRoomPanel();
        RoomWindow.Visible = true;
        RoomWindow.SetMinimized(false);
        roomLayer.MoveChild(RoomWindow, roomLayer.GetChildCount() - 1);
        StopBlink();
        if (RoomTick.IsInsideTree()) RoomTick.Start();
        RefreshHeader();
        RefreshVoice();
        Render();
        RoomThread.StickBottom = true;
        RoomThread.ScrollToEnd();
        AskNames();
    }

    private void Hide()
    {
        roomMedia.HideRoomPanel();
        StopBlink();
        RoomWindow.Visible = false;
        RoomTick.Stop();
        if (VoiceChat.I is { } voice && voice.RecordingFor("")) voice.StopRecording(send: false);
    }

    private void LeaveFromPanel()
    {
        if (nation.NationChat.InPublic) nationRequests.NationPublicLeave();
        else if (nation.NationChat.InPrivate) nationRequests.NationPrivateLeave();
    }

    private void OpenChannelListFromRoom()
    {
        if (!nationChannels.NationShown) nationChannels.ToggleNation();
    }

    private void RefreshHeader()
    {
        if (!GodotObject.IsInstanceValid(RoomWindow)) return;
        var state = nation.NationChat;
        string name = state.RoomName.Length > 0 ? state.RoomName : "Chat room";
        RoomWindow.Title = NationChatRules.Title(name, state.RoomMembers, state.RoomMax);
        if (roomChannelsButton != null) roomChannelsButton.Visible = !RoomWindow.Minimized;
    }

    private void StartBlink()
    {
        if (!Shown || !RoomWindow.Minimized || (roomBlink != null && roomBlink.IsValid())) return;
        var tween = RoomWindow.CreateTween().SetLoops();
        tween.TweenProperty(RoomWindow, "modulate:a", WhispersFeature.BlinkDim, WhispersFeature.BlinkStep);
        tween.TweenProperty(RoomWindow, "modulate:a", 1f, WhispersFeature.BlinkStep);
        roomBlink = tween;
    }

    private void StopBlink()
    {
        if (roomBlink != null && roomBlink.IsValid()) roomBlink.Kill();
        roomBlink = null;
        if (GodotObject.IsInstanceValid(RoomWindow)) RoomWindow.Modulate = Colors.White;
    }

    private void OnLine(RoomChatLine line) => Render();

    private void OnMembers(NationRoomMembers members)
    {
        nation.NationChat.Counted(members.Members.Count);
        RefreshHeader();
        RefreshVoice();
    }

    private void OnPosted(RoomPost post) => Render();

    private void ChatRoomNotice(string text)
    {
        if (text.Length == 0 || !nation.NationChat.InRoom) return;
        roomMedia.RoomTimeline.Add(RoomPost.Notice(text, System.DateTime.Now));
        Render();
    }

    private void NoticeFor(string target, string text)
    {
        if (target.Length == 0) ChatRoomNotice(text);
        else whispers.WhisperNoticeTo(target, text);
    }

    internal void Render()
    {
        if (GodotObject.IsInstanceValid(RoomThread)) RoomThread.Render();
    }

    private void ChatRoomTick()
    {
        if (!Shown || VoiceChat.I is not { } voice) return;
        TickVoice(voice);
    }

    private void SendText(string wire)
    {
        if (!nation.NationChat.InRoom) return;
        if (context.Chat?.SendRefusal() is { } refusal)
        {
            ChatRoomNotice(refusal);
            return;
        }
        nationRequests.RoomChat(wire);
        RoomThread.StickBottom = true;
    }

    internal bool InsertItemLink(int itemId) =>
        Expanded && RoomComposer.Input.HasFocus() && RoomComposer.InsertItemLink(itemId);

    private void SendRecordedNote(byte[] file, double seconds, byte[] waveform, string target)
    {
        if (!roomMediaRequests.SendVoiceNote(file, seconds, waveform, target)) NoticeFor(target, "A voice note is still being sent.");
    }

    private void OnRoomMediaResult(RoomMediaSubOpcode sub, RoomMediaResult result)
    {
        if (result == RoomMediaResult.Ok) return;
        if (sub is RoomMediaSubOpcode.NoteBegin or RoomMediaSubOpcode.NoteEnd) NoticeFor(roomMedia.NoteTarget, RoomMediaResults.Text(result));
        else if (sub is RoomMediaSubOpcode.VoiceJoin or RoomMediaSubOpcode.Kept) ChatRoomNotice(RoomMediaResults.Text(result));
    }

    private const double NameRequestSeconds = 2.0;
    private const float SettingsWidth = 196f;
    private const float PeerWidth = 176f;
    private const float ColumnWidth = 136f;
    private const int StackGap = 6;
    private const float DisabledIconAlpha = 0.35f;
    private const float ColumnGap = 6f;
    private const float ColumnEdgeAlpha = 0.25f;
    private const float ColumnFillAlpha = 0.35f;
    private const float VolumeWidth = 96f;
    private const int JoinMargin = 10;
    private const int MenuWhisper = 1;
    private const int MenuVolume = 2;
    private const int MenuPlayer = 3;
    private const float MicMarkSize = 12f;
    private const string OwnerMark = "★";
    private static readonly Color Speaking = new("7fd98b");
    private static readonly Color IdleDot = new(0.45f, 0.45f, 0.48f, 0.9f);

    private sealed class RosterRow
    {
        public PanelContainer Panel = null!;
        public Label Dot = null!;
        public Label Name = null!;
        public Label Owner = null!;
        public TextureRect Mark = null!;
        public TextureRect Speaker = null!;
        public StyleBoxFlat Style = null!;
        public bool InVoice;
    }

    private VBoxContainer roomMembersColumn = null!;
    private VBoxContainer roomRosterRows = null!;
    private Label roomRosterEmpty = null!;
    private VBoxContainer roomLeaderBox = null!;
    private Button roomLeaderHandOver = null!;
    private Button roomLeaderKick = null!;
    private HBoxContainer roomVoiceRow = null!;
    private Button roomMembersToggle = null!;
    private Button roomConfigButton = null!;
    internal PopupPanel? RoomConfig { get; private set; }
    private LineEdit roomConfigName = null!;
    private OptionButton roomConfigOption = null!;
    private Label roomConfigPasswordTitle = null!;
    private LineEdit roomConfigPassword = null!;
    private OptionButton roomConfigSize = null!;
    private CheckBox roomConfigKeep = null!;
    private Label roomConfigError = null!;
    private Button roomJoin = null!;
    private Button roomMic = null!;
    private Button roomVoiceGear = null!;
    private Button roomVoiceLeave = null!;
    private Button roomDeafen = null!;
    private HSlider roomVolume = null!;
    private PopupMenu? roomMemberMenu;
    private int roomMenuTarget;
    private int roomSelectedMember = NationChatRules.NoRoom;

    private readonly Dictionary<int, RosterRow> roomRoster = new();

    private double roomNamesAskedAt = double.NegativeInfinity;
    internal PopupPanel? RoomVoiceSettings { get; private set; }
    private OptionButton roomDeviceList = null!;
    internal ProgressBar RoomSettingsLevel { get; private set; } = null!;
    private HSlider roomThreshold = null!;
    private CheckBox roomPushToTalk = null!;
    private PopupPanel? roomPeerPopup;
    private Label roomPeerName = null!;
    private HSlider roomPeerVolume = null!;
    private CheckBox roomPeerMute = null!;
    private int roomPeerId;

    private void BuildMembersColumn(HBoxContainer main)
    {
        roomMembersColumn = new VBoxContainer { CustomMinimumSize = new Vector2(ColumnWidth, 0) };
        roomMembersColumn.AddThemeConstantOverride("separation", StackGap);
        main.AddChild(roomMembersColumn);

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, ColumnFillAlpha),
            BorderColor = new Color(UiTheme.Edge, ColumnEdgeAlpha),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(6);
        style.SetContentMarginAll(6);
        var panel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", style);
        roomMembersColumn.AddChild(panel);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        UiTheme.ThinScrollbarThumb(scroll.GetVScrollBar(), true);
        panel.AddChild(scroll);
        var listColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        listColumn.AddThemeConstantOverride("separation", 1);
        scroll.AddChild(listColumn);
        roomRosterEmpty = UiTheme.Text("Loading members...", 11, UiTheme.TextDim, HorizontalAlignment.Center);
        roomRosterEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        listColumn.AddChild(roomRosterEmpty);
        roomRosterRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        roomRosterRows.AddThemeConstantOverride("separation", 1);
        listColumn.AddChild(roomRosterRows);

        roomLeaderBox = new VBoxContainer();
        roomLeaderBox.AddThemeConstantOverride("separation", StackGap);
        roomMembersColumn.AddChild(roomLeaderBox);
        roomLeaderHandOver = UiTheme.SmallButton("Change leader", "");
        roomLeaderHandOver.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        roomLeaderHandOver.Pressed += () => { if (roomSelectedMember > 0) nationRequests.NationHandOver(roomSelectedMember); };
        roomLeaderBox.AddChild(roomLeaderHandOver);
        roomLeaderKick = UiTheme.SmallButton("Kick out", "");
        roomLeaderKick.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        roomLeaderKick.Pressed += () => { if (roomSelectedMember > 0) nationRequests.NationKick(roomSelectedMember); };
        roomLeaderBox.AddChild(roomLeaderKick);
    }

    private void AlignColumns()
    {
        void Match() => roomLeaderBox.CustomMinimumSize = new Vector2(0, roomVoiceRow.Size.Y + StackGap + RoomComposer.Size.Y);
        roomVoiceRow.Resized += Match;
        RoomComposer.Resized += Match;
    }

    private void BuildVoiceRow(VBoxContainer right)
    {
        roomVoiceRow = new HBoxContainer();
        roomVoiceRow.AddThemeConstantOverride("separation", 6);
        right.AddChild(roomVoiceRow);

        roomMembersToggle = UiTheme.IconButton(UiIcons.Get("system/users-three"), "Show or hide the members");
        roomMembersToggle.Pressed += ToggleMembers;
        roomVoiceRow.AddChild(roomMembersToggle);
        roomConfigButton = UiTheme.IconButton(UiIcons.Get("system/sliders"), "Room settings");
        roomConfigButton.Pressed += OpenConfig;
        TintIcon(roomConfigButton, UiTheme.TextHi);
        roomConfigButton.AddThemeColorOverride("icon_disabled_color", new Color(UiTheme.TextLo, DisabledIconAlpha));
        roomConfigButton.AddThemeStyleboxOverride("disabled", roomConfigButton.GetThemeStylebox("normal"));
        roomVoiceRow.AddChild(roomConfigButton);
        roomVoiceRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        roomJoin = UiTheme.ActionButton("Join voice", "Talk with everyone in this room");
        roomJoin.Icon = UiIcons.Get("system/phone");
        roomJoin.AddThemeConstantOverride("icon_max_width", 14);
        roomJoin.AddThemeColorOverride("icon_normal_color", UiTheme.Ink);
        roomJoin.AddThemeColorOverride("icon_hover_color", UiTheme.Ink);
        foreach (string state in new[] { "normal", "hover", "pressed" })
            if (roomJoin.GetThemeStylebox(state) is StyleBoxFlat box)
                box.ContentMarginLeft = box.ContentMarginRight = JoinMargin;
        roomJoin.Pressed += () => VoiceChat.I?.Join();
        roomVoiceRow.AddChild(roomJoin);

        roomDeafen = UiTheme.IconButton(UiIcons.Get("system/speaker-high"), "");
        roomDeafen.Pressed += () => VoiceChat.I?.ToggleDeafen();
        roomVoiceRow.AddChild(roomDeafen);
        roomVolume = new HSlider
        {
            MinValue = 0,
            MaxValue = VoiceLevels.MaxVolume,
            Step = 0.05,
            CustomMinimumSize = new Vector2(VolumeWidth, 0),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = "Voice volume (up to 200%)",
            FocusMode = Control.FocusModeEnum.None,
        };
        roomVolume.ValueChanged += value =>
            Config.SetVoiceChat((float)value, Config.VoiceThreshold, Config.VoicePushToTalk, Config.VoiceInputDevice);
        roomVoiceRow.AddChild(roomVolume);
        roomMic = UiTheme.IconButton(UiIcons.Get("system/microphone"), "");
        roomMic.Pressed += () => VoiceChat.I?.ToggleMute();
        roomVoiceRow.AddChild(roomMic);
        roomVoiceGear = UiTheme.IconButton(UiIcons.Get("system/gear"), "Voice settings");
        roomVoiceGear.Pressed += OpenVoiceSettings;
        roomVoiceRow.AddChild(roomVoiceGear);
        roomVoiceLeave = UiTheme.IconButton(UiIcons.Get("system/phone-x"), "Leave voice");
        roomVoiceLeave.AddThemeColorOverride("icon_normal_color", UiTheme.Bad);
        roomVoiceLeave.AddThemeColorOverride("icon_hover_color", UiTheme.Bad.Lightened(0.25f));
        roomVoiceLeave.Pressed += () => VoiceChat.I?.Leave();
        roomVoiceRow.AddChild(roomVoiceLeave);
    }

    private void ToggleMembers()
    {
        roomMedia.ToggleRoomMembers();
        RefreshVoice();
        RoomWindow.ResetSize();
    }

    private void RefreshVoice()
    {
        if (!GodotObject.IsInstanceValid(roomVoiceRow)) return;
        var voice = VoiceChat.I;
        var state = nation.NationChat;
        bool live = voice?.InVoice == true;
        roomVoiceRow.Visible = state.InPrivate;
        roomMembersColumn.Visible = state.InPrivate && roomMedia.ChatRoomMembersShown;
        TintIcon(roomMembersToggle, roomMedia.ChatRoomMembersShown ? UiTheme.TextHi : UiTheme.TextLo);
        roomConfigButton.Disabled = !state.Owner;
        if (!state.Owner) RoomConfig?.Hide();
        roomJoin.Visible = !live;
        roomDeafen.Visible = live;
        roomVolume.Visible = live;
        roomMic.Visible = live;
        roomVoiceGear.Visible = live;
        roomVoiceLeave.Visible = live;
        if (live && voice != null)
        {
            roomVolume.SetValueNoSignal(Config.VoiceChatVolume);
            bool micOff = voice.MicMuted || !voice.CanSpeak;
            roomMic.Icon = UiIcons.Get(micOff ? "system/microphone-slash" : "system/microphone");
            TintIcon(roomMic, voice.SilencedByLeader ? UiTheme.Warning : micOff ? UiTheme.Bad : UiTheme.Good);
            roomMic.TooltipText = voice.SilencedByLeader ? "The room leader silenced your microphone"
                : !voice.CanSpeak ? RoomMediaResults.Text(RoomMediaResult.Silenced)
                : voice.MicMuted ? "Unmute microphone"
                : Config.VoicePushToTalk ? $"Mute microphone (push to talk: hold {KeyBinds.Get(KeyAction.VoicePushToTalk).Text})"
                : "Mute microphone";
            roomDeafen.Icon = UiIcons.Get(voice.Deafened ? "system/speaker-slash" : "system/speaker-high");
            TintIcon(roomDeafen, voice.Deafened ? UiTheme.Bad : UiTheme.Gold);
            roomDeafen.TooltipText = voice.Deafened ? "Hear the room again" : "Stop hearing the room";
            roomVoiceLeave.TooltipText = voice.CurrentTransport switch
            {
                VoiceChat.Transport.Probing => "Leave voice (connecting...)",
                VoiceChat.Transport.Tcp => "Leave voice (using the game connection)",
                _ => "Leave voice",
            };
        }
        SyncRoster(voice, live);
    }

    private static void TintIcon(Button button, Color colour)
    {
        button.AddThemeColorOverride("icon_normal_color", colour);
        button.AddThemeColorOverride("icon_hover_color", colour.Lightened(0.25f));
        button.AddThemeColorOverride("icon_pressed_color", colour.Darkened(0.15f));
    }

    private int OwnerId() =>
        nation.NationChat.Owner ? context.Self.CharId
        : nation.Members is { } list && list.RoomId == nation.NationChat.RoomId ? list.OwnerId
        : 0;

    private List<RosterEntry> RosterEntries(bool live)
    {
        var members = nation.Members is { } list && list.RoomId == nation.NationChat.RoomId
            ? list.Members.Select(m => (m.CharacterId, m.Name))
            : [];
        var inVoice = new HashSet<int>(roomMedia.VoicePeers.Keys.Where(id => id != context.Self.CharId));
        if (live) inVoice.Add(context.Self.CharId);
        return ChatRoomRoster.Arrange(members, inVoice, NameOf);
    }

    private void SyncRoster(VoiceChat? voice, bool live)
    {
        if (!nation.NationChat.InPrivate)
        {
            ClearRoster();
            return;
        }
        var entries = RosterEntries(live);
        var wanted = entries.Select(e => e.CharacterId).ToHashSet();
        foreach (int gone in roomRoster.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            roomRoster[gone].Panel.QueueFree();
            roomRoster.Remove(gone);
        }
        if (!wanted.Contains(roomSelectedMember)) roomSelectedMember = NationChatRules.NoRoom;
        int owner = OwnerId();
        bool unknown = false;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!roomRoster.TryGetValue(entry.CharacterId, out var row))
            {
                row = BuildRosterRow(entry.CharacterId);
                roomRoster[entry.CharacterId] = row;
                roomRosterRows.AddChild(row.Panel);
            }
            roomRosterRows.MoveChild(row.Panel, i);
            unknown |= entry.Name.Length == 0;
            row.Name.Text = entry.Name.Length > 0 ? entry.Name : "...";
            row.Owner.Visible = entry.CharacterId == owner;
            row.InVoice = entry.InVoice;
            row.Panel.TooltipText = entry.CharacterId == context.Self.CharId ? "" : $"{entry.Name}: double-click to whisper, right-click for more";
            if (voice != null) RefreshRosterRow(entry.CharacterId, row, voice);
        }
        roomRosterEmpty.Visible = entries.Count == 0;
        if (unknown || entries.Count == 0) AskNames();
        RefreshLeader();
    }

    private void ClearRoster()
    {
        foreach (var row in roomRoster.Values) row.Panel.QueueFree();
        roomRoster.Clear();
        roomSelectedMember = NationChatRules.NoRoom;
    }

    private RosterRow BuildRosterRow(int id)
    {
        var style = UiTheme.Row();
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", style);
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 4);
        panel.AddChild(line);
        var dot = UiTheme.Text("●", 11, IdleDot);
        dot.MouseFilter = Control.MouseFilterEnum.Ignore;
        line.AddChild(dot);
        var name = UiTheme.Text("", 12, new Color(RoomNameColours.Of(id)));
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        line.AddChild(name);
        var owner = UiTheme.Text(OwnerMark, 11, UiTheme.GoldBright);
        owner.MouseFilter = Control.MouseFilterEnum.Ignore;
        owner.TooltipText = "Room leader";
        line.AddChild(owner);
        var speaker = UiIcons.Image("system/speaker-slash", new Vector2(11, 11), UiTheme.Bad);
        speaker.Visible = false;
        speaker.MouseFilter = Control.MouseFilterEnum.Pass;
        line.AddChild(speaker);
        var mark = UiIcons.Image("system/microphone-slash", new Vector2(MicMarkSize, MicMarkSize), UiTheme.Bad);
        mark.Visible = false;
        mark.MouseFilter = Control.MouseFilterEnum.Stop;
        mark.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || !CanSilence(id)) return;
            roomMediaRequests.VoiceSilence(id, !PeerSilenced(id));
            mark.AcceptEvent();
        };
        line.AddChild(mark);
        panel.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        panel.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true } click) return;
            if (click.ButtonIndex == MouseButton.Left)
            {
                SelectMember(id);
                if (click.DoubleClick && id != context.Self.CharId) whispers.OpenWhisperWith(NameOf(id));
            }
            else if (click.ButtonIndex == MouseButton.Right && id != context.Self.CharId)
            {
                SelectMember(id);
                OpenMemberMenu(id, click.GlobalPosition);
            }
        };
        return new RosterRow { Panel = panel, Dot = dot, Name = name, Owner = owner, Mark = mark, Speaker = speaker, Style = style };
    }

    internal void SelectMember(int id)
    {
        roomSelectedMember = id;
        if (VoiceChat.I is { } voice)
            foreach (var (rowId, row) in roomRoster) RefreshRosterRow(rowId, row, voice);
        RefreshLeader();
    }

    private void RefreshRosterRow(int id, RosterRow row, VoiceChat voice)
    {
        bool selected = id == roomSelectedMember;
        var baseStyle = UiTheme.Row(selected: selected);
        row.Style.BgColor = baseStyle.BgColor;
        row.Dot.Modulate = row.InVoice ? Colors.White : Colors.Transparent;
        if (!row.InVoice)
        {
            row.Style.BorderColor = baseStyle.BorderColor;
            row.Mark.Visible = false;
            row.Speaker.Visible = false;
            return;
        }
        bool isSelf = id == context.Self.CharId;
        byte flags = isSelf
            ? (byte)((voice.MicMuted ? VoiceFlags.MicMuted : 0) | (voice.Deafened ? VoiceFlags.Deafened : 0)
                | (roomMedia.Voice?.SelfFlags ?? 0) & (VoiceFlags.CannotSpeak | VoiceFlags.Silenced))
            : roomMedia.VoicePeers.GetValueOrDefault(id);
        bool speaking = isSelf ? voice.SelfSpeaking : voice.IsSpeaking(id);
        var mix = isSelf ? new VoiceChat.PeerMix(1f, false) : voice.MixOf(id);
        bool mutedForMe = mix.Muted || mix.Volume <= 0f;
        bool lit = speaking && !mutedForMe;
        row.Dot.AddThemeColorOverride("font_color", lit ? Speaking : IdleDot);
        row.Style.BorderColor = lit ? Speaking : baseStyle.BorderColor;
        bool silenced = (flags & VoiceFlags.Silenced) != 0;
        bool micOff = (flags & VoiceFlags.NoMicrophone) != 0;
        bool deaf = (flags & VoiceFlags.Deafened) != 0;
        bool control = CanSilence(id);
        row.Speaker.Visible = deaf || mutedForMe;
        row.Speaker.TooltipText = deaf ? "Not hearing the room" : "Muted for you";
        row.Mark.Visible = micOff || control;
        row.Mark.Texture = UiIcons.Get(micOff ? "system/microphone-slash" : "system/microphone");
        row.Mark.SelfModulate = silenced ? UiTheme.Warning : micOff ? UiTheme.Bad : UiTheme.TextLo;
        row.Mark.MouseDefaultCursorShape = control ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;
        string name = NameOf(id);
        row.Mark.TooltipText = control
            ? silenced ? $"Let {name} speak again" : $"Silence {name}'s microphone"
            : silenced ? "Silenced by the room leader" : "Microphone off";
    }

    private void RefreshLeader()
    {
        bool leader = nation.NationChat.InPrivate && nation.NationChat.Owner;
        int target = roomSelectedMember;
        bool picked = target > 0 && target != context.Self.CharId && roomRoster.ContainsKey(target);
        roomLeaderHandOver.Disabled = !leader || !picked;
        roomLeaderKick.Disabled = !leader || !picked;
    }

    private bool PeerSilenced(int id) => (roomMedia.VoicePeers.GetValueOrDefault(id) & VoiceFlags.Silenced) != 0;

    private bool CanSilence(int id) =>
        nation.NationChat.InPrivate && nation.NationChat.Owner && id != context.Self.CharId && roomRoster.TryGetValue(id, out var row) && row.InVoice;

    private void OpenMemberMenu(int id, Vector2 at)
    {
        if (roomMemberMenu == null)
        {
            roomMemberMenu = new PopupMenu();
            roomMemberMenu.IdPressed += pick =>
            {
                int who = roomMenuTarget;
                switch ((int)pick)
                {
                    case MenuWhisper: whispers.OpenWhisperWith(NameOf(who)); break;
                    case MenuVolume: OpenPeer(who); break;
                    case MenuPlayer: playerMenu.ShowByName(NameOf(who), context.Root.GetViewport().GetMousePosition()); break;
                }
            };
            roomLayer.AddChild(roomMemberMenu);
        }
        roomMenuTarget = id;
        roomMemberMenu.Clear();
        roomMemberMenu.AddItem("Whisper", MenuWhisper);
        bool inCall = roomRoster.TryGetValue(id, out var row) && row.InVoice;
        if (inCall && VoiceChat.I?.InVoice == true) roomMemberMenu.AddItem("Volume and mute...", MenuVolume);
        roomMemberMenu.AddItem("Player menu...", MenuPlayer);
        UiKit.PopupAtCursor(context.Root, roomMemberMenu, at);
    }

    private void TickVoice(VoiceChat voice)
    {
        foreach (var (id, row) in roomRoster) RefreshRosterRow(id, row, voice);
        if (RoomVoiceSettings is { Visible: true })
            RoomSettingsLevel.Value = voice.MicLevel;
    }

    private string NameOf(int id)
    {
        if (id == context.Self.CharId) return self.LastEnter.Name ?? "";
        if (nation.Members?.Members.FirstOrDefault(m => m.CharacterId == id) is { Name.Length: > 0 } member) return member.Name;
        var posts = roomMedia.RoomTimeline.Posts;
        for (int i = posts.Count - 1; i >= 0; i--)
            if (posts[i].SenderId == id && posts[i].Name.Length > 0) return posts[i].Name;
        return "";
    }

    private void AskNames()
    {
        double now = FrameClock.Now();
        if (!nation.NationChat.InPrivate || now - roomNamesAskedAt < NameRequestSeconds) return;
        roomNamesAskedAt = now;
        nationRequests.NationMembers();
    }

    private void OpenVoiceSettings()
    {
        FillVoiceSettings();
        UiKit.PopupAtCursor(context.Root, RoomVoiceSettings!, context.Root.GetViewport().GetMousePosition());
    }

    internal void FillVoiceSettings()
    {
        if (RoomVoiceSettings == null) BuildVoiceSettings();
        roomDeviceList.Clear();
        string[] devices = VoiceChat.InputDevices();
        int selected = 0;
        for (int i = 0; i < devices.Length; i++)
        {
            roomDeviceList.AddItem(devices[i]);
            if (devices[i] == Config.VoiceInputDevice) selected = i;
        }
        if (devices.Length > 0) roomDeviceList.Select(selected);
        roomThreshold.SetValueNoSignal(Config.VoiceThreshold);
        roomPushToTalk.SetPressedNoSignal(Config.VoicePushToTalk);
        roomPushToTalk.Text = $"Push to talk (hold {KeyBinds.Get(KeyAction.VoicePushToTalk).Text})";
        RoomSettingsLevel.Value = 0;
    }

    private void BuildVoiceSettings()
    {
        RoomVoiceSettings = new PopupPanel();
        RoomVoiceSettings.AddThemeStyleboxOverride("panel", PopupStyle());
        roomLayer.AddChild(RoomVoiceSettings);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(SettingsWidth, 0) };
        column.AddThemeConstantOverride("separation", 6);
        RoomVoiceSettings.AddChild(column);
        column.AddChild(UiTheme.SectionTitle("Voice settings"));
        column.AddChild(UiTheme.Text("Microphone", 12, UiTheme.TextLo));
        roomDeviceList = new OptionButton
        {
            FocusMode = Control.FocusModeEnum.None,
            FitToLongestItem = false,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(SettingsWidth, 0),
        };
        roomDeviceList.ItemSelected += index => SaveVoiceSettings();
        column.AddChild(roomDeviceList);
        column.AddChild(UiTheme.Text("Microphone threshold", 12, UiTheme.TextLo));
        RoomSettingsLevel = Meter(Speaking);
        column.AddChild(RoomSettingsLevel);
        roomThreshold = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, FocusMode = Control.FocusModeEnum.None };
        roomThreshold.ValueChanged += _ => SaveVoiceSettings();
        column.AddChild(roomThreshold);
        var hint = UiTheme.Text("Your voice is sent while the green bar passes the marker. Raise it if noise gets through.", 10, UiTheme.TextDim);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(SettingsWidth, 0);
        column.AddChild(hint);
        roomPushToTalk = UiTheme.FlatCheck("Push to talk", Config.VoicePushToTalk);
        roomPushToTalk.Toggled += _ => SaveVoiceSettings();
        column.AddChild(roomPushToTalk);
    }

    private static StyleBoxFlat PopupStyle()
    {
        var style = UiTheme.WindowPanel(radius: 3, alpha: 1f);
        style.ContentMarginLeft = style.ContentMarginRight = 10;
        style.ContentMarginTop = style.ContentMarginBottom = 8;
        return style;
    }

    private void SaveVoiceSettings()
    {
        string device = roomDeviceList.Selected >= 0 ? roomDeviceList.GetItemText(roomDeviceList.Selected) : Config.DefaultAudioDevice;
        Config.SetVoiceChat(Config.VoiceChatVolume, (float)roomThreshold.Value, roomPushToTalk.ButtonPressed, device);
        RefreshVoice();
    }

    private void OnSilence(RoomMediaReplies.SilenceNotice notice)
    {
        string target = notice.Target.Length > 0 ? notice.Target : NameOf(notice.TargetId);
        ChatRoomNotice((notice with { Target = target }).Text);
    }

    private void OpenConfig()
    {
        if (!nation.NationChat.InPrivate || !nation.NationChat.Owner) return;
        FillConfig();
        UiKit.PopupAtCursor(context.Root, RoomConfig!, context.Root.GetViewport().GetMousePosition());
    }

    internal void FillConfig()
    {
        var state = nation.NationChat;
        if (RoomConfig == null) BuildConfig();
        roomConfigName.Text = state.RoomName;
        roomConfigOption.Selected = state.RoomPrivate ? 1 : 0;
        roomConfigPassword.Text = state.RoomPassword;
        roomConfigSize.Selected = Math.Max(0, Array.IndexOf(NationChatRules.RoomSizes, state.RoomMax));
        roomConfigKeep.SetPressedNoSignal(state.Kept);
        roomConfigError.Visible = false;
        ApplyConfigOption();
    }

    private void BuildConfig()
    {
        RoomConfig = new PopupPanel();
        RoomConfig.AddThemeStyleboxOverride("panel", PopupStyle());
        roomLayer.AddChild(RoomConfig);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(SettingsWidth, 0) };
        column.AddThemeConstantOverride("separation", 6);
        RoomConfig.AddChild(column);
        column.AddChild(UiTheme.SectionTitle("Room settings"));
        column.AddChild(UiTheme.Text("Room name", 12, UiTheme.TextLo));
        roomConfigName = new LineEdit { MaxLength = NationChatRules.TitleMaxLength };
        roomConfigName.TextSubmitted += _ => SaveConfig();
        column.AddChild(roomConfigName);
        column.AddChild(UiTheme.Text("Options", 12, UiTheme.TextLo));
        roomConfigOption = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        roomConfigOption.AddItem(NationChannelsFeature.NationText(NationChatTexts.Public, "Public"));
        roomConfigOption.AddItem(NationChannelsFeature.NationText(NationChatTexts.Private, "Private"));
        roomConfigOption.ItemSelected += _ => ApplyConfigOption();
        column.AddChild(roomConfigOption);
        roomConfigPasswordTitle = UiTheme.Text("Password", 12, UiTheme.TextLo);
        column.AddChild(roomConfigPasswordTitle);
        roomConfigPassword = new LineEdit { MaxLength = NationChatRules.PasswordMaxLength, Secret = true };
        roomConfigPassword.TextSubmitted += _ => SaveConfig();
        column.AddChild(roomConfigPassword);
        column.AddChild(UiTheme.Text("Max admissions", 12, UiTheme.TextLo));
        roomConfigSize = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (int size in NationChatRules.RoomSizes) roomConfigSize.AddItem(size.ToString());
        column.AddChild(roomConfigSize);
        roomConfigKeep = UiTheme.FlatCheck("Keep the room when everyone leaves", false);
        roomConfigKeep.TooltipText = "The room stays in the list while empty, so members can come back to it later";
        roomConfigKeep.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        roomConfigKeep.CustomMinimumSize = new Vector2(SettingsWidth, 0);
        column.AddChild(roomConfigKeep);
        roomConfigError = UiTheme.Text("", 11, UiTheme.Bad);
        roomConfigError.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        roomConfigError.CustomMinimumSize = new Vector2(SettingsWidth, 0);
        roomConfigError.Visible = false;
        column.AddChild(roomConfigError);
        var save = UiTheme.ActionButton("Save", "");
        save.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        save.Pressed += SaveConfig;
        column.AddChild(save);
    }

    private void ApplyConfigOption()
    {
        bool isPrivate = roomConfigOption.Selected == 1;
        roomConfigPassword.Editable = isPrivate;
        roomConfigPasswordTitle.AddThemeColorOverride("font_color", isPrivate ? UiTheme.TextLo : UiTheme.TextDim);
        if (!isPrivate) roomConfigPassword.Text = "";
    }

    private void SaveConfig()
    {
        string title = roomConfigName.Text.Trim();
        bool isPrivate = roomConfigOption.Selected == 1;
        string password = roomConfigPassword.Text;
        string refusal = NationChatRules.Validate(title, isPrivate, password, NationChannelsFeature.NationText);
        roomConfigError.Text = refusal;
        roomConfigError.Visible = refusal.Length > 0;
        if (refusal.Length > 0) return;
        if (roomConfigKeep.ButtonPressed != nation.NationChat.Kept) roomMediaRequests.RoomKept(roomConfigKeep.ButtonPressed);
        nationRequests.Options(title, isPrivate, password, NationChatRules.RoomSizes[Math.Max(0, roomConfigSize.Selected)]);
    }

    private void ConfigReplied(byte code, int text)
    {
        if (RoomConfig is not { Visible: true }) return;
        if (code == 0)
        {
            RoomConfig.Hide();
            return;
        }
        roomConfigError.Text = text != 0 ? NationChannelsFeature.NationText(text, "") : "";
        roomConfigError.Visible = roomConfigError.Text.Length > 0;
    }

    private void OpenPeer(int id)
    {
        if (VoiceChat.I is not { } voice) return;
        if (roomPeerPopup == null) BuildPeerPopup();
        roomPeerId = id;
        var mix = voice.MixOf(id);
        roomPeerName.Text = NameOf(id);
        roomPeerName.AddThemeColorOverride("font_color", new Color(RoomNameColours.Of(id)));
        roomPeerVolume.SetValueNoSignal(mix.Volume);
        roomPeerMute.SetPressedNoSignal(mix.Muted);
        UiKit.PopupAtCursor(context.Root, roomPeerPopup!, context.Root.GetViewport().GetMousePosition());
    }

    private void BuildPeerPopup()
    {
        roomPeerPopup = new PopupPanel();
        roomPeerPopup.AddThemeStyleboxOverride("panel", PopupStyle());
        roomLayer.AddChild(roomPeerPopup);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(PeerWidth, 0) };
        column.AddThemeConstantOverride("separation", 6);
        roomPeerPopup.AddChild(column);
        roomPeerName = UiTheme.Text("", 13, UiTheme.TextHi);
        roomPeerName.AddThemeConstantOverride("font_embolden", 1);
        column.AddChild(roomPeerName);
        column.AddChild(UiTheme.Text("Volume", 12, UiTheme.TextLo));
        roomPeerVolume = new HSlider { MinValue = 0, MaxValue = VoiceLevels.MaxVolume, Step = 0.05, FocusMode = Control.FocusModeEnum.None };
        roomPeerVolume.ValueChanged += value => VoiceChat.I?.SetMix(roomPeerId, new VoiceChat.PeerMix((float)value, roomPeerMute.ButtonPressed));
        column.AddChild(roomPeerVolume);
        roomPeerMute = UiTheme.FlatCheck("Mute for me", false);
        roomPeerMute.Toggled += muted => VoiceChat.I?.SetMix(roomPeerId, new VoiceChat.PeerMix((float)roomPeerVolume.Value, muted));
        column.AddChild(roomPeerMute);
    }

    private void OnRoomNotice(RoomChatKind kind, int members, string name)
    {
        string line = kind switch
        {
            RoomChatKind.Entered => NationChannelsFeature.NationText(NationChatTexts.Entered, "%s has entered. (Current players: %d)"),
            RoomChatKind.Left => NationChannelsFeature.NationText(NationChatTexts.Left, "%s has left. (Current players: %d)"),
            RoomChatKind.NewOwner => NationChannelsFeature.NationText(NationChatTexts.NewOwner, "%s has become the room owner."),
            _ => NationChannelsFeature.NationText(NationChatTexts.Kicked, "%s has been kicked."),
        };
        ChatRoomNotice(line.Replace("%s", name).Replace("%d", members.ToString()));
        RefreshHeader();
        if (roomMedia.Voice != null || (nation.NationChat.InPrivate && Shown)) nationRequests.NationMembers();
    }

    private void OnNationResult(NationChatSubOpcode sub, byte code)
    {
        if (sub == NationChatSubOpcode.Options) ConfigReplied(code, NationChatRules.OptionsText(code));
    }

    private void OnPostChanged(RoomPost post)
    {
        if (GodotObject.IsInstanceValid(RoomThread)) RoomThread.Refresh(post);
    }
}
