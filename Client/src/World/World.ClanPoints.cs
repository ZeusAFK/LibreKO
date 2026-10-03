using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int ClanPointsPanelWidth = 340;

    private CanvasLayer _clanPointsLayer = null!;
    private HudWindow _clanPointsPanel = null!;
    private Label _clanPointsMineLbl = null!, _clanPointsFundLbl = null!, _clanPointsStatus = null!;
    private LineEdit _clanPointsEdit = null!;
    private Button _clanPointsSaveBtn = null!;
    private CheckBox _clanPointsAuto = null!, _clanPointsFree = null!;
    private VBoxContainer _clanPointsMethodBox = null!;
    private bool _clanPointsShown;
    private int _clanPointsMine;

    private void ClanPointsInit()
    {
        _clanPointsLayer = new CanvasLayer { Layer = 74 };
        AddChild(_clanPointsLayer);

        _clanPointsPanel = new HudWindow("clanpoint", Localization.Loc.Tr("Clan Contribution"), new Vector2(240, 140), bodyMinWidth: ClanPointsPanelWidth)
        { Visible = false };
        _clanPointsPanel.Closed += CloseClanPoints;
        _clanPointsLayer.AddChild(_clanPointsPanel);
        var root = _clanPointsPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var intro = UiTheme.Text(
            Localization.Loc.Tr("National points you save here become the clan's Contribution. They pay for the Knights' promotions and capes, and you get back a third if you leave."),
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
        _clanPointsMineLbl = ClanPointsRow(col, Localization.Loc.Tr("Your national points"));
        _clanPointsFundLbl = ClanPointsRow(col, Localization.Loc.Tr("Clan Contribution"));

        var amountRow = new HBoxContainer();
        amountRow.AddThemeConstantOverride("separation", 6);
        root.AddChild(amountRow);
        var amountLbl = UiTheme.Text(Localization.Loc.Tr("Save"), 13, UiTheme.TextHi);
        amountLbl.CustomMinimumSize = new Vector2(60, 0);
        amountRow.AddChild(amountLbl);
        _clanPointsEdit = new LineEdit
        {
            PlaceholderText = Localization.Loc.Tr("national points"),
            Alignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _clanPointsEdit.TextSubmitted += _ => SubmitClanPoints();
        amountRow.AddChild(_clanPointsEdit);
        _clanPointsSaveBtn = new Button { Text = Localization.Loc.Tr("Save"), FocusMode = Control.FocusModeEnum.None };
        _clanPointsSaveBtn.Pressed += SubmitClanPoints;
        amountRow.AddChild(_clanPointsSaveBtn);

        _clanPointsMethodBox = new VBoxContainer();
        _clanPointsMethodBox.AddThemeConstantOverride("separation", 2);
        root.AddChild(_clanPointsMethodBox);
        _clanPointsMethodBox.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("Accumulation")));
        var group = new ButtonGroup();
        _clanPointsAuto = new CheckBox { Text = Localization.Loc.Tr("Automatic: a share of every member's earned points goes to the clan"), ButtonGroup = group, FocusMode = Control.FocusModeEnum.None };
        _clanPointsFree = new CheckBox { Text = Localization.Loc.Tr("Free: members save points here when they choose"), ButtonGroup = group, FocusMode = Control.FocusModeEnum.None };
        _clanPointsMethodBox.AddChild(_clanPointsAuto);
        _clanPointsMethodBox.AddChild(_clanPointsFree);
        var confirmRow = new HBoxContainer();
        _clanPointsMethodBox.AddChild(confirmRow);
        confirmRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var confirm = new Button { Text = Localization.Loc.Tr("Confirm"), FocusMode = Control.FocusModeEnum.None };
        confirm.Pressed += () => Net.I.SendClanPointMethod(
            _clanPointsAuto.ButtonPressed ? ClanTypes.AutomaticAccumulation : ClanTypes.FreeAccumulation);
        confirmRow.AddChild(confirm);

        _clanPointsStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        _clanPointsStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _clanPointsStatus.CustomMinimumSize = new Vector2(1, 0);
        root.AddChild(_clanPointsStatus);

        Net.I.ClanPointStatusEvent += OnClanPointStatus;
        Net.I.ClanDonateEvent += OnClanDonate;
        Net.I.ClanPointMethodEvent += OnClanPointMethod;
        Net.I.LoyaltyChangeEvent += OnClanPointsLoyalty;
    }

    private void ClanPointsDispose()
    {
        Net.I.ClanPointStatusEvent -= OnClanPointStatus;
        Net.I.ClanDonateEvent -= OnClanDonate;
        Net.I.ClanPointMethodEvent -= OnClanPointMethod;
        Net.I.LoyaltyChangeEvent -= OnClanPointsLoyalty;
    }

    private static Label ClanPointsRow(VBoxContainer col, string label)
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

    private void ToggleClanPoints()
    {
        if (_clanPointsShown) { CloseClanPoints(); return; }
        if (!MyClan.InClan) return;
        _clanPointsShown = true;
        _clanPointsPanel.Visible = true;
        _clanPointsEdit.Clear();
        SetClanPointsStatus("", false);
        ApplyClanPointsState();
        Net.I.SendClanPointStatus();
    }

    private void CloseClanPoints()
    {
        if (!_clanPointsShown) return;
        _clanPointsShown = false;
        _clanPointsPanel.Visible = false;
    }

    private void ApplyClanPointsState()
    {
        var clan = MyClan;
        bool accredited = ClanTypes.AcceptsDonations(clan.Flag);
        _clanPointsMineLbl.Text = $"{Sheet.Np:n0}";
        _clanPointsFundLbl.Text = $"{clan.PointFund:n0}   ({clan.PointFund / ClanTypes.NationalPointsPerClanPoint:n0} {Localization.Loc.Tr("points")})";
        _clanPointsSaveBtn.Disabled = !accredited;
        _clanPointsEdit.Editable = accredited;
        _clanPointsMethodBox.Visible = clan.IsChief && accredited;
        _clanPointsAuto.ButtonPressed = clan.PointMethod == ClanTypes.AutomaticAccumulation;
        _clanPointsFree.ButtonPressed = clan.PointMethod != ClanTypes.AutomaticAccumulation;
        if (!accredited) SetClanPointsStatus(ClanMsgPointsOnlyAccredited, true);
    }

    private void SubmitClanPoints()
    {
        if (!int.TryParse(_clanPointsEdit.Text.Trim().Replace(",", ""), out int amount) || amount <= 0)
        {
            SetClanPointsStatus(Localization.Loc.Tr("Enter how many national points to save."), true);
            return;
        }
        Net.I.SendClanDonate(amount);
    }

    private void OnClanPointStatus(bool ok, int np, int fund)
    {
        if (!ok) { SetClanPointsStatus(ClanMsgCommandUnavailable, true); return; }
        _clanPointsMine = np;
        _clanPointsMineLbl.Text = $"{np:n0}";
        _clanPointsFundLbl.Text = $"{fund:n0}   ({fund / ClanTypes.NationalPointsPerClanPoint:n0} {Localization.Loc.Tr("points")})";
        ApplyMyClan();
    }

    private void OnClanDonate(int result, int np, int fund, int amount)
    {
        if (result == Net.KnResultOk)
        {
            _clanPointsEdit.Clear();
            _clanPointsMineLbl.Text = $"{np:n0}";
            _clanPointsFundLbl.Text = $"{fund:n0}   ({fund / ClanTypes.NationalPointsPerClanPoint:n0} {Localization.Loc.Tr("points")})";
            SetClanPointsStatus($"{Localization.Loc.Tr("You have saved up")} {amount:n0} {Localization.Loc.Tr("Contribution for the clan")}", false);
            Chat.Info($"{Localization.Loc.Tr("You have saved up")} {amount:n0} {Localization.Loc.Tr("Contribution for the clan")}");
            ApplyMyClan();
            if (ClanPageVisible && _clanTab == ClanTab.Points) Net.I.SendClanDonationList();
            return;
        }

        SetClanPointsStatus(result switch
        {
            Net.KnDonateNotAccredited => ClanMsgPointsOnlyAccredited,
            Net.KnDonateNotEnough => Localization.Loc.Tr("Saved Contribution is insufficient."),
            _ => Localization.Loc.Tr("You failed to save up enough Contribution."),
        }, true);
    }

    private void OnClanPointMethod(int result, byte method)
    {
        if (result == Net.KnResultOk)
        {
            SetClanPointsStatus(method == ClanTypes.AutomaticAccumulation
                ? Localization.Loc.Tr("Changed to automatic accumulation method.")
                : Localization.Loc.Tr("Changed to free accumulation method."), false);
            _clanPointsAuto.ButtonPressed = method == ClanTypes.AutomaticAccumulation;
            _clanPointsFree.ButtonPressed = method != ClanTypes.AutomaticAccumulation;
            return;
        }

        SetClanPointsStatus(result == Net.KnMethodNotAccredited
            ? Localization.Loc.Tr("Accredited Knights or above can select an accumulation method.")
            : Localization.Loc.Tr("Accumulation method not set."), true);
    }

    private void OnClanPointsLoyalty(int np, int monthly)
    {
        if (_clanPointsShown) _clanPointsMineLbl.Text = $"{np:n0}";
    }

    private void SetClanPointsStatus(string text, bool warn)
    {
        _clanPointsStatus.Text = text;
        _clanPointsStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.Good);
    }
}
