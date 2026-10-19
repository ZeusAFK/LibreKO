using Godot;

namespace LibreKO.Features.Social;

internal sealed class ClanPointsFeature : IFeature, IEscapable
{
    internal const string MsgPointsOnlyAccredited = "Accredited Knights Grade 5 or above can save-up Contribution.";
    private const string MsgCommandUnavailable = "This command cannot be used.";
    private const int PointsPanelWidth = 340;

    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly IClanRequests requests;

    public ClanPointsFeature(IWorldContext context, ClanSession clan, ProgressSession progress, SelfSession self, IClanRequests requests)
    {
        this.context = context;
        this.clan = clan;
        this.progress = progress;
        this.self = self;
        this.requests = requests;
    }

    public void Build()
    {
        BuildPanel();
        clan.ClanPointStatusEvent += OnPointStatus;
        clan.ClanDonateEvent += OnDonate;
        clan.ClanPointMethodEvent += OnPointMethod;
        progress.LoyaltyChangeEvent += OnPointsLoyalty;
    }

    public void Dispose()
    {
        clan.ClanPointStatusEvent -= OnPointStatus;
        clan.ClanDonateEvent -= OnDonate;
        clan.ClanPointMethodEvent -= OnPointMethod;
        progress.LoyaltyChangeEvent -= OnPointsLoyalty;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (!PointsShown) return false;
        ClosePoints();
        return true;
    }

    private CanvasLayer pointsLayer = null!;
    internal HudWindow PointsPanel { get; private set; } = null!;
    private Label pointsMineLbl = null!;
    private Label pointsFundLbl = null!;
    private Label pointsStatus = null!;
    private LineEdit pointsEdit = null!;
    private Button pointsSaveBtn = null!;
    private CheckBox pointsAuto = null!;
    private CheckBox pointsFree = null!;
    private VBoxContainer pointsMethodBox = null!;
    internal bool PointsShown { get; set; }

    internal void BuildPanel()
    {
        pointsLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(pointsLayer);

        PointsPanel = new HudWindow("clanpoint", "Clan Contribution", bodyMinWidth: PointsPanelWidth)
        { Visible = false };
        PointsPanel.Closed += ClosePoints;
        pointsLayer.AddChild(PointsPanel);
        var root = PointsPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var intro = UiTheme.Text(
            "National points you save here become the clan's Contribution. They pay for the Knights' promotions and capes, and you get back a third if you leave.",
            12, UiTheme.TextLo);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.CustomMinimumSize = new Vector2(1, 0);
        root.AddChild(intro);

        var card = UiTheme.Section();
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        root.AddChild(card);
        var margin = new MarginContainer();
        UiTheme.Margins(margin, 10, 8, 10, 8);
        card.AddChild(margin);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        margin.AddChild(col);
        pointsMineLbl = PointsRow(col, "Your national points");
        pointsFundLbl = PointsRow(col, "Clan Contribution");

        var amountRow = new HBoxContainer();
        amountRow.AddThemeConstantOverride("separation", 6);
        root.AddChild(amountRow);
        var amountLbl = UiTheme.Text("Save", 13, UiTheme.TextHi);
        amountLbl.CustomMinimumSize = new Vector2(60, 0);
        amountRow.AddChild(amountLbl);
        pointsEdit = new LineEdit
        {
            PlaceholderText = "national points",
            Alignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        pointsEdit.TextSubmitted += _ => SubmitPoints();
        amountRow.AddChild(pointsEdit);
        pointsSaveBtn = new Button { Text = "Save", FocusMode = Control.FocusModeEnum.None };
        pointsSaveBtn.Pressed += SubmitPoints;
        amountRow.AddChild(pointsSaveBtn);

        pointsMethodBox = new VBoxContainer();
        pointsMethodBox.AddThemeConstantOverride("separation", 2);
        root.AddChild(pointsMethodBox);
        pointsMethodBox.AddChild(UiTheme.SectionTitle("Accumulation"));
        var group = new ButtonGroup();
        pointsAuto = new CheckBox { Text = "Automatic: a share of every member's earned points goes to the clan", ButtonGroup = group, FocusMode = Control.FocusModeEnum.None };
        pointsFree = new CheckBox { Text = "Free: members save points here when they choose", ButtonGroup = group, FocusMode = Control.FocusModeEnum.None };
        pointsMethodBox.AddChild(pointsAuto);
        pointsMethodBox.AddChild(pointsFree);
        var confirmRow = new HBoxContainer();
        pointsMethodBox.AddChild(confirmRow);
        confirmRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var confirm = new Button { Text = "Confirm", FocusMode = Control.FocusModeEnum.None };
        confirm.Pressed += () => requests.PointMethod(
            pointsAuto.ButtonPressed ? ClanTypes.AutomaticAccumulation : ClanTypes.FreeAccumulation);
        confirmRow.AddChild(confirm);

        pointsStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        pointsStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        pointsStatus.CustomMinimumSize = new Vector2(1, 0);
        root.AddChild(pointsStatus);

    }

    private static Label PointsRow(VBoxContainer col, string label)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        col.AddChild(row);
        var key = UiTheme.Text(label, 12, UiTheme.TextLo);
        key.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(key);
        var value = UiTheme.Text("0", 14, UiTheme.GoldBright);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(value);
        return value;
    }

