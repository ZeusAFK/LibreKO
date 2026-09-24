using System.Collections.Generic;
using Godot;

namespace LibreKO.Domain;

public static class SkillData
{
    private const int MasterSkillLevel = 2;
    private const int FirstMasterScroll = 379063000;
    private const int LastMasterScroll = 379066000;
    private const int MasterScrollToStoneOffset = 4000;

    public static bool IsMasterScrollSkill(int level, int useItem) =>
        level == MasterSkillLevel && useItem >= FirstMasterScroll && useItem <= LastMasterScroll;

    public static int ConsumedItemFor(int level, int useItem) =>
        IsMasterScrollSkill(level, useItem) ? useItem - MasterScrollToStoneOffset : useItem;

    public sealed class Skill
    {
        public int Id;
        public string Name = "";
        public string Desc = "";
        public int Type1;
        public int Type2;
        public int Moral;
        public int Level;
        public int Tree;
        public int Msp;
        public int Hp;
        public int Sp;
        public int ItemGroup;
        public int UseItem;
        public float Cast;
        public float Recast;
        public int Success;
        public int Range;
        public int Etc;
        public int SelfAnim1, SelfAnim2, TargetAnim;
        public int Anim1H = -1, Anim2H = -1, AnimJamadar = -1;
        public int SelfFx1Id, SelfPart1, SelfFx2Id, SelfPart2;
        public int FlyingFxId, TargetFxId, TargetPart;
        public string? SelfFx1, SelfFx2, FlyingFx, TargetFx;
        public int Before, Target;
        public string? SelfFx;
        public int Hit;
        public int HitType, HitRate, AddDamage, AddRange;
        public int NeedArrow;
        public int DirectType, FirstDamage, EndDamage, TimeDamage, Duration, Attribute, Radius, Angle;
        public int BuffType;
        public int NeedWeapon, NeedItem;
        public int CooldownGroup;
        private const int PotionItemGroup = 9;
        public Godot.Collections.Dictionary Effect = new();

        public bool IsEnemy => SkillTarget.IsHostile(Moral);
        public bool IsFriendly => SkillTarget.IsFriendly(Moral);
        public bool IsDeadFriend => SkillTarget.IsDeadFriend(Moral);
        public bool NeedsFlying => Type1 == MagicType.Ranged;

        public bool IsAreaMoral => SkillTarget.IsGroundArea(Moral);

        public bool IsCasterAreaMoral => SkillTarget.IsCasterArea(Moral);

        public bool IsArea => Radius > 0 && Type1 is MagicType.DotHeal or MagicType.Aoe or MagicType.Melee;

        public bool IsGroundArea => IsArea && IsAreaMoral && HasCastPhase;

        public bool IsCasterArea => IsArea && (IsCasterAreaMoral || (IsAreaMoral && !HasCastPhase));

        public bool IsAreaCast => IsGroundArea || IsCasterArea;

        public bool IsMelee => Type1 == MagicType.Melee || Type2 == MagicType.Melee;

        public bool IsRanged => Type1 == MagicType.Ranged || Type2 == MagicType.Ranged;

        public bool IsNonAction => SelfAnim1 < 0;

        public bool IsPotion =>
            UseItem != 0 && ItemGroup == PotionItemGroup && Moral == SkillTarget.Self
            && Type1 == MagicType.DotHeal && Cast == 0 && Recast == 0;

        public bool IsMasterScrollSkill => SkillData.IsMasterScrollSkill(Level, UseItem);

        public int ConsumedItem => SkillData.ConsumedItemFor(Level, UseItem);

        public bool HasCastPhase => Cast > 0;

        public bool RootsCaster => HasCastPhase && !IsNonAction;

        public int MoveSpeedPercent =>
            Type1 == MagicType.Buff && Effect.TryGetValue("Speed", out var v) && (int)v > 0
                ? (int)v
                : 100;

        public int AttackSpeedPercent =>
            Type1 == MagicType.Buff && Effect.TryGetValue("AttackSpeed", out var a) && (int)a > 0
                ? (int)a
                : 100;

        public int SpecialKind =>
            Type1 == MagicType.Special && Effect.TryGetValue("Type", out var v) ? (int)v : 0;

        public int NeedStone => Effect.TryGetValue("NeedStone", out var v) ? (int)v : 0;

        public int TransformModelId => Effect.TryGetValue("TransformId", out var v) ? (int)v : 0;

        public float TransformScale =>
            Effect.TryGetValue("Size", out var v) && (int)v > 0 ? (int)v / 100f : 1f;

        public bool IsResurrect => SpecialKind == SpecialMagic.Resurrect;

        public float CastSeconds => Cast / 10f;
        public float RecastSeconds => Recast / 10f;
    }

