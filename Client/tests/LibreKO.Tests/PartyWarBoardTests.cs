using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class PartyWarBoardTests
{
    [Fact]
    public void TheReplyReadsTargetModeAndShares()
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte((byte)PartyWarBoardTarget.Board);
        p.WriteByte((byte)PartyBoardMeterMode.Healing);
        p.WriteByte(2);
        p.WriteInt(11);
        p.WriteShort(750);
        p.WriteInt(12);
        p.WriteShort(250);
        p.ResetOffset();

        var reply = PartyWarBoardWire.ReadWarBoard(p);

        Assert.NotNull(reply);
        Assert.Equal((byte)PartyWarBoardTarget.Board, (byte)reply.Value.Target);
        Assert.Equal(PartyBoardMeterMode.Healing, reply.Value.Mode);
        Assert.Equal(new[] { new WarBoardShare(11, 750), new WarBoardShare(12, 250) }, reply.Value.Shares);
    }

    [Theory]
    [InlineData(0, "0.0%")]
    [InlineData(5, "0.5%")]
    [InlineData(881, "88.1%")]
    [InlineData(1000, "100.0%")]
    public void PercentagesAreTenthsOfThousandths(int share, string text)
    {
        Assert.Equal(text, PartyWarBoard.Percent(share));
    }

    [Fact]
    public void TheRowToggleCyclesOffDamageHealing()
    {
        Assert.Equal(PartyBoardMeterMode.Damage, PartyWarBoard.NextRowMode(PartyBoardMeterMode.Off));
        Assert.Equal(PartyBoardMeterMode.Healing, PartyWarBoard.NextRowMode(PartyBoardMeterMode.Damage));
        Assert.Equal(PartyBoardMeterMode.Off, PartyWarBoard.NextRowMode(PartyBoardMeterMode.Healing));
    }

    [Fact]
    public void TheBoardRanksByShare()
    {
        var ranked = PartyWarBoard.Ranked(new[]
        {
            new WarBoardShare(1, 100), new WarBoardShare(2, 600), new WarBoardShare(3, 300),
        });

        Assert.Equal(new[] { 2, 3, 1 }, new[] { ranked[0].MemberId, ranked[1].MemberId, ranked[2].MemberId });
        Assert.Equal("1. Rin", PartyWarBoard.RowName(1, "Rin"));
    }

    [Fact]
    public void PriestsOpenTheBoardOnHealing()
    {
        Assert.Equal(PartyBoardMeterMode.Healing, PartyWarBoard.DefaultBoardMode(104));
        Assert.Equal(PartyBoardMeterMode.Damage, PartyWarBoard.DefaultBoardMode(201));
    }

    [Theory]
    [InlineData(0, PartyBoardMeterMode.Off)]
    [InlineData(2, PartyBoardMeterMode.Healing)]
    [InlineData(7, PartyBoardMeterMode.Damage)]
    [InlineData(-1, PartyBoardMeterMode.Damage)]
    public void ASavedModeOutsideTheThreeFallsBack(int saved, PartyBoardMeterMode expected) =>
        Assert.Equal(expected, PartyWarBoard.SavedMode((PartyBoardMeterMode)saved, PartyBoardMeterMode.Damage));

    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0f, PartyWarBoard.MinAlpha)]
    [InlineData(3f, 1f)]
    [InlineData(float.NaN, 1f)]
    public void ASavedAlphaStaysInTheSliderRange(float saved, float expected) =>
        Assert.Equal(expected, PartyWarBoard.SavedAlpha(saved));

    [Fact]
    public void TheLimitCountsDownFromTheLastSend()
    {
        Assert.Equal(3.0, PartyWarBoard.SecondsLeft(10, 12, PartyWarBoard.BoardTabSeconds));
        Assert.Equal(0.0, PartyWarBoard.SecondsLeft(10, 16, PartyWarBoard.BoardTabSeconds));
    }

    [Theory]
    [InlineData(true, 4.2, 5)]
    [InlineData(true, 0.4, 1)]
    [InlineData(true, 0.0, 0)]
    [InlineData(false, 3.0, 0)]
    public void TheTabCooldownShowsWholeSecondsOnlyWhileARequestWaits(bool pending, double left, int shown) =>
        Assert.Equal(shown, PartyWarBoard.CooldownShown(pending, left));
}
