using LibreKO.Quests.Binding;

namespace LibreKO.Quests.Runtime;

public interface IQuestHost
{
    int PlayerClassGroup { get; }
    int PlayerClassSubtype { get; }
    int PlayerNation { get; }
    int PlayerLevel { get; }
    int PlayerCoins { get; }
    int PlayerLoyalty { get; }
    int PremiumType { get; }
    int PlayerZone { get; }
    int MonumentNation { get; }
    bool IsClanLeader { get; }
    bool IsInClan { get; }
    int ClanRank { get; }
    int ClanGrade { get; }
    int ClanPoints { get; }
    bool IsInParty { get; }
    bool IsPartyLeader { get; }
    bool IsKing { get; }
    bool ActionFailed { get; }

    int SkillPoints(int tree);
    int ItemCount(int itemId);
    int PlayerFreeSlots { get; }
    bool HasRoomForItem(int itemId, int count);
    bool CanReceiveStacks(int count);
    int PlayerWeight { get; }
    long PlayerExperience { get; }
    int QuestStatus(int questId);
    int KillCount(int questId, int group);
    bool HasActiveKillQuest();
    bool DailyRewardAvailable(int slot);
    bool RollChance(int percent);
    bool ReachedLevel(int level, int expPercent);
    int RollDice(int max);

    void ShowDialog(DialogStyle style, int questId, DialogLine header, IReadOnlyList<DialogButton> buttons);
    void ShowQuestView(QuestView view) => ShowDialog(DialogStyle.Talk, view.Text.QuestId, view.Dialogue, view.Topics);

    void UpdateQuestView(QuestView view) { }
    void Say(IReadOnlyList<DialogLine> lines);
    void SetQuestState(int questId, int status);
    void ApplyReward(IReadOnlyList<BoundStatement.Action> actions);
    void ShowReceipt(int questId) { }
    void GiveItem(int itemId, int count, int rentalHours);
    void GivePremium(int premiumType, int days);
    void GiveClanPremium(int days);
    void GiveAchievement(int achievementId);
    void JoinTempleEvent();
    void SetPlayerLevel(int level);
    void SetDrakiRift(int stage, int subStage);
    void TakeItem(int itemId, int count);
    void GiveGold(int amount);
    void TakeGold(int amount);
    void GiveExp(int amount);
    void GiveLoyalty(int amount);
    void GiveCash(int amount);
    void ChangeJob(int classGroup, bool mastered);

    void PromoteToNovice() { }
    void TakeLoyalty(int amount);
    void RunExchange(int exchangeId);
    void RunQuestExchange(int exchangeId, int award);
    void RunCountExchange(int exchangeId, int count);
    void RunMiningExchange(int oreType);
    void RunGenieExchange(int itemId, int hours);
    void ShowLocation(QuestLocation location, int questId);
    void TeleportToZone(int zoneId, int x, int z);
    void EnterInstance(int zoneId, int set, int x, int z);
    void CastSkill(int skillId);
    void DespawnNpc();
    void SummonNpc(int npcId, int count, int x, int z);
    void PlayEffect(int effectId);
    void PlayNpcEffect(int effectId);
    void PromotePlayer();
    void PromoteClan(int rank);
    void TakeClanPoints(int amount);
    void ResetStatPoints();
    void ResetSkillPoints();
    void OpenStatSkillPanel();
    void OpenRenamePanel();
    void OpenJobChangePanel();
    void OpenClanRenamePanel();

    void Unsupported(string what);
}
