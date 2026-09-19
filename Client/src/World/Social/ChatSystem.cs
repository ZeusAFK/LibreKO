using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

internal sealed class ChatSystem
{
    private readonly IWorldContext _ctx;

    private VBoxContainer _root = null!;
    private HBoxContainer _tabBar = null!;
    private PanelContainer _logPanel = null!;
    private Button? _peek;
    private RichTextLabel? _peekText;
    private bool _chatExpanded;

    private const float PeekWidth = 404f;
    private const float PeekHeight = 46f;
    private const float PeekPad = 12f;
    private const float PeekGlyphSize = 26f;
    private HudLogText _scroll = null!;
    private HBoxContainer _inputRow = null!;
    private LineEdit _input = null!;
    private Label _chanLabel = null!;
    private StyleBoxFlat _panelStyle = null!;
    private HudLayout _layout = null!;
    private bool _active;
    private const float RestAlpha = 0.50f;
    private float _backgroundAlpha = RestAlpha;

    private CanvasLayer _noticeLayer = null!;
    private Label _noticeLabel = null!;
    private int _noticeToken;

    internal const byte WhisperChannel = 2;
    private const byte ChatRoomChannel = 33;
    private const byte ClanRecruitChannel = 34;

    private byte _sendChannel = 1;
    private string _whisperName = "";
    private string? _pendingWhisper;
    private string _pendingWhisperTo = "";

    private readonly Queue<string> _log = new();
    private readonly Dictionary<byte, Button> _channelButtons = new();
    private const int LogMax = 200;

    internal ChatSystem(IWorldContext ctx) => _ctx = ctx;

    internal Func<string, bool>? LocalCommand;

    internal bool IsActive => _active;

    internal Control Panel => _root;

    private static (string Tag, string Col) ChanStyle(byte type) => type switch
    {
        1  => ("",         "e8e8e8"),
        2  => ("whisper",  "ff7ad9"),
        3  => ("party",    "5fd95f"),
        5  => ("shout",    "ff9a3c"),
        6  => ("clan",     "46d3c0"),
        7  => ("GM",       "ffe24a"),
        8  => ("GM",       "ffe24a"),
        12 => ("GM",       "ffe24a"),
        13 => ("nation",   "c8e66b"),
        14 => ("trade",    "d2b48c"),
        15 => ("alliance", "6fb7ff"),
        19 => ("zone",     "9aa0a6"),
        23 => ("officer",  "b48cff"),
        _  => ("",         "e8e8e8"),
    };

    private static string NationCol(int nation, bool isGm) =>
        isGm ? "ffd24a" : nation switch { 1 => "e06666", 2 => "6fa8ff", _ => "dddddd" };

    private static string ChanName(byte type) => type switch
    {
        1 => "General", 2 => "Whisper", 3 => "Party", 5 => "Shout", 6 => "Clan",
        13 => "Nation", 15 => "Alliance", 23 => "Officer", _ => "General",
    };

