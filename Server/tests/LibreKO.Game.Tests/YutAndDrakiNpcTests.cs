using FluentAssertions;
using LibreKO.Game.Protocol.Writers;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class YutAndDrakiNpcTests
{
    private const int Dogae = 29083;
    private const int Dimension = 25267;
    private const int Karus = 1;

    [Fact]
    public void DogaeOpensTheYutGameWhenThePlayerCompetes()
    {
        var talk = new QuestTalk($"{Dogae}.quest", null, Dogae, Karus);

        talk.Labels.Should().Equal("Accept / Compete", "Reject");
        talk.Host.DidNotReceive().OpenYutGame();

        talk.Follow("Accept / Compete");

        talk.Host.Received(1).OpenYutGame();
    }

    [Fact]
    public void TheDimensionOpensTheDrakiRankBoardOnGreeting()
    {
        var talk = new QuestTalk($"{Dimension}.quest", null, Dimension, Karus);

        talk.Host.Received(1).OpenDrakiRankBoard();
    }

    [Theory]
    [InlineData(true, NpcDialogPacketWriter.YutGameStyle)]
    [InlineData(false, NpcDialogPacketWriter.DrakiRankBoardStyle)]
    public void TheWindowsGoOutAsTheirDialogStyles(bool yut, byte style)
    {
        var packet = yut ? NpcDialogPacketWriter.YutGame(Dogae, "29083") : NpcDialogPacketWriter.DrakiRankBoard(Dimension, "25267");
        packet.ResetOffset();
        packet.ReadInt();
        packet.ReadByte().Should().Be(style);
    }
}
