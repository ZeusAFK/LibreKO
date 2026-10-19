using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class QuestBookTests
{
    private const int QuestId = 1043;
    private const int OtherQuestId = 2071;
    private const int RewardItem = 389010000;

    [Fact]
    public void StateOfReadsTheEntryOrZero()
    {
        var book = new QuestBook();
        book.ApplyState(QuestId, QuestStates.ReadyToTurnIn);

        Assert.Equal(QuestStates.ReadyToTurnIn, book.StateOf(QuestId));
        Assert.Equal(0, book.StateOf(OtherQuestId));
    }

    [Fact]
    public void ApplyStateReplacesTheEntryAndDropsKillsWhenTheQuestEnds()
    {
        var book = new QuestBook();
        book.ApplyState(QuestId, QuestStates.Active);
        book.ApplyKillCounts(QuestId, new ushort[] { 3, 0, 0, 0 });

        book.ApplyState(QuestId, QuestStates.Completed);

        Assert.Single(book.Entries);
        Assert.Equal(QuestStates.Completed, book.StateOf(QuestId));
        Assert.False(book.Kills.ContainsKey(QuestId));
    }

    [Fact]
    public void ApplyKillUpdateCountsOneBasedGroupsAndIgnoresOthers()
    {
        var book = new QuestBook();

        book.ApplyKillUpdate(QuestId, 2, 7);
        book.ApplyKillUpdate(QuestId, 0, 9);
        book.ApplyKillUpdate(QuestId, QuestBook.KillGroupCount + 1, 9);

        Assert.Equal(new ushort[] { 0, 7, 0, 0 }, book.Kills[QuestId]);
    }

    [Fact]
    public void ApplyLogKeepsKillsAndForgetsViews()
    {
        var book = new QuestBook();
        book.ApplyView(View(QuestViewState.InProgress));

        book.ApplyLog(new List<QuestEntry> { new(OtherQuestId, QuestStates.Active) });

        Assert.Empty(book.Views);
        Assert.Empty(book.Objectives);
        Assert.Empty(book.Strings);
        Assert.Equal(QuestStates.Active, book.StateOf(OtherQuestId));
        Assert.Equal(0, book.StateOf(QuestId));
        Assert.True(book.Kills.ContainsKey(QuestId));
    }

    [Fact]
    public void ApplyViewStoresTheViewAndItsState()
    {
        var book = new QuestBook();

        book.ApplyView(View(QuestViewState.Claimable));

        Assert.Equal(QuestStates.ReadyToTurnIn, book.StateOf(QuestId));
        Assert.Equal("Title", book.Strings[QuestId].Title);
        Assert.Same(book.Views[QuestId].Objectives, book.Objectives[QuestId]);
        Assert.Equal(new ushort[] { 1 }, book.Kills[QuestId]);
    }

    [Fact]
    public void RebuildTargetsListsUnfinishedMonstersAndClaimableNpcsInTheZone()
    {
        const int zone = 21;
        const int npc = 13013;
        const int monster = 150;
        const int finishedMonster = 160;
        var book = new QuestBook();
        book.ApplyView(View(QuestViewState.InProgress, zone, npc,
            new QuestKillGroup(1, [finishedMonster]), new QuestKillGroup(5, [monster])) with { Counts = [1, 2] });
        book.ApplyView(View(QuestViewState.Claimable, zone, npc) with { QuestId = OtherQuestId });

        book.RebuildTargets(zone);

        Assert.Equal(new[] { monster, npc }.Order(), book.TargetNpcs.Order());
        book.RebuildTargets(zone + 1);
        Assert.Empty(book.TargetNpcs);
    }

    private static QuestView View(QuestViewState state, int zone = 0, int npc = 0, params QuestKillGroup[] groups) =>
        new(QuestId, npc, zone, false, false, false, false, state, 0, "Title", "Journal", "",
            new QuestObjectives(QuestId, false, groups), [1], [], []);

    [Fact]
    public void ClearForgetsEverything()
    {
        var book = new QuestBook();
        book.ApplyState(QuestId, QuestStates.Active);
        book.ApplyKillCounts(QuestId, new ushort[] { 3 });
        book.Track(QuestId, true);
        book.ApplyView(View(QuestViewState.Claimable, npc: OtherQuestId) with { QuestId = OtherQuestId });
        book.RebuildTargets(0);
        book.RewardSelection.Choose(QuestId, new QuestTransfer(false, 0, RewardItem, 1, 0));

        book.Clear();

        Assert.Empty(book.Entries);
        Assert.Empty(book.Kills);
        Assert.Empty(book.Tracked);
        Assert.Empty(book.TargetNpcs);
        Assert.False(book.RewardSelection.Chosen(QuestId, out _));
    }
}