    internal void Toggle()
    {
        if (PointsShown) { ClosePoints(); return; }
        if (!clan.MyClan.InClan) return;
        PointsShown = true;
        PointsPanel.Visible = true;
        pointsEdit.Clear();
        SetPointsStatus("", false);
        ApplyPointsState();
        requests.PointStatus();
    }

    internal void ClosePoints()
    {
        if (!PointsShown) return;
        PointsShown = false;
        PointsPanel.Visible = false;
    }

    internal void ApplyPointsState()
    {
        var clanInfo = clan.MyClan;
        bool accredited = ClanTypes.AcceptsDonations(clanInfo.Flag);
        pointsMineLbl.Text = $"{self.Sheet.Np:n0}";
        pointsFundLbl.Text = $"{clanInfo.PointFund:n0}   ({clanInfo.PointFund / ClanTypes.NationalPointsPerClanPoint:n0} points)";
        pointsSaveBtn.Disabled = !accredited;
        pointsEdit.Editable = accredited;
        pointsMethodBox.Visible = clanInfo.IsChief && accredited;
        pointsAuto.ButtonPressed = clanInfo.PointMethod == ClanTypes.AutomaticAccumulation;
        pointsFree.ButtonPressed = clanInfo.PointMethod != ClanTypes.AutomaticAccumulation;
        if (!accredited) SetPointsStatus(MsgPointsOnlyAccredited, true);
    }

    private void SubmitPoints()
    {
        if (!int.TryParse(pointsEdit.Text.Trim().Replace(",", ""), out int amount) || amount <= 0)
        {
            SetPointsStatus("Enter how many national points to save.", true);
            return;
        }
        requests.Donate(amount);
    }

    internal void OnPointStatus(bool ok, int np, int fund)
    {
        if (!ok) { SetPointsStatus(MsgCommandUnavailable, true); return; }
        pointsMineLbl.Text = $"{np:n0}";
        pointsFundLbl.Text = $"{fund:n0}   ({fund / ClanTypes.NationalPointsPerClanPoint:n0} points)";
    }

    private void OnDonate(KnightsDonateResult result, int np, int fund, int amount)
    {
        if (result == KnightsDonateResult.Succeeded)
        {
            pointsEdit.Clear();
            pointsMineLbl.Text = $"{np:n0}";
            pointsFundLbl.Text = $"{fund:n0}   ({fund / ClanTypes.NationalPointsPerClanPoint:n0} points)";
            SetPointsStatus($"You have saved up {amount:n0} Contribution for the clan", false);
            context.Log.Notice($"You have saved up {amount:n0} Contribution for the clan");
                return;
        }

        SetPointsStatus(result switch
        {
            KnightsDonateResult.ClanNotAccredited => MsgPointsOnlyAccredited,
            KnightsDonateResult.NotEnoughPoints => "Saved Contribution is insufficient.",
            _ => "You failed to save up enough Contribution.",
        }, true);
    }

    private void OnPointMethod(KnightsPointMethodResult result, byte method)
    {
        if (result == KnightsPointMethodResult.Succeeded)
        {
            SetPointsStatus(method == ClanTypes.AutomaticAccumulation
                ? "Changed to automatic accumulation method."
                : "Changed to free accumulation method.", false);
            pointsAuto.ButtonPressed = method == ClanTypes.AutomaticAccumulation;
            pointsFree.ButtonPressed = method != ClanTypes.AutomaticAccumulation;
            return;
        }

        SetPointsStatus(result == KnightsPointMethodResult.ClanNotAccredited
            ? "Accredited Knights or above can select an accumulation method."
            : "Accumulation method not set.", true);
    }

    private void OnPointsLoyalty(LoyaltyChange change)
    {
        if (PointsShown) pointsMineLbl.Text = $"{change.Np:n0}";
    }

    private void SetPointsStatus(string text, bool warn)
    {
        pointsStatus.Text = text;
        pointsStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.Good);
    }
}
