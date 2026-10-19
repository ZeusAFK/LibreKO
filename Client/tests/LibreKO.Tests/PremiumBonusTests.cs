using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class PremiumBonusTests
{
    [Fact]
    public void TheReplyIsThe2619ListThenTheBonuses()
    {
        var p = new Packet(GameOpcodes.GS_PREMIUM);
        p.WriteByte(1);
        p.WriteByte(1);
        p.WriteByte(5);
        p.WriteUShort(72);
        p.WriteByte(5);
        p.WriteByte(2);
        p.WriteByte((byte)PremiumBonusKind.Experience);
        p.WriteShort(400);
        p.WriteByte((byte)PremiumBonusKind.PriceHistory);
        p.WriteShort(0);
        p.ResetOffset();

        Assert.Equal((1, 5, 72), PremiumWire.ReadList(p));
        var bonuses = PremiumWire.ReadBonuses(p);

        Assert.Equal(new[]
        {
            new PremiumBonus(PremiumBonusKind.Experience, 400),
            new PremiumBonus(PremiumBonusKind.PriceHistory, 0),
        }, bonuses);
    }

    [Fact]
    public void AnEmptyListIsNoPremium()
    {
        var p = new Packet(GameOpcodes.GS_PREMIUM);
        p.WriteByte(1);
        p.WriteByte(0);
        p.WriteByte(0);
        p.ResetOffset();

        Assert.Equal((0, 0, 0), PremiumWire.ReadList(p));
    }

    [Fact]
    public void AnOlderReplyWithoutBonusesListsNone()
    {
        var p = new Packet(GameOpcodes.GS_PREMIUM);
        p.ResetOffset();

        Assert.Empty(PremiumWire.ReadBonuses(p));
    }

    [Fact]
    public void TheTooltipNamesThePremiumAndListsKnownBonuses()
    {
        var text = PremiumBonus.Tooltip("GOLD PREMIUM", "Premium Service : 3 day(s) remaining.", new[]
        {
            new PremiumBonus(PremiumBonusKind.Coins, 5),
            new PremiumBonus(PremiumBonusKind.RepairDiscount, 50),
            new PremiumBonus((PremiumBonusKind)200, 1),
            new PremiumBonus(PremiumBonusKind.FreeReset, 0),
        });

        Assert.Equal("GOLD PREMIUM\nPremium Service : 3 day(s) remaining.\nCoins +5%\nRepairs 50% cheaper\nFree stat and skill resets", text);
    }
}
