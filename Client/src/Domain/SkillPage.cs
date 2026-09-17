namespace LibreKO.Domain;

public static class SkillPage
{
    public const int Basic = 0;
    public const int Hidden = -1;
    public const int PassiveTree = 9;
    public const int NationBuffFirstTree = 4000;
    public const int NationBuffEndTree = 10000;
    public const int UsableItemFirstId = 490000;

    public const int Columns = 2;
    public const int Rows = 4;
    public const int SlotsPerPage = Columns * Rows;
    public const int MaxPages = 7;

    public const int NoWeapon = 9;
    public const int AnyWeapon = 0;
    public const int EmoteWeapon = 254;
    public const int DaggerWeapon = 1;
    public const int BowWeapon = 7;
    public const int StaffWeapon = 11;
    public const int JamadarWeapon = 14;

    public static readonly int[] Order = BuildOrder();

    private static int[] BuildOrder()
    {
        int masteries = MasteryPoints.LastTree - MasteryPoints.FirstTree + 1;
        var order = new int[masteries + 1];
        order[0] = Basic;
        for (int i = 0; i < masteries; i++) order[i + 1] = MasteryPoints.FirstTree + i;
        return order;
    }
}
