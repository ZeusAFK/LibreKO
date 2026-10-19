namespace LibreKO.Plugins;

public interface IGameUserVote
{
    string Title { get; }
    string SelectPrompt { get; }
    IReadOnlyList<string> Reasons { get; }
    string CostLine { get; }
    string AskQuestion { get; }
    string AskReason { get; }
    double AskSecondsLeft { get; }
    double AskSeconds { get; }
    string NoticeTitle { get; }
    IReadOnlyList<string> NoticeLines { get; }

    void Send(int reason, string text);
    void CancelSelect();
    void Answer(UserVoteAnswer answer);
    void CloseNotice();

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public IGameUserVote UserVote => userVote;

    private readonly UserVoteProxy userVote = new();

    internal void AttachUserVote(IGameUserVote userVoteSource)
    {
        userVote.Source = userVoteSource;
        RaiseUserVote();
    }

    internal void RaiseUserVote() => userVote.Raise();

    private sealed class UserVoteProxy : IGameUserVote
    {
        public IGameUserVote? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public string Title => Source?.Title ?? "";
        public string SelectPrompt => Source?.SelectPrompt ?? "";
        public IReadOnlyList<string> Reasons => Source?.Reasons ?? Array.Empty<string>();
        public string CostLine => Source?.CostLine ?? "";
        public string AskQuestion => Source?.AskQuestion ?? "";
        public string AskReason => Source?.AskReason ?? "";
        public double AskSecondsLeft => Source?.AskSecondsLeft ?? 0;
        public double AskSeconds => Source?.AskSeconds ?? 1;
        public string NoticeTitle => Source?.NoticeTitle ?? "";
        public IReadOnlyList<string> NoticeLines => Source?.NoticeLines ?? Array.Empty<string>();
        public void Send(int reason, string text) => Source?.Send(reason, text);
        public void CancelSelect() => Source?.CancelSelect();
        public void Answer(UserVoteAnswer answer) => Source?.Answer(answer);
        public void CloseNotice() => Source?.CloseNotice();
    }
}
