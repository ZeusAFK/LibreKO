namespace LibreKO.Network;

public enum ChallengeSubOpcode : byte
{
    PvpRequest = 1,
    PvpCancel = 2,
    PvpAccept = 3,
    PvpReject = 4,
    PvpRequestSent = 5,
    GenericError = 11,
    ZoneError = 12,
    ClanError = 13,
}
