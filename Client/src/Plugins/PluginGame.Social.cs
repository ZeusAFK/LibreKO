using Godot;

namespace LibreKO.Plugins;

public readonly record struct GameWhisperLine(bool Mine, bool Notice, string Text);

public readonly record struct GamePartyMember(
    int Id,
    string Name,
    int Level,
    int Class,
    int Hp,
    int MaxHp,
    int Mp,
    int MaxMp,
    bool Leader,
    bool Picked,
    Color NameColour,
    Color HpColour,
    string Tooltip,
    PartyHpLook HpLook = PartyHpLook.Normal);

public interface IGameWhispers
{
    IReadOnlyList<string> Open { get; }
    IReadOnlyList<GameWhisperLine> History(string name);
    bool StartsCollapsed(string name);
    bool Blocked { get; }
    int Size { get; }
    float Alpha { get; }

    void Send(string name, string text);
    void Close(string name);
    void ToggleBlock();
    void SetLook(int size, float alpha);
    void ShowPlayerMenu(string name);
    void OpenWith(string name);

    event Action<string, GameWhisperLine>? LineAdded;
    event Action? Changed;
}

public readonly record struct GameWarBoardRow(int Rank, string Name, int Class, int Share);

public readonly record struct GamePartyBuff(int SkillId, Texture2D? Icon, string Tooltip);

public interface IGameParty
{
    bool InParty { get; }
    bool IsLeader { get; }
    IReadOnlyList<GamePartyMember> Members { get; }
    IReadOnlyList<GamePartyBuff> MemberBuffs(int id);
    int PingMs(int id);
    double LevelPercent(int id);

    void Pick(int id);
    void Leave();
    void MakeLeader(int id);
    void Kick(int id);
    void MakeCommander(int id);
    void ShowMemberMenu(int id);
    bool CommanderBarHidden { get; }
    void ShowCommanderBar();
    PartyBoardMeterMode RowMeterMode { get; }
    int RowMeterShare(int id);
    void CycleRowMeter();
    bool WarBoardOpen { get; }
    PartyBoardMeterMode WarBoardMode { get; }
    float WarBoardAlpha { get; }
    int WarBoardCooldown { get; }
    IReadOnlyList<GameWarBoardRow> WarBoardRows { get; }
    void ToggleWarBoard();
    void SetWarBoardMode(PartyBoardMeterMode mode);
    void SetWarBoardAlpha(float alpha);
    void MoveRow(int id, int step);
    void MoveRowTo(int id, int index);
    bool AutoRejoin { get; }
    string AutoRejoinTip { get; }
    void ToggleAutoRejoin();
    bool UserVoteHere { get; }
    void UserVote(int id);

    event Action? Changed;
}

public interface IGameTrade
{
    bool IsOpen { get; }
    string Partner { get; }
    IReadOnlyList<GameItem> Mine { get; }
    IReadOnlyList<GameItem> Theirs { get; }
    long MyGold { get; }
    long TheirGold { get; }
    bool MyConfirmed { get; }
    bool TheirConfirmed { get; }

    void Offer(int bagSlot);
    void AskGold();
    void Confirm();
    void Cancel();
    void ShowTooltip(bool mine, int index);
    void HideTooltip();

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public IGameWhispers Whispers => whispers;
    public IGameParty Party => party;
    public IGameTrade Trade => trade;

    private readonly WhispersProxy whispers = new();
    private readonly PartyProxy party = new();
    private readonly TradeProxy trade = new();

    internal void AttachSocial(IGameWhispers whispersSource, IGameParty partySource, IGameTrade tradeSource)
    {
        whispers.Source = whispersSource;
        party.Source = partySource;
        trade.Source = tradeSource;
        RaiseWhispers();
        RaiseParty();
        RaiseTrade();
    }

    private void DetachSocial()
    {
        whispers.Source = null;
        party.Source = null;
        trade.Source = null;
        warp.Source = null;
    }

    internal void RaiseWhispers() => whispers.Raise();
    internal void RaiseWhisperLine(string name, GameWhisperLine line) => whispers.RaiseLine(name, line);
    internal void RaiseParty() => party.Raise();
    internal void RaiseTrade() => trade.Raise();

    private sealed class WhispersProxy : IGameWhispers
    {
        public IGameWhispers? Source;
        public event Action<string, GameWhisperLine>? LineAdded;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();
        internal void RaiseLine(string name, GameWhisperLine line) => LineAdded?.Invoke(name, line);

