using Godot;

namespace LibreKO.Content;

internal static class SkillDataLoader
{
    internal static void Load()
    {
        LoadSkills("res://assets/skills/skills.json");
    }

    private static void LoadSkills(string path)
    {
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushWarning($"[skills] missing {path}"); return; }
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var dict = parsed.AsGodotDictionary();
        foreach (var key in dict.Keys)
        {
            if (!int.TryParse(key.AsString(), out int id)) continue;
            var o = dict[key].AsGodotDictionary();
            SkillData.Add(new SkillData.Skill
            {
                Id = id,
                Name = Str(o, "name"),
                Desc = Str(o, "desc"),
                Type1 = Int(o, "type1"),
                Type2 = Int(o, "type2"),
                Moral = Int(o, "moral"),
                Level = Int(o, "level"),
                Tree = Int(o, "tree"),
                Msp = Int(o, "msp"),
                Hp = Int(o, "hp"),
                Sp = Int(o, "sp"),
                ItemGroup = Int(o, "itemGroup"),
                UseItem = Int(o, "useItem"),
                Cast = (float)Num(o, "cast"),
                Recast = (float)Num(o, "recast"),
                Success = Int(o, "success"),
                Range = Int(o, "range"),
                Etc = Int(o, "etc"),
                Before = Int(o, "before"),
                Target = Int(o, "target"),
                SelfFx = NullStr(o, "selfFx"),
                SelfAnim1 = Int(o, "selfAnim1"),
                SelfAnim2 = Int(o, "selfAnim2"),
                TargetAnim = Int(o, "targetAnim"),
                Anim1H = Int(o, "anim1H", -1),
                Anim2H = Int(o, "anim2H", -1),
                AnimJamadar = Int(o, "animJamadar", -1),
                SelfFx1Id = Int(o, "selfFx1Id"),
                SelfFx1 = NullStr(o, "selfFx1"),
                SelfPart1 = Int(o, "selfPart1"),
                SelfFx2Id = Int(o, "selfFx2Id"),
                SelfFx2 = NullStr(o, "selfFx2"),
                SelfPart2 = Int(o, "selfPart2"),
                FlyingFxId = Int(o, "flyingFxId"),
                FlyingFx = NullStr(o, "flyingFx"),
                TargetFxId = Int(o, "targetFxId"),
                TargetFx = NullStr(o, "targetFx"),
                TargetPart = Int(o, "targetPart"),
                Hit = Int(o, "hit"),
                HitType = Int(o, "hitType"),
                HitRate = Int(o, "hitRate"),
                AddDamage = Int(o, "addDamage"),
                AddRange = Int(o, "addRange"),
                NeedArrow = Int(o, "needArrow"),
                DirectType = Int(o, "directType"),
                FirstDamage = Int(o, "firstDamage"),
                EndDamage = Int(o, "endDamage"),
                TimeDamage = Int(o, "timeDamage"),
                Duration = Int(o, "duration"),
                Attribute = Int(o, "attribute"),
                Radius = Int(o, "radius"),
                Angle = Int(o, "angle"),
                BuffType = Int(o, "buffType"),
                Buff2Type = Int(o, "buff2Type"),
                Buff2Duration = Int(o, "buff2Duration"),
                NeedWeapon = Int(o, "needWeapon"),
                NeedBuffSkill = Int(o, "needBuffSkill"),
                NeedItem = Int(o, "needItem"),
                CooldownGroup = Int(o, "cooldownGroup"),
                Effect = Effects(o, "effect"),
            });
        }
    }

    private static string Str(Godot.Collections.Dictionary o, string k) =>
        o.ContainsKey(k) ? o[k].AsString() : "";
    private static string? NullStr(Godot.Collections.Dictionary o, string k) =>
        o.ContainsKey(k) && o[k].VariantType == Variant.Type.String ? o[k].AsString() : null;
    private static int Int(Godot.Collections.Dictionary o, string k, int fallback = 0) =>
        o.ContainsKey(k) && o[k].VariantType != Variant.Type.Nil ? o[k].AsInt32() : fallback;
    private static double Num(Godot.Collections.Dictionary o, string k) =>
        o.ContainsKey(k) && o[k].VariantType != Variant.Type.Nil ? o[k].AsDouble() : 0.0;
    private static Dictionary<string, int> Effects(Godot.Collections.Dictionary o, string k)
    {
        var effects = new Dictionary<string, int>();
        if (!o.ContainsKey(k) || o[k].VariantType != Variant.Type.Dictionary) return effects;
        foreach (var pair in o[k].AsGodotDictionary()) effects[pair.Key.AsString()] = pair.Value.AsInt32();
        return effects;
    }
}
