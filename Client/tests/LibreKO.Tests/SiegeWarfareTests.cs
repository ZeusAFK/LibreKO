using System.Linq;
using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class SiegeWarfareTests
{
    private static Packet Reply(params object[] parts)
    {
        var p = new Packet(GameOpcodes.GS_SIEGE);
        foreach (var part in parts)
        {
            switch (part)
            {
                case byte b: p.WriteByte(b); break;
                case short s: p.WriteShort(s); break;
                case uint u: p.WriteUInt(u); break;
                case int i: p.WriteInt(i); break;
                case string text: p.WriteSByteString(text); break;
            }
        }
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheScheduleListsWarTypeWeekdayAndTime()
    {
        var schedule = SiegeWire.ReadSchedule(Reply((short)1, (byte)2, (byte)2, (byte)7, (byte)20, (byte)0, (byte)1, (byte)3, (byte)21, (byte)30));

        Assert.Equal(2, schedule.Rows.Count);
        Assert.Equal(new SiegeScheduleRow(1, 3, 21, 30), schedule.Rows[1]);
        Assert.Equal(10101, SiegeWarfare.WarText(schedule.Rows[0].WarType));
        Assert.Equal(10102, SiegeWarfare.WarText(schedule.Rows[1].WarType));
        Assert.Equal(10207, SiegeWarfare.DayText(schedule.Rows[0].Weekday));
        Assert.Equal("21:30", SiegeWarfare.Clock(21, 30));
        Assert.Equal("09:05", SiegeWarfare.Clock(9, 5));
    }

    [Fact]
    public void TheChallengerListEndsWithTheRegistrationTrailer()
    {
        var list = SiegeWire.ReadChallengers(Reply((short)1, (byte)2, "Olympus", (byte)1, (byte)48, "Avalon", (byte)2, (byte)36,
            (byte)2, (byte)6, 1_000_000u, (byte)3, (byte)9, (byte)0, (byte)3));

        Assert.Equal(["Olympus", "Avalon"], list.Clans.Select(c => c.Name));
        Assert.Equal(new SiegeClanRow("Avalon", 2, 36), list.Clans[1]);
        Assert.Equal((2, 6, 1_000_000u), (list.SignedUp, list.Chosen, list.Fee));
        Assert.Equal((3, 9, 0, 3), ((int)list.StartDay, (int)list.StartHour, (int)list.StartMinute, (int)list.EndDay));
    }

    [Fact]
    public void TheRegistrationPeriodEndsAtMidnight()
    {
        Assert.Equal("[Registration Period : Tuesday 09:00 ~ Tuesday 24:00]",
            SiegeWarfare.RegistrationPeriod("[Registration Period : %s %.2d:%.2d ~ %s %.2d:%.2d]", "Tuesday", 9, 0, "Tuesday"));
    }

    [Fact]
    public void TheDefendersListHasNoTrailer()
    {
        var list = SiegeWire.ReadDefenders(Reply((short)1, (byte)1, "Olympus", (byte)1, (byte)48));

        Assert.Equal(new SiegeClanRow("Olympus", 1, 48), list.Clans.Single());
        Assert.Empty(SiegeWire.ReadDefenders(Reply((short)-2)).Clans);
    }

    [Theory]
    [InlineData(-1, true, 10210)]
    [InlineData(-4, false, 10213)]
    [InlineData(-5, true, 1702)]
    [InlineData(-5, false, 0)]
    public void ListErrorsUseTheClientsTexts(short result, bool coins, int text) =>
        Assert.Equal(text, SiegeWarfare.ListErrorText(result, coins));

    [Fact]
    public void OfficeErrorsUseTheClientsTexts()
    {
        Assert.Equal(10229, SiegeWarfare.CollectErrorText(-5));
        Assert.Equal(6514, SiegeWarfare.CollectErrorText(-3));
        Assert.Equal(0, SiegeWarfare.CollectErrorText(-1));
        Assert.Equal(10230, SiegeWarfare.TaxErrorText(-5));
        Assert.Equal(6514, SiegeWarfare.TaxErrorText(-4));
    }

    [Fact]
    public void TheOfficeRepliesReadTheirFields()
    {
        var office = SiegeWire.ReadOffice(Reply(4_250_000u, 7u));
        var collected = SiegeWire.ReadCollected(Reply((short)1, 9_000_000, 4_250_000));
        var rates = SiegeWire.ReadTaxRates(Reply((short)1, (short)3, (short)4, 10_000));

        Assert.Equal(4_250_000u, office.Collectable);
        Assert.Equal((9_000_000, 4_250_000), (collected.Coins, collected.Collected));
        Assert.Equal(((short)3, (short)4, 10_000), (rates.Moradon, rates.Delos, rates.DungeonFee));
    }

    [Fact]
    public void ARateChangeNamesTheZoneWhileAFeeChangeIsADoubleWord()
    {
        var moradon = SiegeWire.ReadRateChanged(Reply((short)1, (short)4, (short)21), SiegeWarfare.MoradonRate);
        var fee = SiegeWire.ReadRateChanged(Reply((short)1, 20_000), SiegeWarfare.DungeonFee);

        Assert.Equal((4, (short)21), (moradon.Value, moradon.Zone));
        Assert.Equal(20_000, fee.Value);
        Assert.Equal(10232, SiegeWarfare.ChangedText(SiegeWarfare.MoradonRate));
        Assert.Equal(10234, SiegeWarfare.ChangedText(SiegeWarfare.DungeonFee));
    }

    [Fact]
    public void RatesAreWordsAndTheFeeIsADoubleWord()
    {
        Assert.Equal(new byte[] { 0x6D, 4, 4, 3, 0 }, SiegeWire.Rate(SiegeRateKind.Moradon, 3).GetBytes());
        Assert.Equal(new byte[] { 0x6D, 4, 5, 5, 0 }, SiegeWire.Rate(SiegeRateKind.Delos, 5).GetBytes());
        Assert.Equal(new byte[] { 0x6D, 4, 6, 0x10, 0x27, 0, 0 }, SiegeWire.Rate(SiegeRateKind.DungeonFee, 10_000).GetBytes());
    }

    [Fact]
    public void TaxRatesStayBetweenZeroAndFive()
    {
        Assert.Equal(5, SiegeWarfare.Step(SiegeRateKind.Moradon, 5, up: true));
        Assert.Equal(0, SiegeWarfare.Step(SiegeRateKind.Delos, 0, up: false));
        Assert.Equal(3, SiegeWarfare.Step(SiegeRateKind.Delos, 2, up: true));
    }

    [Fact]
    public void TheFeeMovesInTenThousandsAndNeverRisesAboveOrFallsBelowTenThousand()
    {
        Assert.Equal(10_000, SiegeWarfare.Step(SiegeRateKind.DungeonFee, 10_000, up: true));
        Assert.Equal(10_000, SiegeWarfare.Step(SiegeRateKind.DungeonFee, 10_000, up: false));
        Assert.Equal(40_000, SiegeWarfare.Step(SiegeRateKind.DungeonFee, 50_000, up: false));
        Assert.Equal(10_000, SiegeWarfare.Step(SiegeRateKind.DungeonFee, 0, up: true));
    }

    [Fact]
    public void EachRateKindHasItsOwnTexts()
    {
        Assert.Equal((10222, 10223), (SiegeWarfare.PromptText(SiegeRateKind.Moradon), SiegeWarfare.ConfirmText(SiegeRateKind.Moradon)));
        Assert.Equal((10224, 10225), (SiegeWarfare.PromptText(SiegeRateKind.Delos), SiegeWarfare.ConfirmText(SiegeRateKind.Delos)));
        Assert.Equal((10226, 10227), (SiegeWarfare.PromptText(SiegeRateKind.DungeonFee), SiegeWarfare.ConfirmText(SiegeRateKind.DungeonFee)));
    }

    [Fact]
    public void NationsUseTheClientsNames()
    {
        Assert.Equal(3102, SiegeWarfare.NationText(Nations.Karus));
        Assert.Equal(3101, SiegeWarfare.NationText(Nations.ElMorad));
        Assert.Equal(0, SiegeWarfare.NationText(0));
    }

    [Fact]
    public void OnlyTheOwnClanSwapsApplicationForCancel()
    {
        Assert.True(SiegeWarfare.IsOwnClan("Valhalla", "Valhalla"));
        Assert.False(SiegeWarfare.IsOwnClan("Valhalla", "valhalla"));
        Assert.False(SiegeWarfare.IsOwnClan("", ""));
    }

    [Fact]
    public void AssaultAndApplyAreBareRequests()
    {
        Assert.Equal(new byte[] { 0x6D, 3, 3 }, SiegeWire.Request(SiegeWarfare.CastleGuard, SiegeWarfare.Assault).GetBytes());
        Assert.Equal(new byte[] { 0x6D, 3, 1, 2 },
            SiegeWire.Request(SiegeWarfare.CastleGuard, SiegeWarfare.Apply, SiegeWarfare.ApplyCancel).GetBytes());
    }
}
