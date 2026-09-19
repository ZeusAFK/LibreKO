using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Syntax;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestHighlightTests
{
    private const string Sample = """
        Bind Npc 16079

        Quest 512 needs any
            Kill 10 of 700

        On greeting
            If player class subtype == 6
                Do nothing
            If player in zone 21 and monument is Karus
                Change job to Warrior unmastered
                Give 500 cash
                Exchange 317 x 2
                By roll of 2
                    0, 1
                        Give 1 coins
                    2
                        Give 2 coins
            Despawn npc
        """;

    private static QuestTokenKind KindOf(string word)
    {
        var compilation = QuestCompilation.Create(Sample, "highlight.quest");
        var source = compilation.Source;
        var span = QuestClassifier.Classify(compilation)
            .First(s => source.Content.Substring(s.Start, s.Length) == word);
        return span.Kind;
    }

    [Theory]
    [InlineData("zone", QuestTokenKind.Reader)]
    [InlineData("subtype", QuestTokenKind.Reader)]
    [InlineData("monument", QuestTokenKind.Reader)]
    [InlineData("job", QuestTokenKind.Reader)]
    [InlineData("unmastered", QuestTokenKind.Reader)]
    [InlineData("cash", QuestTokenKind.Reader)]
    [InlineData("x", QuestTokenKind.Reader)]
    [InlineData("roll", QuestTokenKind.Reader)]
    [InlineData("needs", QuestTokenKind.Reader)]
    [InlineData("any", QuestTokenKind.Reader)]
    [InlineData("Change", QuestTokenKind.Effect)]
    [InlineData("Kill", QuestTokenKind.Effect)]
    [InlineData("Despawn", QuestTokenKind.Effect)]
    [InlineData("Karus", QuestTokenKind.Domain)]
    [InlineData("Quest", QuestTokenKind.Declaration)]
    public void EveryWordTheEditorColoursHasAKind(string word, QuestTokenKind expected) =>
        KindOf(word).Should().Be(expected,
            "the TextMate grammar in the LibreKO-vscode extension colours it from this kind");
}
