using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public enum AchievementConditionTable : byte
{
    War = 1,
    Monster = 2,
    Completion = 3,
    Normal = 4,
    Special = 5,
}

public enum WarAchievementKind : byte
{
    PlayerKill = 1,
    Revenge = 3,
    LunarWarWin = 4,
    NationGuardWin = 5,
    JuraidWin = 6,
    ChaosWarFirst = 7,
    ChaosWarSecond = 8,
    ChaosWarThird = 9,
    CastleSiegeWin = 10,
    ChaosWarKillsInRound = 11,
    RonarkMonsterKill = 20,
    DrakiTowerUnderTime = 21,
    VanguardKill = 23,
    VanguardSurvive = 24,
    ChaosWarKillsTotal = 25,
    BlitzLadderRank = 26,
}

public enum CompletionAchievementKind : byte
{
    Quests = 1,
    Achievements = 2,
}

public enum NormalAchievementKind : byte
{
    King = 1,
    NationalContribution = 2,
    Level = 3,
    KnightsContribution = 5,
    KnightsGrade = 10,
    JuraidWin = 13,
}

public enum AchievementTab : byte
{
    Normal = 0,
    Quest = 1,
    War = 2,
    Adventure = 3,
    Challenge = 4,
}

public class AchievementData
{
    public int Id { get; set; }
    public AchievementConditionTable ConditionTable { get; set; }
    public AchievementTab Tab { get; set; }
    public byte Group { get; set; }
    public short Points { get; set; }
    public short TitleId { get; set; }
    public int RewardItemId { get; set; }
    public short RewardItemCount { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Kind { get; set; }
    public int Target { get; set; }
    public string Npcs { get; set; } = string.Empty;
    public string Requires { get; set; } = string.Empty;

    private int[]? _npcIds;
    private int[]? _requiredIds;

    public int[] NpcIds => _npcIds ??= ParseIds(Npcs);

    public int[] RequiredIds => _requiredIds ??= ParseIds(Requires);

    public WarAchievementKind WarKind => (WarAchievementKind)Kind;

    public CompletionAchievementKind CompletionKind => (CompletionAchievementKind)Kind;

    public NormalAchievementKind NormalKind => (NormalAchievementKind)Kind;

    public bool CountsNpc(int npcId)
    {
        foreach (var id in NpcIds)
        {
            if (id == npcId) return true;
        }
        return false;
    }

    private static int[] ParseIds(string packed)
    {
        if (string.IsNullOrEmpty(packed)) return [];

        var parts = packed.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var ids = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var id) && id != 0)
                ids.Add(id);
        }
        return ids.ToArray();
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<AchievementData>
    {
        public void Configure(EntityTypeBuilder<AchievementData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.Property(p => p.Name).HasMaxLength(64);
            builder.Property(p => p.Npcs).HasMaxLength(128);
            builder.Property(p => p.Requires).HasMaxLength(64);
            builder.Ignore(p => p.NpcIds);
            builder.Ignore(p => p.RequiredIds);
            builder.Ignore(p => p.WarKind);
            builder.Ignore(p => p.CompletionKind);
            builder.Ignore(p => p.NormalKind);
        }
    }
}
