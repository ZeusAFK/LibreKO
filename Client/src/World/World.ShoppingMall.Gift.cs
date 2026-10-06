using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const float PusGiftWidth = 560f;
    private const float PusGiftListHeight = 300f;
    private const float PusGiftTouchListHeight = 360f;
    private const float PusGiftRowHeight = 44f;
    private const float PusGiftTouchRowHeight = 58f;
    private const float PusGiftSearchHeight = 38f;
    private const int PusGiftSearchFontSize = 15;
    private const int PusGiftCardNameFontSize = 24;
    private const double PusGiftSuggestDebounceSeconds = 0.2;
    private const string PusOnlineGlyph = "●";

    private PanelContainer _pusGiftBox = null!;
    private LineEdit _pusGiftSearch = null!;
    private Godot.Timer _pusGiftDebounce = null!;
    private VBoxContainer _pusGiftPickPane = null!;
    private Label _pusGiftListTitle = null!;
    private VBoxContainer _pusGiftList = null!;
    private PanelContainer _pusGiftCard = null!;
    private Label _pusGiftCardName = null!;
    private Label _pusGiftCardInfo = null!;
    private Label _pusGiftCardWarning = null!;
    private Label _pusGiftStatus = null!;
    private readonly List<GiftContact> _pusFriends = new();
    private readonly List<GiftContact> _pusClanmates = new();
    private string _pusGiftChecking = "";
    private PusRecipient? _pusGiftCandidate;

    private Control BuildPusGiftBox()
    {
        _pusGiftBox = PusModalBox(PusGiftWidth, out var body);
        PusModalHeader(body, "Send as a gift", UiIcons.Get("system/gift"), ClosePusModal);

        var help = UiTheme.Text("Choose the friend or clan member who receives everything in your cart. The items are mailed to that character, and a gift can't be taken back.", 13, UiTheme.TextLo);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(help);

        _pusGiftPickPane = new VBoxContainer();
        _pusGiftPickPane.AddThemeConstantOverride("separation", 8);
        body.AddChild(_pusGiftPickPane);

        var search = PusSearchField("Filter your friends and clan", PusGiftSearchFontSize,
            Platform.Pick(PusGiftSearchHeight, PusGiftTouchRowHeight), out _pusGiftSearch);
        _pusGiftSearch.MaxLength = GiftRecipients.NameMaxLength;
        _pusGiftSearch.TextChanged += _ => _pusGiftDebounce.Start();
        _pusGiftSearch.TextSubmitted += SubmitPusGiftSearch;
        _pusGiftPickPane.AddChild(search);
        _pusGiftDebounce = new Godot.Timer { WaitTime = PusGiftSuggestDebounceSeconds, OneShot = true };
        _pusGiftDebounce.Timeout += RenderPusGiftSuggestions;
        _pusGiftSearch.AddChild(_pusGiftDebounce);

        _pusGiftListTitle = UiTheme.SectionTitle("");
        _pusGiftPickPane.AddChild(_pusGiftListTitle);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, Platform.Pick(PusGiftListHeight, PusGiftTouchListHeight)),
        };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        _pusGiftPickPane.AddChild(scroll);
        _pusGiftList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _pusGiftList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_pusGiftList);

        body.AddChild(BuildPusGiftCard());

        _pusGiftStatus = UiTheme.Text("", 13, UiTheme.TextLo, HorizontalAlignment.Center);
        _pusGiftStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_pusGiftStatus);
        return _pusGiftBox;
    }

    private Control BuildPusGiftCard()
    {
        _pusGiftCard = new PanelContainer { Visible = false };
        var style = UiTheme.Inset(5);
        style.BorderColor = UiTheme.GoldVivid;
        style.BgColor = new Color(0.17f, 0.13f, 0.06f, 0.92f);
        style.SetContentMarginAll(16);
        _pusGiftCard.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        _pusGiftCard.AddChild(column);

        column.AddChild(UiTheme.Text("Gift for", 13, UiTheme.TextLo, HorizontalAlignment.Center));
        _pusGiftCardName = UiTheme.Text("", PusGiftCardNameFontSize, UiTheme.GoldBright, HorizontalAlignment.Center);
        column.AddChild(_pusGiftCardName);
        _pusGiftCardInfo = UiTheme.Text("", 14, UiTheme.TextHi, HorizontalAlignment.Center);
        column.AddChild(_pusGiftCardInfo);
        _pusGiftCardWarning = UiTheme.Text("", 13, UiTheme.Warning, HorizontalAlignment.Center);
        _pusGiftCardWarning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_pusGiftCardWarning);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);
        var height = Platform.Pick(PusDetailsButtonHeight, PusDetailsTouchButtonHeight);
        var back = UiTheme.SmallButton("Choose someone else", "Go back to the list");
        back.CustomMinimumSize = new Vector2(0, height);
        back.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        back.AddThemeFontSizeOverride("font_size", 14);
        back.Pressed += ShowPusGiftList;
        buttons.AddChild(back);
        var confirm = UiTheme.ActionButton("Confirm recipient", "Make this purchase a gift for this character");
        confirm.CustomMinimumSize = new Vector2(0, height);
        confirm.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        confirm.AddThemeFontSizeOverride("font_size", 14);
        confirm.Pressed += ConfirmPusGiftRecipient;
        buttons.AddChild(confirm);
        return _pusGiftCard;
    }

    private void OpenPusGiftPicker()
    {
        if (_pusPending != PusPurchase.None) return;
        Net.I.SendFriendListRequest();
        Net.I.SendClanMembersRequest();
        _pusGiftSearch.Text = "";
        ShowPusModal(PusModal.Gift);
        ShowPusGiftList();
        _pusGiftSearch.GrabFocus();
    }

    private void ShowPusGiftList()
    {
        _pusGiftCandidate = null;
        _pusGiftChecking = "";
        _pusGiftCard.Visible = false;
        _pusGiftPickPane.Visible = true;
        SetPusGiftStatus("", false);
        RenderPusGiftSuggestions();
    }

    private void OnPusFriends(List<FriendEntry> friends)
    {
        _pusFriends.Clear();
        _pusFriends.AddRange(friends
            .Where(f => !string.IsNullOrEmpty(f.Name))
            .Select(f => new GiftContact(f.Name, f.Level, f.Class, f.IsOnline, GiftContactSource.Friend)));
        if (_pusModal == PusModal.Gift) RenderPusGiftSuggestions();
    }

    private void OnPusClanMembers(List<ClanMember> members)
    {
        _pusClanmates.Clear();
        _pusClanmates.AddRange(members
            .Where(m => !string.IsNullOrEmpty(m.Name))
            .Select(m => new GiftContact(m.Name, m.Level, m.Class, m.IsOnline, GiftContactSource.Clan)));
        if (_pusModal == PusModal.Gift) RenderPusGiftSuggestions();
    }

    private List<GiftContact> PusGiftContacts() => GiftRecipients.Merge(_pusFriends.Concat(_pusClanmates));

    private static string PusSelfName() => Net.I?.LastEnter.Name ?? "";

    private List<GiftContact> PusGiftMatches() =>
        GiftRecipients.Suggest(PusGiftContacts(), _pusGiftSearch.Text, PusSelfName());

    private void RenderPusGiftSuggestions()
    {
        if (_pusModal != PusModal.Gift || !_pusGiftPickPane.Visible) return;
        var typed = _pusGiftSearch.Text.Trim();
        var anyone = GiftRecipients.Suggest(PusGiftContacts(), "", PusSelfName()).Count > 0;
        var matches = PusGiftMatches();
        _pusGiftListTitle.Text = typed.Length > 0 ? "Matches" : "Your friends and clan";

        ClearChildren(_pusGiftList);
        foreach (var contact in matches)
            _pusGiftList.AddChild(BuildPusContactRow(contact));

        if (matches.Count == 0)
        {
            var empty = UiTheme.Text(!anyone
                    ? "You have no friends or clan members to send a gift to yet."
                    : $"Nobody in your friends or clan matches \"{typed}\".",
                13, UiTheme.TextLo, HorizontalAlignment.Center);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _pusGiftList.AddChild(empty);
        }
    }

    private Control BuildPusContactRow(GiftContact contact)
    {
        var row = PusGiftRow(() => CheckPusRecipient(contact.Name));
        var hb = (HBoxContainer)row.GetChild(0);
        hb.AddChild(UiTheme.Text(PusOnlineGlyph, 12, contact.Online ? UiTheme.Good : UiTheme.TextDim));
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", -2);
        hb.AddChild(text);
        text.AddChild(UiTheme.Text(contact.Name, 15, UiTheme.TextHi));
        var info = PusRecipientInfo(new PusRecipient(contact.Name, contact.Level, contact.Class));
        text.AddChild(UiTheme.Text(contact.Online ? info : info.Length > 0 ? $"{info} · offline" : "Offline", 12, UiTheme.TextLo));
        if (contact.Source.HasFlag(GiftContactSource.Friend)) hb.AddChild(PusTag("Friend", UiTheme.GoldBright));
        if (contact.Source.HasFlag(GiftContactSource.Clan)) hb.AddChild(PusTag("Clan", UiTheme.Premium));
        return row;
    }

    private static PanelContainer PusTag(string text, Color color)
    {
        var tag = UiTheme.Pill(text, color);
        tag.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return tag;
    }

    private PanelContainer PusGiftRow(System.Action pick)
    {
        var row = UiTheme.RowPanel();
        row.CustomMinimumSize = new Vector2(0, Platform.Pick(PusGiftRowHeight, PusGiftTouchRowHeight));
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

    private void SubmitPusGiftSearch(string typed)
    {
        var matches = PusGiftMatches();
        var pick = GiftRecipients.Exact(matches, typed) ?? (matches.Count == 1 ? matches[0] : null);
        if (pick != null) CheckPusRecipient(pick.Name);
        else SetPusGiftStatus(matches.Count == 0 ? "Choose a friend or clan member from the list." : "Pick one of the matches from the list.", true);
    }

    private void CheckPusRecipient(string name)
    {
        _pusGiftChecking = name;
        SetPusGiftStatus($"Checking {name}…", false);
        Net.I.SendPowerUpStoreCheckRecipient(name);
    }

    private void OnShoppingMallRecipient(PowerUpStoreResult result, string name, int level, int cls)
    {
        if (_pusModal != PusModal.Gift || _pusGiftChecking.Length == 0) return;
        var asked = _pusGiftChecking;
        _pusGiftChecking = "";
        if (result != PowerUpStoreResult.Succeeded)
        {
            SetPusGiftStatus(result switch
            {
                PowerUpStoreResult.RecipientIsSelf => "You can't send a gift to yourself.",
                PowerUpStoreResult.RecipientNotAllowed => $"{asked} is no longer your friend or in your clan.",
                _ => $"{asked} no longer exists.",
            }, true);
            return;
        }

        _pusGiftCandidate = new PusRecipient(name, level, cls);
        _pusGiftCardName.Text = name;
        _pusGiftCardInfo.Text = PusRecipientInfo(_pusGiftCandidate);
        _pusGiftCardWarning.Text = $"Everything in your cart will be mailed to {name}. Make sure this is the right character.";
        _pusGiftPickPane.Visible = false;
        _pusGiftCard.Visible = true;
        SetPusGiftStatus("", false);
    }

    private void ConfirmPusGiftRecipient()
    {
        if (_pusGiftCandidate == null) return;
        _pusGiftRecipient = _pusGiftCandidate;
        SetPusCartStatus("", false);
        ClosePusModal();
        RenderPusCart();
    }

    private void SetPusGiftStatus(string text, bool bad)
    {
        _pusGiftStatus.Text = text;
        _pusGiftStatus.Visible = text.Length > 0;
        _pusGiftStatus.AddThemeColorOverride("font_color", bad ? UiTheme.Bad : UiTheme.TextLo);
    }
}
