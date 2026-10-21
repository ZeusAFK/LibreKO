namespace LibreKO.Domain;

public static class SiegeSkills
{
    public enum Verdict { Pass, Failed, ZoneRefused, NotDefender }

    public enum TargetKind { None, Player, HostileStructure, OtherNpc }

    public const int FailedText = 4004;
    public const int ZoneRefusedText = 6523;
    public const int NotDefenderText = 19514;

    private const int FirstBand = 110;
    private const int BandEnd = 160;
    private const int BandWidth = 10;
    private const int BecomeDigit = 1;
    private const int CatapultBand = 110;
    private const int RamBand = 120;
    private const int LadderBand = 130;
    private const int TowerBand = 140;
    private const int MovingTowerBand = 150;

    private const int CatapultLook = 30010;
    private const int RamLook = 30040;
    private const int LadderLook = 30060;
    private const int LadderLookAlt = 30061;
    private const int TowerLook = 30050;

    private const int Catapult = 450001;
    private const int BatteringRam = 450002;
    private const int LadderTruck = 450003;
    private const int MovingTower = 450017;
    private const int GuardTowerArrow = 450018;
    private const int OreadsShell = 490109;

    private const int Oreads = 66;
    private const int FirstLunarZone = 61;
    private const int LastLunarZone = 63;

    private static readonly int[] WideShells = { 490101, 490102, 490104, 490105, 490107, 490108, 490122 };
    private static readonly int[] WaterShells = { 490139, 490142, 490143 };

    private const int LookLevelFloor = 30000;

    public static int RequiredLook(int level) => level >= LookLevelFloor ? level : 0;

    public static bool IsSiegeSkill(int selfAnim2) => selfAnim2 is >= FirstBand and < BandEnd;

    public static bool IsBecome(int selfAnim2) => IsSiegeSkill(selfAnim2) && selfAnim2 % BandWidth == BecomeDigit;

    private static bool InBand(int selfAnim2, int band) => selfAnim2 >= band && selfAnim2 < band + BandWidth;

    private static bool Lunar(int zone) => zone is >= FirstLunarZone and <= LastLunarZone;

    public static bool BecomeAllowedIn(int skillId, int zone) => skillId switch
    {
        Catapult => zone == SiegeWar.Delos || Lunar(zone) || zone == Oreads,
        BatteringRam or MovingTower or GuardTowerArrow => zone == SiegeWar.Delos || zone == Oreads,
        LadderTruck => Lunar(zone) || zone == Oreads,
        _ => zone == SiegeWar.Delos,
    };

    public static Verdict Check(int skillId, int selfAnim2, bool transformed, int look, bool onTower, int zone,
        bool defender, TargetKind target)
    {
        if (!transformed)
        {
            if (!IsSiegeSkill(selfAnim2)) return Verdict.Pass;
            if (!IsBecome(selfAnim2) || onTower) return Verdict.Failed;
            return BecomeAllowedIn(skillId, zone) ? Verdict.Pass : Verdict.Failed;
        }

        if (IsBecome(selfAnim2)) return Verdict.Failed;
        return look switch
        {
            CatapultLook or SiegeTower.GuardTowerLook => InBand(selfAnim2, CatapultBand) ? ShellVerdict(skillId, zone, defender) : Verdict.Failed,
            RamLook => !InBand(selfAnim2, RamBand) || target is TargetKind.Player or TargetKind.OtherNpc ? Verdict.Failed : Verdict.Pass,
            LadderLook or LadderLookAlt => InBand(selfAnim2, LadderBand) ? Verdict.Pass : Verdict.Failed,
            TowerLook => InBand(selfAnim2, TowerBand) ? Verdict.Pass : Verdict.Failed,
            SiegeTower.MovingTowerLook => InBand(selfAnim2, MovingTowerBand) ? Verdict.Pass : Verdict.Failed,
            _ => Verdict.Pass,
        };
    }

    private static Verdict ShellVerdict(int skillId, int zone, bool defender)
    {
        if (System.Array.IndexOf(WideShells, skillId) >= 0)
            return zone == SiegeWar.Delos || Lunar(zone) || zone == Oreads ? Verdict.Pass : Verdict.Failed;
        if (skillId == OreadsShell)
            return zone == Oreads ? Verdict.Pass : Verdict.Failed;
        if (System.Array.IndexOf(WaterShells, skillId) >= 0)
            return zone != SiegeWar.Delos ? Verdict.ZoneRefused : defender ? Verdict.Pass : Verdict.NotDefender;
        return zone == SiegeWar.Delos ? Verdict.Pass : Verdict.Failed;
    }
}
