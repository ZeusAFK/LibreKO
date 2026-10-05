using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

internal sealed partial class ChatSystem
{
    private readonly IWorldContext _ctx;

    private HBoxContainer _frame = null!;
    private VBoxContainer _root = null!;
    private PanelContainer _logPanel = null!;
    private Button? _peek;
    private RichTextLabel? _peekText;
    private bool _chatExpanded;

    private const int ChatLayer = 66;
    private const int NoticeLayer = 69;
    private const float PeekWidth = 404f;
    private const float PeekHeight = 46f;
    private const float PeekPad = 12f;
    private const float PeekGlyphSize = 26f;
    private const float EdgeMargin = 12f;
    private const int NoticeTop = 60;
    private const double NoticeBaseSeconds = 3.5;
    private const double NoticeSecondsPerChar = 0.04;
    private const double NoticeMinSeconds = 4.0;
    private const double NoticeMaxSeconds = 12.0;
    private static readonly Vector2 DefaultSize = new(490, 238);
    private static readonly Vector2 MinimumSize = new(330, 170);
    private static readonly Vector2 ResizeGripOffset = new(0, 23);

    private HudLogText _scroll = null!;
    private HBoxContainer _inputRow = null!;
    private LineEdit _input = null!;
    private StyleBoxFlat _panelStyle = null!;
    private HudLayout _layout = null!;
    private bool _active;

    private CanvasLayer _noticeLayer = null!;
    private Label _noticeLabel = null!;
    private readonly NoticeQueue _notices = new();
    private int _noticeToken;

    internal const byte WhisperChannel = ChatType.Private;
    private const byte GeneralChannel = ChatType.General;
    private const byte ClanChannel = ChatType.Clan;
    private const byte ChatRoomChannel = ChatType.ChatRoom;
    private const byte ClanRecruitChannel = ChatType.ClanRecruit;
    private const byte CommanderChannel = ChatType.Command;

    private byte _sendChannel = GeneralChannel;
    private string _whisperName = "";
    private string? _pendingWhisper;
    private string _pendingWhisperTo = "";
    private string _lastWhisperFrom = "";

    private static readonly HashSet<string> ReplyCommands = new(StringComparer.OrdinalIgnoreCase) { "r", "reply" };
    private static readonly HashSet<string> WhisperCommands = new(StringComparer.OrdinalIgnoreCase) { "w", "tell", "whisper" };
    private static readonly HashSet<string> CommandsOpenToEveryone = new(StringComparer.OrdinalIgnoreCase) { "setlevel" };

    private readonly ChatPrefs _prefs = ChatPrefs.Parse(Config.ChatLook);
    private ChatColors _colors = ChatColors.Parse(Config.ChatColors);

    internal ChatSystem(IWorldContext ctx) => _ctx = ctx;

    internal Func<string, bool>? LocalCommand;
    internal Action<string, Vector2>? NameMenu;
    internal Action<int>? ItemTipShow;
    internal Action? ItemTipHide;
    internal Func<string, bool>? IsFriend;
    internal Action? ColorsRequested;
    internal Action<string>? StatusNotice;
    internal event Action? LayoutChanged;

    internal bool IsActive => _active || PluginTyping;

    internal bool PluginTyping { get; set; }

    internal Control Panel => _frame;

    private static bool DocksNearby => !Platform.TouchUi;

    private static readonly Vector2 NearbyColumnWidth = new(NearbyDock.Width + NearbyDock.Gap, 0);

    private Vector2 NearbyColumn => DocksNearby && _prefs.NearbyShown ? NearbyColumnWidth : Vector2.Zero;

    private Vector2 FrameDefaultSize => DefaultSize + NearbyColumn;

    private Vector2 FrameMinimumSize => MinimumSize + NearbyColumn;

    private NearbyCard? _nearby;

    internal void DockNearby(NearbyCard list)
    {
        _nearby = list;
        list.CustomMinimumSize = new Vector2(NearbyDock.Width, 0);
        list.Visible = _prefs.NearbyShown;
        _frame.AddChild(list);
        _frame.MoveChild(list, 0);
        ApplyBackground();
    }

    internal void PreviewNearbyShown(bool shown) => SetNearbyShown(shown, save: false);

    private void SetNearbyShown(bool shown, bool save = true)
    {
        if (_prefs.NearbyShown == shown) return;
        _prefs.NearbyShown = shown;
        if (save) SavePrefs();
        if (_nearby == null || _layout == null) return;
        _nearby.Visible = shown;
        _layout.MinimumSize = FrameMinimumSize;
        Vector2 shift = shown ? -NearbyColumnWidth : NearbyColumnWidth;
        _layout.Place(
            new Vector2(Mathf.Max(0, _frame.Position.X + shift.X), _frame.Position.Y),
            _frame.Size - shift);
    }

