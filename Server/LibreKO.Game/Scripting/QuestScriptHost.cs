using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Localization;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Scripting;

public sealed class QuestScriptHost(
    UserSession session,
    QuestScriptContext context,
    IQuestTranslations translations,
    ILogger logger,
    string fileName,
    IQuestDefinitionSource? objectives = null,
    TimeProvider? clock = null,
    Func<int, string>? monsterName = null,
    QuestProgram? notificationProgram = null) : IQuestHost
{
    private readonly List<string> _unsupported = [];
    private readonly List<(int ItemId, int Count)> _granted = [];

    public IReadOnlyList<string> UnsupportedActions => _unsupported;

    public int PlayerClassGroup => ClassIdHelper.GroupOf(session.Class);

    public int PlayerClassSubtype => ClassIdHelper.GetSubtype(session.Class);
    public int PlayerNation => (int)session.Nation;
    public int PlayerLevel => session.Level;
    public int PlayerCoins => session.Money;
    public int PlayerLoyalty => session.Loyalty;
    public int PremiumType => session.PremiumType;
    public int PlayerZone => session.ZoneId;
    public int MonumentNation => context.Player.GetPvpMonumentNation(0);
    public bool IsClanLeader => context.ClanParty.IsClanLeader(0);
    public bool IsInClan => context.ClanParty.IsInClan(0);
    public int ClanRank => context.ClanParty.CheckKnight(0);
    public int ClanGrade => context.ClanParty.CheckClanGrade(0);
    public int ClanPoints => context.ClanParty.CheckClanPoint(0);
    public bool IsInParty => context.ClanParty.IsInParty(0);
    public bool IsPartyLeader => context.ClanParty.IsPartyLeader(0);
    public bool IsKing => context.Player.IsKing(0);
    public bool ActionFailed => context.ActionFailed;

    public int ItemCount(int itemId) => context.Items.HowmuchItem(0, itemId);
    public int PlayerFreeSlots => context.Items.GetCountEmptySlot(0);
    public bool HasRoomForItem(int itemId, int count) => context.Items.IsRoomForItem(0, itemId, count) >= 0;
    public bool CanReceiveStacks(int count) => context.Items.CheckGiveSlot(0, count);
    public int PlayerWeight => context.Player.CheckWeight(0);
    public long PlayerExperience => session.Experience;
    public int QuestStatus(int questId)
    {
        if (questId is > 0 and <= short.MaxValue && objectives?.TextFor(questId)?.Daily == true
            && session.WithLock(s => s.Quest.RefreshDailyQuest((short)questId, Today)))
        {
            context.QuestStateDirty = true;
            context.QueuedPackets.Add(QuestPacketWriter.StateChange((short)questId,
                session.Quest.StatusOf((short)questId)));
        }
        return context.Quest.GetQuestStatus(0, questId);
    }

    private DateOnly Today => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);
    public int KillCount(int questId, int group) => context.Quest.CountMonsterQuestSub(0, questId, group);

    public int SkillPoints(int tree) => context.Character.CheckSkillPoint(0, tree);
    public bool HasActiveKillQuest() => context.Quest.ExistMonsterQuestSub(0) != 0;
    public bool DailyRewardAvailable(int slot) => context.Player.GetUserDailyOp(0, slot) == 1;
    public bool RollChance(int percent) => ScriptPlayerQueryService.CheckPercent(percent);
    public bool ReachedLevel(int level, int expPercent) => context.Player.CheckLevelExp(0, level, expPercent);
    public int RollDice(int max) => max <= 0 ? 0 : (int)Random.Shared.NextInt64(0, (long)max + 1);

    private string Localize(string text) => translations.Translate(session.LanguageCode, text);

    private QuestPacketWriter.TextEntry Entry(QuestText text) => new(
        (short)text.QuestId, Localize(text.Title ?? string.Empty), Localize(text.Journal ?? string.Empty));

    public void ShowDialog(DialogStyle style, int questId, DialogLine header, IReadOnlyList<DialogButton> buttons)
    {
        SetTopics(style, buttons);

        var carriesText = header.HasText || buttons.Any(button => button.Label.HasText);

        context.QueuedPackets.Add(NpcDialogPacketWriter.SelectMessage(
            session.Quest.EventNpcId,
            (byte)style,
            questId,
            header.TextId,
            [.. buttons.Select(button => button.Label.TextId)],
            UserSession.SelectMessageEventCount,
            session.Quest.ActiveQuestScript,
            carriesText ? Localize(header.Text ?? string.Empty) : null,
            carriesText ? [.. buttons.Select(button => Localize(button.Label.Text ?? string.Empty))] : null));
    }

    public void ShowQuestView(QuestView view) => SendQuestView(view, ViewNpcId(view), true);

    public void UpdateQuestView(QuestView view) => SendQuestView(view, ViewNpcId(view), false);

    private int ViewNpcId(QuestView view) =>
        notificationProgram?.Flows.Single(f => f.QuestId == view.Text.QuestId)
            .BindingFor((int)session.Nation, session.ZoneId)?.NpcId
            ?? notificationProgram?.NpcId ?? session.Quest.EventNpcId;

    public void SendQuestView(QuestView view, int npcId, bool open)
    {
        var notification = notificationProgram is not null
            && (view.State is QuestViewState.Available or QuestViewState.Claimable
                || view.AutoAccepted && view.State is (QuestViewState.InProgress or QuestViewState.Completed));
        if (notificationProgram is not null && !notification)
        {
            open = false;
            session.Quest.NotificationReplies.Remove(view.Text.QuestId);
        }
        if (open && notification)
            session.Quest.NotificationReplies[view.Text.QuestId] =
                (notificationProgram!, [.. view.Topics.Select(t => t.TargetEvent)], view.State);
        else if (open)
            SetTopics(DialogStyle.Talk, view.Topics);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var nextReset = view.Text.Daily
            ? new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero).ToUnixTimeSeconds() : 0;
        context.QueuedPackets.Add(QuestPacketWriter.View(view, npcId, open, nextReset, Localize, monsterName, notification));
    }

    private void SetTopics(DialogStyle style, IReadOnlyList<DialogButton> buttons)
    {
        session.Quest.SelectMessageFlag = (byte)style;
        session.Quest.IsScriptDialog = true;
        Array.Fill(session.Quest.SelectMessageEvents, NpcDialogPacketWriter.NoText);
        Array.Fill(session.Quest.SelectMessageRewards, -1);
        for (var index = 0; index < buttons.Count && index < session.Quest.SelectMessageEvents.Length; index++)
        {
            session.Quest.SelectMessageEvents[index] = buttons[index].TargetEvent;
            session.Quest.SelectMessageRewards[index] = buttons[index].SelectedReward;
        }

    }

    public void Say(IReadOnlyList<DialogLine> lines)
    {
        var ids = new List<int>(NpcDialogPacketWriter.NpcSayLines);
        for (var index = 0; index < NpcDialogPacketWriter.NpcSayLines; index++)
            ids.Add(index < lines.Count ? lines[index].TextId : NpcDialogPacketWriter.NoText);

        var texts = lines.Any(line => line.HasText)
            ? lines.Select(line => Localize(line.Text ?? string.Empty)).ToList()
            : null;

        context.QueuedPackets.Add(NpcDialogPacketWriter.NpcSay(ids, texts));
    }

    public void SetQuestState(int questId, int status)
    {
        var declared = objectives?.ObjectivesFor(questId);
        if (objectives?.TextFor(questId, (int)session.Nation, PlayerClassGroup) is { } named && !context.ActionFailed)
            context.QueuedPackets.Add(QuestPacketWriter.Texts([Entry(named)]));
        if (status == 1 && declared is not null && !context.ActionFailed)
            context.QueuedPackets.Add(QuestPacketWriter.Objectives(declared));
        session.WithLock(s =>
        {
            context.Quest.SetQuestState(questId, (byte)status,
                declared is { Groups.Count: > 0 });
            if (status == 2 && questId is > 0 and <= short.MaxValue && !context.ActionFailed
                && objectives?.TextFor(questId)?.Daily == true)
                session.Quest.DailyCompletionDays[(short)questId] = Today.DayNumber;
            if (status == 2 && !context.ActionFailed && objectives?.TextFor(questId)?.Repeat == true)
                context.Quest.SetQuestState(questId, 0, declared is { Groups.Count: > 0 });
        });
    }
    public void GiveItem(int itemId, int count, int rentalHours) =>
        context.Items.GiveItem(0, itemId, count, rentalHours);

    public void GivePremium(int premiumType, int days) =>
        context.Character.GivePremium(0, premiumType, days);

    public void GiveClanPremium(int days) => context.ClanParty.GiveClanPremium(0, days);

    public void GiveAchievement(int achievementId) => context.PendingAchievements.Add(achievementId);

    public void JoinTempleEvent() => context.PendingTempleEventJoin = true;

    public void SetPlayerLevel(int level) => context.PendingLevel = level;

    public void SetDrakiRift(int stage, int subStage) =>
        context.Character.SetDrakiRift(0, stage, subStage);
    public void TakeItem(int itemId, int count) => context.Items.RobItem(0, itemId, count);
    public void GiveGold(int amount) => context.Items.GoldGain(0, amount);
    public void TakeGold(int amount) => context.Items.GoldLose(0, amount);
    public void GiveExp(int amount) => context.Character.ExpChange(0, amount);
    public void GiveLoyalty(int amount) => context.Character.GiveLoyalty(0, amount);
    public void TakeLoyalty(int amount) => context.Character.RobLoyalty(0, amount);
    public void GiveCash(int amount) => context.Items.GiveCash(0, amount);

    public void ChangeJob(int classGroup, bool mastered) =>
        context.Character.JobChange(0, (byte)(mastered ? 0 : 1), (byte)JobTarget(classGroup));

    private static int JobTarget(int classGroup) => classGroup switch
    {
        QuestVocabulary.ClassGroupWarrior => (int)JobChangeTarget.Warrior,
        QuestVocabulary.ClassGroupRogue => (int)JobChangeTarget.Rogue,
        QuestVocabulary.ClassGroupMage => (int)JobChangeTarget.Mage,
        QuestVocabulary.ClassGroupPriest => (int)JobChangeTarget.Priest,
        QuestVocabulary.ClassGroupKurian => (int)JobChangeTarget.Kurian,
        _ => 0,
    };
    public void ApplyReward(IReadOnlyList<BoundStatement.Action> actions)
    {
        var take = new List<(int itemId, int count)>();
        var give = new List<(int itemId, int count, int rentalHours)>();
        var promotions = 0;
        var mastered = false;
        var experienceBefore = context.PendingExperience;
        foreach (var action in actions)
        {
            if (action.Kind is QuestActionKind.PromoteNovice or QuestActionKind.Promote)
            {
                promotions++;
                mastered = action.Kind == QuestActionKind.Promote;
                continue;
            }
            var id = action.Kind switch
            {
                QuestActionKind.GiveGold or QuestActionKind.TakeGold => InventoryConstants.ItemGold,
                QuestActionKind.GiveExperience => InventoryConstants.ItemExperience,
                QuestActionKind.GiveNationalPoints or QuestActionKind.TakeNationalPoints => InventoryConstants.ItemLadderPoint,
                _ => action.Arguments.GetInt("item")
            };
            var count = action.Arguments.GetInt("amount", action.Arguments.GetInt("count", 1));
            if (action.Kind is QuestActionKind.TakeItem or QuestActionKind.TakeGold
                or QuestActionKind.TakeNationalPoints)
                take.Add((id, count));
            else
                give.Add((id, count, QuestInterpreter.RentalHours(action.Arguments)));
        }
        if (promotions > 0
            && !(mastered ? context.Character.CanPromoteUser : context.Character.CanPromoteUserNovice))
        {
            context.FailAction("You cannot be promoted from your current class.");
            return;
        }
        if (take.Count + give.Count > 0)
            CheckExchangeResult(context.Items.ApplyScriptReward(take, give));
        _granted.Clear();
        if (context.ActionFailed)
            return;
        for (var promoted = 0; promoted < promotions && !context.ActionFailed; promoted++)
            if (mastered) PromotePlayer(); else PromoteToNovice();
        if (context.ActionFailed)
            return;
        foreach (var (itemId, count, _) in give)
            _granted.Add((itemId, itemId == InventoryConstants.ItemExperience
                ? (int)Math.Clamp(context.PendingExperience - experienceBefore, 0, int.MaxValue)
                : count));
    }

    public void RunExchange(int exchangeId) => CheckExchangeResult(context.Items.RunExchange(0, exchangeId));
    public void RunQuestExchange(int exchangeId, int award) =>
        CheckExchangeResult(context.Items.RunQuestExchange(0, exchangeId, award));
    public void RunCountExchange(int exchangeId, int count) =>
        CheckExchangeResult(context.Items.RunCountExchange(0, exchangeId, count));
    public void RunMiningExchange(int oreType) =>
        CheckExchangeResult(context.Items.RunMiningExchange(0, oreType));
    public void RunGenieExchange(int itemId, int hours) =>
        CheckExchangeResult(context.Items.GenieExchange(0, itemId, hours));

    private void CheckExchangeResult(bool succeeded)
    {
        if (!succeeded)
            context.FailAction("The exchange could not be completed.");
    }
    public void ShowLocation(QuestLocation location, int questId) =>
        context.QueuedPackets.Add(QuestPacketWriter.TargetDetail(
            location, session.ZoneId, (int)session.Nation, Math.Max(questId, 0),
            objectives?.TextFor(questId, (int)session.Nation, PlayerClassGroup)?.Title ?? string.Empty, Localize));

    public void ShowReceipt(int questId)
    {
        if (_granted.Count > 0)
            context.QueuedPackets.Add(QuestPacketWriter.RewardReceipt(questId, _granted));
        _granted.Clear();
    }
    public void TeleportToZone(int zoneId, int x, int z) => context.Dialog.ZoneChange(zoneId, x, z);
    public void CastSkill(int skillId)
    {
        if (!context.Character.CastSkill(0, skillId))
            context.FailAction("The skill could not be cast.");
    }
    public void DespawnNpc() => context.RequestNpcDespawn();
    public void SummonNpc(int npcId, int count, int x, int z) => context.RequestSummon(npcId, count, x, z);

    public void PlayEffect(int effectId) => context.Dialog.ShowEffect(effectId);
    public void PlayNpcEffect(int effectId) => context.Dialog.ShowNpcEffect(effectId);
    public void PromotePlayer() => context.Character.PromoteUser(0);
    public void PromoteToNovice() => context.Character.PromoteUserNovice(0);
    public void PromoteClan(int rank) => context.ClanParty.PromoteKnight(0, rank);
    public void TakeClanPoints(int amount) => context.ClanParty.RobClanPoint(0, amount);
    public void ResetStatPoints() => context.Character.ResetStatPoints(0);
    public void ResetSkillPoints() => context.Character.ResetSkillPoints(0);
    public void OpenStatSkillPanel() => context.Dialog.SendStatSkillDistribute();
    public void OpenRenamePanel() => context.Dialog.SendNameChange();
    public void OpenJobChangePanel() => context.Dialog.SendJobChangePanel();
    public void OpenClanRenamePanel() => context.Dialog.SendClanNameChange();

    public void Unsupported(string what)
    {
        _unsupported.Add(what);
        logger.LogWarning("Quest script {File}: {What}", fileName, what);
    }
}
