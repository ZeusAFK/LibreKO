using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestDialogGateTests
{
    private static QuestCompilation Compile(string text) =>
        QuestCompilation.Create(text, "gates.quest");

    private static BoundStatement.Dialog OnlyDialog(QuestCompilation compilation) =>
        compilation.Program.Events.Values
            .Single(e => e.Name == "greeting")
            .Body.OfType<BoundStatement.Dialog>()
            .Single();

    [Fact]
    public void AButtonNestedTwoGatesDeepStillBelongsToTheDialog()
    {
        var compilation = Compile("""
            Bind Npc 16079

            On greeting
                Say "Have you got them?"
                If player has > 6 of 810369000
                    Topic "All six" goto close
                Else
                    If player has > 3 of 810369000
                        Topic "Only three" goto close
                    Else
                        Topic "None yet" goto close
            """);

        compilation.Diagnostics
            .Should().NotContain(d => d.Id == DiagnosticId.ButtonWithoutMessage);
        OnlyDialog(compilation).Choices.Should().HaveCount(3);
    }

    [Fact]
    public void ATextlessOfferKeepsItsButtonsButAnEmptyLabelIsAnError()
    {
        var source = "Bind Npc 16079\nOn greeting\n    Say \"\" offer\n    Topic \"Accept\" goto close\n";
        var compilation = Compile(source);
        compilation.Succeeded.Should().BeTrue();
        OnlyDialog(compilation).Style.Should().Be(LibreKO.Quests.Binding.DialogStyle.QuestOffer);
        OnlyDialog(compilation).Choices.Should().ContainSingle();
        Compile(source.Replace("\"Accept\"", "\"\""))
            .Diagnostics.Should().Contain(diagnostic => diagnostic.Id == DiagnosticId.EmptyDialogText);
    }

    [Fact]
    public void EveryButtonUnderAGateCarriesACondition()
    {
        var dialog = OnlyDialog(Compile("""
            Bind Npc 16079

            On greeting
                Say "Have you got them?"
                If player has > 6 of 810369000
                    Topic "All six" goto close
                Else
                    Topic "Not yet" goto close
            """));

        dialog.Choices.Should().HaveCount(2);
        dialog.Choices.Should().OnlyContain(c => c.When != null,
            "an else button must not show when the if arm already matched");
    }

    [Fact]
    public void AnElseArmIsTheNegationOfTheArmsBeforeIt()
    {
        var dialog = OnlyDialog(Compile("""
            Bind Npc 16079

            On greeting
                Say "Have you got them?"
                If player has > 6 of 810369000
                    Topic "All six" goto close
                Else
                    Topic "Not yet" goto close
            """));

        dialog.Choices.Last().When.Should().BeOfType<BoundCondition.Not>();
    }

    [Fact]
    public void ANestedButtonIsGatedByBothItsOwnArmAndTheOuterOne()
    {
        var dialog = OnlyDialog(Compile("""
            Bind Npc 16079

            On greeting
                Say "Have you got them?"
                If player has > 6 of 810369000
                    Topic "All six" goto close
                Else
                    If player has > 3 of 810369000
                        Topic "Only three" goto close
            """));

        dialog.Choices.Last().When.Should().BeOfType<BoundCondition.And>();
    }

    [Fact]
    public void SeparateIfStatementsStayIndependentOfEachOther()
    {
        var dialog = OnlyDialog(Compile("""
            Bind Npc 16079

            On greeting
                Say "What do you need?"
                If quest 61 is unstarted
                    Topic "Silk Spool" goto close
                If quest 512 is started
                    Topic "The vouchers" goto close
                Topic "Nothing" goto close
            """));

        dialog.Choices.Should().HaveCount(3);
        dialog.Choices.Count(c => c.When is null).Should().Be(1,
            "two separate gates are independent, and the last button is unconditional");
        dialog.Choices.Select(c => c.When).Should().NotContain(w => w is BoundCondition.Not,
            "nothing here is an else arm");
    }
}