    internal void Build()
    {
        var layer = new CanvasLayer { Layer = 66 };
        _ctx.Root.AddChild(layer);

        _root = new VBoxContainer
        {
            Size = new Vector2(490, 238),
            CustomMinimumSize = new Vector2(330, 170),
        };
        _root.AddThemeConstantOverride("separation", 4);
        layer.AddChild(_root);

        var bar = new HBoxContainer();
        _tabBar = bar;
        bar.AddThemeConstantOverride("separation", 4);
        _root.AddChild(bar);
        foreach (var (label, type) in new (string, byte)[]
                 { ("All", 1), ("Shout", 5), ("Party", 3), ("Clan", 6), ("Nation", 13), ("Alliance", 15) })
        {
            byte t = type;
            var b = new Button
            {
                Text = label,
                FocusMode = Control.FocusModeEnum.None,
                ToggleMode = true,
            };
            b.AddThemeFontSizeOverride("font_size", 11);
            b.AddThemeColorOverride("font_color", new Color("#d4d5d7"));
            b.AddThemeColorOverride("font_hover_color", Colors.White);
            b.AddThemeColorOverride("font_pressed_color", new Color("#f0c879"));
            b.AddThemeStyleboxOverride("normal", ChannelButtonStyle(
                new Color(0.025f, 0.028f, 0.034f, 0.88f), new Color(0.34f, 0.37f, 0.41f, 0.65f)));
            b.AddThemeStyleboxOverride("hover", ChannelButtonStyle(
                new Color(0.055f, 0.060f, 0.070f, 0.94f), new Color(0.68f, 0.71f, 0.75f, 0.85f)));
            b.AddThemeStyleboxOverride("pressed", ChannelButtonStyle(
                new Color(0.095f, 0.082f, 0.050f, 0.98f), new Color("#c7984b")));
            b.Pressed += () => SetChannel(t);
            bar.AddChild(b);
            _channelButtons[t] = b;
        }
        RefreshChannelButtons();

        bar.AddChild(new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var dim = new Button
        {
            Text = "◐",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Background opacity",
        };
        dim.AddThemeFontSizeOverride("font_size", 11);
        dim.AddThemeColorOverride("font_color", new Color("#d4d5d7"));
        dim.AddThemeColorOverride("font_hover_color", Colors.White);
        dim.AddThemeStyleboxOverride("normal", ChannelButtonStyle(
            new Color(0.025f, 0.028f, 0.034f, 0.88f), new Color(0.34f, 0.37f, 0.41f, 0.65f)));
        dim.AddThemeStyleboxOverride("hover", ChannelButtonStyle(
            new Color(0.055f, 0.060f, 0.070f, 0.94f), new Color(0.68f, 0.71f, 0.75f, 0.85f)));
        dim.Pressed += () => _layout.CycleBackgroundOpacity();
        bar.AddChild(dim);

        var panel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _logPanel = panel;
        _panelStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, RestAlpha) };
        foreach (var s in new[] { "left", "right", "top", "bottom" }) _panelStyle.Set($"content_margin_{s}", 7f);
        _panelStyle.SetCornerRadiusAll(6);
        _panelStyle.SetBorderWidthAll(1);
        _panelStyle.BorderColor = new Color(UiTheme.Edge, 0.25f);
        panel.AddThemeStyleboxOverride("panel", _panelStyle);
        _root.AddChild(panel);

        _scroll = new HudLogText
        {
            ScrollbarOnLeft = true,
            BbcodeEnabled = true,
            ScrollActive = true,
            ScrollFollowing = true,
            FitContent = false,
            SelectionEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 110),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _scroll.AddThemeFontSizeOverride("normal_font_size", 13);
        panel.AddChild(_scroll);

        _inputRow = new HBoxContainer();
        _inputRow.AddThemeConstantOverride("separation", 6);
        _root.AddChild(_inputRow);
        _inputRow.AddChild(new Control
        {
            CustomMinimumSize = new Vector2(18, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _chanLabel = new Label { VerticalAlignment = VerticalAlignment.Center };
        _chanLabel.AddThemeFontSizeOverride("font_size", 13);
        _inputRow.AddChild(_chanLabel);
        _input = new LineEdit
        {
            MaxLength = 128,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            PlaceholderText = "Enter to chat — @name whisper · ! shout · # party · $ clan · % nation · & alliance · / command",
        };
        _input.TextSubmitted += Submit;
        _inputRow.AddChild(_input);
        SetInputRowActive(false);

        if (Platform.TouchUi) BuildChatPeek();
        AttachLayout(_root);

        Info("Welcome to LibreKO. Press Enter to chat.");

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

    private static StyleBoxFlat ChannelButtonStyle(Color background, Color edge)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = edge };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(5);
        style.ContentMarginLeft = style.ContentMarginRight = 9;
        style.ContentMarginTop = style.ContentMarginBottom = 4;
        return style;
    }

    internal void AttachLayout(Control target, Func<Vector2>? floating = null, bool persist = true)
    {
        target.Modulate = Colors.White;
        _layout = HudLayout.Attach(
            target, persist ? "hud_chat" : "uilab_actual_chat", null, floating,
            resizable: !Platform.TouchUi,
            defaultSize: new Vector2(490, 238),
            minimumSize: Platform.TouchUi ? Vector2.Zero : new Vector2(330, 170),
            persist: persist,
            resizeCorner: HudLayout.Corner.TopRight,
            moveCorner: HudLayout.Corner.BottomLeft,
            moveGripAlwaysVisible: floating != null,
            resizeGripOffset: new Vector2(0, 23),
            backgroundOpacityChanged: alpha =>
            {
                _backgroundAlpha = alpha;
                _panelStyle.BgColor = new Color(0, 0, 0, alpha);
                GD.Print($"[hud] chat black background opacity={alpha:0.00}");
            },
            anchor: floating == null ? HudPlacement.ChatAnchor : null,
            anchorMargin: HudPlacement.ChatMargin);
    }

    private void BuildNoticeBanner()
    {
        _noticeLayer = new CanvasLayer { Layer = 69, Visible = false };
        _ctx.Root.AddChild(_noticeLayer);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = 96,
        };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(7, true));
        _noticeLayer.AddChild(panel);

