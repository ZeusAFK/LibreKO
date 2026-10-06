using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int SiegeOfficeWidth = 360;
    private const int SiegeTaxRateWidth = 390;
    private const string CastleLordTitle = "Lord of Delos";
    private const string OfficeGreeting = "Lord of Delos!!!";
    private const string OfficeQuestion = "What is it that you want? I shall do as you command.";
    private const string ChangeTaxLabel = "Change tax rate";
    private const string CollectTaxLabel = "Collect tax";
    private const string MaintenanceLabel = "Castle maintenance";
    private const string ExitLabel = "Exit";
    private const string TaxListGreeting = "Lord of Delos!!";
    private const string TaxListQuestion = "Please select the tax rate you want to change.";
    private const string MoradonAreaLabel = "Moradon Area";
    private const string DelosAreaLabel = "Delos Area";
    private const string DungeonFeeLabel = "Dungeon Entrance Fee";

    private HudWindow _siegeOfficePanel = null!, _siegeTaxListPanel = null!, _siegeTaxRatePanel = null!;
    private Label _siegeTaxRatePrompt = null!, _siegeTaxRateValue = null!;
    private SiegeOffice _siegeOffice;
    private SiegeTaxRates _siegeRates;
    private SiegeRateKind _siegeRateKind;
    private int _siegeRateValue, _siegeRateCurrent;
    private bool _siegeOfficeShown, _siegeTaxListShown, _siegeTaxRateShown;

    private void BuildSiegeOffice()
    {
        _siegeOfficePanel = ServiceWindow(_siegeLayer, "siegeoffice", CastleLordTitle, SiegeOfficeWidth, CloseSiegeOffice);
        var body = _siegeOfficePanel.Body;
        body.AddChild(NpcSpeech(SiegeSpeechHeight, out var greeting, out var question));
        SetSpeech(greeting, OfficeGreeting);
        SetSpeech(question, OfficeQuestion);

        var options = new VBoxContainer();
        options.AddThemeConstantOverride("separation", 5);
        body.AddChild(options);
        options.AddChild(NpcOption(ChangeTaxLabel, () => Net.I.SendSiegeTaxList()));
        options.AddChild(NpcOption(CollectTaxLabel, AskSiegeCollect));
        options.AddChild(NpcOption(MaintenanceLabel, CloseSiegeOffice));
        options.AddChild(NpcOption(ExitLabel, CloseSiegeOffice));
    }

    private void OpenSiegeOffice(SiegeOffice office)
    {
        _siegeOffice = office;
        _siegeOfficePanel.Title = NpcWindowTitle(CastleLordTitle);
        _siegeOfficeShown = true;
        _siegeOfficePanel.Visible = true;
    }

    private void CloseSiegeOffice()
    {
        _siegeOfficeShown = false;
        _siegeOfficePanel.Visible = false;
    }

    private void AskSiegeCollect()
    {
        if (_siegeOffice.Collectable == 0)
        {
            SiegeMessage(SiegeWarfare.NothingCollectedText);
            return;
        }
        Notice.Confirm(this, KingFill(SiegeWarfare.CollectableText, "", _siegeOffice.Collectable), YesLabel, NoLabel,
            () => Net.I.SendSiegeCollect(), title: SiegeTitle);
    }

    private void OnSiegeCollected(SiegeCollected collected)
    {
        if (collected.Result != SiegeWarfare.Success)
        {
            SiegeMessage(SiegeWarfare.CollectErrorText(collected.Result));
            return;
        }
        Net.I.RaiseGold(collected.Coins);
        CloseSiegeOffice();
        SiegeMessage(SiegeWarfare.CollectedText, collected.Collected);
    }

    private void BuildSiegeTaxList()
    {
        _siegeTaxListPanel = ServiceWindow(_siegeLayer, "siegetaxlist", CastleLordTitle, SiegeOfficeWidth, CloseSiegeTaxList);
        var body = _siegeTaxListPanel.Body;
        body.AddChild(NpcSpeech(SiegeSpeechHeight, out var greeting, out var question));
        SetSpeech(greeting, TaxListGreeting);
        SetSpeech(question, TaxListQuestion);

        var options = new VBoxContainer();
        options.AddThemeConstantOverride("separation", 5);
        body.AddChild(options);
        options.AddChild(NpcOption(MoradonAreaLabel, () => AskSiegeRate(SiegeRateKind.Moradon)));
        options.AddChild(NpcOption(DelosAreaLabel, () => AskSiegeRate(SiegeRateKind.Delos)));
        options.AddChild(NpcOption(DungeonFeeLabel, () => AskSiegeRate(SiegeRateKind.DungeonFee)));
        options.AddChild(NpcOption(ExitLabel, CloseSiegeTaxList));
    }

    private void OnSiegeTaxRates(SiegeTaxRates rates)
    {
        if (rates.Result != SiegeWarfare.Success)
        {
            SiegeMessage(SiegeWarfare.TaxErrorText(rates.Result));
            return;
        }
        _siegeRates = rates;
        _siegeTaxListPanel.Title = NpcWindowTitle(CastleLordTitle);
        _siegeTaxListShown = true;
        _siegeTaxListPanel.Visible = true;
    }

    private void CloseSiegeTaxList()
    {
        _siegeTaxListShown = false;
        _siegeTaxListPanel.Visible = false;
    }

    private int SiegeRateOf(SiegeRateKind kind) => kind switch
    {
        SiegeRateKind.Moradon => _siegeRates.Moradon,
        SiegeRateKind.Delos => _siegeRates.Delos,
        _ => _siegeRates.DungeonFee,
    };

    private void AskSiegeRate(SiegeRateKind kind)
    {
        Notice.Confirm(this, KingFill(SiegeWarfare.ConfirmText(kind), "", SiegeRateOf(kind)), ChangeTaxLabel,
            KingText(KingElection.OkText, "OK"), () => OpenSiegeTaxRate(kind), title: SiegeTitle);
    }

    private void BuildSiegeTaxRate()
    {
        _siegeTaxRatePanel = ServiceWindow(_siegeLayer, "siegetaxrate", ChangeTaxLabel, SiegeTaxRateWidth, CloseSiegeTaxRate);
        var body = _siegeTaxRatePanel.Body;
        _siegeTaxRatePrompt = SpeechLabel(UiTheme.TextHi);
        _siegeTaxRatePrompt.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(_siegeTaxRatePrompt);
        body.AddChild(TaxArrows(out _siegeTaxRateValue, () => StepSiegeTaxRate(false), () => StepSiegeTaxRate(true)));

        var ok = UiTheme.ActionButton(KingText(KingElection.OkText, "OK"), "");
        ok.Pressed += SubmitSiegeTaxRate;
        var cancel = UiTheme.SmallButton(CancelLabel, "");
        cancel.Pressed += CloseSiegeTaxRate;
        body.AddChild(FooterButtons(ok, cancel));
    }

    private void OpenSiegeTaxRate(SiegeRateKind kind)
    {
        _siegeRateKind = kind;
        _siegeRateCurrent = SiegeRateOf(kind);
        _siegeRateValue = _siegeRateCurrent;
        _siegeTaxRatePrompt.Text = KingText(SiegeWarfare.PromptText(kind));
        RefreshSiegeTaxRate();
        _siegeTaxRateShown = true;
        _siegeTaxRatePanel.Visible = true;
    }

    private void StepSiegeTaxRate(bool up)
    {
        _siegeRateValue = SiegeWarfare.Step(_siegeRateKind, _siegeRateValue, up);
        RefreshSiegeTaxRate();
    }

    private void RefreshSiegeTaxRate() =>
        _siegeTaxRateValue.Text = _siegeRateKind == SiegeRateKind.DungeonFee
            ? $"{NationTreasury.Coins(_siegeRateValue)} {KingText(SiegeWarfare.CoinText, "Coin")}"
            : $"{_siegeRateValue} %";

    private void SubmitSiegeTaxRate()
    {
        if (_siegeRateValue != _siegeRateCurrent) Net.I.SendSiegeRate(_siegeRateKind, _siegeRateValue);
        CloseSiegeTaxRate();
    }

    private void CloseSiegeTaxRate()
    {
        _siegeTaxRateShown = false;
        _siegeTaxRatePanel.Visible = false;
    }

    private void OnSiegeRateChanged(SiegeRateChanged changed)
    {
        if (changed.Result != SiegeWarfare.Success)
        {
            SiegeMessage(SiegeWarfare.TaxErrorText(changed.Result));
            return;
        }
        CombatNotice(KingFill(SiegeWarfare.ChangedText(changed.Sub), "", changed.Value));
        switch (changed.Sub)
        {
            case SiegeWarfare.MoradonRate:
                _siegeRates = _siegeRates with { Moradon = (short)changed.Value };
                break;
            case SiegeWarfare.DelosRate:
                _siegeRates = _siegeRates with { Delos = (short)changed.Value };
                break;
            default:
                _siegeRates = _siegeRates with { DungeonFee = changed.Value };
                break;
        }
        if (changed.Sub != SiegeWarfare.DungeonFee && changed.Zone == _zone) Net.I.SetZoneTariff(changed.Value);
    }
}
