using Godot;

namespace LibreKO.Features.Windows;

internal sealed class FriendsFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly FriendsSession friends;
    private readonly SelfSession self;
    private readonly IFriendsRequests requests;
    private readonly PlayerMenuFeature playerMenu;

    public FriendsFeature(IWorldContext context, FriendsSession friends, SelfSession self, IFriendsRequests requests, PlayerMenuFeature playerMenu)
    {
        this.context = context;
        this.friends = friends;
        this.self = self;
        this.requests = requests;
        this.playerMenu = playerMenu;
    }

    internal VBoxContainer Content { get; private set; } = null!;
    private VBoxContainer listBox = null!;
    private LineEdit friendAddInput = null!;
    private Label friendHeader = null!;
    private Label friendStatus = null!;
    private bool loaded;

    private static readonly Color FriendNoticeColour = new("ffff00");
    private static readonly Color FriendRefusalColour = new("ff0000");

    private bool PageVisible =>
        context.MainWindows.IsOpen("Character") && Content is { Visible: true };

    internal event Action<string>? WhisperRequested;

    public void Build()
    {
        BuildPanel();
        friends.FriendListEvent += OnFriendList;
        friends.FriendAddResultEvent += OnFriendAdd;
        friends.FriendRemoveResultEvent += OnFriendRemove;
        friends.FriendLoggedInEvent += OnFriendLoggedIn;
        friends.FriendRequestEvent += OnFriendRequest;
        friends.FriendRequestResultEvent += OnFriendRequestResult;
    }

    public void Dispose()
    {
        friends.FriendListEvent -= OnFriendList;
        friends.FriendAddResultEvent -= OnFriendAdd;
        friends.FriendRemoveResultEvent -= OnFriendRemove;
        friends.FriendLoggedInEvent -= OnFriendLoggedIn;
        friends.FriendRequestEvent -= OnFriendRequest;
        friends.FriendRequestResultEvent -= OnFriendRequestResult;
    }

    internal void RequestFriend(string name)
    {
        requests.FriendAdd(name);
        FriendLine(ItemData.Text(FriendTexts.RequestSentText, "You have sent a friend request to %s.").Replace("%s", name), FriendNoticeColour);
    }

    private void FriendLine(string text, Color colour)
    {
        if (text.Length == 0) return;
        context.Log.Add(text, CombatLogKind.Status, colour);
        if (PageVisible) SetFriendStatus(text, colour == FriendRefusalColour);
    }

    private void OnFriendLoggedIn(string name)
    {
        FriendLine(ItemData.Text(FriendTexts.LoggedInText, "%s has logged in.").Replace("%s", name), FriendNoticeColour);
        requests.FriendListRequest();
    }

    private void OnFriendRequest(string requester)
    {
        bool answered = false;
        var ask = Notice.Confirm(context.Root,
            ItemData.Text(FriendTexts.RequestAskText, "[%s] sent a friend request. Will you accept?").Replace("%s", requester),
            "OK", "Cancel",
            () => { answered = true; requests.FriendAnswer(requester, true); },
            () => { answered = true; requests.FriendAnswer(requester, false); },
            title: "Friends");
        context.Root.After(FriendTexts.RequestSeconds, () =>
        {
            if (answered || !GodotObject.IsInstanceValid(ask)) return;
            answered = true;
            requests.FriendAnswer(requester, false);
            ask.QueueFree();
        });
    }

    private void OnFriendRequestResult(FriendRequestResult code)
    {
        if (FriendTexts.RequestResultText(code) is { } textId)
            FriendLine(ItemData.Text(textId, ""), FriendRefusalColour);
    }

    internal void BuildPanel()
    {
        var root = new VBoxContainer { CustomMinimumSize = new Vector2(UiKit.CharacterPageWidth, 0) };
        root.AddThemeConstantOverride("separation", 8);
        Content = root;

        friendHeader = UiTheme.Text("", 12, UiTheme.TextLo);
        root.AddChild(friendHeader);

        var addRow = new HBoxContainer();
        addRow.AddThemeConstantOverride("separation", 6);
        friendAddInput = new LineEdit { PlaceholderText = "Character name", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 20 };
        friendAddInput.TextSubmitted += _ => DoFriendAdd();
        addRow.AddChild(friendAddInput);
        var addBtn = new Button { Text = "Add", FocusMode = Control.FocusModeEnum.None };
        addBtn.Pressed += DoFriendAdd;
        addRow.AddChild(addBtn);
        root.AddChild(addRow);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(1, 300),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        root.AddChild(scroll);
        listBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        listBox.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(listBox);

        root.AddChild(new HSeparator());
        var footer = new HBoxContainer();
        friendStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        friendStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(friendStatus);
        var refresh = new Button { Text = "Refresh", FocusMode = Control.FocusModeEnum.None };
        refresh.Pressed += () => requests.FriendListRequest();
        footer.AddChild(refresh);
        root.AddChild(footer);
    }

    internal void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        requests.FriendListRequest();
    }

    private void DoFriendAdd()
    {
        string name = friendAddInput.Text.Trim();
        if (name.Length == 0) return;
        if (name == (self.LastEnter.Name ?? "")) { SetFriendStatus("You can't add yourself.", true); return; }
        RequestFriend(name);
        friendAddInput.Clear();
    }

    private void OnFriendList(List<FriendEntry> list)
    {
        Refresh();
        context.Entities.RefreshNameColours();
    }

    private void OnFriendAdd(FriendAddResult code, string name, FriendEntry entry)
    {
        if (FriendTexts.AddResultText(code) is { } textId)
            FriendLine(ItemData.Text(textId, ""), code == FriendAddResult.Succeeded ? FriendNoticeColour : FriendRefusalColour);
    }

    private void OnFriendRemove(FriendRemoveResult code, string name)
    {
        if (FriendTexts.RemoveResultText(code) is { } textId)
            FriendLine(ItemData.Text(textId, ""), FriendRefusalColour);
    }

    internal void Refresh()
    {
        foreach (var c in listBox.GetChildren()) c.QueueFree();
        int online = 0;
        foreach (var f in friends.Friends.OrderByDescending(friend => friend.Status))
        {
            if (f.IsOnline) online++;
            listBox.AddChild(BuildFriendRow(f));
        }
        if (friends.Friends.Count == 0)
            listBox.AddChild(UiTheme.Text("No friends yet. Add one above.", 12, UiTheme.TextLo, HorizontalAlignment.Center));
        friendHeader.Text = $"Friends  ({online} online)";
        PluginHost.Game.RaiseFriends();
    }

    private Control BuildFriendRow(FriendEntry friend)
    {
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiTheme.Row());
        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 6);
        row.AddChild(hb);

        var dot = UiTheme.Text("●", 12, friend.InParty ? UiTheme.Neutral : friend.IsOnline ? UiTheme.Good : UiTheme.TextDim);
        hb.AddChild(dot);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", -2);
        hb.AddChild(text);

        var name = UiTheme.Text(friend.Name, 13, friend.IsOnline ? UiTheme.TextHi : UiTheme.TextLo);
        text.AddChild(name);
        text.AddChild(UiTheme.Text(FriendDetailLine(friend), 11, UiTheme.TextDim));

        string n = friend.Name;
        var whisper = new Button
        {
            Text = "Whisper",
            TooltipText = $"Whisper {friend.Name}",
            FocusMode = Control.FocusModeEnum.None,
            Disabled = !friend.IsOnline,
        };
        whisper.AddThemeFontSizeOverride("font_size", 10);
        whisper.Pressed += () => WhisperRequested?.Invoke(n);
        hb.AddChild(whisper);

        var party = new Button
        {
            Text = "Party",
            TooltipText = friend.InParty ? $"{friend.Name} is already in a party" : $"Invite {friend.Name} to your party",
            FocusMode = Control.FocusModeEnum.None,
            Disabled = !friend.IsOnline || friend.InParty,
        };
        party.AddThemeFontSizeOverride("font_size", 10);
        party.Pressed += () => playerMenu.InviteToParty(n);
        hb.AddChild(party);

        var rm = UiTheme.IconButton("×", $"Remove {friend.Name}");
        rm.CustomMinimumSize = new Vector2(22, 22);
        rm.Pressed += () => requests.FriendRemove(n);
        hb.AddChild(rm);
        return row;
    }

    private static string FriendDetailLine(FriendEntry friend)
    {
        var parts = new List<string>(4);
        if (friend.Level > 0) parts.Add($"Lv {friend.Level}");
        if (friend.Class > 0) parts.Add(CharacterClassCatalog.DisplayName(friend.Class));
        if (friend.Nation is Nations.Karus or Nations.ElMorad) parts.Add(Nations.Name(friend.Nation));

        if (friend.InParty) parts.Add("in party");
        else if (!friend.IsOnline) parts.Add("offline");
        else parts.Add(FriendZoneName(friend.ZoneId));

        return string.Join("   ·   ", parts);
    }

    internal static string FriendZoneName(int zoneId)
    {
        if (zoneId <= 0) return "online";
        foreach (var zone in ZoneCatalog.All)
            if (zone.Id == zoneId) return zone.Name;
        return $"zone {zoneId}";
    }

    internal void SetFriendStatus(string text, bool warn)
    {
        friendStatus.Text = text;
        friendStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }
}
