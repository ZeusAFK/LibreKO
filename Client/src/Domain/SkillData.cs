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

    public static bool FlyingStageFor(int type1, bool hasFlyingFx, bool hasCastPhase) =>
        type1 == MagicType.Ranged || (hasFlyingFx && hasCastPhase && type1 != MagicType.Melee);

    public static bool FlightHomesFor(int type1, int hitType) => type1 != MagicType.Ranged || hitType != 0;

    public static bool HasSecondaryBuffFor(int type1, int type2, int buff2Type) =>
        type2 == MagicType.Buff && type1 != MagicType.Buff && buff2Type != 0;

    public const int NoNationRole = 0;
    private const int NationRoleFirstBand = 1;
    private const int NationBuffBand = 1000;
    private const int FirstNationBuffLine = 1;
    private const int LastNationBuffLine = 5;
    private const int CommandTreeDigit = 9;
    private static readonly HashSet<int> CommandForms = new() { 20007, 20008, 31501, 31502, 31503, 31504, 31505, 31506, 31507 };

    public static bool IsGranted(int tree, int transformId, int nationRole = NoNationRole, int nationLine = 0)
    {
        if (tree >= SkillPage.NationBuffFirstTree && tree < SkillPage.NationBuffEndTree)
        {
            if (nationRole == NoNationRole) return false;
            if (nationRole == NationRoleFirstBand) return tree < SkillPage.NationBuffFirstTree + NationBuffBand;
            if (nationLine < FirstNationBuffLine || nationLine > LastNationBuffLine) return true;
            int band = SkillPage.NationBuffFirstTree + nationLine * NationBuffBand;
            return tree >= band && tree < band + NationBuffBand;
        }
        return tree % 10 != CommandTreeDigit || CommandForms.Contains(transformId);
    }

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
        public int Buff2Type, Buff2Duration;
        public int NeedWeapon, NeedItem;
        public int NeedBuffSkill;
        public int CooldownGroup;
        private const int PotionItemGroup = 9;
        public Dictionary<string, int> Effect = new();

        public bool IsEnemy => SkillTarget.IsHostile(Moral);
        public bool IsFriendly => SkillTarget.IsFriendly(Moral);
        public bool IsDeadFriend => SkillTarget.IsDeadFriend(Moral);
        public bool NeedsFlying => Type1 == MagicType.Ranged;

        public bool HasFlyingStage => FlyingStageFor(Type1, FlyingFx != null, HasCastPhase);

        public bool FlightHomes => FlightHomesFor(Type1, HitType);

        public bool IsAreaMoral => SkillTarget.IsGroundArea(Moral);

        public bool IsCasterAreaMoral => SkillTarget.IsCasterArea(Moral);

        public bool IsArea => Radius > 0 && Type1 is MagicType.DotHeal or MagicType.Aoe or MagicType.Melee or MagicType.Buff;

        public bool IsGroundArea => IsArea && IsAreaMoral && HasCastPhase;

        public bool IsCasterArea => (IsArea && (IsCasterAreaMoral || (IsAreaMoral && !HasCastPhase)))
            || (IsCasterAreaMoral && Type1 is MagicType.Buff or MagicType.Special)
            || (Moral == SkillTarget.PartyAll && Radius == 0 && Type1 is MagicType.DotHeal or MagicType.Warp);

        public bool IsAreaCast => IsGroundArea || IsCasterArea;

        public bool IsMeleeArea => IsArea && Type1 == MagicType.Melee && IsAreaMoral;

        public bool IsMelee => Type1 == MagicType.Melee || Type2 == MagicType.Melee;

        public bool IsRanged => Type1 == MagicType.Ranged || Type2 == MagicType.Ranged;

        public bool IsNonAction => SelfAnim1 < 0;

        public bool IsPotion =>
            PotionItem != 0 && ItemGroup == PotionItemGroup && Moral == SkillTarget.Self
            && (ItemData.PotionHealFor(Type1, DirectType, FirstDamage, HealTarget.Hp) > 0
                || ItemData.PotionHealFor(Type1, DirectType, FirstDamage, HealTarget.Mp) > 0);

        public int PotionItem => UseItem != 0 ? UseItem : MaestroPotions.ItemFor(DirectType);

        public bool IsMasterScrollSkill => SkillData.IsMasterScrollSkill(Level, UseItem);

        public int ConsumedItem => SkillData.ConsumedItemFor(Level, UseItem);

        public bool HasCastPhase => Cast > 0;

        public bool RootsCaster => HasCastPhase && !IsNonAction;

        public int MoveSpeedPercent =>
            Type1 == MagicType.Buff && Effect.TryGetValue("Speed", out var v) && v > 0
                ? v
                : 100;

        public int AttackSpeedPercent =>
            Type1 == MagicType.Buff && Effect.TryGetValue("AttackSpeed", out var a) && a > 0
                ? a
                : 100;

        public int SpecialKind =>
            Type1 == MagicType.Special && Effect.TryGetValue("Type", out var v) ? v : 0;

        public int StateChange =>
            Type1 == MagicType.Stealth && Effect.TryGetValue("StateChange", out var v) ? v : 0;

        public int NeedStone => Effect.TryGetValue("NeedStone", out var v) ? v : 0;

        public int TransformModelId => Effect.TryGetValue("TransformId", out var v) ? v : 0;

        public float TransformScale =>
            Effect.TryGetValue("Size", out var v) && v > 0 ? v / 100f : 1f;

        public int TransformUse => Effect.TryGetValue("UserSkillUse", out var v) ? v : 0;

        public bool SiegeForm => Effect.TryGetValue("UserSkillUse", out var v) && v == SiegeFormSkillUse;

        public bool IsResurrect => SpecialKind == SpecialMagic.Resurrect;

        public bool Revives => SpecialKind is SpecialMagic.Resurrect or SpecialMagic.ResurrectSelf or SpecialMagic.LifeCrystal;

        public int WarpKind =>
            Type1 == MagicType.Warp && Effect.TryGetValue("WarpType", out var warp) ? warp : 0;

        public bool IsBlink =>
            Type1 == MagicType.Warp && Effect.TryGetValue("WarpType", out var v) && v == WarpType.Blink;

        public float CastSeconds => Cast / 10f;
        public float RecastSeconds => Recast / 10f;
    }

    private static readonly Dictionary<int, Skill> skills = new();
    private static bool loaded;

    internal static Action? Loader { get; set; }

    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        Loader?.Invoke();
    }

    internal static void Add(Skill skill) => skills[skill.Id] = skill;

    public static Skill? Get(int id) => skills.TryGetValue(id, out var s) ? s : null;
    public static bool IsSkill(int id) => skills.ContainsKey(id);
    public static IEnumerable<Skill> All => skills.Values;

    private const int ClassSkillSpan = 1000;

    public static int ClassPrefix(int skillId) => skillId / ClassSkillSpan;

    public static int ClassSkillId(int classCode, int index) => classCode * ClassSkillSpan + index;

    public static bool IsOtherClassSkill(int skillId, int classCode) =>
        CharacterClassCatalog.IsPlayerClass(classCode)
        && CharacterClassCatalog.IsPlayerClass(ClassPrefix(skillId))
        && ClassPrefix(skillId) != classCode;

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

    private const int SiegeFormSkillUse = 0;
    private const int NationSkillSpan = 100_000;
    private const int SiegeBandOffset = 20_000;
    private const int SiegeBandSpan = 10_000;
    private const int RamLook = 30040;
    private const int LadderLook = 30060;
    public const string SiegePageName = "Siege";

    public static List<Page>? SiegeFormPages(Skill? form, int nation)
    {
        if (form is not { SiegeForm: true }) return null;
        int kind = form.TransformModelId switch
        {
            RamLook => MagicType.DotHeal,
            LadderLook => MagicType.Transform,
            _ => MagicType.None,
        };
        var list = new List<Skill>();
        int first = nation * NationSkillSpan + SiegeBandOffset;
        if (kind != MagicType.None)
            foreach (var s in skills.Values)
                if (s.Id >= first && s.Id < first + SiegeBandSpan && (s.Type1 == kind || s.Type2 == kind))
                    list.Add(s);
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        return list.Count == 0 ? new List<Page>() : new List<Page> { new(SkillPage.Basic, SiegePageName, list) };
    }

    public static List<Page> Pages(int classCode, int transformId = 0, Skill? form = null, int nation = 0)
    {
        if (SiegeFormPages(form, nation) is { } siege) return siege;
        var byPage = new Dictionary<int, List<Skill>>();
        foreach (var s in ForClass(classCode))
        {
            if (s.Id >= SkillPage.UsableItemFirstId) continue;
            if (!IsGranted(s.Tree, transformId)) continue;
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
        foreach (var s in skills.Values)
            if (ClassPrefix(s.Id) == classCode)
                list.Add(s);
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        return list;
    }
}
