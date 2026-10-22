namespace LibreKO.Session;

public sealed class SelfSession
{
    public const byte GameMasterAuthority = 0;

    private readonly HashSet<string> openWindows = new();

    public int MyCharId { get; private set; }

    public int Nation { get; private set; }

    public string SelectedChar { get; private set; } = "";

    private MyInfo lastEnter;

    public MyInfo LastEnter
    {
        get => lastEnter;
        private set
        {
            lastEnter = value;
            Race = value.Race;
            Face = value.Face;
            Hair = value.Hair;
            Class = value.Class;
            KnightsRank = value.KnightsRank;
            PersonalRank = value.PersonalRank;
            IsKing = value.IsKing;
        }
    }

    public int Race { get; private set; }

    public int Face { get; private set; }

    public int Hair { get; private set; }

    public int Class { get; private set; }

    public int KnightsRank { get; private set; }

    public int PersonalRank { get; private set; }

    public bool IsKing { get; private set; }

    public int TransformSkill { get; private set; }

    public double StunnedUntil { get; private set; }

    internal void ExtendStun(double until)
    {
        if (until > StunnedUntil) StunnedUntil = until;
    }

    internal void ClearStun() => StunnedUntil = 0;

    public bool Stunned(double now) => now < StunnedUntil;

    public event Action? SheetChangedEvent;

    internal void ApplyRebirth(byte[] picks)
    {
        Sheet.ApplyRebirth(picks);
        SheetChangedEvent?.Invoke();
    }

    internal void ApplyRebirthReset(byte[] picks)
    {
        Sheet.ApplyRebirthReset(picks);
        SheetChangedEvent?.Invoke();
    }

    public bool IsGm => LastEnter.Authority == GameMasterAuthority;

    public CharacterSheet Sheet { get; } = new();

    public Inventory Inventory { get; } = new();

    public Vitals Vitals { get; } = new();

    public MasteryPoints Mastery { get; } = new();

    public bool HealStrikesEnemies { get; private set; }

    private readonly Dictionary<int, double> skillCooldowns = new();

    public IReadOnlyDictionary<int, double> SkillCooldowns => skillCooldowns;

    public double PotionReadyAt { get; private set; }

    private readonly Dictionary<int, double> buffEnds = new();

    public IReadOnlyDictionary<int, double> BuffEnds => buffEnds;

    internal void StartCooldown(int skillId, double readyAt) => skillCooldowns[skillId] = readyAt;

    internal bool CancelCooldown(int skillId) => skillCooldowns.Remove(skillId);

    internal void HoldPotions(double readyAt) => PotionReadyAt = readyAt;

    internal void SetBuffEnd(int skillId, double endsAt) => buffEnds[skillId] = endsAt;

    internal void ForgetBuff(int skillId) => buffEnds.Remove(skillId);

    internal void ForgetBuffs() => buffEnds.Clear();

    internal void ResetCharacterState()
    {
        skillCooldowns.Clear();
        ClearStun();
        PotionReadyAt = 0;
        buffEnds.Clear();
        HealStrikesEnemies = false;
        ClearOpenWindows();
    }

    internal void ApplyCharacterId(int charId) => MyCharId = charId;

    internal void ApplyNation(int nation) => Nation = nation;

    internal void ApplySelectedCharacter(string name) => SelectedChar = name;

    public ulong OnlineSinceMsec { get; private set; }

    internal void MarkOnline(ulong nowMsec) => OnlineSinceMsec = nowMsec;

    public bool GmCollisionsOff { get; private set; }

    internal void SetGmCollisionsOff(bool off) => GmCollisionsOff = off;

    internal void ApplyAdminState(AdminState state)
    {
        Sheet.ApplyLevel(state.Level, state.StatPoints, Sheet.Exp, Sheet.MaxExp);
        Sheet.ApplyReset(new[] { state.Str, state.Sta, state.Dex, state.Intel, state.MagicStat }, state.StatPoints, state.Ap);
        Vitals.ApplyMaxima(state.MaxHp, state.MaxMp);
        if (state.SkillPoints is { Length: >= MasteryPoints.SlotCount } points) Mastery.Seed(points);
    }

