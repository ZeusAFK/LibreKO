using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const int KingNominateWidth = 400;
    private const float KingNominateSpeechHeight = 96f;
    private const int KingPlanWidth = 420;
    private const float KingPlanHeight = 190f;
    private const float NomineeEditWidth = 200f;
    private const int NomineeBoardText = 11342;
    private const string NominateNote = " - Please confirm the character ID. the nomination process cannot be repeated.";
    private const string IdLabel = "ID";
    private const string ConfirmLabel = "Confirm";

    private HudWindow _kingNominatePanel = null!, _kingPlanPanel = null!;
    private Label _kingNominateText = null!, _kingPlanCount = null!;
    private LineEdit _kingNomineeEdit = null!;
    private TextEdit _kingPlanEdit = null!;
    private bool _kingNominateShown, _kingPlanShown, _kingPlanBusy;

    private void BuildKingNominate()
    {
        _kingNominatePanel = ServiceWindow(_kingLayer, "kingnominate", ElectionOfficerTitle, KingNominateWidth, CloseKingNominate);
        var body = _kingNominatePanel.Body;
        body.AddChild(NpcSpeech(KingNominateSpeechHeight, out _kingNominateText, out var note));
        SetSpeech(note, NominateNote);

        var idRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        idRow.AddThemeConstantOverride("separation", 10);
        body.AddChild(idRow);
        var id = UiTheme.Text(IdLabel, 13, UiTheme.Gold);
        id.VerticalAlignment = VerticalAlignment.Center;
        idRow.AddChild(id);
        _kingNomineeEdit = new LineEdit { CustomMinimumSize = new Vector2(NomineeEditWidth, 0) };
        _kingNomineeEdit.TextSubmitted += _ => ConfirmKingNominee();
        idRow.AddChild(_kingNomineeEdit);

        var confirm = UiTheme.ActionButton(ConfirmLabel, "");
        confirm.Pressed += ConfirmKingNominee;
        var cancel = UiTheme.SmallButton(CancelLabel, "");
        cancel.Pressed += CancelKingNominate;
        body.AddChild(FooterButtons(confirm, cancel));
    }

    private void OpenKingNominate(int hour)
    {
        var (textId, from, until) = KingElection.NominateHours(hour);
        _kingNominateText.Text = KingFill(textId, "", from, until);
        _kingNominatePanel.Title = NpcWindowTitle(ElectionOfficerTitle);
        _kingNomineeEdit.Text = "";
        ShowKingNominate();
    }

    private void ShowKingNominate()
    {
        _kingNominateShown = true;
        _kingNominatePanel.Visible = true;
        if (_kingNomineeEdit.IsInsideTree()) _kingNomineeEdit.GrabFocus();
    }

    private void CloseKingNominate()
    {
        _kingNominateShown = false;
        _kingNominatePanel.Visible = false;
    }

    private void CancelKingNominate()
    {
        CloseKingNominate();
        ShowKingElectionPage(ElectionPage.Election);
    }

    private void ConfirmKingNominee()
    {
        string name = _kingNomineeEdit.Text.Trim();
        int refusal = KingElection.NomineeRefusal(name);
        if (refusal != 0)
        {
            KingMessage(KingText(refusal));
            return;
        }
        CloseKingNominate();
        KingConfirm(KingBox.Nominate, KingFill(KingElection.ConfirmNominateText, "", name), name);
    }

    private void BuildKingPlanEditor()
    {
        _kingPlanPanel = ServiceWindow(_kingLayer, "kingplan", KingText(NomineeBoardText, "Nominee Board"), KingPlanWidth,
            CancelKingPlanEditor);
        var body = _kingPlanPanel.Body;

        _kingPlanEdit = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, KingPlanHeight),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        StyleKingTextBox(_kingPlanEdit);
        _kingPlanEdit.TextChanged += RefreshKingPlanCount;
        body.AddChild(_kingPlanEdit);

        _kingPlanCount = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Right);
        body.AddChild(_kingPlanCount);

        var confirm = UiTheme.ActionButton(ConfirmLabel, "");
        confirm.Pressed += PostKingPlan;
        var cancel = UiTheme.SmallButton(CancelLabel, "");
        cancel.Pressed += CancelKingPlanEditor;
        body.AddChild(FooterButtons(confirm, cancel));
        RefreshKingPlanCount();
    }

    private static void StyleKingTextBox(TextEdit edit)
    {
        var box = new StyleBoxFlat { BgColor = new Color(0.04f, 0.03f, 0.02f, 0.9f), BorderColor = new Color(UiTheme.Edge, 0.7f) };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(4);
        box.SetContentMarginAll(6);
        var focus = (StyleBoxFlat)box.Duplicate();
        focus.BorderColor = new Color(UiTheme.Gold, 0.9f);
        edit.AddThemeStyleboxOverride("normal", box);
        edit.AddThemeStyleboxOverride("focus", focus);
        edit.AddThemeStyleboxOverride("read_only", box);
        edit.AddThemeColorOverride("font_color", UiTheme.TextHi);
        edit.AddThemeFontSizeOverride("font_size", 13);
    }

    private void OpenKingPlanEditor()
    {
        _kingPlanBusy = false;
        _kingPlanEdit.Text = "";
        RefreshKingPlanCount();
        _kingPlanShown = true;
        _kingPlanPanel.Visible = true;
        if (_kingPlanEdit.IsInsideTree()) _kingPlanEdit.GrabFocus();
    }

    private void CloseKingPlanEditor()
    {
        _kingPlanShown = false;
        _kingPlanPanel.Visible = false;
    }

    private void CancelKingPlanEditor()
    {
        CloseKingPlanEditor();
        ShowKingElectionPage(ElectionPage.Election, ElectionPage.Board);
    }

    private void RefreshKingPlanCount()
    {
        int length = _kingPlanEdit.Text.Length;
        _kingPlanCount.Text = $"{length} / {KingElection.PlanLimit}";
        _kingPlanCount.AddThemeColorOverride("font_color", length > KingElection.PlanLimit ? UiTheme.Bad : UiTheme.TextLo);
    }

    private void PostKingPlan()
    {
        if (_kingPlanBusy) return;
        string plan = _kingPlanEdit.Text;
        int refusal = KingElection.PlanRefusal(plan);
        if (refusal != 0)
        {
            KingMessage(KingText(refusal));
            return;
        }
        _kingPlanBusy = true;
        Net.I.SendKingPlan(plan);
    }
}
