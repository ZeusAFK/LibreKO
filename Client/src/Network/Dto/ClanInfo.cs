namespace LibreKO.Network;

public struct ClanBrowseEntry
{
    public int Id;
    public string Name;
    public string Chief;
    public int Members;
    public byte Flag;
    public int Points;
}

public struct ClanMember
{
    public string Name;
    public byte Fame;
    public byte Level;
    public int Class;
    public bool IsOnline;
    public string Title;
    public string Note;
    public int Loyalty;
}

public struct MyClanInfo
{
    public bool InClan;
    public int ClanId;
    public string Name;
    public byte Flag;
    public int Members;
    public string Chief;
    public byte Grade;
    public int Points;
    public int PointFund;
    public string Notice;
}
