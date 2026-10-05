using System.Linq;
using Godot;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ChatColorsTests
{
    private readonly ChatColors _colors = new();

    [Theory]
    [InlineData(ChatType.General, true, "ffff00ff")]
    [InlineData(ChatType.General, false, "ffffffff")]
    [InlineData(ChatType.Shout, true, "f86605ff")]
    [InlineData(ChatType.Private, false, "80ffffff")]
    public void TheInputShowsTheColourItWillBeSentIn(byte channel, bool gameMasterMode, string rgba) =>
        Assert.Equal(rgba, _colors.ForInput(channel, gameMasterMode).ToHtml());

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
        Assert.Equal(rgba, _colors.ForLine(type).ToHtml());

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
        Assert.Equal(rgba, _colors.ForLine(type).ToHtml());

    [Fact]
    public void WhispersTakeTheirDirection()
    {
        Assert.Equal("80ffffff", _colors.ForLine(2, WhisperSide.Sent).ToHtml());
        Assert.Equal("ffff00ff", _colors.ForLine(2, WhisperSide.Received).ToHtml());
        Assert.Equal("80ff80ff", _colors.ForLine(2, WhisperSide.ReceivedFromFriend).ToHtml());
    }

    [Fact]
    public void NoahKnightChatFollowsTheSpeakersNation()
    {
        Assert.Equal("87cefaff", _colors.ForLine(34, nation: 1).ToHtml());
        Assert.Equal("ffb6c1ff", _colors.ForLine(34, nation: 2).ToHtml());
        Assert.Equal("ffffffff", _colors.ForLine(34).ToHtml());
    }

    [Fact]
    public void AChangedSlotSurvivesSavingAndResetRestoresIt()
    {
        _colors[ChatColorSlot.Party] = new Color("ffb6c1");
        var loaded = ChatColors.Parse(_colors.Format());
        Assert.Equal("ffb6c1ff", loaded.ForLine(3).ToHtml());
        loaded.Reset();
        Assert.Equal("00c0c0ff", loaded.ForLine(3).ToHtml());
    }

    [Fact]
    public void ABrokenSaveFallsBackToTheDefaults() =>
        Assert.Equal("00c0c0ff", ChatColors.Parse("zz,1,").ForLine(3).ToHtml());
}
