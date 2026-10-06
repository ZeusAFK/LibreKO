using System.Linq;

namespace LibreKO.Domain;

public static class GenderChange
{
    public const int Item = 810594000;
    public const int FailedText = 40021;
    public const int NoItemText = 40020;
    public const int NotForClassText = 40023;

    public static int BaseClass(int classCode)
    {
        int nation = classCode / 100;
        int group = (classCode % 100) switch
        {
            1 or 5 or 6 => 1,
            2 or 7 or 8 => 2,
            3 or 9 or 10 => 3,
            4 or 11 or 12 => 4,
            13 or 14 or 15 => 13,
            _ => 0,
        };
        return group == 0 ? 0 : nation * 100 + group;
    }

    public static int[] AllowedRaces(int classCode)
    {
        int baseClass = BaseClass(classCode);
        return baseClass == 0
            ? System.Array.Empty<int>()
            : StarterStats.RacesFor(classCode / 100).Where(race => StarterStats.ClassesFor(race).Contains(baseClass)).ToArray();
    }

    public static bool CanChange(int classCode) => AllowedRaces(classCode).Length > 1;
}
