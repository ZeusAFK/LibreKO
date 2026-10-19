using Godot;

namespace LibreKO.Features.Social;

internal sealed class NationChannelsFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly NationChatSession nation;
    private readonly INationChatRequests nationRequests;
    private readonly Action showRoom;
    private readonly EntityPlatesSystem entityPlates;

    public NationChannelsFeature(IWorldContext context, NationChatSession nationChat, INationChatRequests nationChatRequests, Action showRoom, EntityPlatesSystem entityPlates)
    {
        this.context = context;
        nation = nationChat;
        nationRequests = nationChatRequests;
        this.showRoom = showRoom;
        this.entityPlates = entityPlates;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (!NationShown) return false;
        CloseNation();
        return true;
    }

    private const float NationRowHeight = 22f;
    private const int NationListWidth = 440;
    private const int NationBarGap = 6;
    private const float NationSearchWidth = 180f;
    private const int NationSmallWidth = 250;
    private const string NationRowTip = "Double-click to enter";
    private static readonly Color NationMine = new("00ff00");
    private static readonly Color NationRefreshWait = new("ff3b3b");

    private CanvasLayer ncLayer = null!;
    internal HudWindow NcList { get; private set; } = null!;
    internal bool NationShown { get; set; }
    private bool ncOnPublic;
    private Button ncPublicTab = null!;
    private Button ncRefresh = null!;
    private Button ncPrivateTab = null!;
    private VBoxContainer ncPublicPage = null!;
    private VBoxContainer ncPrivatePage = null!;
    private VBoxContainer ncPublicRows = null!;
    private VBoxContainer ncPrivateRows = null!;
    private Button ncPubJoin = null!;
    private Button ncPubLeave = null!;
    private Button ncPubOpen = null!;
    private List<NationChannel> ncChannels = [];
    private List<NationRoom> ncRooms = [];
    internal int NationSelectedChannel { get; set; } = NationChatRules.NoRoom;
    internal int NationSelectedRoom { get; set; } = NationChatRules.NoRoom;
    private ulong ncPublicRefreshAt;
    private ulong ncPrivateRefreshAt;

    internal HudWindow NcCreate { get; private set; } = null!;
    internal LineEdit NcCreateTitle { get; private set; } = null!;
    internal LineEdit NcCreatePassword { get; private set; } = null!;
    private Label ncCreatePasswordTitle = null!;
    internal OptionButton NcCreateOption { get; private set; } = null!;
    internal OptionButton NcCreateSize { get; private set; } = null!;

    private HudWindow ncPassword = null!;
    private LineEdit ncPasswordEdit = null!;
    private LineEdit ncSearchEdit = null!;

    internal static string NationText(int id, string fallback) => ItemData.Text(id, fallback);

    public void Build()
    {
        ncLayer = new CanvasLayer { Layer = UiLayers.Announcements };
        ncLayer.SetMeta(Audio.QuietMeta, true);
        context.Root.AddChild(ncLayer);
        BuildNationChannelList();
        BuildNationCreate();
        BuildNationPassword();

        nation.NationChannelsEvent += OnNationChannels;
        nation.NationRoomsEvent += OnNationRooms;
        nation.NationSearchEvent += OnNationSearch;
        nation.NationChatResultEvent += OnNationResult;
        nation.NationChatStateEvent += RefreshNationButtons;
    }

    public void Dispose()
    {
        nation.NationChannelsEvent -= OnNationChannels;
        nation.NationRoomsEvent -= OnNationRooms;
        nation.NationSearchEvent -= OnNationSearch;
        nation.NationChatResultEvent -= OnNationResult;
        nation.NationChatStateEvent -= RefreshNationButtons;
    }

    internal void ToggleNation()
    {
        if (NationShown) { CloseNation(); return; }
        NationShown = true;
        NcList.Visible = true;
        ShowNationTab(publicTab: false);
    }

    internal void CloseNation()
    {
        if (!NationShown) return;
        NationShown = false;
        NcList.Visible = false;
        NcCreate.Visible = false;
        ncPassword.Visible = false;
    }

    private void BuildNationChannelList()
    {
        NcList = new HudWindow("nation_chat", "Channel list", bodyMinWidth: NationListWidth) { Visible = false };
        NcList.Closed += CloseNation;
        ncLayer.AddChild(NcList);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        NcList.Body.AddChild(tabs);
        ncPublicTab = UiTheme.TopTabButton(NationText(NationChatTexts.Public, "Public"));
        ncPublicTab.Pressed += () => ShowNationTab(publicTab: true);
        tabs.AddChild(ncPublicTab);
        ncPrivateTab = UiTheme.TopTabButton(NationText(NationChatTexts.Private, "Private"));
        ncPrivateTab.Pressed += () => ShowNationTab(publicTab: false);
        tabs.AddChild(ncPrivateTab);
        ncRefresh = UiTheme.IconButton("⟳", "");
        ncRefresh.Pressed += RefreshNationList;
        tabs.AddChild(ncRefresh);

        ncPublicPage = new VBoxContainer();
        ncPublicPage.AddThemeConstantOverride("separation", 6);
        NcList.Body.AddChild(ncPublicPage);
        ncPublicRows = NationRowBox(ncPublicPage, NationChatRules.PublicRows);
        var pubButtons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pubButtons.AddThemeConstantOverride("separation", 8);
        ncPublicPage.AddChild(pubButtons);
        ncPubJoin = NationButton(pubButtons, "Enter", JoinNationChannel);
        ncPubLeave = NationButton(pubButtons, "Leave", () => nationRequests.NationPublicLeave());
        ncPubOpen = NationButton(pubButtons, "Open", showRoom);
        NationButton(pubButtons, "Cancel", CloseNation);

        ncPrivatePage = new VBoxContainer();
        ncPrivatePage.AddThemeConstantOverride("separation", 6);
        NcList.Body.AddChild(ncPrivatePage);
        ncPrivateRows = NationRowBox(ncPrivatePage, NationChatRules.PrivateRows);
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", NationBarGap);
        ncPrivatePage.AddChild(bar);
        ncSearchEdit = new LineEdit
        {
            MaxLength = NationChatRules.TitleMaxLength,
            PlaceholderText = "Room name",
            CustomMinimumSize = new Vector2(NationSearchWidth, 0),
        };
        ncSearchEdit.TextSubmitted += _ => SubmitNationSearch();
        bar.AddChild(ncSearchEdit);
        NationButton(bar, "Search", SubmitNationSearch).SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        bar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        NationButton(bar, "Create room", OpenNationCreate).SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
    }

    private static VBoxContainer NationRowBox(VBoxContainer page, int rows)
    {
        var section = UiTheme.Section();
        page.AddChild(section);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, rows * NationRowHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        section.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(box);
        return box;
    }

    private static Button NationButton(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        button.AddThemeFontSizeOverride("font_size", 12);
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private void ShowNationTab(bool publicTab)
    {
        ncOnPublic = publicTab;
        ncPublicPage.Visible = publicTab;
        ncPrivatePage.Visible = !publicTab;
        ncPublicTab.SetPressedNoSignal(publicTab);
        ncPrivateTab.SetPressedNoSignal(!publicTab);
        NationSelectedChannel = NationSelectedRoom = NationChatRules.NoRoom;
        ncRefresh.TooltipText = publicTab
            ? NationText(NationChatTexts.PublicRefreshTip, "Refreshing public channel info. May be used once every 10 secs.")
            : NationText(NationChatTexts.PrivateRefreshTip, "Refreshing private channel info. May be used once every 10 secs.");
        if (publicTab) nationRequests.NationChannels();
        else nationRequests.NationRooms();
        RefreshNationButtons();
        NcList.ResetSize();
    }

    private void RefreshNationList()
    {
        ulong now = Time.GetTicksMsec();
        ref ulong at = ref (ncOnPublic ? ref ncPublicRefreshAt : ref ncPrivateRefreshAt);
        if (now < at)
        {
            int left = (int)Math.Ceiling((at - now) / 1000.0);
            string wait = string.Format(NationText(NationChatTexts.RefreshWait, "Available after {0} seconds.").Replace("%d", "{0}"), left);
            context.Log.Add(wait, CombatLogKind.Status, NationRefreshWait);
            return;
        }
        at = now + (ulong)(NationChatRules.RefreshCooldownSeconds * 1000);
        if (ncOnPublic) nationRequests.NationChannels();
        else nationRequests.NationRooms();
    }

    internal void RefreshNationButtons()
    {
        if (!GodotObject.IsInstanceValid(NcList)) return;
        var state = nation.NationChat;
        ncPubJoin.Visible = !state.InPublic;
        ncPubLeave.Visible = state.InPublic;
        ncPubOpen.Visible = state.InPublic;
    }

    internal void OnNationChannels(List<NationChannel> channels)
    {
        ncChannels = channels;
        foreach (var child in ncPublicRows.GetChildren()) child.QueueFree();
        foreach (var channel in channels)
        {
            bool mine = nation.NationChat.InPublic && nation.NationChat.RoomId == channel.Id;
            int id = channel.Id;
            ncPublicRows.AddChild(NationRow(NationChatRules.Row(channel.Name, channel.Members, NationChatRules.PublicCapacity),
                mine, id == NationSelectedChannel, () => SelectNationChannel(id), JoinNationChannel));
        }
    }

    internal void OnNationRooms(List<NationRoom> rooms)
    {
        ncRooms = rooms;
        foreach (var child in ncPrivateRows.GetChildren()) child.QueueFree();
        foreach (var room in rooms)
        {
            bool mine = nation.NationChat.InPrivate && nation.NationChat.RoomId == room.Id;
            int id = room.Id;
            ncPrivateRows.AddChild(NationRow(NationChatRules.Row(room.Name, room.Members, room.MaxMembers),
                mine, id == NationSelectedRoom, () => SelectNationRoom(id), JoinNationRoom));
        }
    }

    private void OnNationSearch(List<NationRoom> rooms)
    {
        ShowNationTabQuietly(publicTab: false);
        OnNationRooms(rooms);
    }

    internal void ShowNationTabQuietly(bool publicTab)
    {
        ncOnPublic = publicTab;
        ncPublicPage.Visible = publicTab;
        ncPrivatePage.Visible = !publicTab;
        ncPublicTab.SetPressedNoSignal(publicTab);
        ncPrivateTab.SetPressedNoSignal(!publicTab);
    }

    private Control NationRow(string text, bool mine, bool selected, Action select, Action join)
    {
        var row = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, NationRowHeight),
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = NationRowTip,
        };
        var box = UiTheme.Row(selected: true);
        if (!selected) { box.BgColor = Colors.Transparent; box.BorderColor = Colors.Transparent; }
        row.AddThemeStyleboxOverride("panel", box);
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 4);
        row.AddChild(line);
        var label = UiTheme.Text(text, 12, mine ? NationMine : selected ? UiTheme.GoldBright : UiTheme.TextHi);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.VerticalAlignment = VerticalAlignment.Center;
        line.AddChild(label);
        row.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
            select();
            if (click.DoubleClick) join();
        };
        return row;
    }

    private void SelectNationChannel(int id)
    {
        NationSelectedChannel = id;
        OnNationChannels(ncChannels);
    }

    private void SelectNationRoom(int id)
    {
        NationSelectedRoom = id;
        OnNationRooms(ncRooms);
    }

    private void JoinNationChannel()
    {
        if (NationSelectedChannel == NationChatRules.NoRoom) { NationNotice(NationChatTexts.SelectRoom, "Select a chatroom."); return; }
        var state = nation.NationChat;
        if (state.InPublic && state.RoomId == NationSelectedChannel) { showRoom(); return; }
        if (state.InRoom) { NationNotice(NationChatTexts.AlreadyInRoom, "You already are part of another chatroom."); return; }
        var channel = ncChannels.Find(c => c.Id == NationSelectedChannel);
        nation.NameNextRoom(channel.Name, false, channel.Members, NationChatRules.PublicCapacity);
        nationRequests.PublicEnter(NationSelectedChannel);
    }

    private void JoinNationRoom()
    {
        if (NationSelectedRoom == NationChatRules.NoRoom) { NationNotice(NationChatTexts.SelectRoom, "Select a chatroom."); return; }
        var state = nation.NationChat;
        if (state.InPrivate && state.RoomId == NationSelectedRoom) { showRoom(); return; }
        if (state.InRoom) { NationNotice(NationChatTexts.AlreadyInRoom, "You already are part of another chatroom."); return; }
        var room = ncRooms.Find(r => r.Id == NationSelectedRoom);
        nation.NameNextRoom(room.Name, room.Locked, room.Members, room.MaxMembers);
        if (room.Locked)
        {
            ncPasswordEdit.Text = "";
            ncPassword.Visible = true;
            ncPasswordEdit.GrabFocus();
            return;
        }
        nationRequests.NationPrivateEnter(room.Id, "");
    }

    private void NationNotice(int textId, string fallback) =>
        context.Log.Add(NationText(textId, fallback), CombatLogKind.Status, UiTheme.GoldBright);

    private void BuildNationCreate()
    {
        NcCreate = new HudWindow("nation_chat_create", "Create room", bodyMinWidth: NationSmallWidth) { Visible = false };
        NcCreate.Closed += () => NcCreate.Visible = false;
        ncLayer.AddChild(NcCreate);
        var col = NcCreate.Body;
        col.AddThemeConstantOverride("separation", 6);
        col.AddChild(UiTheme.Text("Room name", 12, UiTheme.TextLo));
        NcCreateTitle = new LineEdit { MaxLength = NationChatRules.TitleMaxLength };
        col.AddChild(NcCreateTitle);
        ncCreatePasswordTitle = UiTheme.Text("Password", 12, UiTheme.TextLo);
        col.AddChild(ncCreatePasswordTitle);
        NcCreatePassword = new LineEdit { MaxLength = NationChatRules.PasswordMaxLength, Secret = true };
        col.AddChild(NcCreatePassword);
        col.AddChild(UiTheme.Text("Options", 12, UiTheme.TextLo));
        NcCreateOption = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        NcCreateOption.AddItem(NationText(NationChatTexts.Public, "Public"));
        NcCreateOption.AddItem(NationText(NationChatTexts.Private, "Private"));
        NcCreateOption.ItemSelected += _ => ApplyNationCreateOption();
        col.AddChild(NcCreateOption);
        col.AddChild(UiTheme.Text("Max admissions", 12, UiTheme.TextLo));
        NcCreateSize = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (int size in NationChatRules.RoomSizes) NcCreateSize.AddItem(size.ToString());
        col.AddChild(NcCreateSize);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        col.AddChild(buttons);
        NationButton(buttons, "Confirm", SubmitNationRoom);
        NationButton(buttons, "Cancel", () => NcCreate.Visible = false);
    }

    internal void ApplyNationCreateOption()
    {
        bool isPrivate = NcCreateOption.Selected == 1;
        ncCreatePasswordTitle.AddThemeColorOverride("font_color", isPrivate ? UiTheme.GoldBright : UiTheme.TextLo);
        NcCreatePassword.Editable = isPrivate;
        if (!isPrivate) NcCreatePassword.Text = "";
    }

    internal void OpenNationCreate()
    {
        if (entityPlates.RefusedInSiegeWar()) return;
        if (nation.NationChat.InRoom) { NationNotice(NationChatTexts.CreateWhileInRoom, "You may not create another chatroom when already in one."); return; }
        NcCreateTitle.Text = "";
        NcCreatePassword.Text = "";
        NcCreateOption.Selected = 0;
        NcCreateSize.Selected = 0;
        ApplyNationCreateOption();
        NcCreate.Visible = true;
        NcCreateTitle.GrabFocus();
    }

    private void SubmitNationRoom()
    {
        string title = NcCreateTitle.Text.Trim();
        bool isPrivate = NcCreateOption.Selected == 1;
        string password = NcCreatePassword.Text;
        string refusal = NationChatRules.Validate(title, isPrivate, password, NationText);
        if (refusal.Length > 0)
        {
            context.Log.Add(refusal, CombatLogKind.Status, UiTheme.GoldBright);
            return;
        }
        int size = NationChatRules.RoomSizes[Math.Max(0, NcCreateSize.Selected)];
        nationRequests.NationCreate(title, isPrivate, password, size);
    }

    private void BuildNationPassword()
    {
        ncPassword = new HudWindow("nation_chat_password", "Password", bodyMinWidth: NationSmallWidth) { Visible = false };
        ncPassword.Closed += () => ncPassword.Visible = false;
        ncLayer.AddChild(ncPassword);
        ncPassword.Body.AddThemeConstantOverride("separation", 6);
        ncPassword.Body.AddChild(UiTheme.Text("Enter the password.", 12, UiTheme.TextLo));
        ncPasswordEdit = new LineEdit { MaxLength = NationChatRules.PasswordMaxLength, Secret = true };
        ncPasswordEdit.TextSubmitted += _ => SubmitNationPassword();
        ncPassword.Body.AddChild(ncPasswordEdit);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        ncPassword.Body.AddChild(buttons);
        NationButton(buttons, "Confirm", SubmitNationPassword);
        NationButton(buttons, "Cancel", () => ncPassword.Visible = false);
    }

    private void SubmitNationPassword()
    {
        if (ncPasswordEdit.Text.Length == 0 || NationSelectedRoom == NationChatRules.NoRoom) return;
        nationRequests.NationPrivateEnter(NationSelectedRoom, ncPasswordEdit.Text);
        ncPassword.Visible = false;
    }

    private void SubmitNationSearch()
    {
        string word = ncSearchEdit.Text.Trim();
        if (word.Length == 0) nationRequests.NationRooms();
        else nationRequests.NationSearch(word);
    }

    private void OnNationResult(NationChatSubOpcode sub, byte code)
    {
        int text = sub switch
        {
            NationChatSubOpcode.PublicEnter => NationChatRules.PublicEnterText(code),
            NationChatSubOpcode.PublicLeave or NationChatSubOpcode.PrivateLeave => code == 0 ? NationChatTexts.YouLeft : 0,
            NationChatSubOpcode.Create => NationChatRules.CreateText(code),
            NationChatSubOpcode.PrivateEnter => NationChatRules.PrivateEnterText(code),
            NationChatSubOpcode.Search => NationChatRules.SearchText(code),
            NationChatSubOpcode.Options => NationChatRules.OptionsText(code),
            NationChatSubOpcode.HandOver => NationChatRules.HandOverText(code),
            NationChatSubOpcode.Kick => NationChatRules.KickText(code),
            NationChatSubOpcode.Members => code == NationChatRules.MembersNoRoomCode ? NationChatTexts.NoRoom : 0,
            _ => 0,
        };
        if (text != 0) NationNotice(text, "");

        bool created = sub == NationChatSubOpcode.Create && code == NationChatWire.Created;
        if (created) NcCreate.Visible = false;

        if (!NationShown) return;
        if (sub is NationChatSubOpcode.PublicEnter or NationChatSubOpcode.PublicLeave && code == 0) nationRequests.NationChannels();
        if (sub is NationChatSubOpcode.PrivateEnter or NationChatSubOpcode.PrivateLeave or NationChatSubOpcode.Options
                or NationChatSubOpcode.Kick && (code == 0 || code == NationChatRules.KickedCode)
            || created)
            nationRequests.NationRooms();
    }
}
