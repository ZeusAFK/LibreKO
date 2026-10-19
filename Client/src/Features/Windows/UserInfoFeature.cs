using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class UserInfoFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly NearbyPlayersSession nearbyPlayers;
    private readonly PlayerInspectSession playerInspect;
    private readonly IPlayerInspectRequests playerInspectRequests;
    private readonly ClanMarksFeature clanMarks;
    private readonly DailyRankFeature dailyRankWindow;
    private readonly FriendsSession friends;

    public UserInfoFeature(IWorldContext context, ClanSession clan, NearbyPlayersSession nearbyPlayers, PlayerInspectSession playerInspect, IPlayerInspectRequests playerInspectRequests, ClanMarksFeature clanMarks, DailyRankFeature dailyRankWindow, FriendsSession friends)
    {
        this.context = context;
        this.clan = clan;
        this.nearbyPlayers = nearbyPlayers;
        this.playerInspect = playerInspect;
        this.playerInspectRequests = playerInspectRequests;
        this.clanMarks = clanMarks;
        this.dailyRankWindow = dailyRankWindow;
        this.friends = friends;
    }

    private const string RankMarkArt = "res://assets/ui/daily_rank/marks/";

    private const string ClanPodiumArt = "res://assets/ui/daily_rank/medal_{0}.png";
    private const int Width = 440;
    private const float MarkSize = 66f;
    private const float RankRibbonWidth = 45f;
    private const float RankRibbonHeight = 17f;
    private const int RankRibbonFont = 9;
    private const int SummaryColumns = 9;
    private const float KeyWidth = 120f;
    private const float PlaceWidth = 56f;
    private const int ClanPlaceFont = 24;

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private UserInfoReply? userInfo;
    private GameUserCard? card;
    private bool shown;

    internal bool IsOpen => shown;

    private readonly Dictionary<string, Texture2D?> rankRibbons = new();

    private TextureRect uiClanMark = null!;
    private TextureRect uiClanPodium = null!;
    private Label uiClanPlace = null!;
    private Label uiClanName = null!;
    private Label uiClanGrade = null!;
    private Label uiClanChief = null!;
    private Label uiName = null!;
    private Label uiLevel = null!;
    private Label uiJob = null!;
    private Label uiLoyalty = null!;
    private Label uiState = null!;
    private Label uiRelation = null!;
    private GridContainer uiSummary = null!;
    private VBoxContainer uiRows = null!;

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.TopWindows };
        context.Root.AddChild(layer);
        Panel = new HudWindow("userinfo", "Character Info", new Vector2(320, 120), Width) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);
        BuildBody(Panel.Body);
        playerInspect.UserInformationEvent += OnInformation;
        clanMarks.MarkChanged += RefreshMark;
        playerInspect.UserInformationMissingEvent += OnInformationMissing;
        PluginHost.Game.AttachUserInfo(new PluginUserInfoBridge(this));
    }

    public void Dispose()
    {
        playerInspect.UserInformationEvent -= OnInformation;
        clanMarks.MarkChanged -= RefreshMark;
        playerInspect.UserInformationMissingEvent -= OnInformationMissing;
    }

    private void BuildBody(VBoxContainer root)
    {
        root.AddThemeConstantOverride("separation", 8);

        var clanRow = new HBoxContainer();
        clanRow.AddThemeConstantOverride("separation", 10);
        root.AddChild(clanRow);
        var podium = new Panel { CustomMinimumSize = new Vector2(MarkSize, MarkSize) };
        podium.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        uiClanPodium = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        uiClanPodium.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        podium.AddChild(uiClanPodium);
        uiClanPlace = UiTheme.Text("", ClanPlaceFont, UiTheme.GoldBright, HorizontalAlignment.Center);
        uiClanPlace.VerticalAlignment = VerticalAlignment.Center;
        uiClanPlace.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        podium.AddChild(uiClanPlace);
        clanRow.AddChild(podium);
        var markFrame = new Panel { CustomMinimumSize = new Vector2(MarkSize, MarkSize) };
        markFrame.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        uiClanMark = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        uiClanMark.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        markFrame.AddChild(uiClanMark);
        clanRow.AddChild(markFrame);
        var clanFacts = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        clanFacts.AddThemeConstantOverride("v_separation", 4);
        clanRow.AddChild(clanFacts);
        uiClanName = Fact(clanFacts, "Clan:");
        uiClanGrade = Fact(clanFacts, "Grade:");
        uiClanChief = Fact(clanFacts, "Leader:");

        root.AddChild(UiTheme.Rule());

        var facts = new GridContainer { Columns = 2 };
        facts.AddThemeConstantOverride("v_separation", 5);
        root.AddChild(facts);
        uiName = Fact(facts, "Character name");
        uiLevel = Fact(facts, "Level");
        uiJob = Fact(facts, "Job");
        uiLoyalty = Fact(facts, "Cont/Ladder");
        uiState = Fact(facts, "Status");
        uiRelation = Fact(facts, "Relation");

        root.AddChild(UiTheme.Rule());

        uiSummary = new GridContainer { Columns = SummaryColumns };
        uiSummary.AddThemeConstantOverride("h_separation", 0);
        uiSummary.AddThemeConstantOverride("v_separation", 0);
        root.AddChild(uiSummary);

        uiRows = new VBoxContainer();
        uiRows.AddThemeConstantOverride("separation", 3);
        root.AddChild(uiRows);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        root.AddChild(footer);
        var ok = new Button { Text = "Confirm", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 0) };
        ok.Pressed += Close;
        footer.AddChild(ok);
    }

    private static Label Fact(GridContainer grid, string caption)
    {
        var key = UiTheme.Text(caption, 12, UiTheme.Gold);
        key.CustomMinimumSize = new Vector2(KeyWidth, 0);
        grid.AddChild(key);
        var value = UiTheme.Text("", 13, UiTheme.TextHi);
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        grid.AddChild(value);
        return value;
    }

    internal void RequestInformation(string name)
    {
        if (name.Length == 0) return;
        playerInspectRequests.UserInformationRequest(name);
    }

    internal void Close()
    {
        shown = false;
        Panel.Visible = false;
        PluginNotify();
    }

    private void PluginNotify() => Callable.From(PluginHost.Game.RaiseUserInfo).CallDeferred();

    private void OnInformationMissing()
    {
        context.Log.Notice(ItemData.Text(UserInfoCard.NoUserText, "No available user"));
    }

    internal void OnInformation(UserInfoReply info)
    {
        userInfo = info;
        card = BuildCard(info);
        Fill(card);
        Panel.Visible = true;
        shown = true;
        PluginNotify();
    }

    private void RefreshMark(int clanId)
    {
        if (userInfo == null || userInfo.ClanId != clanId) return;
        card = BuildCard(userInfo);
        uiClanMark.Texture = card.ClanMark;
        PluginNotify();
    }

    private GameUserCard BuildCard(UserInfoReply info)
    {
        NearbyPlayer? nearby = null;
        foreach (var p in nearbyPlayers.Players)
            if (string.Equals(p.Name, info.Name, StringComparison.OrdinalIgnoreCase)) { nearby = p; break; }
        int nation = nearby?.Nation ?? NationInView(info.Name);
        string state = ItemData.Text(UserInfoCard.StateText(nearby?.State ?? 0), "Online");
        if (info.InParty) state = $"{state} / {ItemData.Text(UserInfoCard.PartyText, "Party")}";

        bool friend = friends.IsFriend(info.Name);
        bool sameClan = info.ClanId != 0 && clan.MyClan.InClan && info.ClanId == clan.MyClan.ClanId;
        var (gradeText, gradeNumber) = UserInfoCard.ClanGrade(info.ClanFlag, info.ClanGrade);
        string grade = gradeText == 0
            ? info.ClanFlag == 0 ? "" : "Unknown Grade"
            : ItemData.Text(gradeText, "%d").Replace("%d", gradeNumber.ToString());
        int classText = UserInfoCard.ClassText(info.Class);

        var summary = new List<GameRankRibbon>();
        foreach (var cell in UserInfoCard.Summary(info.Marks))
            summary.Add(new GameRankRibbon(RankRibbon(UserInfoCard.Ribbon(cell.Type, cell.Tier, cell.Count)),
                cell.Count.ToString(), MarkTooltipText(cell.Type)));

        var rows = new List<GameUserRankRow>();
        var ranks = UserInfoCard.Rows(info.RankPlaces);
        for (int r = 0; r < ranks.Count; r++)
        {
            var tiers = new GameRankRibbon[UserInfoCard.Tiers];
            if (r < info.Marks.Count)
            {
                var mark = info.Marks[r];
                for (int t = 0; t < tiers.Length; t++)
                {
                    int count = mark.At(t);
                    bool special = mark.Type >= UserInfoCard.SpecialMarkFirst;
                    tiers[t] = special && t < UserInfoCard.Tiers - 1
                        ? default
                        : new GameRankRibbon(RankRibbon(UserInfoCard.Ribbon(mark.Type, t, count)),
                            count > 0 ? count.ToString() : "", MarkTooltipText(mark.Type));
                }
            }
            int place = ranks[r].Place;
            rows.Add(new GameUserRankRow(DailyRankTitle(ranks[r].Board), UserInfoCard.PlaceText(place),
                UserInfoCard.PlaceColour(place), tiers));
        }

        return new GameUserCard(
            info.Name,
            UserInfoCard.NameColour(nation),
            UserInfoCard.LevelLine(info.Level, info.RebirthLevel),
            classText > 0 ? ItemData.Text(classText, CharacterClassCatalog.DisplayName(info.Class)) : "",
            UserInfoCard.Thousands(info.Loyalty),
            UserInfoCard.Thousands(info.MonthlyLoyalty),
            state,
            UserInfoCard.Relation(friend, sameClan),
            info.ClanId != 0,
            info.ClanName,
            grade,
            info.ClanChief,
            info.ClanId != 0 ? clanMarks.Mark(info.ClanId, info.ClanMarkVersion) : null,
            info.ClanRanking,
            summary,
            rows);
    }

    private int NationInView(string name)
    {
        foreach (var entity in context.Entities.All())
            if (!entity.IsNpc && entity.Id != context.Self.CharId && string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase))
                return entity.Nation;
        return 0;
    }

    private string DailyRankTitle(int board) => dailyRankWindow.DrBoards.Find(b => b.Id == board)?.Title ?? "";

    private static string MarkTooltipText(int type)
    {
        int text = UserInfoCard.MarkTooltip(type);
        return text > 0 ? ItemData.Text(text, "") : "";
    }

    private Texture2D? RankRibbon(string name)
    {
        if (rankRibbons.TryGetValue(name, out var cached)) return cached;
        string path = RankMarkArt + name + ".png";
        var texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        rankRibbons[name] = texture;
        return texture;
    }

    private static string RelationText(UserRelation relation) => relation switch
    {
        UserRelation.Friend => "Friend",
        UserRelation.Clan => "Guild",
        UserRelation.Both => "Friend / Guild",
        _ => "-",
    };

    private void Fill(GameUserCard card)
    {
        uiClanName.Text = card.ClanName;
        uiClanGrade.Text = card.ClanGrade;
        uiClanChief.Text = card.ClanChief;
        uiClanMark.Texture = card.ClanMark;
        bool podium = card.InClan && card.ClanRanking is >= 1 and <= UserInfoCard.PodiumPlaces;
        string podiumPath = string.Format(ClanPodiumArt, card.ClanRanking);
        uiClanPodium.Texture = podium && ResourceLoader.Exists(podiumPath) ? ResourceLoader.Load<Texture2D>(podiumPath) : null;
        uiClanPlace.Text = card.InClan && card.ClanRanking > UserInfoCard.PodiumPlaces ? card.ClanRanking.ToString() : "";

        uiName.Text = card.Name;
        uiName.AddThemeColorOverride("font_color", card.NameColour ?? UiTheme.TextHi);
        uiLevel.Text = card.Level;
        uiJob.Text = card.Job;
        uiLoyalty.Text = $"{card.Loyalty} / {card.MonthlyLoyalty}";
        uiState.Text = card.State;
        uiRelation.Text = RelationText(card.Relation);

        foreach (var child in uiSummary.GetChildren()) child.QueueFree();
        foreach (var cell in card.Summary) uiSummary.AddChild(RibbonCell(cell));
        uiSummary.Visible = card.Summary.Count > 0;

        foreach (var child in uiRows.GetChildren()) child.QueueFree();
        foreach (var row in card.Rows)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 6);
            var title = UiTheme.Text(row.Title, 12, UiTheme.TextHi);
            title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(title);
            var place = UiTheme.Text(row.Place, 12, row.PlaceColour, HorizontalAlignment.Center);
            place.CustomMinimumSize = new Vector2(PlaceWidth, 0);
            line.AddChild(place);
            foreach (var tier in row.Tiers) line.AddChild(RibbonCell(tier));
            uiRows.AddChild(line);
        }
    }

    private static Control RibbonCell(GameRankRibbon ribbon)
    {
        var cell = new Control { CustomMinimumSize = new Vector2(RankRibbonWidth, RankRibbonHeight), TooltipText = ribbon.Tooltip };
        if (ribbon.Ribbon == null) return cell;
        var image = new TextureRect
        {
            Texture = ribbon.Ribbon,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        image.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        cell.AddChild(image);
        var count = UiTheme.Text(ribbon.Count, RankRibbonFont, UiTheme.GoldBright, HorizontalAlignment.Center);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        count.MouseFilter = Control.MouseFilterEnum.Ignore;
        count.AddThemeConstantOverride("outline_size", 3);
        count.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        cell.AddChild(count);
        return cell;
    }

    private sealed class PluginUserInfoBridge : IGameUserInfo
    {
        private readonly UserInfoFeature userInfo;

        public PluginUserInfoBridge(UserInfoFeature userInfo) => this.userInfo = userInfo;

        public bool IsOpen => userInfo.IsOpen;
        public GameUserCard? Card => userInfo.card;
        public void Close() => userInfo.Close();

        event Action? IGameUserInfo.Changed { add { } remove { } }
    }
}