    internal void SeedState(
            int level, long exp, long maxExp,
            int hp, int maxHp, int mp, int maxMp,
            int str, int sta, int dex, int intel, int magicStat, int statPoints,
            int strBonus, int staBonus, int dexBonus, int intelBonus, int magicBonus,
            int ap, int ac, int gold, int np, int maxWeight,
            int fireR, int coldR, int lightningR, int magicR, int diseaseR, int poisonR,
            byte[] skillPoints)
    {
        Sheet.SeedStats(str, sta, dex, intel, magicStat, statPoints);
        Sheet.SeedStatBonuses(strBonus, staBonus, dexBonus, intelBonus, magicBonus);
        Sheet.SeedCombat(ap, ac);
        Sheet.SeedWealth(gold, np);
        Sheet.SeedResists(fireR, coldR, lightningR, magicR, diseaseR, poisonR);
        Sheet.SeedProgress(level, exp, maxExp);
        Sheet.SetMaxWeight(maxWeight);
        Vitals.Seed(hp, maxHp, mp, maxMp);
        Mastery.Seed(skillPoints);
    }

    internal void ChangeClass(int classCode)
    {
        var info = LastEnter;
        info.Class = classCode;
        LastEnter = info;
    }

    public event Action? ClassChanged;

    internal void ApplyClassChange(int classCode)
    {
        if (classCode == 0 || classCode == LastEnter.Class) return;
        ChangeClass(classCode);
        ClassChanged?.Invoke();
    }

    internal void ChangeLook(int face, int hair)
    {
        var info = LastEnter;
        info.Face = face;
        info.Hair = hair;
        LastEnter = info;
    }

    internal void ChangeBody(int race, int face, int hair)
    {
        var info = LastEnter;
        info.Race = race;
        info.Face = face;
        info.Hair = hair;
        LastEnter = info;
    }

    internal void ChangeOrigin(int nation, int race)
    {
        var info = LastEnter;
        info.Nation = nation;
        info.Race = race;
        LastEnter = info;
    }

    internal void ChangeCape(int capeId, int r, int g, int b)
    {
        var info = LastEnter;
        info.CapeId = capeId;
        info.CapeR = r;
        info.CapeG = g;
        info.CapeB = b;
        LastEnter = info;
    }

    internal void MoveTo(float x, float z, float y)
    {
        var info = LastEnter;
        info.X = x;
        info.Z = z;
        info.Y = y;
        LastEnter = info;
    }

    internal void EnterZone(int zone, float x, float z, float y)
    {
        var info = LastEnter;
        info.Zone = zone;
        info.X = x;
        info.Z = z;
        info.Y = y;
        LastEnter = info;
    }

    public event Action? NationRanksEvent;

    internal void ApplyNationRanks(int knightsRank, int personalRank)
    {
        if (KnightsRank == knightsRank && PersonalRank == personalRank) return;
        var info = LastEnter;
        info.KnightsRank = knightsRank;
        info.PersonalRank = personalRank;
        LastEnter = info;
        NationRanksEvent?.Invoke();
    }

    public event Action? TransformChanged;

    internal void WearTransform(int skillId)
    {
        TransformSkill = skillId;
        TransformChanged?.Invoke();
    }

    internal void EndTransform()
    {
        TransformSkill = 0;
        TransformChanged?.Invoke();
    }

    public bool IsWindowOpen(string key) => openWindows.Contains(key);

    public void SetWindowOpen(string key, bool open)
    {
        if (open) openWindows.Add(key);
        else openWindows.Remove(key);
    }

    internal void ClearOpenWindows() => openWindows.Clear();

    internal void SeedPreviewEnter(MyInfo info) => LastEnter = info;

    internal void ApplyMyInfo(MyInfo info, ItemSlot[] inventory)
    {
        LastEnter = info;
        Inventory.Reset(inventory);
    }

