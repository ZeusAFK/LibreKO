namespace LibreKO.Features.Social;

internal interface IChatHost
{
    bool RunLocalCommand(string command);

    bool IsFriend(string name);

    bool NoahKnight { get; }

    int PersonalRankOf(int charId);

    string? SendRefusal();
}
