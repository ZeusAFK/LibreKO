using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _chatRoomLayer = null!;
    private HudWindow _chatRoomPanel = null!;
    private VBoxContainer _chatRoomList = null!;
    private VBoxContainer _chatRoomLog = null!;
    private ScrollContainer _chatRoomLogScroll = null!;
    private LineEdit _chatRoomNameInput = null!;
    private LineEdit _chatRoomSayInput = null!;
    private Label _chatRoomStatus = null!;
    private bool _chatRoomShown;
    private int _chatRoomCurrentId;

    private void ChatRoomInit()
    {
        _chatRoomLayer = new CanvasLayer { Layer = 68 };
        AddChild(_chatRoomLayer);
        _chatRoomPanel = new HudWindow("chatrooms", "Chat Rooms", new Vector2(200, 120)) { Visible = false };
        _chatRoomPanel.Closed += CloseChatRoom;
        _chatRoomLayer.AddChild(_chatRoomPanel);

        var root = _chatRoomPanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        root.AddChild(UiTheme.SectionTitle("Rooms"));
        var listScroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, 150), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(listScroll);
        _chatRoomList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _chatRoomList.AddThemeConstantOverride("separation", 3);
        listScroll.AddChild(_chatRoomList);

        var createRow = new HBoxContainer(); createRow.AddThemeConstantOverride("separation", 6);
        root.AddChild(createRow);
        _chatRoomNameInput = new LineEdit { PlaceholderText = "New room name", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 30 };
        _chatRoomNameInput.TextSubmitted += _ => DoChatRoomCreate();
        createRow.AddChild(_chatRoomNameInput);
        var createBtn = new Button { Text = "Create", FocusMode = Control.FocusModeEnum.None };
        createBtn.Pressed += DoChatRoomCreate;
        createRow.AddChild(createBtn);
        var refreshBtn = new Button { Text = "Refresh", FocusMode = Control.FocusModeEnum.None };
        refreshBtn.Pressed += () => Net.I.SendChatRoomList();
        createRow.AddChild(refreshBtn);

        _chatRoomStatus = UiTheme.Text("Not in a room.", 12, UiTheme.TextLo);
        root.AddChild(_chatRoomStatus);

        root.AddChild(UiTheme.SectionTitle("Chat"));
        _chatRoomLogScroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, 140), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(_chatRoomLogScroll);
        _chatRoomLog = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _chatRoomLog.AddThemeConstantOverride("separation", 2);
        _chatRoomLogScroll.AddChild(_chatRoomLog);

        var sayRow = new HBoxContainer(); sayRow.AddThemeConstantOverride("separation", 6);
        root.AddChild(sayRow);
        _chatRoomSayInput = new LineEdit { PlaceholderText = "Message", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 128 };
        _chatRoomSayInput.TextSubmitted += _ => DoChatRoomSay();
        sayRow.AddChild(_chatRoomSayInput);
        var sayBtn = new Button { Text = "Send", FocusMode = Control.FocusModeEnum.None };
        sayBtn.Pressed += DoChatRoomSay;
        sayRow.AddChild(sayBtn);
        var leaveBtn = new Button { Text = "Leave", FocusMode = Control.FocusModeEnum.None };
        leaveBtn.Pressed += () => Net.I.SendChatRoomLeave();
        sayRow.AddChild(leaveBtn);

        Net.I.ChatRoomListEvent += OnChatRoomList;
        Net.I.ChatRoomCreateEvent += OnChatRoomCreate;
        Net.I.ChatRoomJoinEvent += OnChatRoomJoin;
        Net.I.ChatRoomLeaveEvent += OnChatRoomLeave;
        Net.I.ChatRoomSayEvent += OnChatRoomSay;
    }

    private void ChatRoomDispose()
    {
        Net.I.ChatRoomListEvent -= OnChatRoomList;
        Net.I.ChatRoomCreateEvent -= OnChatRoomCreate;
        Net.I.ChatRoomJoinEvent -= OnChatRoomJoin;
        Net.I.ChatRoomLeaveEvent -= OnChatRoomLeave;
        Net.I.ChatRoomSayEvent -= OnChatRoomSay;
    }

    private void ToggleChatRoom()
    {
        if (_chatRoomShown) { CloseChatRoom(); return; }
        _chatRoomPanel.Visible = true;
        _chatRoomShown = true;
        Net.I.SendChatRoomList();
    }

    private void CloseChatRoom()
    {
        if (!_chatRoomShown) return;
        _chatRoomShown = false;
        _chatRoomPanel.Visible = false;
    }

    private void DoChatRoomCreate()
    {
        string name = _chatRoomNameInput.Text.Trim();
        if (name.Length == 0) return;
        Net.I.SendChatRoomCreate(name);
        _chatRoomNameInput.Text = "";
    }

    private void DoChatRoomSay()
    {
        string text = _chatRoomSayInput.Text.Trim();
        if (text.Length == 0) return;
        if (_chatRoomCurrentId == 0) return;
        Net.I.SendChatRoomSay(text);
        _chatRoomSayInput.Text = "";
    }

    private void OnChatRoomList(List<ChatRoomEntry> list)
    {
        foreach (var c in _chatRoomList.GetChildren()) c.QueueFree();
        foreach (var r in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row(selected: r.RoomId == _chatRoomCurrentId));
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var name = UiTheme.Text(r.Name, 13, UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(name);
            hb.AddChild(UiTheme.Text($"{r.MemberCount}", 12, UiTheme.Gold));
            if (r.RoomId == _chatRoomCurrentId)
            {
                hb.AddChild(UiTheme.Text("Joined", 12, UiTheme.Gold));
            }
            else
            {
                int id = r.RoomId;
                var btn = new Button { Text = "Join", FocusMode = Control.FocusModeEnum.None };
                btn.Pressed += () => Net.I.SendChatRoomJoin(id);
                hb.AddChild(btn);
            }
            _chatRoomList.AddChild(row);
        }
        if (_chatRoomList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No rooms yet — create one.";
            _chatRoomList.AddChild(e);
        }
    }

    private void OnChatRoomCreate(int roomId, bool ok)
    {
        if (ok)
        {
            _chatRoomCurrentId = roomId;
            UpdateChatRoomStatus();
            AppendChatRoomLine("System", "Room created. You can chat now.", UiTheme.Gold);
            Net.I.SendChatRoomList();
        }
    }

    private void OnChatRoomJoin(int roomId, bool ok)
    {
        if (ok)
        {
            _chatRoomCurrentId = roomId;
            UpdateChatRoomStatus();
            AppendChatRoomLine("System", "Joined the room.", UiTheme.Gold);
            Net.I.SendChatRoomList();
        }
        else
        {
            Net.I.SendChatRoomList();
        }
    }

    private void OnChatRoomLeave(bool ok)
    {
        if (ok)
        {
            AppendChatRoomLine("System", "Left the room.", UiTheme.TextLo);
            _chatRoomCurrentId = 0;
            UpdateChatRoomStatus();
        }
        Net.I.SendChatRoomList();
    }

    private void OnChatRoomSay(int roomId, string sender, string text)
    {
        AppendChatRoomLine(sender, text, UiTheme.TextHi);
    }

    private void AppendChatRoomLine(string sender, string text, Color color)
    {
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 5);
        line.AddChild(UiTheme.Text($"{sender}:", 12, UiTheme.Gold));
        var body = UiTheme.Text(text, 12, color);
        body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(body);
        _chatRoomLog.AddChild(line);
        while (_chatRoomLog.GetChildCount() > 100)
            _chatRoomLog.GetChild(0).QueueFree();
        var scroll = _chatRoomLogScroll;
        Callable.From(() => scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue).CallDeferred();
    }

    private void UpdateChatRoomStatus()
    {
        _chatRoomStatus.Text = _chatRoomCurrentId == 0 ? "Not in a room." : $"In room #{_chatRoomCurrentId}.";
    }
}