        public IReadOnlyList<string> Open => Source?.Open ?? Array.Empty<string>();
        public IReadOnlyList<GameWhisperLine> History(string name) => Source?.History(name) ?? Array.Empty<GameWhisperLine>();
        public bool StartsCollapsed(string name) => Source?.StartsCollapsed(name) ?? false;
        public bool Blocked => Source?.Blocked ?? false;
        public int Size => Source?.Size ?? 0;
        public float Alpha => Source?.Alpha ?? 1f;
        public void Send(string name, string text) => Source?.Send(name, text);
        public void OpenWith(string name) => Source?.OpenWith(name);
        public void Close(string name) => Source?.Close(name);
        public void ToggleBlock() => Source?.ToggleBlock();
        public void SetLook(int size, float alpha) => Source?.SetLook(size, alpha);
        public void ShowPlayerMenu(string name) => Source?.ShowPlayerMenu(name);
    }

    private sealed class PartyProxy : IGameParty
    {
        public IGameParty? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool InParty => Source?.InParty ?? false;
        public bool IsLeader => Source?.IsLeader ?? false;
        public IReadOnlyList<GamePartyMember> Members => Source?.Members ?? Array.Empty<GamePartyMember>();
        public IReadOnlyList<GamePartyBuff> MemberBuffs(int id) => Source?.MemberBuffs(id) ?? Array.Empty<GamePartyBuff>();
        public int PingMs(int id) => Source?.PingMs(id) ?? PartySignal.Unknown;
        public double LevelPercent(int id) => Source?.LevelPercent(id) ?? 0.0;
        public void Pick(int id) => Source?.Pick(id);
        public void Leave() => Source?.Leave();
        public void MakeLeader(int id) => Source?.MakeLeader(id);
        public void Kick(int id) => Source?.Kick(id);
        public void MakeCommander(int id) => Source?.MakeCommander(id);
        public bool CommanderBarHidden => Source?.CommanderBarHidden ?? false;
        public void ShowCommanderBar() => Source?.ShowCommanderBar();
        public void ShowMemberMenu(int id) => Source?.ShowMemberMenu(id);
        public PartyBoardMeterMode RowMeterMode => Source?.RowMeterMode ?? PartyBoardMeterMode.Off;
        public int RowMeterShare(int id) => Source?.RowMeterShare(id) ?? -1;
        public void CycleRowMeter() => Source?.CycleRowMeter();
        public bool WarBoardOpen => Source?.WarBoardOpen ?? false;
        public PartyBoardMeterMode WarBoardMode => Source?.WarBoardMode ?? PartyBoardMeterMode.Off;
        public float WarBoardAlpha => Source?.WarBoardAlpha ?? 1f;
        public int WarBoardCooldown => Source?.WarBoardCooldown ?? 0;
        public IReadOnlyList<GameWarBoardRow> WarBoardRows => Source?.WarBoardRows ?? Array.Empty<GameWarBoardRow>();
        public void ToggleWarBoard() => Source?.ToggleWarBoard();
        public void SetWarBoardMode(PartyBoardMeterMode mode) => Source?.SetWarBoardMode(mode);
        public void SetWarBoardAlpha(float alpha) => Source?.SetWarBoardAlpha(alpha);
        public void MoveRow(int id, int step) => Source?.MoveRow(id, step);
        public void MoveRowTo(int id, int index) => Source?.MoveRowTo(id, index);
        public bool AutoRejoin => Source?.AutoRejoin ?? false;
        public string AutoRejoinTip => Source?.AutoRejoinTip ?? "";
        public void ToggleAutoRejoin() => Source?.ToggleAutoRejoin();
        public bool UserVoteHere => Source?.UserVoteHere ?? false;
        public void UserVote(int id) => Source?.UserVote(id);
    }

    private sealed class TradeProxy : IGameTrade
    {
        public IGameTrade? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public string Partner => Source?.Partner ?? "";
        public IReadOnlyList<GameItem> Mine => Source?.Mine ?? Array.Empty<GameItem>();
        public IReadOnlyList<GameItem> Theirs => Source?.Theirs ?? Array.Empty<GameItem>();
        public long MyGold => Source?.MyGold ?? 0;
        public long TheirGold => Source?.TheirGold ?? 0;
        public bool MyConfirmed => Source?.MyConfirmed ?? false;
        public bool TheirConfirmed => Source?.TheirConfirmed ?? false;
        public void Offer(int bagSlot) => Source?.Offer(bagSlot);
        public void AskGold() => Source?.AskGold();
        public void Confirm() => Source?.Confirm();
        public void Cancel() => Source?.Cancel();
        public void ShowTooltip(bool mine, int index) => Source?.ShowTooltip(mine, index);
        public void HideTooltip() => Source?.HideTooltip();
    }
}
