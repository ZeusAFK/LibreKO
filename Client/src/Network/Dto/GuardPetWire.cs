namespace LibreKO.Network;

public enum GuardPetSubOpcode : byte
{
    Status = 1,
}

public struct GuardPetStatus
{
    public bool Active;
    public int Hp;
    public int MaxHp;
}
