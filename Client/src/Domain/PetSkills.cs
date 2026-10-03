using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public static class PetSkills
{
    public const int DesignatedAttack = 301_000;
    public const int FirstSkill = 301_000;
    public const int LastSkill = 400_000;
    public const int SharedPageFirst = 1000;
    public const int SharedPageLast = 1009;
    public const int ClassPageDivisor = 10;
    public const int OwnerMoral = 33;
    public const int SlotsPerPage = 8;
    public const int Pages = 2;
    public const int StageCasting = 1;
    public const int StageEffecting = 3;

    public static bool BelongsTo(int skillId, int page, int petClass) =>
        skillId is >= FirstSkill and <= LastSkill
        && (page is >= SharedPageFirst and <= SharedPageLast || page / ClassPageDivisor == petClass);

    public static List<int> BarSkills(IEnumerable<(int Id, int Page)> skills, int petClass) =>
        skills.Where(s => s.Id != DesignatedAttack && BelongsTo(s.Id, s.Page, petClass))
              .Select(s => s.Id)
              .OrderBy(id => id)
              .Take(SlotsPerPage * Pages)
              .ToList();

    public static bool Unlocked(int requiredLevel, int petLevel) => requiredLevel <= petLevel;
}
