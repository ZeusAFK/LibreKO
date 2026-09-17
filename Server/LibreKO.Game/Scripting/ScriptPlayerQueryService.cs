using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0060
namespace LibreKO.Game.Scripting;

public class ScriptPlayerQueryService(
    UserSession session,
    NpcInstance? npc,
    IGameDataService gameData,
    SessionManager sessionManager,
    ILogger logger)
{
    private ClassSubtype ClassSub => (ClassSubtype)ClassIdHelper.GetSubtype(session.Class);

    public string GetName(int _uid) => session.Name;
    public int GetZoneID(int _uid) => session.ZoneId;
    public int GetPvpMonumentNation(int _uid) =>
        sessionManager.Battle.GetPvpMonumentNation((byte)session.ZoneId);
    public double GetX(int _uid) => session.X;
    public double GetY(int _uid) => session.Y;
    public double GetZ(int _uid) => session.Z;
    public int GetNation(int _uid) => (int)session.Nation;
    public int GetLevel(int _uid) => session.Level;
    public int GetClass(int _uid) => session.Class;
    public int GetClassType(int _uid) => (int)ClassSub;
    public int GetCoins(int _uid) => session.Money;
    public int GetCash(int _uid) => session.KnightCash;
    public bool CheckCash(int _uid, int amount) => session.KnightCash >= amount;
    public int NpcKillID(int _uid) => session.LastKilledNpcId;
    public int GetLoyalty(int _uid) => session.Loyalty;
    public int GetMonthlyLoyalty(int _uid) => session.MonthlyLoyalty;
    public int GetDailyLoyalty(int _uid) => session.DailyLoyalty;
    public static int GetManner(int _uid) => 0;
    public int GetRace(int _uid) => session.Race;
    public long GetExp(int _uid) => session.Experience;

    public int GetExpPercent(int _uid)
    {
        var maxExp = RebirthBonus.RequiredExperience(
            gameData.GetMaxExpForLevel(session.Level), session.RebirthLevel);
        if (maxExp <= 0) return 0;
        return (int)Math.Min(100, session.Experience * 100 / maxExp);
    }

    public bool CheckLevelExp(int _uid, int level, int expRate) =>
        session.Level >= level && GetExpPercent(_uid) >= expRate;

    public int GetStat(int _uid, int statIndex) => session.GetStat((StatType)(statIndex + 1));

    public bool IsWarrior(int _uid) => ClassIdHelper.IsWarrior(session.Class);
    public bool IsRogue(int _uid) => ClassIdHelper.IsRogue(session.Class);
    public bool IsMage(int _uid) => ClassIdHelper.IsMage(session.Class);
    public bool IsPriest(int _uid) => ClassIdHelper.IsPriest(session.Class);

    public bool CheckLevel(int _uid, int requiredLevel) => session.Level >= requiredLevel;

    public bool CheckClass(int _uid, int classId) =>
        session.Class == classId || (int)ClassSub == classId;

    public bool CheckNation(int _uid, int nation) => (int)session.Nation == nation;

    public static bool CheckPercent(int percentage) => Random.Shared.Next(100) < percentage;

    public string GetAccountName(int _uid) => session.AccountId.ToString();
    public static int GetInnCoins(int _uid) => 0;

    public bool IsKing(int _uid)
    {
        var kingData = gameData.KingSystemTable.TryGetValue((byte)session.Nation, out var data)
            ? data
            : null;
        return kingData != null && kingData.KingName == session.Name;
    }

    public static bool HasInnCoins(int _uid, int _amount) => false;
    public bool HasMonthlyLoyalty(int _uid, int amount) => session.MonthlyLoyalty >= amount;
    public static bool HasManner(int _uid, int _amount) => false;
    public int CheckWeight(int _uid) => session.Stats.ItemWeight;

    public bool IsBeginnerWarrior(int _uid) => ClassSub == ClassSubtype.WarriorBeginner;
    public bool IsBeginnerRogue(int _uid) => ClassSub == ClassSubtype.RogueBeginner;
    public bool IsBeginnerMage(int _uid) => ClassSub == ClassSubtype.MageBeginner;
    public bool IsBeginnerPriest(int _uid) => ClassSub == ClassSubtype.PriestBeginner;
    public bool IsBeginnerKurian(int _uid) => ClassSub == ClassSubtype.KurianBeginner;
    public bool IsNoviceWarrior(int _uid) => ClassSub == ClassSubtype.WarriorNovice;
    public bool IsNoviceRogue(int _uid) => ClassSub == ClassSubtype.RogueNovice;
    public bool IsNoviceMage(int _uid) => ClassSub == ClassSubtype.MageNovice;
    public bool IsNovicePriest(int _uid) => ClassSub == ClassSubtype.PriestNovice;
    public bool IsNoviceKurian(int _uid) => ClassSub == ClassSubtype.KurianNovice;
    public bool IsMasteredWarrior(int _uid) => ClassSub == ClassSubtype.WarriorMastered;
    public bool IsMasteredRogue(int _uid) => ClassSub == ClassSubtype.RogueMastered;
    public bool IsMasteredMage(int _uid) => ClassSub == ClassSubtype.MageMastered;
    public bool IsMasteredPriest(int _uid) => ClassSub == ClassSubtype.PriestMastered;
    public bool IsMasteredKurian(int _uid) => ClassSub == ClassSubtype.KurianMastered;

    public int GetUserDailyOp(int _uid, int _opType)
    {
        if (!Enum.IsDefined(typeof(DailyOperation), _opType))
            return 0;

        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var last = session.DailyOps[_opType];
        if (last > 0 && (now - last) / 60 <= GameConstants.DailyOperationWindowMinutes)
            return 0;

        session.DailyOps[_opType] = now;
        return 1;
    }

    public int GetEventTrigger(int _uid)
    {
        if (npc == null)
            return EventTriggerData.NoTrigger;

        var triggerNum = gameData.GetEventTrigger(npc.NpcType, npc.TrapNumber);
        if (triggerNum == EventTriggerData.NoTrigger)
            logger.LogWarning(
                "No EVENT_TRIGGER row for NPC {NpcId} (type {NpcType}, trap {TrapNumber}) in zone {ZoneId}",
                npc.NpcId, npc.NpcType, npc.TrapNumber, npc.ZoneId);

        return triggerNum;
    }

    public int GetPremium(int _uid) => session.PremiumType;

    public static int CheckBeefRoastVictory(int _uid) => 0;
    public static int RequestPersonalRankReward(int _uid) => 0;
    public static int RequestReward(int _uid) => 0;
    public static int CheckWarVictory(int _uid) => 0;
    public static int CheckMiddleStatueCapture(int _uid) => 0;
    public static void MoveMiddleStatue(int _uid)
    {
    }
}
#pragma warning restore IDE0060
