using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ChatColorsTests
{
    private readonly ChatColors colors = new();

    [Theory]
    [InlineData((byte)ChatType.General, true, "ffff00ff")]
    [InlineData((byte)ChatType.General, false, "ffffffff")]
    [InlineData((byte)ChatType.Shout, true, "f86605ff")]
    [InlineData((byte)ChatType.Private, false, "80ffffff")]
    public void TheInputShowsTheColourItWillBeSentIn(byte channel, bool gameMasterMode, string rgba) =>
        Assert.Equal(rgba, colors.ForInput((ChatType)channel, gameMasterMode).ToHtml());

    [Fact]
    public void PaletteOffersEveryDefault()
    {
        foreach (var colour in ChatColors.Defaults)
            Assert.Contains(colour, ChatColors.Palette);
    }

    [Fact]
    public void PaletteHasNoRepeats() =>
        Assert.Equal(ChatColors.Palette.Length, ChatColors.Palette.Distinct().Count());

    [Theory]
    [InlineData(1, "ffffffff")]
    [InlineData(5, "f86605ff")]
    [InlineData(3, "00c0c0ff")]
    [InlineData(6, "00ff00ff")]
    [InlineData(15, "ff6b6bff")]
    [InlineData(33, "8080ffff")]
    public void PlayerSetChannelsStartFromTheirDefaults(byte type, string rgba) =>
        Assert.Equal(rgba, colors.ForLine((ChatType)type).ToHtml());

    [Theory]
    [InlineData(4, "00c0c0ff")]
    [InlineData(7, "ffff00ff")]
    [InlineData(8, "ffff00ff")]
    [InlineData(9, "ffff00ff")]
    [InlineData(21, "ffff00ff")]
    [InlineData(13, "00ff00ff")]
    [InlineData(14, "c6c6fbff")]
    [InlineData(19, "32f640ff")]
    [InlineData(23, "64ffffff")]
    [InlineData(35, "ffffffff")]
    [InlineData(12, "ffffffff")]
    [InlineData(99, "ffffffff")]
    public void OtherTypesKeepTheirFixedColours(byte type, string rgba) =>
        Assert.Equal(rgba, colors.ForLine((ChatType)type).ToHtml());

    [Fact]
    public void WhispersTakeTheirDirection()
    {
        Assert.Equal("80ffffff", colors.ForLine(ChatType.Private, WhisperSide.Sent).ToHtml());
        Assert.Equal("ffff00ff", colors.ForLine(ChatType.Private, WhisperSide.Received).ToHtml());
        Assert.Equal("80ff80ff", colors.ForLine(ChatType.Private, WhisperSide.ReceivedFromFriend).ToHtml());
    }

    [Fact]
    public void NoahKnightChatFollowsTheSpeakersNation()
    {
        Assert.Equal("87cefaff", colors.ForLine(ChatType.NoahKnights, nation: 1).ToHtml());
        Assert.Equal("ffb6c1ff", colors.ForLine(ChatType.NoahKnights, nation: 2).ToHtml());
        Assert.Equal("ffffffff", colors.ForLine(ChatType.NoahKnights).ToHtml());
    }

    [Fact]
    public void AChangedSlotSurvivesSavingAndResetRestoresIt()
    {
        colors[ChatColorSlot.Party] = new Color("ffb6c1");
        var loaded = ChatColors.Parse(colors.Format());
        Assert.Equal("ffb6c1ff", loaded.ForLine(ChatType.Party).ToHtml());
        loaded.Reset();
        Assert.Equal("00c0c0ff", loaded.ForLine(ChatType.Party).ToHtml());
    }

    [Fact]
    public void ABrokenSaveFallsBackToTheDefaults() =>
        Assert.Equal("00c0c0ff", ChatColors.Parse("zz,1,").ForLine(ChatType.Party).ToHtml());
}
