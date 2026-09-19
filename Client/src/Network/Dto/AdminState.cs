namespace LibreKO.Network;

public struct AdminState
{
    public bool Granted;
    public int Class;
    public byte Race;
    public byte Face;
    public int Hair;
    public int Level;
    public int Str, Sta, Dex, Intel, MagicStat;
    public int StatPoints;
    public int MaxHp, MaxMp;
    public int Ap, Ac;
    public int Gold;
    public byte[] SkillPoints;
    public int[] ClassOptions;
}
