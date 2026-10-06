using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int NationTaxWidth = 400;
    private const float NationTaxSpeechHeight = 0f;
    private const int NationTaxRateWidth = 390;
    private const int NationIntroWidth = 340;
    private const float NationIntroHeight = 210f;
    private const float TaxArrowWidth = 34f;
    private const float TaxArrowHeight = 28f;
    private const float TaxValueWidth = 120f;
    private const string GrandChamberlainTitle = "Grand Chamberlain";
    private const string CheckTreasuryLabel = "Check National Treasury";
    private const string ChangeTaxRateLabel = "Change Tax Rate";
    private const string KingsFundsLabel = "King's Funds";
    private const string KingItemLabel = "King Item";
    private const string NationIntroLabel = "Nation Introduction";
    private const string TaxRatePrompt = "You can change the tax rate by clicking on the arrow below";
    private const string RevisionLabel = "Revision";

    private HudWindow _nationTaxPanel = null!, _nationTaxRatePanel = null!, _nationIntroPanel = null!;
    private Label _nationTaxNotice = null!, _nationTaxNotice2 = null!, _nationTaxRateValue = null!;
    private VBoxContainer _nationTaxOptions = null!;
    private TextEdit _nationIntroEdit = null!;
    private KingTreasury _nationTreasury;
    private int _nationTaxRate;
    private bool _nationTaxShown, _nationTaxRateShown, _nationIntroShown, _nationTaxBusy, _nationTaxRateBusy;

    private void BuildNationTax()
    {
        _nationTaxPanel = ServiceWindow(_kingLayer, "nationtax", GrandChamberlainTitle, NationTaxWidth, CloseNationTax);
        var body = _nationTaxPanel.Body;
        body.AddChild(NpcSpeech(NationTaxSpeechHeight, out _nationTaxNotice, out _nationTaxNotice2));
        _nationTaxNotice.AddThemeColorOverride("font_color", UiTheme.GoldBright);

        _nationTaxOptions = new VBoxContainer();
        _nationTaxOptions.AddThemeConstantOverride("separation", 5);
        body.AddChild(_nationTaxOptions);
        _nationTaxOptions.AddChild(NpcOption(CheckTreasuryLabel, CheckNationTreasury));
        _nationTaxOptions.AddChild(NpcOption(ChangeTaxRateLabel, AskNationTaxRate));
        _nationTaxOptions.AddChild(NpcOption(KingsFundsLabel, AskKingsFund));
        _nationTaxOptions.AddChild(NpcOption(KingItemLabel, AskKingItem));
        _nationTaxOptions.AddChild(NpcOption(NationIntroLabel, AskNationIntro));
    }

    private void OnKingTreasury(KingTreasury treasury)
    {
        _nationTaxBusy = false;
        if (treasury.View is not (NationTreasury.KingView or NationTreasury.CitizenView))
        {
            KingResultMessage(KingReply.Treasury, treasury.View, NationTreasuryTitle);
            return;
        }
        _nationTreasury = treasury;
        bool king = treasury.View == NationTreasury.KingView;
        SetSpeech(_nationTaxNotice, KingText(NationTreasury.Heading(treasury.View, Net.I.LastEnter.Nation)));
        SetSpeech(_nationTaxNotice2, king ? ""
            : KingFill(NationTreasury.CitizenTreasuryText, "", NationTreasury.Coins(treasury.Treasury)));
        _nationTaxOptions.Visible = king;
        _nationTaxPanel.Title = NpcWindowTitle(GrandChamberlainTitle);
        ShowNationTax();
    }

    private void ShowNationTax()
    {
        _nationTaxShown = true;
        _nationTaxPanel.Visible = true;
    }

    private void CloseNationTax()
    {
        _nationTaxBusy = false;
        _nationTaxShown = false;
        _nationTaxPanel.Visible = false;
    }

    private bool NationTaxIdle => !_nationTaxBusy && !_kingBoxOpen;

    private void CheckNationTreasury()
    {
        if (!NationTaxIdle) return;
        CloseNationTax();
        KingMessage(KingFill(NationTreasury.CheckTreasuryText, "", NationTreasury.Coins(_nationTreasury.Treasury)), NationTreasuryTitle);
    }

    private void AskNationTaxRate()
    {
        if (!NationTaxIdle) return;
        _nationTaxBusy = true;
        Net.I.SendKingTariffRead();
    }

    private void AskKingsFund()
    {
        if (!NationTaxIdle) return;
        CloseNationTax();
        KingConfirm(KingBox.Fund, KingFill(NationTreasury.ConfirmFundText, "", NationTreasury.Coins(_nationTreasury.Tribute)));
    }

    private void AskKingItem()
    {
        if (!NationTaxIdle) return;
        var bag = Enumerable.Range(GridStart, GridCount).Where(abs => abs < Inv.Length).Select(abs => Inv[abs].ItemId);
        if (NationTreasury.HasSceptre(bag))
        {
            CloseNationTax();
            KingMessage(KingText(KingElection.ItemOwnedText), NationTreasuryTitle);
            return;
        }
        _nationTaxBusy = true;
        Net.I.SendKingItem();
    }

    private void AskNationIntro()
    {
        if (!NationTaxIdle) return;
        Net.I.SendKingIntroRead();
    }

    private void OnKingFund(KingCoins coins)
    {
        _nationTaxBusy = false;
        if (coins.Result != KingElection.Success)
        {
            KingResultMessage(KingReply.Fund, coins.Result, NationTreasuryTitle);
            return;
        }
        Net.I.RaiseGold((int)coins.Coins);
        KingMessage(KingFill(NationTreasury.FundCollectedText, "", NationTreasury.Coins(coins.Amount)), NationTreasuryTitle);
    }

    private void OnKingReserve(KingCoins coins)
    {
        _nationTaxBusy = false;
        if (coins.Result == KingElection.Success) Net.I.RaiseGold((int)coins.Coins);
        else KingResultMessage(KingReply.Reserve, coins.Result, NationTreasuryTitle);
    }

    private void BuildNationTaxRate()
    {
        _nationTaxRatePanel = ServiceWindow(_kingLayer, "nationtaxrate", ChangeTaxRateLabel, NationTaxRateWidth, CloseNationTaxRate);
        var body = _nationTaxRatePanel.Body;
        var prompt = SpeechLabel(UiTheme.TextHi);
        prompt.HorizontalAlignment = HorizontalAlignment.Center;
        prompt.Text = TaxRatePrompt;
        body.AddChild(prompt);
        body.AddChild(TaxArrows(out _nationTaxRateValue, () => StepNationTaxRate(-1), () => StepNationTaxRate(1)));

        var ok = UiTheme.ActionButton(KingText(KingElection.OkText, "OK"), "");
        ok.Pressed += SubmitNationTaxRate;
        var cancel = UiTheme.SmallButton(CancelLabel, "");
        cancel.Pressed += CloseNationTaxRate;
        body.AddChild(FooterButtons(ok, cancel));
    }

    private static HBoxContainer TaxArrows(out Label value, System.Action down, System.Action up)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        var less = UiTheme.SmallButton("<", "");
        less.CustomMinimumSize = new Vector2(TaxArrowWidth, TaxArrowHeight);
        less.Pressed += down;
        row.AddChild(less);
        var frame = UiTheme.Section();
        frame.CustomMinimumSize = new Vector2(TaxValueWidth, 0);
        row.AddChild(frame);
        value = UiTheme.Text("", 15, UiTheme.GoldBright, HorizontalAlignment.Center);
        frame.AddChild(value);
        var more = UiTheme.SmallButton(">", "");
        more.CustomMinimumSize = new Vector2(TaxArrowWidth, TaxArrowHeight);
        more.Pressed += up;
        row.AddChild(more);
        return row;
    }

    private void OnKingTariffRead(KingTariff tariff)
    {
        _nationTaxBusy = false;
        if (tariff.Result != KingElection.Success)
        {
            KingResultMessage(KingReply.TariffRead, tariff.Result, NationTreasuryTitle);
            return;
        }
        _nationTaxRate = tariff.Tariff;
        _nationTaxRateBusy = false;
        RefreshNationTaxRate();
        _nationTaxRateShown = true;
        _nationTaxRatePanel.Visible = true;
    }

    private void StepNationTaxRate(int delta)
    {
        _nationTaxRate = NationTreasury.StepTaxRate(_nationTaxRate, delta);
        RefreshNationTaxRate();
    }

    private void RefreshNationTaxRate() => _nationTaxRateValue.Text = $"{_nationTaxRate} %";

    private void SubmitNationTaxRate()
    {
        if (_nationTaxRateBusy) return;
        _nationTaxRateBusy = true;
        Net.I.SendKingTariff(_nationTaxRate);
    }

    private void CloseNationTaxRate()
    {
        _nationTaxRateBusy = false;
        _nationTaxRateShown = false;
        _nationTaxRatePanel.Visible = false;
    }

    private void OnKingTariffSet(KingTariff tariff)
    {
        _nationTaxRateBusy = false;
        if (tariff.Result != KingElection.Success)
        {
            KingResultMessage(KingReply.TariffSet, tariff.Result, NationTreasuryTitle);
            return;
        }
        CloseNationTaxRate();
        KingMessage(KingFill(NationTreasury.TaxRateSetText, "", tariff.Tariff), NationTreasuryTitle);
    }

    private void BuildNationIntro()
    {
        _nationIntroPanel = ServiceWindow(_kingLayer, "nationintro", NationIntroLabel, NationIntroWidth, CloseNationIntro);
        var body = _nationIntroPanel.Body;
        _nationIntroEdit = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, NationIntroHeight),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        StyleKingTextBox(_nationIntroEdit);
        body.AddChild(_nationIntroEdit);

        var revision = UiTheme.SmallButton(RevisionLabel, "");
        revision.Pressed += () => SaveNationIntro(close: false);
        var confirm = UiTheme.ActionButton(ConfirmLabel, "");
        confirm.Pressed += () => SaveNationIntro(close: true);
        body.AddChild(FooterButtons(revision, confirm));
    }

    private void OpenNationIntro(string text)
    {
        _nationIntroEdit.Text = NationTreasury.IntroOrWelcome(text, KingText(NationTreasury.IntroWelcomeText));
        _nationIntroShown = true;
        _nationIntroPanel.Visible = true;
    }

    private void CloseNationIntro()
    {
        _nationIntroShown = false;
        _nationIntroPanel.Visible = false;
    }

    private void SaveNationIntro(bool close)
    {
        string text = _nationIntroEdit.Text;
        int refusal = NationTreasury.IntroRefusal(text);
        if (refusal != 0)
        {
            CombatNotice(KingText(refusal));
            return;
        }
        Net.I.SendKingIntro(text);
        if (close) CloseNationIntro();
    }

    private void OnNationIntroSaved(bool saved) =>
        CombatNotice(KingText(saved ? NationTreasury.IntroSavedText : NationTreasury.IntroFailedText));
}
