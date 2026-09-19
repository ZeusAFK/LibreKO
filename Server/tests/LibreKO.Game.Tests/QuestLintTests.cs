using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Catalog;
using LibreKO.Quests.Text;

namespace LibreKO.Game.Tests;

public class QuestLintTests
{
    private sealed class Stacking(params long[] stacking) : IQuestCatalog
    {
        public bool KnowsItems => true;
        public bool KnowsExchanges => false;
        public bool KnowsQuests => false;
        public bool KnowsNpcs => false;
        public bool KnowsZones => false;
        public bool KnowsText => false;

        public bool ItemStacks(long itemId) => stacking.Contains(itemId);
        public string? ItemName(long itemId) => itemId.ToString();
        public string? ExchangeSummary(long exchangeId) => null;
        public string? QuestSummary(long questId) => null;
        public string? QuestTitle(long questId) => null;
        public IReadOnlyCollection<long> NpcsForQuest(long questId) => [];
        public string? EventTriggerSummary(long npcId, long eventId) => null;
        public string? NpcName(long npcId) => null;
        public string? ZoneName(long zoneId) => null;
        public string? MapName(long mapId) => null;
        public string? TalkText(long textId) => null;
        public string? MenuText(long textId) => null;
    }

    private static QuestCompilation Compile(string body, IQuestCatalog? catalog = null) =>
        QuestCompilation.Create($"""
            Bind Npc 100
            Quest 61 "Lint"

            On fulfil
            {body}
            """, "lint.quest", catalog);

    [Theory]
    [InlineData("    If player lacks 123 or player lacks 123\n        Give 1 coins")]
    [InlineData("    If player has 123 and player has 123\n        Give 1 coins")]
    public void AskingTheSameThingOnBothSidesIsWorthSaying(string body)
    {
        Compile(body).Diagnostics.Should().Contain(d => d.Id == DiagnosticId.RepeatedCondition);
    }

    [Fact]
    public void TwoDifferentOperandsAreNotFlagged()
    {
        Compile("    If player lacks 123 or player lacks 456\n        Give 1 coins")
            .Diagnostics.Should().NotContain(d => d.Id == DiagnosticId.RepeatedCondition);
    }

    [Fact]
    public void TwoTransactionsOverTheSameItemCanHalfApply()
    {
        Compile("""
                    Trade 1 of 123 for 1 of 456
                    Trade 1 of 456 for 1 of 789
            """).Diagnostics.Should().Contain(d => d.Id == DiagnosticId.SplitTransaction);
    }

    [Fact]
    public void TwoTransactionsOverDifferentItemsAreNotFlagged()
    {
        Compile("""
                    Trade 1 of 123 for 1 of 456
                    Trade 1 of 789 for 1 of 111
            """).Diagnostics.Should().NotContain(d => d.Id == DiagnosticId.SplitTransaction);
    }

    [Fact]
    public void OneTransactionIsNotFlagged()
    {
        Compile("    Trade 1 of 123 for 1 of 456")
            .Diagnostics.Should().NotContain(d => d.Id == DiagnosticId.SplitTransaction);
    }

    [Fact]
    public void SuccessDialogueBeforeTheTransactionIsWorthSaying()
    {
        Compile("""
                    Say "Here you go!"
                    Topic "Thanks" goto close
                    Trade 1 of 123 for 1 of 456
            """).Diagnostics.Should().Contain(d => d.Id == DiagnosticId.RewardBeforeItIsPaid);
    }

    [Fact]
    public void DialogueInsideTheBlockIsNotFlagged()
    {
        Compile("""
                    Transaction
                        Take 1 of 123
                        Give 1 of 456
                        Say "Here you go!"
                        Topic "Thanks" goto close
            """).Diagnostics.Should().NotContain(d => d.Id == DiagnosticId.RewardBeforeItIsPaid);
    }

    [Fact]
    public void RepeatingATransferIsWorthSaying()
    {
        Compile("""
                    Transaction
                        Take 1 of 123
                        Give 1 of 456
                        Give 1 of 456
            """).Diagnostics.Should().Contain(d => d.Id == DiagnosticId.RepeatedTransfer);
    }

    [Fact]
    public void HandingOverTwoOfSomethingThatDoesNotStackIsNotFlagged()
    {
        Compile("""
                    Transaction
                        Take 1 of 123
                        Give 1 of 456
                        Give 1 of 456
            """, new Stacking(123)).Diagnostics.Should().NotContain(d => d.Id == DiagnosticId.RepeatedTransfer);
    }

    [Fact]
    public void RepeatingAStackableTransferIsStillWorthSaying()
    {
        Compile("""
                    Transaction
                        Take 1 of 123
                        Give 1 of 456
                        Give 1 of 456
            """, new Stacking(123, 456)).Diagnostics.Should().Contain(d => d.Id == DiagnosticId.RepeatedTransfer);
    }

    [Fact]
    public void DifferentAmountsOfTheSameItemAreNotFlagged()
    {
        Compile("""
                    Transaction
                        Take 1 of 123
                        Give 1 of 456
                        Give 2 of 456
            """).Diagnostics.Should().NotContain(d => d.Id == DiagnosticId.RepeatedTransfer);
    }
}
