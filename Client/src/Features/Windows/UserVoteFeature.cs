using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class UserVoteFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly NpcQuestSession npcQuest;
    private readonly PartySession party;
    private readonly IPartyRequests partyRequests;

    public UserVoteFeature(IWorldContext context, NpcQuestSession npcQuest, PartySession party, IPartyRequests partyRequests)
    {
        this.context = context;
        this.npcQuest = npcQuest;
        this.party = party;
        this.partyRequests = partyRequests;
    }

    public void Tick(double delta, double nowSec)
    {
        UserVoteTick(nowSec);
    }

    internal const int PartyMenu = 4;
    private const int Width = 300;

    private CanvasLayer layer = null!;
    private HudWindow? select;
    private HudWindow? notice;
    private int member = -1;
    private string memberName = "";
    private UserVoteAsk? askShown;
    private string[] noticeLines = Array.Empty<string>();
    private HudWindow? askWindow;
    private ProgressBar? askBar;
    private double sentAt = double.NegativeInfinity;
    private double askUntil;

    private string VoteText(int id, string fallback) => ItemData.Text(id, fallback);

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.Prompts };
        context.Root.AddChild(layer);
        party.UserVoteAskEvent += OnAsk;
        party.UserVoteRefusedEvent += OnRefused;
        party.UserVoteResultEvent += OnResult;
        npcQuest.JuraidWarningEvent += OnJuraidWarning;
        PluginHost.Game.AttachUserVote(new PluginBridge(this));
        if (context.Zone != TempleZones.JuraidMountain && party.TakeVoteNotice() is { } pending)
        {
            Callable.From(() => ShowVoteNotice(pending.Warnings, pending.Limit)).CallDeferred();
        }
    }

    public void Dispose()
    {
        party.UserVoteAskEvent -= OnAsk;
        party.UserVoteRefusedEvent -= OnRefused;
        party.UserVoteResultEvent -= OnResult;
        npcQuest.JuraidWarningEvent -= OnJuraidWarning;
    }

    internal void Open(int memberId)
    {
        if (memberId == context.Self.CharId) return;
        if (context.Zone != TempleZones.JuraidMountain)
        {
            context.Log.Notice(VoteText(UserVote.NotHereText, "You cannot use that here."));
            return;
        }
        double left = UserVote.CooldownLeft(sentAt, context.Now);
        if (left > 0)
        {
            context.Log.Notice(VoteText(UserVote.AvailableInText, "It is available in %d seconds")
                .Replace("%d", Math.Ceiling(left).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return;
        }
        select?.QueueFree();
        member = memberId;
        memberName = party.NameOf(memberId);
        select = BuildSelect(memberId, memberName);
        layer.AddChild(select);
        PluginNotify();
    }

    private void PluginNotify() => Callable.From(PluginHost.Game.RaiseUserVote).CallDeferred();

    private string Prompt(string name) =>
        VoteText(UserVote.ChooseReasonText, "Please select the reason for %s warning").Replace("%s", name);

    private string CostLine() => VoteText(UserVote.CostText, "It will cost 4 million Noah to apply for a vote.");

    private bool Submit(int reason, string text)
    {
        if (member < 0 || reason is < 1 or > UserVote.Reasons) return false;
        text = reason == UserVote.ReasonOther ? text.Trim() : "";
        if (!UserVote.TextFits(text))
        {
            context.Log.Notice(VoteText(UserVote.TextTooLongText, "You have exceeded the maximum characters allowed."));
            return false;
        }
        partyRequests.UserVote(member, (byte)reason, text);
        sentAt = context.Now;
        CloseSelect();
        return true;
    }

    private void CloseSelect()
    {
        select?.QueueFree();
        select = null;
        member = -1;
        PluginNotify();
    }

    internal HudWindow BuildSelect(int memberId, string name)
    {
        var window = new HudWindow("user_vote", VoteText(UserVote.TitleText, "User Vote"), new Vector2(420, 180), Width);
        var body = window.Body;
        body.AddThemeConstantOverride("separation", 6);
        var prompt = UiTheme.Text(Prompt(name), 12, UiTheme.TextHi);
        prompt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(prompt);
        var group = new ButtonGroup();
        var options = new CheckBox[UserVote.Reasons];
        var other = new LineEdit { MaxLength = UserVote.OtherTextMaxLetters };
        int chosen = 1;
        for (int reason = 1; reason <= UserVote.Reasons; reason++)
        {
            int which = reason;
            var option = UiTheme.FlatCheck(VoteText(UserVote.ReasonText(reason), $"Reason {reason}"), reason == 1);
            option.ButtonGroup = group;
            option.Pressed += () => chosen = which;
            options[reason - 1] = option;
            body.AddChild(option);
        }
        other.TextChanged += _ =>
        {
            chosen = UserVote.ReasonOther;
            options[UserVote.ReasonOther - 1].SetPressedNoSignal(true);
        };
        body.AddChild(other);
        var cost = UiTheme.Text(CostLine(), 11, UiTheme.Gold);
        cost.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(cost);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 8);
        body.AddChild(buttons);
        var ok = new Button { Text = "OK", FocusMode = Control.FocusModeEnum.None };
        ok.Pressed += () => Submit(chosen, other.Text);
        buttons.AddChild(ok);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.Pressed += CloseSelect;
        buttons.AddChild(cancel);
        window.Visible = true;
        return window;
    }

    private void OnAsk(UserVoteAsk ask)
    {
        askWindow?.QueueFree();
        askShown = ask;
        askWindow = BuildAsk(ask);
        layer.AddChild(askWindow);
        askUntil = context.Now + UserVote.AnswerSeconds;
        PluginNotify();
    }

    private string Question(UserVoteAsk ask) =>
        VoteText(UserVote.AskText, "Would you like to vote for warning of %s?").Replace("%s", ask.Target);

    private string ReasonLine(UserVoteAsk ask) =>
        ask.Reason == UserVote.ReasonOther && ask.Text.Length > 0
            ? ask.Text
            : ask.Reason is >= 1 and <= UserVote.Reasons ? VoteText(UserVote.ReasonText(ask.Reason), "") : "";

    internal HudWindow BuildAsk(UserVoteAsk ask)
    {
        var window = new HudWindow("user_vote_ask", VoteText(UserVote.TitleText, "User Vote"), new Vector2(420, 220), Width, closable: false);
        var body = window.Body;
        body.AddThemeConstantOverride("separation", 6);
        var question = UiTheme.Text(Question(ask), 12, UiTheme.TextHi);
        question.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(question);
        string reason = ReasonLine(ask);
        if (reason.Length > 0) body.AddChild(UiTheme.Text(reason, 11, UiTheme.Gold));
        askBar = new ProgressBar { MaxValue = UserVote.AnswerSeconds, Value = UserVote.AnswerSeconds, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };
        body.AddChild(askBar);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 8);
        body.AddChild(buttons);
        foreach (var (caption, answer) in new[] { ("Yes", UserVoteAnswer.Yes), ("Draw", UserVoteAnswer.Draw), ("No", UserVoteAnswer.No) })
        {
            var button = new Button { Text = caption, FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => Answer(answer);
            buttons.AddChild(button);
        }
        window.Visible = true;
        return window;
    }

    private void Answer(UserVoteAnswer answer)
    {
        if (askWindow == null) return;
        partyRequests.UserVoteAnswer(answer);
        askWindow.QueueFree();
        askWindow = null;
        askBar = null;
        askShown = null;
        PluginNotify();
    }

    private void UserVoteTick(double now)
    {
        if (askWindow == null || askBar == null || !GodotObject.IsInstanceValid(askWindow)) return;
        double left = askUntil - now;
        askBar.Value = Math.Max(0, left);
        if (left <= 0) Answer(UserVoteAnswer.Draw);
    }

    private void OnRefused(short result) =>
        context.Log.Notice(VoteText(UserVote.RefusalText(result), "The vote could not start."));

    private void OnResult(UserVoteOutcome outcome)
    {
        if (outcome.Counted) return;
        context.Log.Notice(outcome.Warned
            ? VoteText(UserVote.WarnedText, "Voting Result: 1 warning has been given to the target user.")
            : VoteText(UserVote.VoidedText, "Voting Result: The vote was voided."));
    }

    private void ShowVoteNotice(int warnings, int limit)
    {
        string juraid = VoteText(UserVote.JuraidNameText, "Juraid");
        noticeLines = new[]
        {
            VoteText(UserVote.NoticeReceivedText, "You have received  %d warnings as a result of user voting of  %s.")
                .Replace("%d", warnings.ToString()).Replace("%s", juraid),
            Fill(VoteText(UserVote.NoticeCumulativeText, "Cumulative Warning:  %d / %d"), warnings, limit),
            VoteText(UserVote.NoticeLimitText, "If you get %d warnings, you will not be enter %s.")
                .Replace("%d", limit.ToString()).Replace("%s", juraid),
        };
        notice?.QueueFree();
        var window = new HudWindow("user_vote_notice", VoteText(UserVote.NoticeTitleText, "Party member evaluation"),
            new Vector2(420, 200), Width, closable: false);
        notice = window;
        layer.AddChild(window);
        window.Body.AddThemeConstantOverride("separation", 6);
        foreach (var line in noticeLines)
        {
            var label = UiTheme.Text(line, 12, UiTheme.TextHi);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            window.Body.AddChild(label);
        }
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        window.Body.AddChild(row);
        var ok = new Button { Text = "OK", FocusMode = Control.FocusModeEnum.None };
        ok.Pressed += CloseVoteNotice;
        row.AddChild(ok);
        window.Visible = true;
        PluginNotify();
    }

    private void CloseVoteNotice()
    {
        notice?.QueueFree();
        notice = null;
        PluginNotify();
    }

    private sealed class PluginBridge : LibreKO.Plugins.IGameUserVote
    {
        private readonly UserVoteFeature userVote;

        public PluginBridge(UserVoteFeature userVote) => this.userVote = userVote;

        public string Title => userVote.VoteText(UserVote.TitleText, "User Vote");
        public string SelectPrompt => userVote.Prompt(userVote.memberName);

        public IReadOnlyList<string> Reasons
        {
            get
            {
                var reasons = new string[UserVote.Reasons];
                for (int i = 0; i < reasons.Length; i++) reasons[i] = userVote.VoteText(UserVote.ReasonText(i + 1), $"Reason {i + 1}");
                return reasons;
            }
        }

        public string CostLine => userVote.CostLine();
        public string AskQuestion => userVote.askShown is { } ask ? userVote.Question(ask) : "";
        public string AskReason => userVote.askShown is { } ask ? userVote.ReasonLine(ask) : "";
        public double AskSecondsLeft => userVote.askShown != null ? Math.Max(0, userVote.askUntil - userVote.context.Now) : 0;
        public double AskSeconds => UserVote.AnswerSeconds;
        public string NoticeTitle => userVote.VoteText(UserVote.NoticeTitleText, "Party member evaluation");
        public IReadOnlyList<string> NoticeLines => userVote.noticeLines;
        public void Send(int reason, string text) => userVote.Submit(reason, text);
        public void CancelSelect() => userVote.CloseSelect();
        public void Answer(UserVoteAnswer answer) => userVote.Answer(answer);
        public void CloseNotice() => userVote.CloseVoteNotice();

        event Action? LibreKO.Plugins.IGameUserVote.Changed { add { } remove { } }
    }

    private void OnJuraidWarning(int warnings, int limit)
    {
        string text = Fill(VoteText(UserVote.EntryBarredText,
            "Currently, the cumulative warning is  %d times. If  %d times are accumulated, you will not be able to enter %s"),
            warnings, limit).Replace("%s", VoteText(UserVote.JuraidNameText, "Juraid"));
        Notice.Show(context.Root, text, VoteText(UserVote.NoticeTitleText, "Party member evaluation"));
    }

    internal static string Fill(string format, int first, int second)
    {
        int at = format.IndexOf("%d", StringComparison.Ordinal);
        if (at >= 0) format = format[..at] + first + format[(at + 2)..];
        at = format.IndexOf("%d", StringComparison.Ordinal);
        if (at >= 0) format = format[..at] + second + format[(at + 2)..];
        return format;
    }
}
