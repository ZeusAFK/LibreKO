using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public interface IUserSessionCharacterMapper
{
    void HydrateSession(
        UserSession session,
        Character character,
        Account account,
        Warehouse warehouse,
        short maxHp,
        short maxMp,
        IGameDataService gameData);

    void HydrateDailyOps(UserSession session, UserDailyOp dailyOp);
    void ApplyToCharacter(UserSession session, Character character);
    void ApplyToDailyOps(UserSession session, UserDailyOp dailyOp);
    void ApplyToWarehouse(UserSession session, Warehouse warehouse);
    void ApplyToAccount(UserSession session, Account account);
}

public class UserSessionCharacterMapper : IUserSessionCharacterMapper
{
    public void HydrateSession(
        UserSession session,
        Character character,
        Account account,
        Warehouse warehouse,
        short maxHp,
        short maxMp,
        IGameDataService gameData)
    {
        session.Name = character.Name;
        session.Nation = account.Nation;
        session.KnightCash = account.KnightCash;
        session.Language = account.Language;
        session.LoadVipWarehouse(account.VipWarehouseItems);
        session.VipVaultExpiry = account.VipVaultExpiry;
        session.VipPassword = account.VipPassword ?? string.Empty;
        session.SealCode = account.SealCode ?? string.Empty;
        // Password challenge fires once per login: when a PIN exists, the first
        // VIP_Open must be preceded by VIP_EnterPassword. Cleared by a successful
        // unlock for the rest of the session.
        session.VipPasswordRequest = session.VipPassword.Length == 4 ? (byte)1 : (byte)0;
        session.Race = character.Race;
        session.Class = character.Class;
        session.Level = character.Level;
        session.Face = character.Face;
        session.Hair = character.Hair;
        session.StatPoints = character.StatPoints;
        session.Money = character.Money;
        session.Experience = character.Experience;
        session.Strength = character.Strength;
        session.Stamina = character.Stamina;
        session.Dexterity = character.Dexterity;
        session.Intelligence = character.Intelligence;
        session.Magic = character.Magic;
        session.X = character.X;
        session.Y = character.Y;
        session.Z = character.Z;
        session.ZoneId = character.MapId;
        session.MaxHp = maxHp > 0 ? maxHp : (short)1;
        session.MaxMp = maxMp > 0 ? maxMp : (short)0;
        session.Hp = ResolveHydratedHp(character.Hp, session.MaxHp);
        session.DeathExpLoss = session.Hp > 0 ? 0 : character.DeathExpLoss;
        session.Mp = (short)Math.Clamp(character.Mp, 0, session.MaxMp);
        session.Loyalty = character.Loyalty;
        session.MonthlyLoyalty = character.LoyaltyMonthly;
        session.DailyLoyalty = character.LoyaltyDaily;
        session.PlayMinutes = character.PlayMinutes;
        session.MonstersDefeated = character.MonstersDefeated;
        session.PlayersDefeated = character.PlayersDefeated;
        session.Deaths = character.Deaths;
        session.SessionStartedAt = DateTime.UtcNow;
        session.KnightsId = character.KnightsId;
        session.KnightsPoints = character.KnightsPoints;
        session.Fame = character.Fame;
        session.IsMuted = character.IsMuted;

        if (character.PetItemId > 0)
        {
            session.Pet = new PetState
            {
                ItemId = character.PetItemId,
                Satisfaction = character.PetSatisfaction,
                Level = character.PetLevel == 0 ? (byte)1 : character.PetLevel,
                Exp = character.PetExp,
            };
        }

        session.GenieExpiry = character.GenieExpiry;
        session.GenieOptions = character.GenieOptions;
        session.DrakiStage = character.DrakiStage;
        session.DrakiSubStage = character.DrakiSubStage;
        session.AttendanceDays = character.AttendanceDays;
        session.AttendanceClaimedDays = character.AttendanceClaimedDays;
        session.AttendanceClaimedBonus = character.AttendanceClaimedBonus;
        session.AttendanceCheckedOn = character.AttendanceCheckedOn;

        session.RebirthLevel = character.RebirthLevel;
        session.RebStr = character.RebStr;
        session.RebSta = character.RebSta;
        session.RebDex = character.RebDex;
        session.RebIntel = character.RebIntel;
        session.RebMagic = character.RebMagic;

        session.WarehouseMoney = warehouse.Money;
        session.Quest.BindPoint = character.Bind;
        session.SkillData = character.SkillData ?? [];

        Array.Clear(session.SkillPoints);
        if (character.SkillPointData is { Length: >= 9 })
            Array.Copy(character.SkillPointData, session.SkillPoints, 9);

        UserSessionBinaryState.LoadItems(session.Inventory, character.Items);
        StarterCharacterLoadout.EnsureStarterWeapon(session.Inventory, session.Class, session.Level, session.Experience);
        UserSessionBinaryState.LoadWarehouse(session.Warehouse, warehouse.Items);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ItemExpiry.Sweep(session.Inventory, now);
        ItemExpiry.Sweep(session.Warehouse, now);
        session.LoadQuestData(character.QuestData);
        session.Achievements.Load(character.AchievementData);
        session.DisplayTitleId = character.DisplayTitleId;
        session.InvalidateTitleBonuses();
        UserSessionMagicState.LoadSavedMagic(session, character.SavedMagic, gameData);
    }

