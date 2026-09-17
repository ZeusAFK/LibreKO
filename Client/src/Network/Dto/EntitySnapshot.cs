namespace LibreKO.Network;

public sealed class EntitySnapshot
{
    public int Id;
    public bool IsNpc;
    public bool IsMonster;
    public bool Attackable;
    public string Name = "";
    public int Nation;
    public int Level;
    public int NpcId;
    public int NpcType;
    public int ObjectType;
    public int ModelId;
    public int Size;
    public int Race, Class, Face, Hair;
    public int[] Gear = System.Array.Empty<int>();
    public int CapeId;
    public int CapeR, CapeG, CapeB;
    public int KnightsId;
    public string ClanName = "";
    public int ClanGrade;
    public int TitleId;
    public bool IsGm;
    public bool Invisible;
    public bool HelmetHidden;
    public bool Sitting;
    public bool Dead;
    public bool Gathering;
    public bool GatherFishing;
    public float X, Z, Y;
    public float Dir;
}