    public SkillLearnInfo SkillLearnOf(SkillData.Skill s)
    {
        if (SkillData.IsOtherClassSkill(s.Id, Class))
            return new SkillLearnInfo(SkillLearnState.Locked, 0, 0, false);
        if (SiegeSkills.RequiredLook(s.Level) is var look and > 0)
            return new SkillLearnInfo(look == (SkillData.Get(TransformSkill)?.TransformModelId ?? 0) ? SkillLearnState.Learned : SkillLearnState.Locked, 0, 0, false);
        int mastery = SkillData.MasteryType(s.Tree);
        return SkillLearn.For(mastery, s.Level, Mastery.InTree(mastery), Class, Sheet.Level);
    }

    public bool SkillLearned(SkillData.Skill s) => SkillLearnOf(s).State == SkillLearnState.Learned;

    public void Rename(string name)
    {
        var info = LastEnter;
        info.Name = name;
        LastEnter = info;
    }

    public void SetInventorySlot(int absSlot, ItemSlot item)
    {
        if (absSlot < 0) return;
        Inventory.Put(absSlot, item);
        RefreshGearFor(absSlot);
    }

    public void SetInventoryDurability(int absSlot, short durability)
    {
        if (absSlot < 0) return;
        Inventory.SetDurability(absSlot, durability);
        RefreshGearFor(absSlot);
    }

    internal void ApplyInventorySlot(int absSlot, ItemSlot item)
    {
        if (absSlot < 0) return;
        Inventory.ApplySlotUpdate(absSlot, item);
        RefreshGearFor(absSlot);
    }

    internal void ApplyInventoryGrid(ItemSlot[] items) => Inventory.ApplyGridRefresh(items);

    internal void ApplyItemMove(ItemMoveDirection direction, int from, int to, ushort amount)
    {
        Inventory.ApplyMove(direction, from, to, amount);
        RefreshGear();
    }

    private void RefreshGearFor(int absSlot)
    {
        if (absSlot < InventoryConstants.SlotMax || InventoryConstants.IsCospreSlot(absSlot))
            RefreshGear();
    }

    internal void ApplyDerivedStats(DerivedStats stats)
    {
        Vitals.ApplyMaxima(stats.MaxHp, stats.MaxMp);
        Sheet.ApplyDerived(stats);
        HealStrikesEnemies = stats.HealStrikesEnemies;
    }

    internal void RefreshGear()
    {
        var e = LastEnter;
        var gear = e.Gear is { Length: InventoryConstants.VisualSlotCount } existing
            ? existing
            : new int[InventoryConstants.VisualSlotCount];
        for (int i = 0; i < InventoryConstants.VisualSlots.Length; i++)
            gear[i] = Inventory.ItemIdAt(InventoryConstants.VisualSlots[i]);
        e.Gear = gear;
        LastEnter = e;
    }

    internal void PreserveDurability(int absSlot, ref ItemSlot item)
    {
        if (item.Durability != 0) return;

        if (Inventory.Holds(absSlot, item.ItemId) && Inventory[absSlot].Durability != 0)
        {
            item.Durability = Inventory[absSlot].Durability;
            return;
        }

        var def = ItemData.Get(item.ItemId);
        if (def != null && def.Duration > 1)
            item.Durability = (short)def.Duration;
    }

    public event Action<SelfHpChange>? SelfHpEvent;

    internal void ApplyHp(int hp, int maxHp, int attackerId)
    {
        var (damage, recovered) = Vitals.ApplyHp(hp, maxHp);
        SelfHpEvent?.Invoke(new SelfHpChange(hp, maxHp, damage, recovered, attackerId));
    }

    public event Action<VitalChange>? SelfMpEvent;

    internal void ApplyMp(int mp, int maxMp) => SelfMpEvent?.Invoke(new VitalChange(mp, maxMp, Vitals.ApplyMp(mp, maxMp)));

    public event Action<VitalChange>? SelfStaminaEvent;

    internal void ApplyStamina(int sp, int maxSp) => SelfStaminaEvent?.Invoke(new VitalChange(sp, maxSp, Vitals.ApplySp(sp, maxSp)));

    public event Action<float, float>? RegeneEvent;

    internal void RaiseRegene(float arg1, float arg2) => RegeneEvent?.Invoke(arg1, arg2);
}
