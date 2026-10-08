using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class QuestRewardSelectionTests
{
    private const int QuestId = 1755;
    private const int OtherQuestId = 1756;
    private const int SwordId = 120050000;
    private const int ShieldId = 120060000;
    private const int RewardCount = 1;

    private static readonly QuestTransfer Sword = new(false, 0, SwordId, RewardCount, 0);
    private static readonly QuestTransfer Shield = new(false, 0, ShieldId, RewardCount, 0);
    private static readonly QuestTransfer[] Options = [Sword, Shield];
    private static readonly QuestReceipt Receipt = new(QuestId, [new QuestReceiptEntry(ShieldId, RewardCount)]);

    private static QuestRewardSelection<QuestTransfer, QuestReceipt> Chosen(QuestTransfer option)
    {
        var selection = new QuestRewardSelection<QuestTransfer, QuestReceipt>();
        selection.Choose(QuestId, option);
        return selection;
    }

    [Fact]
    public void NothingIsChosenUntilThePlayerPicks() =>
        Assert.Equal(-1, new QuestRewardSelection<QuestTransfer, QuestReceipt>().ChoiceIndex(QuestId, Options));

    [Fact]
    public void AChoiceSurvivesARefreshOfTheClaimableQuest()
    {
        var selection = Chosen(Shield);
        selection.Viewed(QuestId, true, false, [new QuestTransfer(false, 0, SwordId, RewardCount, 0), Shield]);
        Assert.Equal(1, selection.ChoiceIndex(QuestId, Options));
    }

    [Fact]
    public void AChoiceFollowsTheOptionWhenTheOrderChanges() =>
        Assert.Equal(0, Chosen(Shield).ChoiceIndex(QuestId, [Shield, Sword]));

    [Fact]
    public void AChoiceIsDroppedWhenTheOptionIsNoLongerOffered()
    {
        var selection = Chosen(Shield);
        selection.Viewed(QuestId, true, false, [Sword]);
        Assert.False(selection.Chosen(QuestId, out _));
    }

    [Fact]
    public void AChoiceIsDroppedWhenTheQuestCanNoLongerBeTurnedIn()
    {
        var selection = Chosen(Shield);
        selection.Viewed(QuestId, false, false, Options);
        Assert.False(selection.Chosen(QuestId, out _));
    }

    [Fact]
    public void AChoiceBelongsToItsQuest()
    {
        var selection = Chosen(Shield);
        selection.Viewed(OtherQuestId, false, false, Options);
        Assert.Equal(-1, selection.ChoiceIndex(OtherQuestId, Options));
        Assert.Equal(1, selection.ChoiceIndex(QuestId, Options));
    }

    [Fact]
    public void AReceiptReplacesThePendingChoice()
    {
        var selection = Chosen(Shield);
        selection.Receive(QuestId, Receipt);
        Assert.False(selection.Chosen(QuestId, out _));
        Assert.True(selection.Received(QuestId, out var receipt));
        Assert.Same(Receipt, receipt);
    }

    [Fact]
    public void AReceiptSurvivesARefreshOfTheCompletedQuest()
    {
        var selection = new QuestRewardSelection<QuestTransfer, QuestReceipt>();
        selection.Receive(QuestId, Receipt);
        selection.Viewed(QuestId, false, true, Options);
        Assert.True(selection.Received(QuestId, out _));
    }

    [Fact]
    public void AReceiptIsForgottenWhenTheQuestStartsAgain()
    {
        var selection = new QuestRewardSelection<QuestTransfer, QuestReceipt>();
        selection.Receive(QuestId, Receipt);
        selection.Viewed(QuestId, true, false, Options);
        Assert.False(selection.Received(QuestId, out _));
    }
}
