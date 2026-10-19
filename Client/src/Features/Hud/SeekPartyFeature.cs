using Godot;

namespace LibreKO.Features.Hud;

internal sealed class SeekPartyFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly BifrostSession bifrost;
    private readonly PartySession party;
    private readonly SelfSession self;
    private readonly IBifrostRequests bifrostRequests;
    private readonly IPartyRequests requests;
    private readonly IShoutRequests shoutRequests;
    private readonly EntryPopupsFeature entryPopups;
    private readonly PartyInvitesFeature partyInvites;

    public SeekPartyFeature(IWorldContext context, BifrostSession bifrost, PartySession party, SelfSession self, IBifrostRequests bifrostRequests, IPartyRequests requests, IShoutRequests shoutRequests, EntryPopupsFeature entryPopups, PartyInvitesFeature partyInvites)
    {
        this.context = context;
        this.bifrost = bifrost;
        this.party = party;
        this.self = self;
        this.bifrostRequests = bifrostRequests;
        this.requests = requests;
        this.shoutRequests = shoutRequests;
        this.entryPopups = entryPopups;
        this.partyInvites = partyInvites;
    }

    public void Build()
    {
        BuildSeekPanel();
        party.PartyBbsRegisterEvent += OnBbsRegister;
        party.PartyBbsDeleteEvent += OnBbsDelete;
        party.PartyBbsListEvent += OnBbsList;
        party.PartyBbsWantedFailEvent += OnBbsWantedFail;
        FullMoonInit();
    }

    public void Dispose()
    {
        FullMoonDispose();
        party.PartyBbsRegisterEvent -= OnBbsRegister;
        party.PartyBbsDeleteEvent -= OnBbsDelete;
        party.PartyBbsListEvent -= OnBbsList;
        party.PartyBbsWantedFailEvent -= OnBbsWantedFail;
    }

    public void Tick(double delta, double nowSec) => FullMoonAutoTick(nowSec);

    private CanvasLayer seekLayer = null!;
    internal Control SeekPanel { get; private set; } = null!;
    private VBoxContainer seekListBox = null!;
    private Label seekPageLbl = null!;
    private Label seekStatusLbl = null!;
    private Button seekRegisterBtn = null!;
    private OptionButton wantedClassOpt = null!;
    private LineEdit wantedMsgInput = null!;
    private HBoxContainer seekFilters = null!;
    private HBoxContainer fullMoonRow = null!;
    private Label seekHeading = null!;
    internal PartyBoardType? SeekBoardKind { get; set; }

    private PartyBoardType SeekBoard => SeekBoardKind == PartyBoardType.FullMoon ? PartyBoardType.FullMoon : PartyBoardType.Normal;

    internal bool SeekShown { get; set; }

    private bool seeking;

    private int seekPage;
    private int seekTotal;
    private PartyBoardFilter seekFilter = new(PartyBoardKind.All, false, false);
    private List<PartyBbsEntry> seekEntries = new();

    private static readonly (string Label, int Code)[] WantedClasses =
    {
        ("Any", 0), ("Warrior", 1), ("Rogue", 2), ("Mage", 3), ("Priest", 4),
    };

    internal void BuildSeekPanel()
    {
        seekLayer = new CanvasLayer { Layer = UiLayers.HudPopups };
        context.Root.AddChild(seekLayer);

        var win = new HudWindow("seek_party", "Party Board", new Vector2(300, 110), 420) { Visible = false };
        win.Closed += () =>
        {
            SeekShown = false;
            if (!seeking) SeekBoardKind = null;
        };
        seekLayer.AddChild(win);
        SeekPanel = win;

        var root = win.Body;
        root.AddThemeConstantOverride("separation", 6);

        seekHeading = UiTheme.Text("", 14, UiTheme.GoldBright);
        root.AddChild(seekHeading);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 5); root.AddChild(top);
        seekRegisterBtn = SmallButton("Look for a party", top);
        seekRegisterBtn.Pressed += ToggleSeeking;
        SmallButton("Refresh", top).Pressed += () => requests.BbsList(seekPage, SeekBoard);
        SmallButton("Invite by name", top).Pressed += partyInvites.OpenInvitePrompt;

        seekStatusLbl = HudStyle.Label(12);
        seekStatusLbl.AddThemeColorOverride("font_color", new Color("b9c0c8"));
        root.AddChild(seekStatusLbl);

        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 8);
        root.AddChild(filters);
        seekFilters = filters;
        fullMoonRow = new HBoxContainer { Visible = false };
        fullMoonRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(fullMoonRow);
        SmallButton("Enter", fullMoonRow).Pressed += EnterFullMoon;
        SmallButton("Party Board", fullMoonRow).Pressed += ShoutFullMoon;
        SmallButton("Auto Entry", fullMoonRow).Pressed += StartFullMoonAutoEntry;
        var kind = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        kind.AddThemeFontSizeOverride("font_size", 12);
        kind.AddItem("Everyone", (int)PartyBoardKind.All);
        kind.AddItem("Players", (int)PartyBoardKind.Players);
        kind.AddItem("Parties", (int)PartyBoardKind.Parties);
        kind.ItemSelected += index => SetSeekFilter(seekFilter with { Kind = (PartyBoardKind)index });
        filters.AddChild(kind);
        var zone = new CheckBox { Text = "This zone", FocusMode = Control.FocusModeEnum.None };
        zone.AddThemeFontSizeOverride("font_size", 12);
        zone.Toggled += on => SetSeekFilter(seekFilter with { SameZone = on });
        filters.AddChild(zone);
        var band = new CheckBox { Text = "My level range", FocusMode = Control.FocusModeEnum.None };
        band.AddThemeFontSizeOverride("font_size", 12);
        band.Toggled += on => SetSeekFilter(seekFilter with { MyLevelBand = on });
        filters.AddChild(band);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(420, 230) };
        root.AddChild(scroll);
        seekListBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        seekListBox.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(seekListBox);

        var nav = new HBoxContainer(); nav.AddThemeConstantOverride("separation", 6); root.AddChild(nav);
        SmallButton("◄ Prev", nav).Pressed += () => { if (seekPage > 0) { seekPage--; requests.BbsList(seekPage, SeekBoard); } };
        seekPageLbl = HudStyle.Label(12); seekPageLbl.Text = "Page 1"; nav.AddChild(seekPageLbl);
        SmallButton("Next ►", nav).Pressed += () =>
        {
            if ((seekPage + 1) * 10 < seekTotal) { seekPage++; requests.BbsList(seekPage, SeekBoard); }
        };

        root.AddChild(new HSeparator());

        var wlbl = HudStyle.Label(13); wlbl.Text = "Recruit (party leader only):"; root.AddChild(wlbl);
        var wrow = new HBoxContainer(); wrow.AddThemeConstantOverride("separation", 5); root.AddChild(wrow);
        wantedClassOpt = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        wantedClassOpt.AddThemeFontSizeOverride("font_size", 12);
        for (int i = 0; i < WantedClasses.Length; i++) wantedClassOpt.AddItem(WantedClasses[i].Label, i);
        wrow.AddChild(wantedClassOpt);
        wantedMsgInput = new LineEdit { MaxLength = 60, PlaceholderText = "Recruiting message",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        wantedMsgInput.TextSubmitted += _ => PostWanted();
        wrow.AddChild(wantedMsgInput);
        SmallButton("Post", wrow).Pressed += PostWanted;
    }

    internal void ToggleSeek()
    {
        if (!SeekShown && SeekBoardKind == null) SeekBoardKind = PartyBoardType.Normal;
        SeekShown = !SeekShown;
        SeekPanel.Visible = SeekShown;
        if (SeekShown) { ApplySeekBoard(); requests.BbsList(seekPage, SeekBoard); UpdateSeekStatus(); }
    }

    internal void ApplySeekBoard()
    {
        bool fullMoon = SeekBoard == PartyBoardType.FullMoon;
        seekFilters.Visible = !fullMoon;
        fullMoonRow.Visible = fullMoon;
        seekHeading.Visible = fullMoon;
        seekHeading.Text = fullMoon ? ItemData.Text(FullMoon.ZoneNameText, "Full Moon Rift") : "";
        if (fullMoon) seekFilter = new PartyBoardFilter(PartyBoardKind.All, false, false);
    }

    private void ToggleSeeking()
    {
        if (seeking) requests.BbsDelete(SeekBoard);
        else requests.BbsRegister(SeekBoard);
    }

    private void PostWanted()
    {
        int code = WantedClasses[Mathf.Clamp(wantedClassOpt.Selected, 0, WantedClasses.Length - 1)].Code;
        requests.BbsWanted(code, seekPage, wantedMsgInput.Text.Trim(), SeekBoard);
    }

    internal void UpdateSeekStatus()
    {
        seekRegisterBtn.Text = seeking ? "Stop looking" : "Look for a party";
        seekStatusLbl.Text = seeking
            ? "You are listed as looking for a party."
            : "Browse players seeking a party, or list yourself.";
    }

    private void OnBbsRegister(bool ok)
    {
        if (ok) { seeking = true; context.Log.Notice("You are now listed as looking for a party."); }
        else context.Log.Notice("Can't seek a party while you're already in one.");
        UpdateSeekStatus();
        if (SeekShown) requests.BbsList(seekPage, SeekBoard);
    }

    private void OnBbsDelete()
    {
        seeking = false;
        context.Log.Notice("Removed your seek-party listing.");
        UpdateSeekStatus();
        if (SeekShown) requests.BbsList(seekPage, SeekBoard);
    }

    private void OnBbsWantedFail() => context.Log.Notice("Only the party leader can post a recruiting message.");

    private void SetSeekFilter(PartyBoardFilter filter)
    {
        seekFilter = filter;
        if (SeekShown) ShowSeekEntries();
    }

    internal void OnBbsList(int page, int total, List<PartyBbsEntry> entries)
    {
        seekPage = page;
        seekTotal = total;
        seekEntries = entries;
        if (!SeekShown) return;
        ShowSeekEntries();
    }

    private void ShowSeekEntries()
    {
        int page = seekPage, total = seekTotal;
        string selfName = self.LastEnter.Name ?? "";
        var entries = seekEntries.FindAll(e => seekFilter.Passes(e, context.Zone, self.Sheet.Level)
            && !string.Equals(e.Name, selfName, System.StringComparison.OrdinalIgnoreCase));

        foreach (var c in seekListBox.GetChildren()) c.QueueFree();

        int pages = Mathf.Max(1, (total + 9) / 10);
        seekPageLbl.Text = $"Page {page + 1} / {pages}";

        if (entries.Count == 0)
        {
            var empty = HudStyle.Label(13);
            empty.Text = seekEntries.Count > 0 ? "Nobody here matches these filters." : "Nobody is seeking a party right now.";
            empty.AddThemeColorOverride("font_color", new Color("b9c0c8"));
            seekListBox.AddChild(empty);
            return;
        }

        foreach (var e in entries)
            seekListBox.AddChild(BuildSeekRow(e));
    }

    private Control BuildSeekRow(PartyBbsEntry e)
    {
        var panel = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.05f) };
        foreach (var s in new[] { "left", "right", "top", "bottom" }) sb.Set($"content_margin_{s}", 6f);
        sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 3;
        panel.AddThemeStyleboxOverride("panel", sb);

        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);

        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", 1);
        row.AddChild(info);

        var head = HudStyle.Label(14);
        head.Text = e.IsLeaderRecruiting
            ? $"{e.Name}  ·  party {e.MemberCount}/8  ·  wants {CharacterClassCatalog.DisplayName(e.ClassOrWanted)}"
            : $"{e.Name}  ·  Lv {e.Level}  {CharacterClassCatalog.DisplayName(e.ClassOrWanted)}";
        info.AddChild(head);

        var sub = HudStyle.Label(11);
        string zone = SeekZoneName(e.ZoneId);
        sub.Text = e.Message.Length > 0 ? $"[{zone}]  {e.Message}" : $"[{zone}]";
        sub.AddThemeColorOverride("font_color", new Color("b9c0c8"));
        info.AddChild(sub);

        string name = e.Name;
        var inviteBtn = SmallButton("Invite", row);
        inviteBtn.Pressed += () =>
        {
            requests.BbsInvite(name, SeekBoard);
            context.Log.Notice($"{name} {ItemData.Text(PartyInvitesFeature.InvitedText, "was invited into the party. Waiting for a response.")}");
        };
        return panel;
    }

    private static string SeekZoneName(int zoneId)
    {
        foreach (var z in ZoneCatalog.All) if (z.Id == zoneId) return z.Name;
        return $"Zone {zoneId}";
    }

    private const string FullMoonCaptionPath = "res://assets/ui/fullmoon/caption.png";
    private static readonly Vector2 FullMoonCaptionSize = new(800f, 605f);
    private static readonly Rect2 FullMoonHideToday = new(481f, 576f, 22f, 21f);
    private static readonly Rect2 FullMoonClose = new(723f, 578f, 49f, 17f);
    private static readonly Color FullMoonLineColour = new(1f, 1f, 0f);
    private static readonly Color FullMoonRefusalColour = new(1f, 0f, 0f);

    private static readonly int[] FullMoonNoAutoBoardZones = { 37, 38, 81, 82, 83, 84, 87 };

    internal CanvasLayer? FullMoonCaptionLayer { get; private set; }
    internal Control? FullMoonCaption { get; set; }
    private double fullMoonShoutAt = double.NegativeInfinity;
    private bool fullMoonAutoEntry;
    private double fullMoonAutoAt = double.NegativeInfinity;

    private void FullMoonInit()
    {
        bifrost.FullMoonEntryEvent += OnFullMoonEntry;
        bifrost.FullMoonTowerEvent += OnFullMoonTower;
        bifrost.FullMoonStageEvent += OnFullMoonStage;
        party.PartyMemberEvent += OnFullMoonMember;
    }

    private void FullMoonDispose()
    {
        bifrost.FullMoonEntryEvent -= OnFullMoonEntry;
        bifrost.FullMoonTowerEvent -= OnFullMoonTower;
        bifrost.FullMoonStageEvent -= OnFullMoonStage;
        party.PartyMemberEvent -= OnFullMoonMember;
    }

    private int Size => party.Members.Count;

    internal void OpenFullMoonBoard()
    {
        if (FullMoon.OpenRefusal(party.InParty, Size, party.PartyType, SeekBoardKind) is { } refusal)
        {
            context.Log.Notice(ItemData.Text(refusal.Id, refusal.Fallback), FullMoonLineColour);
            return;
        }
        SeekBoardKind = PartyBoardType.FullMoon;
        seekPage = 0;
        SeekShown = true;
        SeekPanel.Visible = true;
        ApplySeekBoard();
        UpdateSeekStatus();
        requests.BbsList(0, PartyBoardType.FullMoon);
    }

    private void OnFullMoonMember(PartyMember member)
    {
        if (member.CharId == context.Self.CharId || !party.IsLeader(context.Self.CharId) || Size != FullMoon.PartySize
            || party.PartyType != PartyBoardType.FullMoon || Array.IndexOf(FullMoonNoAutoBoardZones, context.Zone) >= 0)
            return;
        OpenFullMoonBoard();
    }

    private int FullMoonAutoPick() =>
        PartyAutoEntry.Pick(seekEntries, context.Zone, self.LastEnter.Class, self.Sheet.Level);

    private void StartFullMoonAutoEntry()
    {
        if (party.InParty || fullMoonAutoEntry) return;
        if (FullMoonAutoPick() < 0)
        {
            context.Log.Notice(ItemData.Text(PartyAutoEntry.NoneText, "There are no parties you can enter automatically."), FullMoonLineColour);
            return;
        }
        fullMoonAutoEntry = true;
        fullMoonAutoAt = double.NegativeInfinity;
    }

    private void FullMoonAutoTick(double now)
    {
        if (!fullMoonAutoEntry) return;
        if (party.InParty || !SeekShown)
        {
            fullMoonAutoEntry = false;
            return;
        }
        if (now - fullMoonAutoAt < PartyAutoEntry.RetrySeconds) return;
        fullMoonAutoAt = now;
        context.Log.Notice(ItemData.Text(PartyAutoEntry.SearchingText, "Searching for a party"), FullMoonLineColour);
        int pick = FullMoonAutoPick();
        if (pick < 0) return;
        string leader = seekEntries[pick].Name;
        requests.BbsInvite(leader, SeekBoard);
        context.Log.Notice(ItemData.Text(PartyAutoEntry.AppliedText, "Sent invitation to join the party to %s.").Replace("%s", leader), FullMoonLineColour);
    }

    private void EnterFullMoon()
    {
        if (party.InParty && !party.IsLeader(context.Self.CharId)) return;
        bifrostRequests.FullMoonEnter();
    }

    private void ShoutFullMoon()
    {
        if (!party.InParty || !party.IsLeader(context.Self.CharId) || Size < FullMoon.PartySize) return;
        double left = fullMoonShoutAt + FullMoon.ShoutCooldownSeconds - context.Now;
        if (left > 0)
        {
            context.Log.Notice(TextTemplate.Fill(ItemData.Text(FullMoon.CooldownText, "It is available in %d seconds"), (int)left + 1),
                FullMoonLineColour);
            return;
        }
        fullMoonShoutAt = context.Now;
        shoutRequests.FullMoonShout(ItemData.Text(FullMoon.ShoutText, "%s is searching for Full Moon Rift Party")
            .Replace("%s", self.LastEnter.Name));
    }

    private void OnFullMoonEntry(byte code)
    {
        var text = FullMoon.EntryText(code);
        context.Log.Notice(ItemData.Text(text.Id, text.Fallback), FullMoonRefusalColour);
        SeekShown = false;
        SeekPanel.Visible = false;
    }

    private void OnFullMoonTower(byte remaining)
    {
        var line = FullMoon.TowerText(remaining);
        context.Log.Notice(TextTemplate.Fill(ItemData.Text(line.Id, line.Fallback), Array.ConvertAll(line.Args, a => (object)a)),
            FullMoonLineColour);
    }

    private void OnFullMoonStage(byte total, byte current)
    {
        var line = FullMoon.StageLine(total, current);
        context.Log.Notice(TextTemplate.Fill(ItemData.Text(line.Id, line.Fallback), Array.ConvertAll(line.Args, a => (object)a)),
            FullMoonLineColour);
    }

    internal void ShowFullMoonCaptionOnEntry()
    {
        if (context.Zone != FullMoon.Zone || !ResourceLoader.Exists(FullMoonCaptionPath)
            || TimedItems.HiddenToday(Config.FullMoonCaptionHiddenDay, DateTime.Now)) return;
        entryPopups.QueuePopup(EntryPopup.ZoneCaption, () =>
        {
            FullMoonCaption ??= BuildFullMoonCaption();
            if (FullMoonCaption == null) return null;
            FullMoonCaption.Visible = true;
            return FullMoonCaption;
        });
    }

    internal Control? BuildFullMoonCaption()
    {
        if (!ResourceLoader.Exists(FullMoonCaptionPath)) return null;

        FullMoonCaptionLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(FullMoonCaptionLayer);
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        FullMoonCaptionLayer.AddChild(centre);
        var picture = new TextureRect
        {
            Texture = GD.Load<Texture2D>(FullMoonCaptionPath),
            CustomMinimumSize = FullMoonCaptionSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        centre.AddChild(picture);
        picture.AddChild(FullMoonHotspot(FullMoonHideToday, "Do not show for a day", () =>
        {
            Config.SetFullMoonCaptionHiddenDay(TimedItems.DayKey(DateTime.Now));
            centre.Visible = false;
        }));
        picture.AddChild(FullMoonHotspot(FullMoonClose, "Close", () => centre.Visible = false));
        return centre;
    }

    private static Button FullMoonHotspot(Rect2 area, string tip, Action pressed)
    {
        var button = new Button { Flat = true, Position = area.Position, Size = area.Size, TooltipText = tip, FocusMode = Control.FocusModeEnum.None };
        foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        button.Pressed += pressed;
        return button;
    }

    private static Button SmallButton(string text, Container parent)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 12);
        parent.AddChild(button);
        return button;
    }
}
