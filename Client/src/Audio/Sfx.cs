namespace LibreKO;

public static class Sfx
{
    public const string BgmIntroFile = "intro_sound.ogg";
    public const int BgmTown = 20000;

    public const int UiWindow = 40;
    public const int UiButton = 2000;
    public const int UiRepair = 2001;
    public const int ItemWeapon = 2002;
    public const int ItemArmor = 2003;
    public const int CharSelectTurn = 2501;
    public const int Gold = 3000;

    public const int WeaponImpact = 100;

    public const int WeatherWindy = 200;
    public const int WeatherRainy = 201;

    public const int EatItem = 110;
    public const int EatExp = 109;

    public const int LevelUpKarus = 9000;
    public const int LevelUpElMorad = 9500;

    public const int WarpZone = 400;
    public const int TransformScroll = 340102;
    public const int TradeComplete = 340103;
    public const int Upgrade = 340105;
    public const int StorageOn = 340107;
    public const int QuestComplete = 340109;
    public const string SkillReadyFile = "ui_button1.ogg";

    public const int GetItem = 340070;
    public const int GetUniqueItem = 340071;
    public const int GetNoahExp = 340072;
    public const int ChatAlarm = 340050;
    public const int LootUnique = 340051;

    public const int AbilityClick = 340125;
    public const int CheckboxClick = 340127;
    public const int CoinGet = 340129;
    public const int InventoryClose = 340131;
    public const int InventoryOpen = 340132;
    public const int MsgBoxPop = 340133;
    public const int NpcMenuSelect = 340134;
    public const int RepairFailed = 340136;
    public const int TaskbarOpen = 340138;
    public const int WarpGate = 340139;

    public static int LevelUp(int nation) => nation == Nations.Karus ? LevelUpKarus : LevelUpElMorad;
}
