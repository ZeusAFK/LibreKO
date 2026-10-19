using Godot;

namespace LibreKO.Features.Windows;

internal sealed class KingTreasuryFeature : IFeature, IEscapable
{
    private const string NationTreasuryTitle = "National Treasury";
    private const int NationTaxWidth = 400;
    private const float NationTaxSpeechHeight = 0f;
    private const int NationTaxRateWidth = 390;
    private const int NationIntroWidth = 340;
    private const float NationIntroHeight = 210f;

    private const string GrandChamberlainTitle = "Grand Chamberlain";
    private const string CheckTreasuryLabel = "Check National Treasury";
    private const string ChangeTaxRateLabel = "Change Tax Rate";
    private const string KingsFundsLabel = "King's Funds";
    private const string ItemLabel = "King Item";
    private const string NationIntroLabel = "Nation Introduction";
    private const string TaxRatePrompt = "You can change the tax rate by clicking on the arrow below";
    private const string RevisionLabel = "Revision";

    private readonly IWorldContext context;
    private readonly KingSession king;
    private readonly NpcQuestSession npcQuest;
    private readonly SelfSession self;
    private readonly IKingRequests requests;
    private readonly IInteractionDialogs interactionDialogs;

    private CanvasLayer layer = null!;
    private bool boxOpen;
    private IClosableWindow[]? panels;

    internal HudWindow NationTaxPanel { get; private set; } = null!;
    internal HudWindow NationTaxRatePanel { get; private set; } = null!;
    internal HudWindow NationIntroPanel { get; private set; } = null!;
    private Label nationTaxNotice = null!;
    private Label nationTaxNotice2 = null!;
    private Label nationTaxRateValue = null!;
    private VBoxContainer nationTaxOptions = null!;
    private TextEdit nationIntroEdit = null!;
    private KingTreasury nationTreasury;
    private int nationTaxRate;
    private bool nationTaxShown;
    private bool nationTaxRateShown;
    private bool nationIntroShown;
    private bool nationTaxBusy;
    private bool nationTaxRateBusy;

    public KingTreasuryFeature(IWorldContext context, KingSession king, NpcQuestSession npcQuest, SelfSession self, IKingRequests requests, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.king = king;
        this.npcQuest = npcQuest;
        this.self = self;
        this.requests = requests;
        this.interactionDialogs = interactionDialogs;
    }

    public void Build()
    {
        BuildWindows();
        foreach (var panel in Panels) interactionDialogs.InteractionCloses(panel);

        king.KingResultEvent += OnResult;
        king.KingTreasuryEvent += OnTreasury;
        king.KingFundEvent += OnFund;
        king.KingTariffReadEvent += OnTariffRead;
        king.KingTariffSetEvent += OnTariffSet;
        king.KingReserveEvent += OnReserve;
        king.KingIntroEvent += OpenNationIntro;
        king.KingIntroSavedEvent += OnNationIntroSaved;
        king.KingTreasuryNoticeEvent += OnTreasuryNotice;
    }

    private IReadOnlyList<IClosableWindow> Panels => panels ??=
    [
        new ClosablePanel(() => nationTaxRateShown, CloseNationTaxRate),
        new ClosablePanel(() => nationIntroShown, CloseNationIntro),
        new ClosablePanel(() => nationTaxShown, CloseNationTax),
    ];

    bool IEscapable.CloseOnEscape() => EscapeOrder.CloseFirst(Panels);

    public void Dispose()
    {
        king.KingResultEvent -= OnResult;
        king.KingTreasuryEvent -= OnTreasury;
        king.KingFundEvent -= OnFund;
        king.KingTariffReadEvent -= OnTariffRead;
        king.KingTariffSetEvent -= OnTariffSet;
        king.KingReserveEvent -= OnReserve;
        king.KingIntroEvent -= OpenNationIntro;
        king.KingIntroSavedEvent -= OnNationIntroSaved;
        king.KingTreasuryNoticeEvent -= OnTreasuryNotice;
    }