        var m = new MarginContainer();
        UiTheme.Margins(m, 18, 9, 18, 9);
        panel.AddChild(m);
        _noticeLabel = UiTheme.Text("", 16, UiTheme.GoldBright, HorizontalAlignment.Center);
        _noticeLabel.AddThemeConstantOverride("outline_size", 5);
        m.AddChild(_noticeLabel);
    }

    private void OnNotice(string msg)
    {
        Info(msg);
        _noticeLabel.Text = msg;
        _noticeLayer.Visible = true;
        int token = ++_noticeToken;
        double secs = Mathf.Clamp(3.5 + msg.Length * 0.04, 4.0, 12.0);
        _ctx.Root.GetTree().CreateTimer(secs).Timeout += () => { 
            if (_noticeToken == token && _noticeLayer != null && GodotObject.IsInstanceValid(_noticeLayer)) 
                _noticeLayer.Visible = false; 
        };
    }

    internal void Open()
    {
        _active = true;
        SetInputRowActive(true);
        _panelStyle.BgColor = new Color(0, 0, 0, _backgroundAlpha);
        UpdateChanIndicator();
        _input.Clear();
        _input.GrabFocus();
    }

    internal void Close()
    {
        _active = false;
        _input.ReleaseFocus();
        _input.Clear();
        SetInputRowActive(false);
        _panelStyle.BgColor = new Color(0, 0, 0, _backgroundAlpha);
    }

    private void SetInputRowActive(bool active)
    {
        _inputRow.Modulate = active ? Colors.White : new Color(1, 1, 1, 0);
        _inputRow.MouseFilter = active
            ? Control.MouseFilterEnum.Pass
            : Control.MouseFilterEnum.Ignore;
        _input.MouseFilter = active
            ? Control.MouseFilterEnum.Stop
            : Control.MouseFilterEnum.Ignore;
        _input.Editable = active;
        _input.FocusMode = active
            ? Control.FocusModeEnum.All
            : Control.FocusModeEnum.None;
    }

    internal void SetPreviewState(bool inputActive, byte channel)
    {
        _active = inputActive;
        SetChannel(channel);
        SetInputRowActive(inputActive);
        if (inputActive) UpdateChanIndicator();
    }

    internal void SetChannel(byte channel)
    {
        _sendChannel = channel;
        RefreshChannelButtons();
        if (_active) UpdateChanIndicator();
    }

    private void RefreshChannelButtons()
    {
        foreach (var (channel, button) in _channelButtons)
            button.SetPressedNoSignal(channel == _sendChannel);
    }

    private void UpdateChanIndicator()
    {
        var (_, col) = ChanStyle(_sendChannel);
        string name = _sendChannel == WhisperChannel && _whisperName.Length > 0 ? $"To {_whisperName}" : ChanName(_sendChannel);
        _chanLabel.Text = $"[{name}]";
        _chanLabel.AddThemeColorOverride("font_color", new Color("#" + col));
    }

    private void Submit(string text)
    {
        text = text.TrimEnd();
        if (text.Length == 0) { Close(); return; }

        if ((text[0] == '/' || text[0] == '+') && LocalCommand?.Invoke(text.Substring(1).Trim()) == true) { Close(); return; }

        var (chan, body, target) = Parse(text);

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
            if (chan == ChatRoomChannel)
                Net.I.SendChatRoomSay(body);
            else if (chan == ClanRecruitChannel)
                Info("Clan recruitment chat is not available yet.");
            else
                Net.I.SendChat(body, chan);
        }

        Close();
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
            case '!': return (5, rest, "");
            case '#': return (3, rest, "");
            case '$': return (6, rest, "");
            case '%': return (13, rest, "");
            case '&': return (15, rest, "");
            case '~': return (23, rest, "");
            case '|': return (34, rest, "");
            case '\\': return (33, rest, "");
            case '/': return (1, "+" + text.Substring(1), "");
            case '+': return (1, text, "");
            default:  return (_sendChannel, text, "");
        }
    }

    private void OnChat(ChatLine line)
    {
        if (line.Type == WhisperChannel) return;

        string text = Understandable(line) ? line.Message : Garble(line.Message);

        var (tag, col) = ChanStyle(line.Type);
        string nameCol = NationCol(line.Nation, line.IsGm);
        var b = new System.Text.StringBuilder();
        if (tag.Length > 0) b.Append($"[color=#{col}][lb]{tag}[rb] [/color]");
        b.Append($"[color=#{nameCol}]{BbCode.Esc(line.Name)}[/color]");
        b.Append(line.Type == WhisperChannel ? "[color=#ff7ad9] » [/color]" : ": ");
        b.Append($"[color=#{col}]{BbCode.Esc(text)}[/color]");
        Append(b.ToString());

        ShowBubble(line.CharId, line.Type, text);
    }

    private static bool Understandable(ChatLine line)
        => line.IsGm
        || line.Nation == 0
        || line.Nation == Net.I.Nation
        || Net.I.CurrentZoneAbility.CanTalk
        || line.Type is not (1 or 5 or 14);

    private static string Garble(string message)
    {
        var scrambled = new char[message.Length];
        for (int i = 0; i < scrambled.Length; i++)
            scrambled[i] = (char)(GD.Randi() % 10 + 33);
        return new string(scrambled);
    }

    private const double BubbleDur = 6.0;
    private const double BubbleFade = 1.2;
    private const float BubbleHeadY = 2.35f;
    private const float BubbleWrap = 360f;

    private sealed class Bubble
    {
        public Node3D Holder = null!;
        public Label3D Label = null!;
        public StandardMaterial3D? BgMat, BorderMat;
        public double Start;
        public bool Sized;
    }

    private readonly Dictionary<int, Bubble> _bubbles = new();

    private readonly List<int> _expiredBubbles = new();

    private void ShowBubble(int charId, byte type, string message)
    {
        Node3D? host = _ctx.BodyOf(charId);
        if (host == null || message.Length == 0) return;

        if (_bubbles.TryGetValue(charId, out var old))
        {
            if (GodotObject.IsInstanceValid(old.Holder)) old.Holder.QueueFree();
            _bubbles.Remove(charId);
        }

        var col = new Color("#" + ChanStyle(type).Col);
        var holder = new Node3D { Position = new Vector3(0, BubbleHeadY, 0) };
        var label = new Label3D
        {
            Text = message,
            Width = BubbleWrap,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 48,
            OutlineSize = 10,
            PixelSize = 0.005f,
            Modulate = col,
            OutlineModulate = new Color(0, 0, 0, 0.95f),
            RenderPriority = 12,
        };
        holder.AddChild(label);
        host.AddChild(holder);
        _bubbles[charId] = new Bubble { Holder = holder, Label = label, Start = _ctx.Now };
    }

    internal void TickBubbles(double now)
    {
        if (_bubbles.Count == 0) return;
        var done = _expiredBubbles;
        done.Clear();
        foreach (var (id, b) in _bubbles)
        {
            if (!GodotObject.IsInstanceValid(b.Holder) || !GodotObject.IsInstanceValid(b.Label))
            { done.Add(id); continue; }

            double t = now - b.Start;
            if (t >= BubbleDur) { b.Holder.QueueFree(); done.Add(id); continue; }

            if (!b.Sized) SizeBubble(b);

            float a = t > BubbleDur - BubbleFade
                ? Mathf.Clamp((float)((BubbleDur - t) / BubbleFade), 0f, 1f) : 1f;
            var m = b.Label.Modulate; m.A = a; b.Label.Modulate = m;
            var o = b.Label.OutlineModulate; o.A = 0.95f * a; b.Label.OutlineModulate = o;
            if (b.BgMat != null) { var c = b.BgMat.AlbedoColor; c.A = 0.62f * a; b.BgMat.AlbedoColor = c; }
            if (b.BorderMat != null) { var c = b.BorderMat.AlbedoColor; c.A = 0.92f * a; b.BorderMat.AlbedoColor = c; }
        }
        foreach (var id in done) _bubbles.Remove(id);
    }

    private void SizeBubble(Bubble b)
    {
        var aabb = b.Label.GetAabb();
        if (aabb.Size.X <= 0f || aabb.Size.Y <= 0f) return;

        var cx = aabb.Position.X + aabb.Size.X / 2f;
        var cy = aabb.Position.Y + aabb.Size.Y / 2f;
        const float pad = 0.09f, frame = 0.035f;
        var col = b.Label.Modulate;

        b.BorderMat = BubbleMat(new Color(col.R, col.G, col.B, 0.92f), 10);
        b.Holder.AddChild(BubbleQuad(b.BorderMat,
            aabb.Size.X + (pad + frame) * 2f, aabb.Size.Y + (pad + frame) * 2f, cx, cy, -0.002f));

        b.BgMat = BubbleMat(new Color(0.05f, 0.05f, 0.07f, 0.62f), 11);
        b.Holder.AddChild(BubbleQuad(b.BgMat,
            aabb.Size.X + pad * 2f, aabb.Size.Y + pad * 2f, cx, cy, -0.001f));

        b.Sized = true;
    }

    private static StandardMaterial3D BubbleMat(Color c, int renderPriority) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = c,
        AlbedoTexture = BubbleTexture(),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        RenderPriority = renderPriority,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
    };

    private static ImageTexture? _bubbleTex;
    private static ImageTexture BubbleTexture()
    {
        if (_bubbleTex != null) return _bubbleTex;
        const int s = 96;
        const float r = 18f;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        float h = s * 0.5f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - h) - (h - r), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - h) - (h - r), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy) - r;
                float a = Mathf.Clamp(0.5f - dist, 0f, 1f);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        _bubbleTex = ImageTexture.CreateFromImage(img);
        return _bubbleTex;
    }

    private static MeshInstance3D BubbleQuad(Material mat, float w, float h, float cx, float cy, float z) => new()
    {
        Mesh = new QuadMesh { Size = new Vector2(w, h), CenterOffset = new Vector3(cx, cy, z) },
        MaterialOverride = mat,
    };

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

    private void AppendWhisperEcho(string name, string msg) => WhisperEcho?.Invoke(name, msg);

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

    internal void Info(string msg) => Append($"[color=#ffe24a]{BbCode.Esc(msg)}[/color]");

    internal void Append(string bbcode)
    {
        _log.Enqueue(bbcode);
        UpdateChatPeek(bbcode);
        if (_log.Count > LogMax)
        {
            while (_log.Count > LogMax) _log.Dequeue();
            _scroll.Text = string.Join("\n", _log);
        }
        else
        {
            if (_scroll.GetParsedText().Length > 0) _scroll.AppendText("\n");
            _scroll.AppendText(bbcode);
        }
    }
}
