using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int SiegeListWidth = 380;
    private const float SiegeScheduleHeight = 190f;
    private const float SiegeChallengersHeight = 150f;
    private const float SiegeDefendersHeight = 230f;
    private const float SiegeColumnWidth = 64f;
    private const float SiegeDayColumnWidth = 90f;
    private const float SiegeNationColumnWidth = 84f;
    private const string WarLabel = "War";
    private const string DayLabel = "Day";
    private const string TimeLabel = "Time";
    private const string ChallengersTitle = "List of Challengers";
    private const string KnightsLabel = "Knights";
    private const string NationLabel = "Nation";
    private const string MembersLabel = "Members";
    private const string ApplicationLabel = "Application";
    private const string ViewDefendersLabel = "View Defending Confederacy";
    private const string DefendersTitle = "Defending Confederacy";
    private const string ConfederacyLabel = "Confederacy";
    private const string ChallengeListLabel = "Challenge List";
    private const string ReadAgainLabel = "Read again";

    private HudWindow _siegeSchedulePanel = null!, _siegeChallengersPanel = null!, _siegeDefendersPanel = null!;
    private VBoxContainer _siegeScheduleRows = null!, _siegeChallengerRows = null!, _siegeDefenderRows = null!;
    private Label _siegeSignedUp = null!, _siegeChosen = null!, _siegeFee = null!, _siegePeriod = null!;
    private Button _siegeApply = null!, _siegeCancel = null!;
    private uint _siegeChallengeFee;
    private bool _siegeScheduleShown, _siegeChallengersShown, _siegeDefendersShown;

    private void BuildSiegeSchedule()
    {
        _siegeSchedulePanel = ServiceWindow(_siegeLayer, "siegeschedule", WarLabel, SiegeListWidth, CloseSiegeSchedule);
        var body = _siegeSchedulePanel.Body;
        _siegeScheduleRows = SiegeTable(body, SiegeScheduleHeight,
            [(WarLabel, SiegeColumnWidth), (DayLabel, SiegeDayColumnWidth), (TimeLabel, SiegeColumnWidth)]);

        var ok = UiTheme.ActionButton(KingText(KingElection.OkText, "OK"), "");
        ok.Pressed += CloseSiegeSchedule;
        var again = UiTheme.SmallButton(ReadAgainLabel, "");
        again.Pressed += () => Net.I.SendSiegeSchedule();
        body.AddChild(FooterButtons(ok, again));
    }

    private void OnSiegeSchedule(SiegeSchedule schedule)
    {
        if (schedule.Result != SiegeWarfare.Success)
        {
            SiegeMessage(SiegeWarfare.ListErrorText(schedule.Result, coins: false));
            return;
        }
        ClearSiegeRows(_siegeScheduleRows);
        foreach (var row in schedule.Rows)
            _siegeScheduleRows.AddChild(SiegeRow(
                SiegeCell(KingText(SiegeWarfare.WarText(row.WarType)), SiegeColumnWidth, UiTheme.TextHi, expand: true),
                SiegeCell(KingText(SiegeWarfare.DayText(row.Weekday)), SiegeDayColumnWidth, UiTheme.TextLo),
                SiegeCell(SiegeWarfare.Clock(row.Hour, row.Minute), SiegeColumnWidth, UiTheme.Gold)));
        _siegeScheduleShown = true;
        _siegeSchedulePanel.Visible = true;
    }

    private void CloseSiegeSchedule()
    {
        _siegeScheduleShown = false;
        _siegeSchedulePanel.Visible = false;
    }

    private static (string, float)[] SiegeClanColumns(string first) =>
        [(first, SiegeColumnWidth), (NationLabel, SiegeNationColumnWidth), (MembersLabel, SiegeColumnWidth)];

    private static void FillSiegeClans(VBoxContainer rows, IEnumerable<SiegeClanRow> clans)
    {
        ClearSiegeRows(rows);
        foreach (var clan in clans)
            rows.AddChild(SiegeRow(
                SiegeCell(clan.Name, SiegeColumnWidth, UiTheme.TextHi, expand: true),
                SiegeCell(KingText(SiegeWarfare.NationText(clan.Nation)), SiegeNationColumnWidth, UiTheme.TextLo),
                SiegeCell(clan.Members.ToString(), SiegeColumnWidth, UiTheme.Gold)));
    }

    private void BuildSiegeChallengers()
    {
        _siegeChallengersPanel = ServiceWindow(_siegeLayer, "siegechallengers", ChallengersTitle, SiegeListWidth, CloseSiegeChallengers);
        var body = _siegeChallengersPanel.Body;
        _siegeChallengerRows = SiegeTable(body, SiegeChallengersHeight, SiegeClanColumns(KnightsLabel));

        var info = UiTheme.Section();
        body.AddChild(info);
        var lines = new VBoxContainer();
        lines.AddThemeConstantOverride("separation", 3);
        info.AddChild(lines);
        _siegeSignedUp = SpeechLabel(UiTheme.TextLo);
        _siegeChosen = SpeechLabel(UiTheme.TextLo);
        _siegeFee = SpeechLabel(UiTheme.Gold);
        _siegePeriod = SpeechLabel(UiTheme.TextHi);
        foreach (var line in new[] { _siegeSignedUp, _siegeChosen, _siegeFee, _siegePeriod }) lines.AddChild(line);

        _siegeApply = UiTheme.ActionButton(ApplicationLabel, "");
        _siegeApply.Pressed += () => Net.I.SendSiegeApply(join: true);
        _siegeCancel = UiTheme.SmallButton(CancelLabel, "");
        _siegeCancel.Pressed += AskSiegeCancel;
        var defenders = UiTheme.SmallButton(ViewDefendersLabel, "");
        defenders.Pressed += () => Net.I.SendSiegeDefenders();
        var footer = FooterButtons(_siegeApply, _siegeCancel, defenders);
        defenders.CustomMinimumSize = new Vector2(0, 30);
        body.AddChild(footer);
    }

    private void OnSiegeApply(short result)
    {
        if (result != SiegeWarfare.Success) SiegeMessage(SiegeWarfare.ListErrorText(result, coins: true));
    }

    private void OnSiegeChallengers(SiegeChallengers list)
    {
        if (list.Result != SiegeWarfare.Success)
        {
            SiegeMessage(SiegeWarfare.ListErrorText(list.Result, coins: true));
            return;
        }
        FillSiegeClans(_siegeChallengerRows, list.Clans);
        _siegeChallengeFee = list.Fee;
        _siegeSignedUp.Text = KingFill(SiegeWarfare.SignedUpText, "", list.SignedUp);
        _siegeChosen.Text = KingFill(SiegeWarfare.ChosenText, "", list.Chosen);
        _siegeFee.Text = KingFill(SiegeWarfare.FeeText, "", list.Fee);
        _siegePeriod.Text = SiegeWarfare.RegistrationPeriod(KingText(SiegeWarfare.PeriodText),
            KingText(SiegeWarfare.DayText(list.StartDay)), list.StartHour, list.StartMinute, KingText(SiegeWarfare.DayText(list.EndDay)));

        string mine = Net.I.MyClan.InClan ? Net.I.MyClan.Name : "";
        bool applied = list.Clans.Any(c => SiegeWarfare.IsOwnClan(c.Name, mine));
        _siegeApply.Visible = !applied;
        _siegeCancel.Visible = applied;
        _siegeChallengersShown = true;
        _siegeChallengersPanel.Visible = true;
    }

    private void AskSiegeCancel()
    {
        Notice.Confirm(this, KingFill(SiegeWarfare.CancelChallengeText, "", _siegeChallengeFee), YesLabel, NoLabel,
            () => Net.I.SendSiegeApply(join: false), title: SiegeTitle);
    }

    private void CloseSiegeChallengers()
    {
        _siegeChallengersShown = false;
        _siegeChallengersPanel.Visible = false;
    }

    private void BuildSiegeDefenders()
    {
        _siegeDefendersPanel = ServiceWindow(_siegeLayer, "siegedefenders", DefendersTitle, SiegeListWidth, CloseSiegeDefenders);
        var body = _siegeDefendersPanel.Body;
        _siegeDefenderRows = SiegeTable(body, SiegeDefendersHeight, SiegeClanColumns(ConfederacyLabel));

        var challengers = UiTheme.SmallButton(ChallengeListLabel, "");
        challengers.Pressed += () => Net.I.SendSiegeChallengers();
        var again = UiTheme.SmallButton(ReadAgainLabel, "");
        again.Pressed += () => Net.I.SendSiegeDefenders();
        body.AddChild(FooterButtons(challengers, again));
    }

    private void OnSiegeDefenders(SiegeDefenders list)
    {
        if (list.Result != SiegeWarfare.Success)
        {
            SiegeMessage(SiegeWarfare.ListErrorText(list.Result, coins: false));
            return;
        }
        FillSiegeClans(_siegeDefenderRows, list.Clans);
        _siegeDefendersShown = true;
        _siegeDefendersPanel.Visible = true;
    }

    private void CloseSiegeDefenders()
    {
        _siegeDefendersShown = false;
        _siegeDefendersPanel.Visible = false;
    }
}
