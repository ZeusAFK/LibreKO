namespace LibreKO.Network;

public enum ChangeHairSubOpcode : byte
{
    InGame = 1,
}

public enum ChangeHairResult : byte
{
    Succeeded = 0,
    Failed = 1,
    OpenShop = 2,
}
