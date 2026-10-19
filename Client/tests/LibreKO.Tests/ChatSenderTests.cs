using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ChatSenderTests
{
    private const int SelfId = 7;
    private const int OtherId = 8;

    [Theory]
    [InlineData(37)]
    [InlineData(38)]
    [InlineData(39)]
    [InlineData(76)]
    public void EverySpeakerIsAPathoistInThoseZones(int zone) =>
        Assert.Equal(ChatSender.PathoistText,
            ChatSender.MaskText(zone, true, Nations.Karus, SelfId, ChatType.General, Nations.Karus, SelfId));

    [Fact]
    public void AnEnemySpeakerInBifrostIsNamedByTheirNation()
    {
        Assert.Equal(PlateRules.ElMoradHeroText,
            ChatSender.MaskText(31, false, Nations.Karus, SelfId, ChatType.General, Nations.ElMorad, OtherId));
        Assert.Equal(PlateRules.KarusHeroText,
            ChatSender.MaskText(46, false, Nations.ElMorad, SelfId, ChatType.Shout, Nations.Karus, OtherId));
        Assert.Equal(0, ChatSender.MaskText(31, false, Nations.Karus, SelfId, ChatType.General, Nations.Karus, OtherId));
    }

    [Fact]
    public void GameMastersAreNeverMaskedOutsideThePathoistZones()
    {
        Assert.Equal(0, ChatSender.MaskText(31, true, Nations.Karus, SelfId, ChatType.General, Nations.ElMorad, OtherId));
        Assert.Equal(0, ChatSender.MaskText(85, false, Nations.Karus, SelfId, ChatType.GameMaster, Nations.Karus, OtherId));
    }

    [Theory]
    [InlineData(Nations.ElMorad, SelfId, ChatSender.ElMoradDeityText)]
    [InlineData(Nations.ElMorad, OtherId, ChatSender.KarusDeityText)]
    [InlineData(Nations.Karus, SelfId, ChatSender.KarusDeityText)]
    [InlineData(Nations.Karus, OtherId, ChatSender.ElMoradDeityText)]
    public void InTheDeityZonesYouAreYourOwnDeityAndEveryoneElseTheOther(int selfNation, int senderId, int text)
    {
        Assert.Equal(text, ChatSender.MaskText(85, false, selfNation, SelfId, ChatType.General, selfNation, senderId));
        Assert.Equal(text, ChatSender.MaskText(45, false, selfNation, SelfId, ChatType.Party, selfNation, senderId));
    }

    [Fact]
    public void LineBreaksAreFlattenedExceptInWhispers()
    {
        Assert.Equal("a_b", ChatSender.Flatten(ChatType.General, "a\nb"));
        Assert.Equal("a\nb", ChatSender.Flatten(ChatType.Private, "a\nb"));
    }
}
