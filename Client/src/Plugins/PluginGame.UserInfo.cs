using Godot;
using LibreKO.Domain;

namespace LibreKO.Plugins;

public readonly record struct GameRankRibbon(Texture2D? Ribbon, string Count, string Tooltip);

public readonly record struct GameUserRankRow(string Title, string Place, Color PlaceColour, IReadOnlyList<GameRankRibbon> Tiers);

public sealed record GameUserCard(
    string Name,
    Color? NameColour,
    string Level,
    string Job,
    string Loyalty,
    string MonthlyLoyalty,
    string State,
    UserRelation Relation,
    bool InClan,
    string ClanName,
    string ClanGrade,
    string ClanChief,
    Texture2D? ClanMark,
    int ClanRanking,
    IReadOnlyList<GameRankRibbon> Summary,
    IReadOnlyList<GameUserRankRow> Rows);

public interface IGameUserInfo
{
    bool IsOpen { get; }
    GameUserCard? Card { get; }

    void Close();

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public IGameUserInfo UserInfo => userInfo;

    private readonly UserInfoProxy userInfo = new();

    internal void AttachUserInfo(IGameUserInfo userInfoSource)
    {
        userInfo.Source = userInfoSource;
        RaiseUserInfo();
    }

    internal void RaiseUserInfo() => userInfo.Raise();

    private sealed class UserInfoProxy : IGameUserInfo
    {
        public IGameUserInfo? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public GameUserCard? Card => Source?.Card;
        public void Close() => Source?.Close();
    }
}
