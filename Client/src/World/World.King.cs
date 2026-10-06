using System;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int KingLayerIndex = 75;
    private const string KingElectionTitle = "King Election";
    private const string NationTreasuryTitle = "National Treasury";
    private const string CancelLabel = "Cancel";
    private const float NpcOptionHeight = 34f;
    private const int NpcOptionFontSize = 13;
    private const float FooterButtonWidth = 110f;
    private const float FooterButtonHeight = 30f;

    private CanvasLayer _kingLayer = null!;
    private bool _kingBoxOpen;

    private void KingInit()
    {
        BuildKingWindows();

        Net.I.KingElectionOpenEvent += OpenKingElection;
        Net.I.KingScheduleEvent += OnKingSchedule;
        Net.I.KingResultEvent += OnKingResult;
        Net.I.KingCandidatesEvent += OnKingCandidates;
        Net.I.KingPlanEvent += OnKingPlan;
        Net.I.KingShoutEvent += OnKingShout;
        Net.I.KingSenatorsEvent += OnKingSenators;
        Net.I.KingImpeachmentProposedEvent += OnKingImpeachmentProposed;
        Net.I.KingTreasuryEvent += OnKingTreasury;
        Net.I.KingFundEvent += OnKingFund;
        Net.I.KingTariffReadEvent += OnKingTariffRead;
        Net.I.KingTariffSetEvent += OnKingTariffSet;
        Net.I.KingReserveEvent += OnKingReserve;
        Net.I.KingIntroEvent += OpenNationIntro;
        Net.I.KingIntroSavedEvent += OnNationIntroSaved;
        Net.I.KingTreasuryNoticeEvent += OnKingTreasuryNotice;
    }

    private void KingDispose()
    {
        Net.I.KingElectionOpenEvent -= OpenKingElection;
        Net.I.KingScheduleEvent -= OnKingSchedule;
        Net.I.KingResultEvent -= OnKingResult;
        Net.I.KingCandidatesEvent -= OnKingCandidates;
        Net.I.KingPlanEvent -= OnKingPlan;
        Net.I.KingShoutEvent -= OnKingShout;
        Net.I.KingSenatorsEvent -= OnKingSenators;
        Net.I.KingImpeachmentProposedEvent -= OnKingImpeachmentProposed;
        Net.I.KingTreasuryEvent -= OnKingTreasury;
        Net.I.KingFundEvent -= OnKingFund;
        Net.I.KingTariffReadEvent -= OnKingTariffRead;
        Net.I.KingTariffSetEvent -= OnKingTariffSet;
        Net.I.KingReserveEvent -= OnKingReserve;
        Net.I.KingIntroEvent -= OpenNationIntro;
        Net.I.KingIntroSavedEvent -= OnNationIntroSaved;
        Net.I.KingTreasuryNoticeEvent -= OnKingTreasuryNotice;
    }

    private void BuildKingWindows()
    {
        _kingLayer = new CanvasLayer { Layer = KingLayerIndex };
        AddChild(_kingLayer);
        BuildKingElection();
        BuildKingNominate();
        BuildKingPlanEditor();
        BuildKingVote();
        BuildNationTax();
        BuildNationIntro();
        BuildNationTaxRate();
        BuildKingBallot();
    }

    private static string KingText(int id, string fallback = "") => ItemData.Text(id, fallback);

    private static string KingFill(int id, string fallback, params object[] args) =>
        TextTemplate.Fill(ItemData.Text(id, fallback), args);

    private string NpcWindowTitle(string fallback) =>
        _npcTalkId >= 0 && _vendorNpcName.Length > 0 ? _vendorNpcName : fallback;

    private static HudWindow ServiceWindow(CanvasLayer layer, string id, string title, int width, Action closed)
    {
        var window = new HudWindow(id, title, bodyMinWidth: width) { Visible = false };
        window.Closed += closed;
        layer.AddChild(window);
        window.Body.AddThemeConstantOverride("separation", 8);
        return window;
    }

    private static PanelContainer NpcSpeech(float minHeight, out Label upper, out Label lower)
    {
        var box = UiTheme.Section();
        box.CustomMinimumSize = new Vector2(0, minHeight);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        box.AddChild(column);
        upper = SpeechLabel(UiTheme.TextHi);
        column.AddChild(upper);
        lower = SpeechLabel(UiTheme.TextLo);
        column.AddChild(lower);
        return box;
    }

    private static Label SpeechLabel(Color colour)
    {
        var label = UiTheme.Text("", 13, colour);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.CustomMinimumSize = new Vector2(1, 0);
        return label;
    }

    private static void SetSpeech(Label label, string text)
    {
        label.Text = text;
        label.Visible = text.Length > 0;
    }

    private static Button NpcOption(string text, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, NpcOptionHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.AddThemeFontSizeOverride("font_size", NpcOptionFontSize);
        button.Pressed += pressed;
        return button;
    }

    private static HBoxContainer FooterButtons(params Button[] buttons)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        foreach (var button in buttons)
        {
            button.CustomMinimumSize = new Vector2(FooterButtonWidth, FooterButtonHeight);
            row.AddChild(button);
        }
        return row;
    }

    private void KingMessage(string text, string title = KingElectionTitle)
    {
        if (text.Length > 0) Notice.Show(this, text, title);
    }

    private void KingResultMessage(KingReply reply, short result, string title = KingElectionTitle)
    {
        int id = KingElection.ResultText(reply, result);
        if (id == KingElection.UnknownResult) KingMessage(KingElection.UnknownResultLine(result), title);
        else if (id != 0) KingMessage(KingText(id), title);
    }

    private void KingConfirm(KingBox box, string text, string subject = "", bool fromPush = false)
    {
        _kingBoxOpen = true;
        if (KingElection.TwoChoices(box))
        {
            OpenKingBallot(box, text, fromPush);
            return;
        }
        string title = box == KingBox.Fund ? NationTreasuryTitle : KingElectionTitle;
        Notice.Confirm(this, text, KingText(KingElection.OkText, "OK"), CancelLabel,
            () => { _kingBoxOpen = false; KingBoxAccepted(box, subject); },
            () => { _kingBoxOpen = false; KingBoxCancelled(box); },
            title);
    }

    private void KingBoxAccepted(KingBox box, string subject)
    {
        switch (box)
        {
            case KingBox.Nominate:
                Net.I.SendKingNominate(subject);
                break;
            case KingBox.TurnDown:
                _kingBusy = true;
                Net.I.SendKingWithdraw();
                break;
            case KingBox.Fund:
                _nationTaxBusy = true;
                Net.I.SendKingFund();
                break;
            case KingBox.Propose:
                Net.I.SendKingImpeachmentPropose();
                break;
        }
    }

    private void KingBoxCancelled(KingBox box)
    {
        switch (box)
        {
            case KingBox.Nominate:
                ShowKingNominate();
                break;
            case KingBox.TurnDown:
                ShowKingElectionPage(ElectionPage.Election);
                break;
            case KingBox.Fund:
                ShowNationTax();
                break;
            case KingBox.Propose:
            case KingBox.SenatorBallot:
                ShowKingElectionPage(ElectionPage.Impeachment, ElectionPage.Proposal);
                break;
            case KingBox.PublicBallot:
                ShowKingElectionPage(ElectionPage.Impeachment);
                break;
        }
    }

    private void OnKingResult(KingReply reply, short result)
    {
        switch (reply)
        {
            case KingReply.Nominate:
            case KingReply.Withdraw:
            case KingReply.Propose:
            case KingReply.SenatorVote:
            case KingReply.PublicVote:
                _kingBusy = false;
                KingResultMessage(reply, result);
                break;
            case KingReply.PlanPosted:
                _kingPlanBusy = false;
                if (result == KingElection.Success) CloseKingPlanEditor();
                KingResultMessage(reply, result);
                break;
            case KingReply.Board:
                _kingBusy = false;
                if (result == KingElection.Success) ShowKingElectionPage(ElectionPage.Board);
                else KingResultMessage(reply, result);
                break;
            case KingReply.Vote:
                _kingVoteBusy = false;
                CloseKingVote();
                KingResultMessage(reply, result);
                break;
            case KingReply.SenatorBallot:
            case KingReply.PublicBallot:
                _kingBusy = false;
                if (result != KingElection.Success)
                {
                    KingResultMessage(reply, result);
                    break;
                }
                HideKingElection();
                bool senators = reply == KingReply.SenatorBallot;
                KingConfirm(senators ? KingBox.SenatorBallot : KingBox.PublicBallot,
                    KingText(senators ? KingElection.SenatorBallotText : KingElection.PublicBallotText));
                break;
            case KingReply.KingItem:
                _nationTaxBusy = false;
                CloseNationTax();
                KingResultMessage(reply, result, NationTreasuryTitle);
                break;
            case KingReply.ChannelOnly:
                _nationTaxBusy = false;
                KingResultMessage(reply, result, NationTreasuryTitle);
                break;
        }
    }

    private void OnKingImpeachmentProposed()
    {
        HideKingElection();
        KingConfirm(KingBox.SenatorBallot, KingText(KingElection.SenatorBallotText), fromPush: true);
    }

    private void OnKingTreasuryNotice(KingTreasuryNotice notice)
    {
        if (!notice.Shown) return;
        CombatNotice(KingFill(NationTreasury.TreasuryUsedText, "", notice.Used));
        CombatNotice(KingFill(NationTreasury.TreasuryLeftText, "", notice.Left));
    }
}
