using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using LibreKO.Game.Protocol.Writers;

#pragma warning disable IDE0060
namespace LibreKO.Game.Scripting;

public class ScriptCharacterService(
    UserSession session,
    NpcInstance? npc,
    IGameDataService gameData,
    List<Packet> queuedPackets,
    int expMultiplier,
    QuestScriptContext context)
{
    private static readonly Dictionary<ClassSubtype, ClassSubtype> NoviceOfBeginner = new()
    {
        [ClassSubtype.WarriorBeginner] = ClassSubtype.WarriorNovice,
        [ClassSubtype.RogueBeginner] = ClassSubtype.RogueNovice,
        [ClassSubtype.MageBeginner] = ClassSubtype.MageNovice,
        [ClassSubtype.PriestBeginner] = ClassSubtype.PriestNovice,
        [ClassSubtype.KurianBeginner] = ClassSubtype.KurianNovice,
    };

    private static readonly Dictionary<ClassSubtype, ClassSubtype> MasteredOfNovice = new()
    {
        [ClassSubtype.WarriorNovice] = ClassSubtype.WarriorMastered,
        [ClassSubtype.RogueNovice] = ClassSubtype.RogueMastered,
        [ClassSubtype.MageNovice] = ClassSubtype.MageMastered,
        [ClassSubtype.PriestNovice] = ClassSubtype.PriestMastered,
        [ClassSubtype.KurianNovice] = ClassSubtype.KurianMastered,
    };

    private short ClassIdFor(ClassSubtype subtype) => (short)(((int)session.Nation * 100) + (int)subtype);

    private ClassSubtype CurrentSubtype => (ClassSubtype)(session.Class % 100);

    public void ExpChange(int _uid, int amount)
    {
        if (amount == 0)
            return;

        context.AddExperience(amount > 0 ? (long)amount * Math.Max(1, expMultiplier) : amount);
    }

    public void GiveLoyalty(int _uid, int amount)
    {
        session.Loyalty += amount;
        session.MonthlyLoyalty += amount;
        QueueLoyaltyUpdate();
    }

    public void RobLoyalty(int _uid, int amount)
    {
        session.Loyalty = Math.Max(0, session.Loyalty - amount);
        QueueLoyaltyUpdate();
    }

    public bool GivePremium(int _uid, int premiumType, int days)
    {
        if (premiumType <= 0 || days <= 0)
            return false;

        var now = DateTime.UtcNow;
        var standing = session.PremiumService == premiumType && session.PremiumExpiry > now
            ? session.PremiumExpiry!.Value
            : now;

        session.PremiumService = (byte)premiumType;
        session.PremiumExpiry = standing.AddDays(days);
        session.AccountStatus = UserSession.AccountStatusPremium;
        queuedPackets.Add(MiscPacketWriter.Premium(
            session.AccountStatus, session.PremiumType, session.PremiumTime));
        return true;
    }

    public const int DrakiRiftSeconds = 300;

    public bool SetDrakiRift(int _uid, int stage, int subStage)
    {
        if (stage is < UserSession.DrakiStageMin or > UserSession.DrakiStageMax
            || subStage is < UserSession.DrakiSubStageMin or > UserSession.DrakiSubStageMax)
            return false;

        session.DrakiStage = (byte)stage;
        session.DrakiSubStage = (byte)subStage;
        queuedPackets.Add(EventPacketWriter.DrakiTimer(
            (ushort)stage, (ushort)subStage, DrakiRiftSeconds, 0));
        return true;
    }

    public static void ChangeManner(int _uid, int _amount)
    {
    }

    public int CheckStatPoint(int _uid) => session.StatPoints;

    public void ResetStatPoints(int _uid)
    {
        session.StatPoints += (short)(session.Strength + session.Stamina
            + session.Dexterity + session.Intelligence + session.Magic - 300);
        session.Strength = 60;
        session.Stamina = 60;
        session.Dexterity = 60;
        session.Intelligence = 60;
        session.Magic = 60;
    }

    public int CheckSkillPoint(int _uid, int category) =>
        category >= 0 && category < session.SkillPoints.Length ? session.SkillPoints[category] : 0;

    public void ResetSkillPoints(int _uid)
    {
        var total = 0;
        for (var index = 5; index < 9; index++)
        {
            total += session.SkillPoints[index];
            session.SkillPoints[index] = 0;
        }

        session.SkillPoints[0] += (byte)total;
    }

    public bool PromoteUser(int _uid)
    {
        if (!MasteredOfNovice.TryGetValue(CurrentSubtype, out var mastered))
        {
            context.FailAction("You cannot be promoted from your current class.");
            return false;
        }

        ApplyPromotion(ClassIdFor(mastered));
        return true;
    }

    public bool JobChange(int _uid, byte changeType, byte newJob)
    {
        var token = JobChangeRules.FindChangeToken(session, changeType);
        if (token == 0)
        {
            context.FailAction("You need a job change token to do that.");
            return false;
        }

        if (JobChangeRules.HasEquippedItems(session))
        {
            context.FailAction("Take off everything you are wearing first.");
            return false;
        }

        var resolved = JobChangeRules.Resolve(
            session.Class, session.Race, (byte)session.Nation, newJob, changeType);
        if (resolved is null)
        {
            context.FailAction("You cannot change to that class.");
            return false;
        }

        if (!ConsumeToken(token))
        {
            context.FailAction("You need a job change token to do that.");
            return false;
        }

        session.Race = resolved.Value.NewRace;
        ApplyPromotion(resolved.Value.NewClass);
        return true;
    }

    private bool ConsumeToken(int itemId)
    {
        for (var i = InventoryConstants.SlotMax; i < InventoryConstants.SlotMax + InventoryConstants.HaveMax; i++)
        {
            var slot = session.Inventory[i];
            if (slot.ItemId != itemId || slot.Count == 0)
                continue;

            slot.Count--;
            if (slot.Count == 0)
                slot.Clear();

            queuedPackets.Add(new ItemCountChangePacketWriter()
                .Add((byte)(i - InventoryConstants.InventoryStart), itemId, slot.Count, slot.Durability)
                .Build());
            return true;
        }
        return false;
    }

    public bool OpenJobChangePanel(int _uid)
    {
        queuedPackets.Add(ClassChangePacketWriter.OpenJobChangePanel());
        return true;
    }

    public bool CanPromoteUserNovice => NoviceOfBeginner.ContainsKey(CurrentSubtype);

    public bool CanPromoteUser => MasteredOfNovice.ContainsKey(CurrentSubtype);

    public bool PromoteUserNovice(int _uid)
    {
        if (!NoviceOfBeginner.TryGetValue(CurrentSubtype, out var novice))
        {
            context.FailAction("You cannot be promoted from your current class.");
            return false;
        }

        ApplyPromotion(ClassIdFor(novice));
        return true;
    }

    private void ApplyPromotion(short newClass)
    {
        queuedPackets.Add(ClassChangePacketWriter.Promotion(newClass, session.CharacterId));

        session.Class = newClass;
        context.ClassChanged = true;

        var coefficient = gameData.GetCoefficient(newClass);
        if (coefficient != null)
        {
            session.RecalculateStats(coefficient, gameData);
            session.Hp = (short)Math.Min(session.Hp, session.MaxHp);
            session.Mp = (short)Math.Min(session.Mp, session.MaxMp);
        }
    }

    public static void KissUser(int _uid)
    {
    }

    public bool CastSkill(int _uid, int skillId)
    {
        var magic = gameData.GetMagic(skillId);
        if (magic == null || !MagicTypeLookup.TryResolve(gameData.MagicType4Table, magic, skillId, out var type4Data))
            return false;

        var buff = new ActiveBuff
        {
            MagicId = skillId,
            CasterId = npc?.UniqueId ?? 0,
            Duration = type4Data.Duration,
            ExpireTicks = DateTime.UtcNow.AddSeconds(type4Data.Duration).Ticks,
            BonusAc = type4Data.Ac,
            BonusAcPct = type4Data.AcPct,
            BonusAttack = type4Data.Attack,
            BonusMagicAttack = type4Data.MagicAttack,
            BonusMaxHp = type4Data.MaxHP,
            BonusMaxHpPct = type4Data.MaxHPPct,
            BonusMaxMp = type4Data.MaxMP,
            BonusMaxMpPct = type4Data.MaxMPPct,
            BonusHitRate = type4Data.HitRate,
            BonusAvoidRate = type4Data.AvoidRate,
            BonusStr = type4Data.Str,
            BonusSta = type4Data.Sta,
            BonusDex = type4Data.Dex,
            BonusIntel = type4Data.Intel,
            BonusCha = type4Data.Cha,
            BonusFireR = type4Data.FireR,
            BonusColdR = type4Data.ColdR,
            BonusLightningR = type4Data.LightningR,
            BonusMagicR = type4Data.MagicR,
            BonusPoisonR = type4Data.PoisonR,
            BonusDiseaseR = type4Data.DiseaseR,
            BonusSpeed = type4Data.Speed,
            BonusAttackSpeed = type4Data.AttackSpeed
        };

        session.ActiveBuffs[skillId] = buff;
        if (buff.HasStatModifiers)
            session.RecalculateStatsWithBuffs(gameData);
        return true;
    }

    private void QueueLoyaltyUpdate()
    {
        var packet = LoyaltyChangePacketWriter.Totals(session.Loyalty, session.MonthlyLoyalty);
        queuedPackets.Add(packet);
    }
}
#pragma warning restore IDE0060