    private static readonly Dictionary<int, Skill> _skills = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        LoadSkills("res://assets/skills/skills.json");
    }

    public static Skill? Get(int id) => _skills.TryGetValue(id, out var s) ? s : null;
    public static bool IsSkill(int id) => _skills.ContainsKey(id);
    public static IEnumerable<Skill> All => _skills.Values;

    private static readonly Dictionary<int, Texture2D?> _iconCache = new();
    private static Texture2D? _enigma;
    private static bool _enigmaTried;

    public static Texture2D? EnigmaIcon()
    {
        if (_enigmaTried) return _enigma;
        _enigmaTried = true;
        const string path = "res://assets/skills/icons/enigma.png";
        _enigma = Godot.ResourceLoader.Exists(path) ? Godot.ResourceLoader.Load<Texture2D>(path) : null;
        return _enigma;
    }

    public static Texture2D? Icon(int skillId)
    {
        if (_iconCache.TryGetValue(skillId, out var cached)) return cached;
        string path = $"res://assets/skills/icons/{skillId}.png";
        Texture2D? tex = Godot.ResourceLoader.Exists(path) ? Godot.ResourceLoader.Load<Texture2D>(path) : null;
        _iconCache[skillId] = tex;
        return tex;
    }

    public static int ClassPrefix(int skillId) => skillId / 1000;

    private static readonly string[][] PageNames =
    {
        new[]{ "Basic Skill", "", "", "", "", "Attack",  "Defence",     "Passion",  "Master" },
        new[]{ "Basic Skill", "", "", "", "", "Archery", "Assassinate", "Search",   "Master" },
        new[]{ "Basic Skill", "", "", "", "", "Fire",    "Ice",         "Lightning","Master" },
        new[]{ "Basic Skill", "", "", "", "", "Heal",    "Aura",        "Spirit",   "Master" },
        new[]{ "Basic Skill", "", "", "", "", "Attack",  "Defence",     "Devil",    "Master" },
    };

    public static int MasteryType(int tree)
    {
        if (tree >= SkillPage.NationBuffFirstTree && tree < SkillPage.NationBuffEndTree) return 0;
        int digit = tree % 10;
        return MasteryPoints.IsTree(digit) ? digit : 0;
    }

    public static int PageOf(int tree)
    {
        if (tree >= SkillPage.NationBuffFirstTree && tree < SkillPage.NationBuffEndTree)
            return SkillPage.Basic;
        int digit = tree % 10;
        if (digit == SkillPage.Basic || digit == SkillPage.PassiveTree) return SkillPage.Basic;
        return MasteryPoints.IsTree(digit) ? digit : SkillPage.Hidden;
    }

    public static string PageName(int classCode, int page)
    {
        int fam = CharacterClassCatalog.Family(classCode);
        if (fam >= 1 && fam <= PageNames.Length && page >= 0 && page < PageNames[fam - 1].Length)
        {
            string name = PageNames[fam - 1][page];
            if (name.Length > 0) return name;
        }
        return page == SkillPage.Basic ? "Basic Skill" : $"Mastery {page}";
    }

    public static string WeaponRequirementName(int needWeapon) => needWeapon switch
    {
        SkillPage.DaggerWeapon => "Dagger, Jamadar",
        SkillPage.BowWeapon => "Bow",
        SkillPage.StaffWeapon => "Staff",
        SkillPage.JamadarWeapon => "Jamadar",
        _ => "",
    };

    public readonly record struct Page(int Category, string Label, List<Skill> Skills);

    public static List<Page> Pages(int classCode)
    {
        var byPage = new Dictionary<int, List<Skill>>();
        foreach (var s in ForClass(classCode))
        {
            if (s.Id >= SkillPage.UsableItemFirstId) continue;
            int page = PageOf(s.Tree);
            if (page == SkillPage.Hidden) continue;
            if (!byPage.TryGetValue(page, out var l)) byPage[page] = l = new List<Skill>();
            l.Add(s);
        }
        var pages = new List<Page>();
        foreach (int page in SkillPage.Order)
            if (byPage.TryGetValue(page, out var l))
                pages.Add(new Page(page, PageName(classCode, page), l));
        return pages;
    }

    public static List<Skill> ForClass(int classCode)
    {
        var list = new List<Skill>();
        foreach (var s in _skills.Values)
            if (ClassPrefix(s.Id) == classCode)
                list.Add(s);
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        return list;
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
            _skills[id] = new Skill
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
                NeedWeapon = Int(o, "needWeapon"),
                NeedItem = Int(o, "needItem"),
                CooldownGroup = Int(o, "cooldownGroup"),
                Effect = Dict(o, "effect"),
            };
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
    private static Godot.Collections.Dictionary Dict(Godot.Collections.Dictionary o, string k) =>
        o.ContainsKey(k) && o[k].VariantType == Variant.Type.Dictionary ? o[k].AsGodotDictionary() : new Godot.Collections.Dictionary();
}