    internal ChatColors Colors => _colors;

    private static string ChanTag(byte type) => type switch
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
        _ => "",
    };

    internal void SendText(string text) => Submit(text);

    internal void Build()
    {
        var layer = new CanvasLayer { Layer = ChatLayer };
        _ctx.Root.AddChild(layer);
        if (PluginHost.Ui.HudHidden(LibreKO.Plugins.HudPart.Chat))
        {
            layer.Visible = false;
            if (PluginHost.Ui.HudReplacement(LibreKO.Plugins.HudPart.Chat) is { } build)
            {
                var pluginLayer = new CanvasLayer { Layer = ChatLayer };
                _ctx.Root.AddChild(pluginLayer);
                pluginLayer.AddChild(build());
            }
        }

        _frame = new HBoxContainer { Size = FrameDefaultSize };
        _frame.AddThemeConstantOverride("separation", (int)NearbyDock.Gap);
        layer.AddChild(_frame);
        _root = new VBoxContainer { CustomMinimumSize = MinimumSize, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _root.AddThemeConstantOverride("separation", 4);
        _frame.AddChild(_root);

        _root.AddChild(BuildTabBar());
        BuildLogPanel();
        BuildInputRow();
        SetInputRowActive(false);

        if (Platform.TouchUi) BuildChatPeek();
        AttachLayout(_frame, Platform.TouchUi ? null : DefaultSpot);
        _frame.Resized += () => LayoutChanged?.Invoke();
        ApplyLook();

        BuildNoticeBanner();

        Net.I.ChatEvent += OnChat;
        Net.I.ChatTargetEvent += OnChatTarget;
        Net.I.NoticeEvent += OnNotice;
    }

    internal void DetachNetwork()
    {
        Net.I.ChatEvent -= OnChat;
        Net.I.ChatTargetEvent -= OnChatTarget;
        Net.I.NoticeEvent -= OnNotice;
    }

    internal void Dispose() => DetachNetwork();

    private Vector2 DefaultSpot()
    {
        float height = _frame.IsInsideTree() ? _frame.GetViewportRect().Size.Y : FrameDefaultSize.Y + EdgeMargin * 2;
        return new Vector2(EdgeMargin, height - EdgeMargin - Mathf.Max(_frame.Size.Y, FrameMinimumSize.Y));
    }

    internal void AttachLayout(Control target, Func<Vector2>? floating = null, bool persist = true)
    {
        target.Modulate = Godot.Colors.White;
        _layout = HudLayout.Attach(
            target, persist ? "hud_chat" : "uilab_actual_chat", floating != null ? _moveTool : null, floating,
            resizable: !Platform.TouchUi,
            defaultSize: FrameDefaultSize,
            minimumSize: Platform.TouchUi ? Vector2.Zero : FrameMinimumSize,
            persist: persist,
            resizeCorner: HudLayout.Corner.TopRight,
            resizeGripOffset: ResizeGripOffset,
            anchor: floating == null ? HudPlacement.ChatAnchor : null,
            anchorMargin: HudPlacement.ChatMargin,
            moveGripOverlay: false);
        _layout.Locked = _prefs.Locked;
        _layout.Placed += () => LayoutChanged?.Invoke();
    }

    private void BuildNoticeBanner()
    {
        _noticeLayer = new CanvasLayer { Layer = NoticeLayer, Visible = false };
        _ctx.Root.AddChild(_noticeLayer);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = NoticeTop,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", World.QuestToastStyle());
        _noticeLayer.AddChild(panel);
        NoticePanel = panel;

        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 6);
        panel.AddChild(rows);
        rows.AddChild(World.QuestToastRule());
        var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiTheme.Margins(m, 58, 3, 58, 3);
        rows.AddChild(m);
        _noticeLabel = UiTheme.Text("", 16, UiTheme.GoldBright, HorizontalAlignment.Center);
        _noticeLabel.AddThemeConstantOverride("font_embolden", 1);
        _noticeLabel.AddThemeConstantOverride("outline_size", 4);
        _noticeLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        m.AddChild(_noticeLabel);
        rows.AddChild(World.QuestToastRule());
    }

    internal Control NoticePanel { get; private set; } = null!;

    internal void ShowNoticePreview(string msg) => OnNotice(msg);

    private void OnNotice(string msg)
    {
        AddEntry(ChatEntry.Plain(ChatCategory.Notice, NoticeType, msg));
        if (_notices.Enqueue(msg)) ShowNotice(msg);
    }

    private const byte NoticeType = ChatType.WarSystem;

    private void ShowNotice(string msg)
    {
        _noticeLabel.Text = msg;
        _noticeLayer.Visible = true;
        int token = ++_noticeToken;
        double secs = Mathf.Clamp(NoticeBaseSeconds + msg.Length * NoticeSecondsPerChar, NoticeMinSeconds, NoticeMaxSeconds);
        if (!_ctx.Root.IsInsideTree()) return;
        _ctx.Root.GetTree().CreateTimer(secs).Timeout += () =>
        {
            if (_noticeToken != token || _noticeLayer == null || !GodotObject.IsInstanceValid(_noticeLayer)) return;
            if (_notices.Advance() is { } next) ShowNotice(next);
            else _noticeLayer.Visible = false;
        };
    }

    internal void Open()
    {
        if (PluginHost.Ui.HudHidden(HudPart.Chat))
        {
            PluginHost.Game.RaiseChatInputRequested();
            return;
        }
        _active = true;
        SetInputRowActive(true);
        UpdateInputColour();
        _input.GrabFocus();
        _input.CaretColumn = _input.Text.Length;
        UpdateCounter();
    }

    internal void OpenWith(string text)
    {
        Open();
        ShowTyped(text);
    }

    internal void Close()
    {
        _active = false;
        _input.ReleaseFocus();
        _input.Clear();
        _lastInputText = "";
        _linkDraft.Clear();
        ApplyInputLimit();
        _history.StopBrowsing();
        SetInputRowActive(false);
        UpdateCounter();
    }

    private void Suspend()
    {
        if (!_active || _settingsMenu.Visible || _input.HasFocus()) return;
        if (_input.Text.Length == 0)
        {
            Close();
            return;
        }
        _active = false;
        SetInputRowActive(false);
        UpdateCounter();
    }

    private void SetInputRowActive(bool active)
    {
        bool clickToType = !Platform.TouchUi;
        _inputRow.Modulate = active ? Godot.Colors.White : new Color(1, 1, 1, clickToType ? IdleInputAlpha : 0);
        _inputRow.MouseFilter = active || clickToType ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
        _input.MouseFilter = active || clickToType ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        _input.Editable = active;
        _input.FocusMode = active ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        ApplyInputStyle();
    }

    internal void SetPreviewState(bool inputActive, byte channel)
    {
        _active = inputActive;
        SetChannel(channel);
        SetInputRowActive(inputActive);
        if (inputActive) UpdateInputColour();
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
        if (tail.Length > 0) ShowTyped(_input.Text + tail);
    }

    internal void PreviewScrolledUp(IEnumerable<ChatLine> fresh)
    {
        _scroll.ScrollToLine(0);
        foreach (var line in fresh) OnChat(line);
    }

    internal void SetChannel(byte channel)
    {
        _sendChannel = channel;
        UpdateInputColour();
    }

    private void Submit(string text)
    {
        string visible = text.TrimEnd();
        if (visible.Length == 0) { Close(); return; }
        _history.Push(visible);
        string wire = _linkDraft.ToWire(visible);

        if (visible[0] == '/' && LocalCommand?.Invoke(visible.Substring(1).Trim()) == true) { Close(); return; }
        if (visible[0] == '/' && RunSlashCommand(visible.Substring(1).Trim())) { Close(); return; }

        var (chan, body, target) = Parse(wire);

        if (target.Length > 0)
        {
            _pendingWhisper = body.Length > 0 ? body : null;
            _pendingWhisperTo = target;
            Net.I.SendChatTarget(target);
        }
        else if (chan == WhisperChannel)
        {
            if (_whisperName.Length == 0)
                Info("No whisper target. Use @name message to start a whisper.");
            else if (body.Length > 0)
            {
                Net.I.SendChat(body, WhisperChannel);
                AppendWhisperEcho(_whisperName, body);
            }
        }
        else if (body.Length > 0)
        {
            if (chan == CommanderChannel && Net.I.MyClan.Fame != ClanRanks.CommandCaptain)
                Info("Only war captains can use commander chat.");
            else if (chan == ChatRoomChannel)
                Net.I.SendChatRoomSay(body);
            else if (chan == ClanRecruitChannel)
                Info("Clan recruitment chat is not available yet.");
            else
                Net.I.SendChat(body, chan);
        }

        Close();
    }

    private bool RunSlashCommand(string command)
    {
        int sp = command.IndexOf(' ');
        string word = sp < 0 ? command : command[..sp];
        string args = sp < 0 ? "" : command[(sp + 1)..].Trim();

        if (ReplyCommands.Contains(word))
        {
            if (_lastWhisperFrom.Length == 0) Info("No one has whispered you yet.");
            else if (args.Length == 0) WhisperOpened?.Invoke(_lastWhisperFrom);
            else SendWhisper(_lastWhisperFrom, args);
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

        if (Net.I.IsGm || CommandsOpenToEveryone.Contains(word)) return false;
        Info($"Unknown command: /{word}");
        return true;
    }

    internal Action<string, string>? WhisperEcho;
    internal Action<string, string>? WhisperNotice;
    internal Action<string>? WhisperOpened;

    internal void SendWhisper(string target, string message)
    {
        if (target.Length == 0 || message.Length == 0) return;
        _pendingWhisper = message;
        _pendingWhisperTo = target;
        Net.I.SendChatTarget(target);
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
        return prefixed == ChatPrefixes.NotChat ? (_sendChannel, text, "") : (prefixed, rest, "");
    }

    private void OnChat(ChatLine line)
    {
        if (line.Type == WhisperChannel)
        {
            if (line.Name.Length == 0 || IsSelf(line.Name)) return;
            _lastWhisperFrom = line.Name;
            var side = IsFriend?.Invoke(line.Name) == true ? WhisperSide.ReceivedFromFriend : WhisperSide.Received;
            AddEntry(ChatEntry.Whisper(side, line.Name, line.Nation, line.IsGm, line.Message));
            return;
        }

        if (line.CharId < 0 && line.Name.Length == 0)
        {
            var category = ChatCategories.Of(line.Type, false);
            if (category == ChatCategory.Notice) StatusNotice?.Invoke(line.Message);
            else AddEntry(ChatEntry.Plain(category, line.Type, line.Message));
            return;
        }

        string text = Understandable(line) ? line.Message : Garble(line.Message);
        AddEntry(ChatEntry.Player(line.Type, line.Name, line.Nation, line.IsGm, text));
        ShowBubble(line.CharId, line.Type, PlainText(text));
    }

    private static bool IsSelf(string name) =>
        string.Equals(name, Net.I.LastEnter.Name, StringComparison.OrdinalIgnoreCase);

    private static bool Understandable(ChatLine line)
        => line.IsGm
        || line.Nation == 0
        || line.Nation == Net.I.Nation
        || Net.I.CurrentZoneAbility.CanTalk
        || line.Type is not (ChatType.General or ChatType.Shout or ChatType.Merchant);

    private static string Garble(string message)
    {
        var scrambled = new char[message.Length];
        for (int i = 0; i < scrambled.Length; i++)
            scrambled[i] = (char)(GD.Randi() % 10 + 33);
        return new string(scrambled);
    }

    private void OnChatTarget(int result, string name)
    {
        switch (result)
        {
            case Net.ChatTargetConnected:
                _whisperName = name;
                if (_pendingWhisper is { } msg)
                {
                    Net.I.SendChat(msg, WhisperChannel);
                    AppendWhisperEcho(name, msg);
                    _pendingWhisper = null;
                }
                WhisperOpened?.Invoke(name);
                break;
            case Net.ChatTargetNotFound:
                WhisperNotice?.Invoke(_pendingWhisperTo, "Player not found.");
                _pendingWhisper = null;
                break;
            case Net.ChatTargetBlocked:
                WhisperNotice?.Invoke(_pendingWhisperTo, "That player is blocking whispers.");
                _pendingWhisper = null;
                break;
            case Net.ChatTargetCrossNation:
                WhisperNotice?.Invoke(_pendingWhisperTo, "You cannot whisper between nations in this zone.");
                _pendingWhisper = null;
                break;
            case Net.ChatTargetSenderBlocked:
                WhisperNotice?.Invoke(_pendingWhisperTo, "You have blocked whispers.");
                _pendingWhisper = null;
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
        _peek = new Button
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
            _peek.AddThemeStyleboxOverride(state, skin);

        var bubble = new ChatBubbleGlyph
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(PeekPad, (PeekHeight - PeekGlyphSize) * 0.5f),
            Size = Vector2.One * PeekGlyphSize,
        };
        _peek.AddChild(bubble);

        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        centre.OffsetLeft = PeekPad * 2f + PeekGlyphSize;
        centre.OffsetRight = -PeekPad;
        _peek.AddChild(centre);

        _peekText = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = false,
            FitContent = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(PeekWidth - PeekPad * 3f - PeekGlyphSize, 0f),
        };
        _peekText.AddThemeFontSizeOverride("normal_font_size", 15);
        centre.AddChild(_peekText);

        _peek.Pressed += () => SetChatExpanded(!_chatExpanded);
        _root.AddChild(_peek);
        _root.MoveChild(_peek, 0);
        SetChatExpanded(false);
    }

    private void SetChatExpanded(bool expanded)
    {
        _chatExpanded = expanded;
        if (_peek == null) return;
        _tabBar.Visible = expanded;
        _logPanel.Visible = expanded;
        _peek.Visible = !expanded;
    }

    private void UpdateChatPeek(string bbcode)
    {
        if (_peekText == null) return;
        _peekText.Text = bbcode;
    }

    private void Info(string msg) => StatusNotice?.Invoke(msg);

    internal void AppendShout(string message) => AddEntry(ChatEntry.Plain(ChatCategory.Shout, ChatType.Shout, message));
}
