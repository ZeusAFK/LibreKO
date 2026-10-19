using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ClanHandoverFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly SelfSession self;
    private readonly IClanRequests clanRequests;

    public ClanHandoverFeature(IWorldContext context, ClanSession clan, SelfSession self, IClanRequests clanRequests)
    {
        this.context = context;
        this.clan = clan;
        this.self = self;
        this.clanRequests = clanRequests;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    private const int TextHandoverNoAuthority = 15718;
    private const int TextHandoverNoVice = 15719;
    private const int TextHandoverInfo = 15720;
    private const int TextHandoverDone = 15722;
    private const int TextHandoverFailed = 15723;
    private const int TextHandoverRefused = 15726;
    internal const byte HandoverListOk = 1;
    private const byte HandoverListNoAuthority = 4;
    private const int HandoverChoices = 3;
    private const double HandoverResendSeconds = 5.0;
    private const float HandoverInfoWidth = 260f;

    internal HudWindow? HandoverWindow { get; private set; }
    private Label handoverInfo = null!;
    private readonly Button[] handoverChoices = new Button[HandoverChoices];
    private Button handoverConfirm = null!;
    internal string HandoverPreselect { get; set; } = "";
    private double handoverSentAt = double.NegativeInfinity;

    internal void Request(string preselect)
    {
        HandoverPreselect = preselect;
        clanRequests.HandoverList();
    }

    private void BuildHandoverWindow()
    {
        HandoverWindow = new HudWindow("clanhandover", "Leader Transfer") { Visible = false };
        var layer = new CanvasLayer { Layer = UiLayers.TopWindows };
        context.Root.AddChild(layer);
        layer.AddChild(HandoverWindow);
        var root = HandoverWindow.Body;
        root.AddThemeConstantOverride("separation", 8);
        handoverInfo = UiTheme.Text("", 12, UiTheme.TextHi);
        handoverInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        handoverInfo.CustomMinimumSize = new Vector2(HandoverInfoWidth, 0);
        root.AddChild(handoverInfo);
        var group = new ButtonGroup();
        for (int i = 0; i < HandoverChoices; i++)
        {
            var choice = new Button { ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None };
            choice.AddThemeStyleboxOverride("pressed", HandoverPickedStyle());
            choice.AddThemeColorOverride("font_pressed_color", UiTheme.GoldBright);
            choice.Toggled += _ => RefreshHandoverConfirm();
            handoverChoices[i] = choice;
            root.AddChild(choice);
        }
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        footer.AddThemeConstantOverride("separation", 8);
        root.AddChild(footer);
        handoverConfirm = UiTheme.SmallButton("Confirm", "Hand the clan over to the chosen vice-chief");
        handoverConfirm.Pressed += ConfirmHandover;
        footer.AddChild(handoverConfirm);
        var close = UiTheme.SmallButton("Close", "");
        close.Pressed += () => HandoverWindow!.Visible = false;
        footer.AddChild(close);
    }

    internal void ShowHandoverWindow(byte state, List<string> names)
    {
        if (state == HandoverListNoAuthority)
        {
            context.Log.Notice(GameText.System(TextHandoverNoAuthority, "You don't have the authority to change leadership."));
            return;
        }
        if (state != HandoverListOk)
        {
            context.Log.Notice(GameText.System(TextHandoverRefused, "You can't change leadership."));
            return;
        }
        if (HandoverWindow == null) BuildHandoverWindow();
        handoverInfo.Text = names.Count == 0
            ? GameText.System(TextHandoverNoVice, "There is no Co-leader authorized to change leadership.")
            : TextTemplate.Fill(GameText.System(TextHandoverInfo,
                "%s, the leader of %s, refuses to be the leader of the clan and passes  leadership to the next clan member."),
                self.LastEnter.Name, clan.MyClan.Name);
        for (int i = 0; i < HandoverChoices; i++)
        {
            bool shown = i < names.Count;
            handoverChoices[i].Visible = shown;
            handoverChoices[i].Text = shown ? names[i] : "";
            handoverChoices[i].SetPressedNoSignal(shown && (names.Count == 1
                || string.Equals(names[i], HandoverPreselect, System.StringComparison.OrdinalIgnoreCase)));
        }
        HandoverPreselect = "";
        RefreshHandoverConfirm();
        HandoverWindow!.Visible = true;
        HandoverWindow.ResetSize();
    }

    private static StyleBoxFlat HandoverPickedStyle()
    {
        var style = new StyleBoxFlat { BgColor = new Color(UiTheme.Gold, 0.18f), BorderColor = UiTheme.GoldBright };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(3);
        return style;
    }

    private string? HandoverChoice()
    {
        foreach (var choice in handoverChoices)
            if (choice.Visible && choice.ButtonPressed) return choice.Text;
        return null;
    }

    private void RefreshHandoverConfirm() => handoverConfirm.Disabled = HandoverChoice() == null;

    private void ConfirmHandover()
    {
        if (HandoverChoice() is not { } heir || context.Now - handoverSentAt < HandoverResendSeconds) return;
        handoverSentAt = context.Now;
        clanRequests.Handover(heir);
        HandoverWindow!.Visible = false;
    }

    internal string HandoverDoneText(string oldChief, string newChief) =>
        string.Equals(oldChief, self.LastEnter.Name, System.StringComparison.OrdinalIgnoreCase)
        ? TextTemplate.Fill(GameText.System(TextHandoverDone, "You have handed over leadership to %s."), newChief)
        : $"{oldChief}, the leader of {clan.MyClan.Name}, passes leadership to {newChief}.";

    internal string HandoverFailedText => GameText.System(TextHandoverFailed, "You failed to pass on the leadership.");
}
