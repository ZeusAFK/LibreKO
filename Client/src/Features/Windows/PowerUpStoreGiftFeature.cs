using Godot;

namespace LibreKO.Features.Windows;

internal sealed class PowerUpStoreGiftFeature : IFeature
{
    private readonly ClanSession clan;
    private readonly FriendsSession friends;
    private readonly SelfSession self;
    private readonly ShoppingMallSession shoppingMall;
    private readonly IClanRequests clanRequests;
    private readonly IFriendsRequests friendsRequests;
    private readonly IShoppingMallRequests requests;

    public PowerUpStoreGiftFeature(ClanSession clan, FriendsSession friends, SelfSession self, ShoppingMallSession shoppingMall, IClanRequests clanRequests, IFriendsRequests friendsRequests, IShoppingMallRequests requests)
    {
        this.clan = clan;
        this.friends = friends;
        this.self = self;
        this.shoppingMall = shoppingMall;
        this.clanRequests = clanRequests;
        this.friendsRequests = friendsRequests;
        this.requests = requests;
    }

    private const float GiftWidth = 560f;
    private const float GiftListHeight = 300f;
    private const float GiftTouchListHeight = 360f;
    private const float GiftRowHeight = 44f;
    private const float GiftTouchRowHeight = 58f;
    private const float GiftSearchHeight = 38f;
    private const int GiftSearchFontSize = 15;
    private const int GiftCardNameFontSize = 24;
    private const double GiftSuggestDebounceSeconds = 0.2;
    private const string OnlineGlyph = "●";

    internal event Action? CloseRequested;

    internal event Action<PowerUpStoreRecipient>? RecipientConfirmed;

    private PanelContainer giftBox = null!;
    internal LineEdit GiftSearch { get; private set; } = null!;
    private Godot.Timer giftDebounce = null!;
    private VBoxContainer giftPickPane = null!;
    private Label giftListTitle = null!;
    private VBoxContainer giftList = null!;
    private PanelContainer giftCard = null!;
    private Label giftCardName = null!;
    private Label giftCardInfo = null!;
    private Label giftCardWarning = null!;
    private Label giftStatus = null!;
    private bool shown;
    internal List<GiftContact> PusFriends { get; } = new();
    internal List<GiftContact> Clanmates { get; } = new();
    internal string GiftChecking { get; set; } = "";
    private PowerUpStoreRecipient? giftCandidate;

    public void Build()
    {
        shoppingMall.ShoppingMallRecipientEvent += OnRecipient;
        friends.FriendListEvent += OnFriends;
        clan.ClanMembersEvent += OnClanMembers;
    }

    public void Dispose()
    {
        shoppingMall.ShoppingMallRecipientEvent -= OnRecipient;
        friends.FriendListEvent -= OnFriends;
        clan.ClanMembersEvent -= OnClanMembers;
    }

