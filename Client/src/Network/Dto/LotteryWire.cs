namespace LibreKO.Network;

public enum LotterySubOpcode : byte
{
    State = 1,
    Join = 2,
    Progress = 3,
    Ended = 4,
    Close = 5,
}
