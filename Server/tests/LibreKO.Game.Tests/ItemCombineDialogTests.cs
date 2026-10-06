using FluentAssertions;
using LibreKO.Game.Protocol.Writers;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ItemCombineDialogTests
{
    private const int Shozin = 19073;
    private const int Juel = 31402;
    private const int Karus = 1;

    [Fact]
    public void ShozinStartsManufacturingOnlyWhenAsked()
    {
        var talk = new QuestTalk("19073_21_combine.quest", "start", Shozin, Karus);

        talk.Labels.Should().Equal("Yes, please show me your power", "No, I will come back later");
        talk.Host.DidNotReceive().OpenItemCombinePanel();

        talk.Follow("Yes, please show me your power");

        talk.Host.Received(1).OpenItemCombinePanel();
    }

    [Fact]
    public void JuelAssemblesAndShowsHerRecipes()
    {
        new QuestTalk("31402_21_combine.quest", "start", Juel, Karus).Follow("Yes, I ask").Host.Received(1).OpenItemCombinePanel();
        new QuestTalk("31402_21_combine.quest", "recipes", Juel, Karus).Host.Received(1).OpenCombineRecipeBook();
    }

    [Fact]
    public void JuelExplainsWhatSheNeeds()
    {
        var talk = new QuestTalk("31402_21_combine.quest", "asking", Juel, Karus);

        talk.Header!.Text.Should().Contain("Bring me 3 identical Old Accesory, Nest Scrap (5), Scrap of Steel (50) and Soft Stone (1)");
    }

    [Theory]
    [InlineData(18, true)]
    [InlineData(21, false)]
    public void TheCombineWindowsAreDialogStylesEighteenAndTwentyOne(byte style, bool combine)
    {
        var packet = combine
            ? NpcDialogPacketWriter.ItemCombinePanel(Shozin, "19073_21_combine")
            : NpcDialogPacketWriter.CombineRecipeBook(Shozin, "19073_21_combine");
        packet.ResetOffset();
        packet.ReadInt();
        packet.ReadByte().Should().Be(style);
    }
}
