using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int WhisperBubbleWrapWidth = 208;
    private const int WhisperWrapThreshold = 34;
    private const int WhisperBodyWidth = 300;
    private const int WhisperLogHeight = 210;
    private const float WhisperBlinkDim = 0.42f;
    private const float WhisperBlinkStep = 0.32f;
    private const float WhisperBottomSlack = 8f;
    private static readonly Color WhisperBlue = new("2f80d4");
    private static readonly Color WhisperBlueEdge = new("5aa6e8");
    private static readonly Color WhisperBlueText = new("eaf3fd");
    private static readonly Vector2 WhisperFirstPos = new(430, 300);
    private static readonly Vector2 WhisperCascade = new(28, 26);

    private sealed class WhisperChat
    {
        public string Name = "";
        public HudWindow Window = null!;
        public VBoxContainer Log = null!;
        public ScrollContainer Scroll = null!;
        public LineEdit Input = null!;
        public bool StickBottom = true;
        public Tween? Blink;
    }

    private CanvasLayer _whisperLayer = null!;
    private readonly Dictionary<string, WhisperChat> _whispers = new(StringComparer.OrdinalIgnoreCase);
    private int _whisperOpened;

    private void WhisperInit()
    {
        _whisperLayer = new CanvasLayer { Layer = 79 };
        AddChild(_whisperLayer);

        Net.I.ChatEvent += OnWhisperChat;
        Chat.WhisperEcho += OnWhisperEcho;
        Chat.WhisperNotice += OnWhisperNotice;
        Chat.WhisperOpened += OnWhisperOpened;
    }

    private void WhisperDispose()
    {
        Net.I.ChatEvent -= OnWhisperChat;
        Chat.WhisperEcho -= OnWhisperEcho;
        Chat.WhisperNotice -= OnWhisperNotice;
        Chat.WhisperOpened -= OnWhisperOpened;
    }

    private static StyleBoxFlat WhisperButtonStyle(Color fill, Color edge)
    {
        var sb = new StyleBoxFlat { BgColor = fill, BorderColor = edge };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(3);
        sb.ContentMarginLeft = sb.ContentMarginRight = 12;
        sb.ContentMarginTop = sb.ContentMarginBottom = 4;
        return sb;
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

    private WhisperChat GetOrCreateWhisper(string name, bool minimized)
    {
        if (_whispers.TryGetValue(name, out var existing)) return existing;

        var chat = new WhisperChat { Name = name };

        var offset = WhisperFirstPos + WhisperCascade * (_whisperOpened % 6);
        _whisperOpened++;

        chat.Window = new HudWindow(
            $"whisper_{name}", name, offset, WhisperBodyWidth,
            persistLayout: false, minimizable: true)
        {
            Visible = true,
        };
        chat.Window.AddThemeStyleboxOverride("panel", WhisperPanelStyle());
        chat.Window.SetHeaderAccent(WhisperBlue, WhisperBlueEdge, WhisperBlueText);
        chat.Window.Closed += () => CloseWhisper(name);
        chat.Window.MinimizedChanged += isMinimized =>
        {
            if (isMinimized || !_whispers.TryGetValue(name, out var restored)) return;
            StopWhisperBlink(restored);
            restored.StickBottom = true;
            ScrollWhisperToEnd(restored);
            restored.Input.CallDeferred(Control.MethodName.GrabFocus);
        };
        _whisperLayer.AddChild(chat.Window);

        var root = chat.Window.Body;
        root.AddThemeConstantOverride("separation", 6);

        chat.Scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(WhisperBodyWidth, WhisperLogHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        chat.Scroll.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        root.AddChild(chat.Scroll);

        chat.Log = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        chat.Log.AddThemeConstantOverride("separation", 4);
        chat.Scroll.AddChild(chat.Log);
        chat.Scroll.GetVScrollBar().Changed += () => { if (chat.StickBottom) ScrollWhisperToEnd(chat); };

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        root.AddChild(row);

        chat.Input = new LineEdit
        {
            PlaceholderText = "Message",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MaxLength = 128,
            KeepEditingOnTextSubmit = true,
        };
        chat.Input.TextSubmitted += _ => SendWhisperFrom(name);
        row.AddChild(chat.Input);

        var send = new Button { Text = "Send", FocusMode = Control.FocusModeEnum.None };
        send.AddThemeColorOverride("font_color", WhisperBlueText);
        send.AddThemeColorOverride("font_hover_color", Colors.White);
        send.AddThemeColorOverride("font_pressed_color", Colors.White);
        send.AddThemeStyleboxOverride("normal", WhisperButtonStyle(WhisperBlue, WhisperBlueEdge));
        send.AddThemeStyleboxOverride("hover", WhisperButtonStyle(WhisperBlue.Lightened(0.12f), WhisperBlueEdge));
        send.AddThemeStyleboxOverride("pressed", WhisperButtonStyle(WhisperBlue.Darkened(0.14f), WhisperBlueEdge));
        send.Pressed += () => SendWhisperFrom(name);
        row.AddChild(send);

        _whispers[name] = chat;
        foreach (var line in Net.I.WhisperHistory(name))
            chat.Log.AddChild(BuildWhisperRow(line.Mine, line.Notice, line.Text));
        ScrollWhisperToEnd(chat);
        if (minimized) chat.Window.SetMinimized(true);
        return chat;
    }

    private void OpenWhisperWith(string name)
    {
        if (name.Length == 0) return;
        var chat = GetOrCreateWhisper(name, minimized: false);
        chat.Window.Visible = true;
        chat.Window.SetMinimized(false);
        StopWhisperBlink(chat);
        chat.StickBottom = true;
        ScrollWhisperToEnd(chat);
        chat.Input.GrabFocus();
    }

    private void CloseWhisper(string name)
    {
        if (!_whispers.Remove(name, out var chat)) return;
        StopWhisperBlink(chat);
        chat.Window.QueueFree();
    }

    private void SendWhisperFrom(string name)
    {
        if (!_whispers.TryGetValue(name, out var chat)) return;
        string message = chat.Input.Text.Trim();
        if (message.Length == 0) return;

        Chat.SendWhisper(name, message);
        chat.Input.Clear();
        if (!chat.Input.HasFocus()) chat.Input.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void FocusWhisperAt(Vector2 screenPos)
    {
        foreach (var chat in _whispers.Values)
        {
            if (!GodotObject.IsInstanceValid(chat.Window) || !chat.Window.Visible) continue;
            if (chat.Window.Minimized || !chat.Window.GetGlobalRect().HasPoint(screenPos)) continue;
            if (!chat.Input.HasFocus()) chat.Input.CallDeferred(Control.MethodName.GrabFocus);
            return;
        }
    }

    private void OnWhisperChat(ChatLine line)
    {
        if (line.Type != ChatSystem.WhisperChannel || line.Name.Length == 0) return;
        if (string.Equals(line.Name, Net.I.LastEnter.Name, StringComparison.OrdinalIgnoreCase)) return;

        bool isNew = !_whispers.ContainsKey(line.Name);
        var chat = GetOrCreateWhisper(line.Name, minimized: isNew);
        chat.Window.Visible = true;
        AppendWhisper(chat, mine: false, notice: false, line.Message);

        if (chat.Window.Minimized) StartWhisperBlink(chat);
    }

    private void OnWhisperEcho(string name, string message)
    {
        var chat = GetOrCreateWhisper(name, minimized: false);
        chat.Window.Visible = true;
        AppendWhisper(chat, mine: true, notice: false, message);
    }

    private void OnWhisperOpened(string name) => OpenWhisperWith(name);

    private void OnWhisperNotice(string name, string text)
    {
        if (name.Length == 0 || !_whispers.TryGetValue(name, out var chat))
        {
            CombatNotice(text);
            return;
        }
        AppendWhisper(chat, mine: false, notice: true, text);
    }

    private void AppendWhisper(WhisperChat chat, bool mine, bool notice, string text)
    {
        Net.I.RecordWhisper(chat.Name, mine, notice, text);
        chat.StickBottom = mine || WhisperAtBottom(chat);

        chat.Log.AddChild(BuildWhisperRow(mine, notice, text));
        while (chat.Log.GetChildCount() > Net.WhisperLogMax)
        {
            var first = chat.Log.GetChild(0);
            chat.Log.RemoveChild(first);
            first.QueueFree();
        }

        ScrollWhisperToEnd(chat);
    }

    private static bool WhisperAtBottom(WhisperChat chat)
    {
        if (!GodotObject.IsInstanceValid(chat.Scroll)) return true;
        var bar = chat.Scroll.GetVScrollBar();
        if (bar.MaxValue <= bar.Page) return true;
        return bar.Value + bar.Page >= bar.MaxValue - WhisperBottomSlack;
    }

    private static void ScrollWhisperToEnd(WhisperChat chat)
    {
        if (!GodotObject.IsInstanceValid(chat.Scroll)) return;
        chat.Scroll.ScrollVertical = (int)chat.Scroll.GetVScrollBar().MaxValue;
    }

    private static Control BuildWhisperRow(bool mine, bool notice, string text)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.Alignment = notice
            ? BoxContainer.AlignmentMode.Center
            : mine ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Begin;

        if (notice)
        {
            row.AddChild(UiTheme.Text(text, 11, UiTheme.Warning));
            return row;
        }

        var bubble = new PanelContainer();
        bubble.AddThemeStyleboxOverride("panel", WhisperBubbleStyle(mine));

        var label = UiTheme.Text(text, 12, mine ? new Color("fff0fa") : UiTheme.TextHi);
        if (text.Length > WhisperWrapThreshold)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(WhisperBubbleWrapWidth, 0);
        }
        bubble.AddChild(label);
        row.AddChild(bubble);
        return row;
    }

    private static StyleBoxFlat WhisperBubbleStyle(bool mine)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = mine
                ? new Color(0.42f, 0.20f, 0.36f, 0.60f)
                : new Color(0.17f, 0.18f, 0.21f, 0.66f),
            BorderColor = mine
                ? new Color(0.72f, 0.42f, 0.66f, 0.50f)
                : new Color(0.48f, 0.48f, 0.52f, 0.45f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(5);
        sb.ContentMarginLeft = sb.ContentMarginRight = 8;
        sb.ContentMarginTop = sb.ContentMarginBottom = 4;
        return sb;
    }

    private void StartWhisperBlink(WhisperChat chat)
    {
        if (chat.Blink != null && chat.Blink.IsValid()) return;
        var tween = chat.Window.CreateTween().SetLoops();
        tween.TweenProperty(chat.Window, "modulate:a", WhisperBlinkDim, WhisperBlinkStep);
        tween.TweenProperty(chat.Window, "modulate:a", 1f, WhisperBlinkStep);
        chat.Blink = tween;
    }

    private static void StopWhisperBlink(WhisperChat chat)
    {
        if (chat.Blink != null && chat.Blink.IsValid()) chat.Blink.Kill();
        chat.Blink = null;
        chat.Window.Modulate = Colors.White;
    }

    private bool AnyWhisperExpanded()
    {
        foreach (var chat in _whispers.Values)
            if (IsWhisperExpanded(chat)) return true;
        return false;
    }

    private void MinimizeAllWhispers()
    {
        foreach (var chat in _whispers.Values)
            if (IsWhisperExpanded(chat)) chat.Window.SetMinimized(true);
    }

    private static bool IsWhisperExpanded(WhisperChat chat) =>
        GodotObject.IsInstanceValid(chat.Window) && chat.Window.Visible && !chat.Window.Minimized;

    private bool WhisperInputHasFocus()
    {
        if (GetViewport().GuiGetFocusOwner() is not LineEdit focused) return false;
        foreach (var chat in _whispers.Values)
            if (chat.Input == focused) return true;
        return false;
    }

    private bool TryMinimizeFocusedWhisper()
    {
        var focus = GetViewport().GuiGetFocusOwner();
        for (Node? n = focus; n != null; n = n.GetParent())
        {
            if (n is not HudWindow window) continue;
            foreach (var chat in _whispers.Values)
            {
                if (chat.Window != window || window.Minimized) continue;
                window.SetMinimized(true);
                focus?.ReleaseFocus();
                return true;
            }
            return false;
        }
        return false;
    }
}