    internal void BuildWindows()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);
        BuildNationTax();
        BuildNationIntro();
        BuildNationTaxRate();
    }

    private void Message(string text) => KingMessages.Show(context.Root, text, NationTreasuryTitle);

    private void ResultMessage(KingReply reply, short result) => KingMessages.Result(context.Root, reply, result, NationTreasuryTitle);

    private void OnResult(KingReply reply, short result)
    {
        switch (reply)
        {
            case KingReply.KingItem:
                nationTaxBusy = false;
                CloseNationTax();
                ResultMessage(reply, result);
                break;
            case KingReply.ChannelOnly:
                nationTaxBusy = false;
                ResultMessage(reply, result);
                break;
        }
    }

    private void OnTreasuryNotice(KingTreasuryNotice notice)
    {
        if (!notice.Shown) return;
        context.Log.Notice(NpcServiceKit.Fill(NationTreasury.TreasuryUsedText, "", notice.Used));
        context.Log.Notice(NpcServiceKit.Fill(NationTreasury.TreasuryLeftText, "", notice.Left));
    }

    private void BuildNationTax()
    {
        NationTaxPanel = NpcServiceKit.ServiceWindow(layer, "nationtax", GrandChamberlainTitle, NationTaxWidth, CloseNationTax);
        var body = NationTaxPanel.Body;
        body.AddChild(NpcServiceKit.NpcSpeech(NationTaxSpeechHeight, out nationTaxNotice, out nationTaxNotice2));
        nationTaxNotice.AddThemeColorOverride("font_color", UiTheme.GoldBright);

        nationTaxOptions = new VBoxContainer();
        nationTaxOptions.AddThemeConstantOverride("separation", 5);
        body.AddChild(nationTaxOptions);
        nationTaxOptions.AddChild(NpcServiceKit.NpcOption(CheckTreasuryLabel, CheckNationTreasury));
        nationTaxOptions.AddChild(NpcServiceKit.NpcOption(ChangeTaxRateLabel, AskNationTaxRate));
        nationTaxOptions.AddChild(NpcServiceKit.NpcOption(KingsFundsLabel, AskKingsFund));
        nationTaxOptions.AddChild(NpcServiceKit.NpcOption(ItemLabel, AskItem));
        nationTaxOptions.AddChild(NpcServiceKit.NpcOption(NationIntroLabel, AskNationIntro));
    }

    internal void OnTreasury(KingTreasury treasury)
    {
        nationTaxBusy = false;
        if (treasury.View is not (NationTreasury.KingView or NationTreasury.CitizenView))
        {
            ResultMessage(KingReply.Treasury, treasury.View);
            return;
        }
        nationTreasury = treasury;
        bool isKing = treasury.View == NationTreasury.KingView;
        NpcServiceKit.SetSpeech(nationTaxNotice, NpcServiceKit.Text(NationTreasury.Heading(treasury.View, self.LastEnter.Nation)));
        NpcServiceKit.SetSpeech(nationTaxNotice2, isKing ? ""
            : NpcServiceKit.Fill(NationTreasury.CitizenTreasuryText, "", NationTreasury.Coins(treasury.Treasury)));
        nationTaxOptions.Visible = isKing;
        NationTaxPanel.Title = NpcServiceKit.WindowTitle(npcQuest, GrandChamberlainTitle);
        ShowNationTax();
    }

    private void ShowNationTax()
    {
        nationTaxShown = true;
        NationTaxPanel.Visible = true;
    }

    internal void CloseNationTax()
    {
        nationTaxBusy = false;
        nationTaxShown = false;
        NationTaxPanel.Visible = false;
    }

    private bool NationTaxIdle => !nationTaxBusy && !boxOpen;

    private void CheckNationTreasury()
    {
        if (!NationTaxIdle) return;
        CloseNationTax();
        Message(NpcServiceKit.Fill(NationTreasury.CheckTreasuryText, "", NationTreasury.Coins(nationTreasury.Treasury)));
    }

    private void AskNationTaxRate()
    {
        if (!NationTaxIdle) return;
        nationTaxBusy = true;
        requests.TariffRead();
    }

    private void AskKingsFund()
    {
        if (!NationTaxIdle) return;
        CloseNationTax();
        ConfirmFund(NpcServiceKit.Fill(NationTreasury.ConfirmFundText, "", NationTreasury.Coins(nationTreasury.Tribute)));
    }

    private void AskItem()
    {
        if (!NationTaxIdle) return;
        var bag = Enumerable.Range(Inventory.GridStart, Inventory.GridCount).Where(abs => abs < self.Inventory.Length).Select(abs => self.Inventory[abs].ItemId);
        if (NationTreasury.HasSceptre(bag))
        {
            CloseNationTax();
            Message(NpcServiceKit.Text(KingElection.ItemOwnedText));
            return;
        }
        nationTaxBusy = true;
        requests.Item();
    }

    private void AskNationIntro()
    {
        if (!NationTaxIdle) return;
        requests.IntroRead();
    }

    private void ConfirmFund(string text)
    {
        boxOpen = true;
        Notice.Confirm(context.Root, text, NpcServiceKit.Text(KingElection.OkText, "OK"), NpcServiceKit.CancelLabel,
            () =>
            {
                boxOpen = false;
                nationTaxBusy = true;
                requests.Fund();
            },
            () =>
            {
                boxOpen = false;
                ShowNationTax();
            },
            NationTreasuryTitle);
    }

    private void OnFund(KingCoins coins)
    {
        nationTaxBusy = false;
        if (coins.Result != KingWire.Success)
        {
            ResultMessage(KingReply.Fund, coins.Result);
            return;
        }
        Message(NpcServiceKit.Fill(NationTreasury.FundCollectedText, "", NationTreasury.Coins(coins.Amount)));
    }

    private void OnReserve(KingCoins coins)
    {
        nationTaxBusy = false;
        if (coins.Result != KingWire.Success) ResultMessage(KingReply.Reserve, coins.Result);
    }

    private void BuildNationTaxRate()
    {
        NationTaxRatePanel = NpcServiceKit.ServiceWindow(layer, "nationtaxrate", ChangeTaxRateLabel, NationTaxRateWidth, CloseNationTaxRate);
        var body = NationTaxRatePanel.Body;
        var prompt = NpcServiceKit.SpeechLabel(UiTheme.TextHi);
        prompt.HorizontalAlignment = HorizontalAlignment.Center;
        prompt.Text = TaxRatePrompt;
        body.AddChild(prompt);
        body.AddChild(NpcServiceKit.TaxArrows(out nationTaxRateValue, () => StepNationTaxRate(-1), () => StepNationTaxRate(1)));

        var ok = UiTheme.ActionButton(NpcServiceKit.Text(KingElection.OkText, "OK"), "");
        ok.Pressed += SubmitNationTaxRate;
        var cancel = UiTheme.SmallButton(NpcServiceKit.CancelLabel, "");
        cancel.Pressed += CloseNationTaxRate;
        body.AddChild(NpcServiceKit.FooterButtons(ok, cancel));
    }

    internal void OnTariffRead(KingTariff tariff)
    {
        nationTaxBusy = false;
        if (tariff.Result != KingWire.Success)
        {
            ResultMessage(KingReply.TariffRead, tariff.Result);
            return;
        }
        nationTaxRate = tariff.Tariff;
        nationTaxRateBusy = false;
        RefreshNationTaxRate();
        nationTaxRateShown = true;
        NationTaxRatePanel.Visible = true;
    }

    private void StepNationTaxRate(int delta)
    {
        nationTaxRate = NationTreasury.StepTaxRate(nationTaxRate, delta);
        RefreshNationTaxRate();
    }

    private void RefreshNationTaxRate() => nationTaxRateValue.Text = $"{nationTaxRate} %";

    private void SubmitNationTaxRate()
    {
        if (nationTaxRateBusy) return;
        nationTaxRateBusy = true;
        requests.Tariff(nationTaxRate);
    }

    internal void CloseNationTaxRate()
    {
        nationTaxRateBusy = false;
        nationTaxRateShown = false;
        NationTaxRatePanel.Visible = false;
    }

    private void OnTariffSet(KingTariff tariff)
    {
        nationTaxRateBusy = false;
        if (tariff.Result != KingWire.Success)
        {
            ResultMessage(KingReply.TariffSet, tariff.Result);
            return;
        }
        CloseNationTaxRate();
        Message(NpcServiceKit.Fill(NationTreasury.TaxRateSetText, "", tariff.Tariff));
    }

    private void BuildNationIntro()
    {
        NationIntroPanel = NpcServiceKit.ServiceWindow(layer, "nationintro", NationIntroLabel, NationIntroWidth, CloseNationIntro);
        var body = NationIntroPanel.Body;
        nationIntroEdit = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, NationIntroHeight),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        NpcServiceKit.StyleTextBox(nationIntroEdit);
        body.AddChild(nationIntroEdit);

        var revision = UiTheme.SmallButton(RevisionLabel, "");
        revision.Pressed += () => SaveNationIntro(close: false);
        var confirm = UiTheme.ActionButton(NpcServiceKit.ConfirmLabel, "");
        confirm.Pressed += () => SaveNationIntro(close: true);
        body.AddChild(NpcServiceKit.FooterButtons(revision, confirm));
    }

    internal void OpenNationIntro(string text)
    {
        nationIntroEdit.Text = NationTreasury.IntroOrWelcome(text, NpcServiceKit.Text(NationTreasury.IntroWelcomeText));
        nationIntroShown = true;
        NationIntroPanel.Visible = true;
    }

    internal void CloseNationIntro()
    {
        nationIntroShown = false;
        NationIntroPanel.Visible = false;
    }

    private void SaveNationIntro(bool close)
    {
        string text = nationIntroEdit.Text;
        int refusal = NationTreasury.IntroRefusal(text);
        if (refusal != 0)
        {
            context.Log.Notice(NpcServiceKit.Text(refusal));
            return;
        }
        requests.Intro(text);
        if (close) CloseNationIntro();
    }

    private void OnNationIntroSaved(bool saved) =>
        context.Log.Notice(NpcServiceKit.Text(saved ? NationTreasury.IntroSavedText : NationTreasury.IntroFailedText));
}