    public void ApplyToCharacter(UserSession session, Character character)
    {
        character.Hp = session.Hp;
        character.DeathExpLoss = session.Hp > 0 ? 0 : session.DeathExpLoss;
        character.Mp = session.Mp;
        character.X = session.X;
        character.Y = session.Y;
        character.Z = session.Z;
        character.MapId = session.ZoneId;
        character.Level = session.Level;
        character.Strength = session.Strength;
        character.Stamina = session.Stamina;
        character.Dexterity = session.Dexterity;
        character.Intelligence = session.Intelligence;
        character.Magic = session.Magic;
        character.StatPoints = session.StatPoints;
        character.Money = session.Money;
        character.Experience = session.Experience;
        character.Items = session.SerializeItems();
        character.SkillData = session.SkillData;
        character.SkillPointData = (byte[])session.SkillPoints.Clone();
        character.Bind = session.Quest.BindPoint;
        character.Class = session.Class;
        character.Hair = session.Hair;
        character.Face = session.Face;
        character.Loyalty = session.Loyalty;
        character.LoyaltyMonthly = session.MonthlyLoyalty;
        character.LoyaltyDaily = session.DailyLoyalty;
        character.PlayMinutes = session.AccumulatedPlayMinutes();
        character.MonstersDefeated = session.MonstersDefeated;
        character.PlayersDefeated = session.PlayersDefeated;
        character.Deaths = session.Deaths;
        character.KnightsId = session.KnightsId;
        character.KnightsPoints = session.KnightsPoints;
        character.Fame = session.KnightsId > 0 ? session.KnightsFame : session.Fame;
        character.IsMuted = session.IsMuted;

        if (session.Pet != null)
        {
            character.PetItemId = session.Pet.ItemId;
            character.PetSatisfaction = session.Pet.Satisfaction;
            character.PetLevel = session.Pet.Level;
            character.PetExp = session.Pet.Exp;
        }
        else
        {
            character.PetItemId = 0;
            character.PetSatisfaction = 0;
            character.PetLevel = 0;
            character.PetExp = 0;
        }

        character.GenieExpiry = session.GenieExpiry;
        character.GenieOptions = session.GenieOptions;
        character.DrakiStage = session.DrakiStage;
        character.DrakiSubStage = session.DrakiSubStage;
        character.AttendanceDays = session.AttendanceDays;
        character.AttendanceClaimedDays = session.AttendanceClaimedDays;
        character.AttendanceClaimedBonus = session.AttendanceClaimedBonus;
        character.AttendanceCheckedOn = session.AttendanceCheckedOn;

        character.RebirthLevel = session.RebirthLevel;
        character.RebStr = session.RebStr;
        character.RebSta = session.RebSta;
        character.RebDex = session.RebDex;
        character.RebIntel = session.RebIntel;
        character.RebMagic = session.RebMagic;

        character.QuestData = session.SerializeQuestData();
        character.AchievementData = session.Achievements.Serialize();
        character.DisplayTitleId = session.DisplayTitleId;
        character.SavedMagic = session.SerializeSavedMagic();
    }

    public void HydrateDailyOps(UserSession session, UserDailyOp dailyOp)
    {
        var stored = dailyOp.ToTimestamps();
        for (var op = 0; op < session.DailyOps.Length && op < stored.Length; op++)
            session.DailyOps[op] = stored[op];
    }

    public void ApplyToDailyOps(UserSession session, UserDailyOp dailyOp)
    {
        dailyOp.FromTimestamps(session.DailyOps);
    }

    public void ApplyToWarehouse(UserSession session, Warehouse warehouse)
    {
        warehouse.Items = session.SerializeWarehouse();
        warehouse.Money = session.WarehouseMoney;
    }

    public void ApplyToAccount(UserSession session, Account account)
    {
        // Account-scoped mutable state — propagated back on every persist tick.
        account.KnightCash = session.KnightCash;
        account.PremiumDate = session.PremiumExpiry;
        account.PremiumType = session.PremiumService;
        account.Language = session.Language;
        account.VipWarehouseItems = session.SerializeVipWarehouse();
        account.VipVaultExpiry = session.VipVaultExpiry;
        account.VipPassword = session.VipPassword;
        account.SealCode = session.SealCode;
    }

    private static short ResolveHydratedHp(int currentHp, short maxHp)
        => currentHp > 0 ? (short)Math.Min(currentHp, (int)maxHp) : (short)0;
}