    internal Control BuildGiftBox()
    {
        giftBox = PowerUpStoreUi.ModalBox(GiftWidth, out var body);
        PowerUpStoreUi.ModalHeader(body, "Send as a gift", UiIcons.Get("system/gift"), () => CloseRequested?.Invoke());

        var help = UiTheme.Text("Choose the friend or clan member who receives everything in your cart. The items are mailed to that character, and a gift can't be taken back.", 13, UiTheme.TextLo);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(help);

        giftPickPane = new VBoxContainer();
        giftPickPane.AddThemeConstantOverride("separation", 8);
        body.AddChild(giftPickPane);

        var search = PowerUpStoreUi.SearchField("Filter your friends and clan", GiftSearchFontSize,
            Platform.Pick(GiftSearchHeight, GiftTouchRowHeight), out var searchInput);
        GiftSearch = searchInput;
        GiftSearch.MaxLength = GiftRecipients.NameMaxLength;
        GiftSearch.TextChanged += _ => giftDebounce.Start();
        GiftSearch.TextSubmitted += SubmitGiftSearch;
        giftPickPane.AddChild(search);
        giftDebounce = new Godot.Timer { WaitTime = GiftSuggestDebounceSeconds, OneShot = true };
        giftDebounce.Timeout += RenderGiftSuggestions;
        GiftSearch.AddChild(giftDebounce);

        giftListTitle = UiTheme.SectionTitle("");
        giftPickPane.AddChild(giftListTitle);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, Platform.Pick(GiftListHeight, GiftTouchListHeight)),
        };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        giftPickPane.AddChild(scroll);
        giftList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        giftList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(giftList);

        body.AddChild(BuildGiftCard());

        giftStatus = UiTheme.Text("", 13, UiTheme.TextLo, HorizontalAlignment.Center);
        giftStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(giftStatus);
        return giftBox;
    }

    private Control BuildGiftCard()
    {
        giftCard = new PanelContainer { Visible = false };
        var style = UiTheme.Inset(5);
        style.BorderColor = UiTheme.GoldVivid;
        style.BgColor = new Color(0.17f, 0.13f, 0.06f, 0.92f);
        style.SetContentMarginAll(16);
        giftCard.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        giftCard.AddChild(column);

        column.AddChild(UiTheme.Text("Gift for", 13, UiTheme.TextLo, HorizontalAlignment.Center));
        giftCardName = UiTheme.Text("", GiftCardNameFontSize, UiTheme.GoldBright, HorizontalAlignment.Center);
        column.AddChild(giftCardName);
        giftCardInfo = UiTheme.Text("", 14, UiTheme.TextHi, HorizontalAlignment.Center);
        column.AddChild(giftCardInfo);
        giftCardWarning = UiTheme.Text("", 13, UiTheme.Warning, HorizontalAlignment.Center);
        giftCardWarning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(giftCardWarning);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);
        var height = Platform.Pick(PowerUpStoreUi.ModalButtonHeight, PowerUpStoreUi.ModalTouchButtonHeight);
        var back = UiTheme.SmallButton("Choose someone else", "Go back to the list");
        back.CustomMinimumSize = new Vector2(0, height);
        back.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        back.AddThemeFontSizeOverride("font_size", 14);
        back.Pressed += ShowGiftList;
        buttons.AddChild(back);
        var confirm = UiTheme.ActionButton("Confirm recipient", "Make this purchase a gift for this character");
        confirm.CustomMinimumSize = new Vector2(0, height);
        confirm.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        confirm.AddThemeFontSizeOverride("font_size", 14);
        confirm.Pressed += ConfirmGiftRecipient;
        buttons.AddChild(confirm);
        return giftCard;
    }

    internal void SetShown(bool value)
    {
        shown = value;
        giftBox.Visible = value;
        if (!value) GiftChecking = "";
    }

    internal void Open()
    {
        friendsRequests.FriendListRequest();
        clanRequests.MembersRequest();
        GiftSearch.Text = "";
        ShowGiftList();
        GiftSearch.GrabFocus();
    }

    internal void ShowGiftList()
    {
        giftCandidate = null;
        GiftChecking = "";
        giftCard.Visible = false;
        giftPickPane.Visible = true;
        SetGiftStatus("", false);
        RenderGiftSuggestions();
    }

    private void OnFriends(List<FriendEntry> friendEntries)
    {
        PusFriends.Clear();
        PusFriends.AddRange(friendEntries
            .Where(f => !string.IsNullOrEmpty(f.Name))
            .Select(f => new GiftContact(f.Name, f.Level, f.Class, f.IsOnline, GiftContactSource.Friend)));
        if (shown) RenderGiftSuggestions();
    }

    private void OnClanMembers(List<ClanMember> members)
    {
        Clanmates.Clear();
        Clanmates.AddRange(members
            .Where(m => !string.IsNullOrEmpty(m.Name))
            .Select(m => new GiftContact(m.Name, m.Level, m.Class, m.IsOnline, GiftContactSource.Clan)));
        if (shown) RenderGiftSuggestions();
    }

    private List<GiftContact> GiftContacts() => GiftRecipients.Merge(PusFriends.Concat(Clanmates));

    private string SelfName() => self.LastEnter.Name ?? "";

    private List<GiftContact> GiftMatches() =>
        GiftRecipients.Suggest(GiftContacts(), GiftSearch.Text, SelfName());

    private void RenderGiftSuggestions()
    {
        if (!shown || !giftPickPane.Visible) return;
        var typed = GiftSearch.Text.Trim();
        var anyone = GiftRecipients.Suggest(GiftContacts(), "", SelfName()).Count > 0;
        var matches = GiftMatches();
        giftListTitle.Text = typed.Length > 0 ? "Matches" : "Your friends and clan";

        UiKit.ClearChildren(giftList);
        foreach (var contact in matches)
            giftList.AddChild(BuildContactRow(contact));

        if (matches.Count == 0)
        {
            var empty = UiTheme.Text(!anyone
                    ? "You have no friends or clan members to send a gift to yet."
                    : $"Nobody in your friends or clan matches \"{typed}\".",
                13, UiTheme.TextLo, HorizontalAlignment.Center);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            giftList.AddChild(empty);
        }
    }

    private Control BuildContactRow(GiftContact contact)
    {
        var row = GiftRow(() => CheckRecipient(contact.Name));
        var hb = (HBoxContainer)row.GetChild(0);
        hb.AddChild(UiTheme.Text(OnlineGlyph, 12, contact.Online ? UiTheme.Good : UiTheme.TextDim));
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", -2);
        hb.AddChild(text);
        text.AddChild(UiTheme.Text(contact.Name, 15, UiTheme.TextHi));
        var info = new PowerUpStoreRecipient(contact.Name, contact.Level, contact.Class).Summary;
        text.AddChild(UiTheme.Text(contact.Online ? info : info.Length > 0 ? $"{info} · offline" : "Offline", 12, UiTheme.TextLo));
        if (contact.Source.HasFlag(GiftContactSource.Friend)) hb.AddChild(Tag("Friend", UiTheme.GoldBright));
        if (contact.Source.HasFlag(GiftContactSource.Clan)) hb.AddChild(Tag("Clan", UiTheme.Premium));
        return row;
    }

    private static PanelContainer Tag(string text, Color color)
    {
        var tag = UiTheme.Pill(text, color);
        tag.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return tag;
    }

    private PanelContainer GiftRow(System.Action pick)
    {
        var row = UiTheme.RowPanel();
        row.CustomMinimumSize = new Vector2(0, Platform.Pick(GiftRowHeight, GiftTouchRowHeight));
        var normal = UiTheme.Row();
        var hover = UiTheme.Row(selected: true);
        row.MouseEntered += () => row.AddThemeStyleboxOverride("panel", hover);
        row.MouseExited += () => row.AddThemeStyleboxOverride("panel", normal);
        row.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            row.AcceptEvent();
            Callable.From(pick).CallDeferred();
        };
        var hb = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        hb.AddThemeConstantOverride("separation", 10);
        row.AddChild(hb);
        return row;
    }

    private void SubmitGiftSearch(string typed)
    {
        var matches = GiftMatches();
        var pick = GiftRecipients.Exact(matches, typed) ?? (matches.Count == 1 ? matches[0] : null);
        if (pick != null) CheckRecipient(pick.Name);
        else SetGiftStatus(matches.Count == 0 ? "Choose a friend or clan member from the list." : "Pick one of the matches from the list.", true);
    }

    private void CheckRecipient(string name)
    {
        GiftChecking = name;
        SetGiftStatus($"Checking {name}…", false);
        requests.PowerUpStoreCheckRecipient(name);
    }

    internal void OnRecipient(PowerUpStoreResult result, string name, int level, int cls)
    {
        if (!shown || GiftChecking.Length == 0) return;
        var asked = GiftChecking;
        GiftChecking = "";
        if (result != PowerUpStoreResult.Succeeded)
        {
            SetGiftStatus(result switch
            {
                PowerUpStoreResult.RecipientIsSelf => "You can't send a gift to yourself.",
                PowerUpStoreResult.RecipientNotAllowed => $"{asked} is no longer your friend or in your clan.",
                _ => $"{asked} no longer exists.",
            }, true);
            return;
        }

        giftCandidate = new PowerUpStoreRecipient(name, level, cls);
        giftCardName.Text = name;
        giftCardInfo.Text = giftCandidate.Summary;
        giftCardWarning.Text = $"Everything in your cart will be mailed to {name}. Make sure this is the right character.";
        giftPickPane.Visible = false;
        giftCard.Visible = true;
        SetGiftStatus("", false);
    }

    private void ConfirmGiftRecipient()
    {
        if (giftCandidate != null) RecipientConfirmed?.Invoke(giftCandidate);
    }

    private void SetGiftStatus(string text, bool bad)
    {
        giftStatus.Text = text;
        giftStatus.Visible = text.Length > 0;
        giftStatus.AddThemeColorOverride("font_color", bad ? UiTheme.Bad : UiTheme.TextLo);
    }
}
