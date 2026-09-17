namespace LibreKO.Game.World;

public class PetState
{
    public const byte ModeAttack = 3;
    public const byte ModeDefence = 4;
    public const byte ModeLooting = 8;
    public const byte ModeChat = 9;

    public const short MaxSatisfaction = 10000;
    public const byte MaxPetLevel = 60;

    public int ItemId { get; set; }
    public int Nid { get; set; }
    public byte Mode { get; set; } = ModeDefence;
    public short Satisfaction { get; set; } = MaxSatisfaction;
    public byte Level { get; set; } = 1;
    public long Exp { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
}
