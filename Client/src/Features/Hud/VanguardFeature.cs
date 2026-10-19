using Godot;

namespace LibreKO.Features.Hud;

internal sealed class VanguardFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly VanguardSession vanguard;
    private readonly IVanguardRequests requests;
    private readonly MerchantFeature merchant;
    private readonly MapFeature map;

    public VanguardFeature(IWorldContext context, SelfSession self, VanguardSession vanguard, IVanguardRequests requests, MerchantFeature merchant, MapFeature map)
    {
        this.context = context;
        this.self = self;
        this.vanguard = vanguard;
        this.requests = requests;
        this.merchant = merchant;
        this.map = map;
    }

    public void Tick(double delta, double nowSec)
    {
        VanguardTick(delta);
    }

    private const int Hidden = 0;
    internal const int InviteFull = 1;
    internal const int ListFull = 2;
    private const int InviteMiniId = 3;
    internal const int ListMiniId = 4;
    private const int InviteWidth = 389;
    private const int InviteMiniWidth = 204;
    private const int ListWidth = 384;
    private const int ListMiniWidth = 171;
    private const int WindowInset = 20;
    private const int TopDivisor = 8;
    private const float BarHeight = 12f;
    private const float RingInner = 0.85f;
    private const float RingOuter = 1.05f;
    private const float RingLift = 0.08f;
    private const float BlipRadius = 6f;
    private static readonly Color LineColour = new("#" + Vanguard.ListColour);
    private static readonly Color AppointColour = new("#" + Vanguard.AppointedColour);
    private static readonly Color RingColour = new(0.62f, 0.25f, 0.95f, 0.85f);
    private static readonly Color BarColour = new("c0392b");
    private const string AppointTitle = "[Appoint Vanguard]";
    private const string AppointHeading = "ANNOUNCEMENT";
    private const string AppointBody = "Enemy nation is keeping an eye on you. Your nation want to appoint you as a Vanguard to raise the morale. Once you become a Vanguard, enemy nation will put a reward on your neck. However, if you survive, you will receive a huge reward. Would like to accept the offer?";
    private const string AppointSafety = "(Entering into Safety Zone is prohibited. You will be teleported to center of Ronarkland)";
    private const string AppointMiniLine = "If accept, You will be appointed as a Vanguard";
    private const string AppointMiniSafety = "(If accepted, you cannot enter Safety Zone)";
    private const string AppointHide = "Do not show [Appoint Vanguard]";
    private const string WantedTitle = "Wanted List";
    private const string WantedHeading = "WANTED";
    private const string WantedBody = "Enemy nation's vanguards are rampaging. Kill the vanguards from enemy nation and raise the morale of allies. Authorities will reward the knight who kills the enemy vanguard.";
    private const string WantedSee = "See [Wanted List]";
    private const string WantedHide = "Do not show [Wanted List]";
    private const string WantedToggleTip = "Wanted List UI on/off";
    private const string WantedOpenTip = "Wanted List UI";

    internal CanvasLayer Layer { get; set; } = null!;
    internal HudWindow Invite { get; private set; } = null!;
    internal HudWindow InviteMini { get; private set; } = null!;
    internal HudWindow List { get; private set; } = null!;
    internal HudWindow ListMini { get; private set; } = null!;
    private Label count = null!;
    internal Label Others { get; private set; } = null!;
    internal ColorRect NationBand { get; private set; } = null!;
    private StatBar bar = null!;
    private StatBar miniBar = null!;
    internal Label[] Rows { get; } = new Label[Vanguard.ListRows];
    private CheckBox inviteHide = null!;
    private CheckBox listHide = null!;
    private Button wantedToggle = null!;
    private Button wantedOpen = null!;
    internal bool UiOn { get; private set; } = true;
    internal double Countdown { get; set; }
    private readonly Dictionary<string, MeshInstance3D> rings = new(System.StringComparer.OrdinalIgnoreCase);

    public void Build()
    {
        map.AddMiniSource(AddBlips);
        Layer = new CanvasLayer { Layer = UiLayers.EventPanels };
        context.Root.AddChild(Layer);
        BuildInvite();
        BuildInviteMini();
        BuildList();
        BuildListMini();
        BuildWantedMiniMapButtons();
        vanguard.VanguardEvent += On;
    }

    public void Dispose() => vanguard.VanguardEvent -= On;

    private HudWindow Window(string id, string title, int width)
    {
        float screenWidth = context.Root.IsInsideTree() ? context.Root.GetViewport().GetVisibleRect().Size.X : 0f;
        float screenHeight = context.Root.IsInsideTree() ? context.Root.GetViewport().GetVisibleRect().Size.Y : 0f;
        var origin = new Vector2(Mathf.Max(0f, screenWidth - width), screenHeight / TopDivisor);
        var window = new HudWindow(id, title, origin, width - WindowInset) { Visible = false };
        window.Closed += () => window.Visible = false;
        Layer.AddChild(window);
        return window;
    }

    private static VBoxContainer Body(HudWindow window)
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        window.Body.AddChild(body);
        return body;
    }

    private static Label Text(string text, int size, Color colour, bool wrap = false, bool centred = false)
    {
        var label = UiTheme.Text(text, size, colour);
        if (wrap) label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        if (centred) label.HorizontalAlignment = HorizontalAlignment.Center;
        return label;
    }

    internal void BuildInvite()
    {
        Invite = Window("vanguard_invite", AppointTitle, InviteWidth);
        var body = Body(Invite);
        NationBand = new ColorRect { CustomMinimumSize = new Vector2(0f, 4f) };
        body.AddChild(NationBand);
        body.AddChild(Text(AppointHeading, 14, UiTheme.GoldBright, centred: true));
        body.AddChild(Text(AppointBody, 12, UiTheme.TextHi, wrap: true));
        body.AddChild(Text(AppointSafety, 12, UiTheme.TextLo, wrap: true));

        var countRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        countRow.AddChild(Text("You have", 12, UiTheme.TextHi));
        count = Text("10", 12, UiTheme.GoldBright);
        countRow.AddChild(count);
        countRow.AddChild(Text("seconds left to decide", 12, UiTheme.TextHi));
        body.AddChild(countRow);
        bar = new StatBar(BarColour, new Vector2(InviteWidth - 2 * WindowInset, BarHeight));
        body.AddChild(bar);
        body.AddChild(AnswerRow());

        inviteHide = UiTheme.FlatCheck(AppointHide, false);
        inviteHide.Toggled += on => { if (on) TurnUiOff(); };
        body.AddChild(inviteHide);
    }

    private HBoxContainer AnswerRow()
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 24);
        var accept = UiTheme.ActionButton("Accept", "");
        accept.Pressed += Accept;
        var reject = UiTheme.SmallButton("Reject", "");
        reject.Pressed += HideInvites;
        row.AddChild(accept);
        row.AddChild(reject);
        return row;
    }

    internal void BuildInviteMini()
    {
        InviteMini = Window("vanguard_invite_mini", AppointTitle, InviteMiniWidth);
        var body = Body(InviteMini);
        var detail = UiTheme.SmallButton("Detail", "");
        detail.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        detail.Pressed += () => ShowMode(InviteFull);
        body.AddChild(detail);
        body.AddChild(Text(AppointMiniLine, 12, UiTheme.TextHi, wrap: true));
        body.AddChild(Text(AppointMiniSafety, 12, UiTheme.TextLo, wrap: true));
        miniBar = new StatBar(BarColour, new Vector2(InviteMiniWidth - 2 * WindowInset, BarHeight));
        body.AddChild(miniBar);
        body.AddChild(AnswerRow());
    }

    internal void BuildList()
    {
        List = Window("vanguard_list", WantedTitle, ListWidth);
        var body = Body(List);
        body.AddChild(Text(WantedHeading, 14, UiTheme.GoldBright, centred: true));
        body.AddChild(Text(WantedBody, 12, UiTheme.TextHi, wrap: true));
        var rowsGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        rowsGrid.AddThemeConstantOverride("h_separation", 8);
        for (int i = 0; i < Vanguard.ListRows; i++)
        {
            rowsGrid.AddChild(Text(Vanguard.RowLabel(i), 13, UiTheme.TextLo));
            Rows[i] = Text(Vanguard.EmptyRow, 13, UiTheme.TextHi);
            Rows[i].CustomMinimumSize = new Vector2(160f, 0f);
            rowsGrid.AddChild(Rows[i]);
        }
        body.AddChild(rowsGrid);
        var othersRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        othersRow.AddChild(Text("...", 12, UiTheme.TextLo));
        Others = Text("0", 12, UiTheme.GoldBright);
        othersRow.AddChild(Others);
        othersRow.AddChild(Text("user(s)", 12, UiTheme.TextLo));
        body.AddChild(othersRow);
        listHide = UiTheme.FlatCheck(WantedHide, false);
        listHide.Toggled += on =>
        {
            if (!on) return;
            TurnUiOff();
            List.Visible = false;
        };
        body.AddChild(listHide);
    }

    internal void BuildListMini()
    {
        ListMini = Window("vanguard_list_mini", WantedTitle, ListMiniWidth);
        var body = Body(ListMini);
        body.AddChild(Text(WantedSee, 12, UiTheme.TextHi, centred: true));
        var detail = UiTheme.ActionButton("Detail", "");
        detail.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        detail.Pressed += () =>
        {
            ListMini.Visible = false;
            if (Vanguard.ModeShown(UiOn, merchant.SelfStallOpen)) ShowWantedList();
        };
        body.AddChild(detail);
    }

    private void BuildWantedMiniMapButtons()
    {
        if (map.MiniMap == null) return;
        wantedToggle = UiTheme.IconButton(UiIcons.Get("system/scroll"), WantedToggleTip);
        wantedToggle.Position = new Vector2(4f, 162f);
        wantedToggle.Pressed += ToggleUi;
        map.MiniMap.AddChild(wantedToggle);
        wantedOpen = UiTheme.IconButton(UiIcons.Get("system/search"), WantedOpenTip);
        wantedOpen.Position = new Vector2(4f, 186f);
        wantedOpen.Pressed += ShowWantedList;
        map.MiniMap.AddChild(wantedOpen);
        RefreshWantedToggle();
    }

    internal void ToggleUi()
    {
        UiOn = !UiOn;
        RefreshWantedToggle();
    }

    private void TurnUiOff()
    {
        UiOn = false;
        HideInvites();
        RefreshWantedToggle();
    }

    private void RefreshWantedToggle()
    {
        if (wantedToggle == null || !GodotObject.IsInstanceValid(wantedToggle)) return;
        wantedToggle.Modulate = UiOn ? Colors.White : UiTheme.TextLo;
    }

    private void HideInvites()
    {
        Invite.Visible = false;
        InviteMini.Visible = false;
    }

    private void Accept()
    {
        HideInvites();
        requests.Accept();
    }

    private void ShowMode(int mode)
    {
        if (merchant.SelfStallOpen) return;
        if (mode == InviteMiniId) Countdown = Vanguard.InviteSeconds;
        bool shown = UiOn;
        Invite.Visible = shown && mode == InviteFull;
        InviteMini.Visible = shown && mode == InviteMiniId;
        ListMini.Visible = shown && mode == ListMiniId;
        if (shown && mode == ListFull) ShowWantedList();
        else List.Visible = false;
        if (Invite.Visible)
            NationBand.Color = new Color("#" + DeathNotices.NameColour(self.Nation));
        foreach (var window in new[] { Invite, InviteMini, ListMini })
            if (window.Visible) UiKit.ShowOnTop(window);
    }

    internal void ShowWantedList()
    {
        var names = vanguard.WantedNames;
        for (int i = 0; i < Vanguard.ListRows; i++)
            Rows[i].Text = i < names.Count ? names[i] : Vanguard.EmptyRow;
        Others.Text = Vanguard.OthersCount(vanguard.WantedCount).ToString();
        Invite.Visible = false;
        InviteMini.Visible = false;
        ListMini.Visible = false;
        UiKit.ShowOnTop(List);
    }

    private void On(VanguardReply reply)
    {
        switch (reply.Kind)
        {
            case VanguardReplyKind.Invite:
                ShowMode(InviteMiniId);
                Countdown = Vanguard.InviteSeconds;
                break;
            case VanguardReplyKind.Appointed:
                context.Log.Notice(ItemData.Text(Vanguard.AppointedText, "You have been appointed as a Vanguard. You are now wanted from enemy nation"), AppointColour);
                break;
            case VanguardReplyKind.AppointFailed:
                context.Log.Notice(ItemData.Text(Vanguard.FailedText, "Nomination Failed - Vanguard recruitment has been completed"), AppointColour);
                break;
            case VanguardReplyKind.List:
                if (reply.Names.Count == 0) break;
                ShowMode(ListMiniId);
                foreach (var line in Vanguard.ListLines(reply.Names))
                    context.Log.Notice(line, LineColour);
                break;
            case VanguardReplyKind.Positions:
                if (self.IsGm)
                    for (int i = 0; i < reply.Marks.Count; i++)
                        context.Log.Notice(Vanguard.GmLine(i, reply.Marks[i].Name, reply.Marks[i].X, reply.Marks[i].Z), LineColour);
                break;
            case VanguardReplyKind.Closed:
                context.Log.Notice(ItemData.Text(Vanguard.ClosedText, "Closed"), LineColour);
                break;
        }
    }

    private void VanguardTick(double delta)
    {
        if (Countdown > 0)
        {
            Countdown = Mathf.Max(0, Countdown - delta);
            float left = (float)(Countdown / Vanguard.InviteSeconds);
            bar.SetFraction(left, "");
            miniBar.SetFraction(left, "");
            count.Text = ((int)Countdown).ToString();
            if (Countdown <= 0) HideInvites();
        }
        SyncRings();
    }

    private void SyncRings()
    {
        var wanted = vanguard.WantedNames;
        if (wanted.Count == 0 && rings.Count == 0) return;
        var keep = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var e in context.Entities.Views.Values)
        {
            if (e.IsNpc || e.Dead || !wanted.Contains(e.Name)) continue;
            keep.Add(e.Name);
            if (rings.TryGetValue(e.Name, out var ring) && GodotObject.IsInstanceValid(ring) && ring.GetParent() == e.Body) continue;
            if (ring != null && GodotObject.IsInstanceValid(ring)) ring.QueueFree();
            ring = Ring();
            e.Body.AddChild(ring);
            rings[e.Name] = ring;
        }
        foreach (var name in new List<string>(rings.Keys))
        {
            if (keep.Contains(name)) continue;
            if (GodotObject.IsInstanceValid(rings[name])) rings[name].QueueFree();
            rings.Remove(name);
        }
    }

    private static MeshInstance3D Ring() => new()
    {
        Mesh = new TorusMesh { InnerRadius = RingInner, OuterRadius = RingOuter },
        Position = new Vector3(0f, RingLift, 0f),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = RingColour,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        },
    };

    private void AddBlips(List<MiniMap.Blip> blips)
    {
        foreach (var mark in vanguard.VanguardMarks)
            blips.Add(new MiniMap.Blip(mark.X, mark.Z, NamePlate.Enemy, BlipRadius, true));
    }
}
