namespace LibreKO.Domain;

public static class SkillText
{
    public const int BuffTooltipWidth = 44;

    public static string BuffTooltip(SkillData.Skill s) => $"{s.Name}\n{TextWrap.Wrap(s.Desc, BuffTooltipWidth)}";

    public static string Requirement(SkillData.Skill s)
    {
        int mastery = SkillData.MasteryType(s.Tree);
        return mastery > 0
            ? $"Requires {SkillData.PageName(SkillData.ClassPrefix(s.Id), mastery)} mastery {s.Level}"
            : $"Requires level {s.Level}";
    }

    public static string Tooltip(SkillData.Skill s)
    {
        var lines = new List<string> { s.Name };
        if (!string.IsNullOrWhiteSpace(s.Desc))
            lines.Add(s.Desc);

        if (s.Level > 0)
            lines.Add(Requirement(s));

        var cost = new List<string>();
        if (s.Msp > 0) cost.Add($"MP {s.Msp}");
        if (s.Hp > 0) cost.Add($"HP {s.Hp}");
        if (s.Sp > 0) cost.Add($"SP {s.Sp}");
        if (s.NeedArrow > 0) cost.Add($"Arrows {s.NeedArrow}");
        if (cost.Count > 0) lines.Add(string.Join("  ", cost));

        if (s.IsResurrect && s.NeedStone > 0 && s.UseItem != 0)
            lines.Add($"Costs the target {s.NeedStone} × {ItemData.DisplayName(s.UseItem)}");
        if (s.IsMasterScrollSkill)
            lines.Add($"Consumes 1 × {ItemData.DisplayName(s.ConsumedItem)}");

        var timing = new List<string>();
        if (s.Cast > 0) timing.Add($"Cast {s.CastSeconds:0.#}s");
        if (s.Recast > 0) timing.Add($"Cooldown {s.RecastSeconds:0.#}s");
        if (s.Range > 0) timing.Add($"Range {s.Range}");
        if (timing.Count > 0) lines.Add(string.Join("  ", timing));

        if (s.Hit > 0) lines.Add($"Damage {s.Hit}%");
        else if (s.Type1 == MagicType.Ranged && s.AddDamage > 0) lines.Add($"Damage {s.AddDamage}%");
        else if (s.AddDamage is > 0 and not 100) lines.Add($"Add damage {s.AddDamage}");
        if (s.FirstDamage != 0) lines.Add($"{(s.FirstDamage < 0 ? "Damage" : "Heal")} {System.Math.Abs(s.FirstDamage)}");
        if (s.TimeDamage != 0 || s.Duration > 0)
            lines.Add($"Over time {System.Math.Abs(s.TimeDamage)} / {s.Duration}s");
        if (s.Radius > 0) lines.Add($"Radius {s.Radius}");
        if (s.Angle > 0) lines.Add($"Angle {s.Angle}");
        if (s.Attribute > 0) lines.Add($"Attribute {s.Attribute}");
        return string.Join("\n", lines);
    }
}
