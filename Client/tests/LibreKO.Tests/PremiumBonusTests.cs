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

        Assert.Equal(PremiumSubOpcode.List, (PremiumSubOpcode)p.ReadByte());
        var list = PremiumWire.ReadList(p);
        Assert.Equal(new[] { new HeldPremium(5, 72) }, list.Held);
        Assert.Equal(5, list.InUse);
        Assert.Equal(72, list.InUseHours);
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

        p.ReadByte();
        var list = PremiumWire.ReadList(p);
        Assert.Empty(list.Held);
        Assert.Equal(0, list.InUseHours);
    }

    [Fact]
    public void EveryHeldPremiumIsListedWithItsHours()
    {
        var p = new Packet(GameOpcodes.GS_PREMIUM);
        p.WriteByte(1);
        p.WriteByte(3);
        foreach (var type in new byte[] { 10, 11, 12 })
        {
            p.WriteByte(type);
            p.WriteUShort(720);
        }
        p.WriteByte(11);
        p.WriteByte(0);
        p.ResetOffset();

        p.ReadByte();
        var list = PremiumWire.ReadList(p);
        Assert.Equal(new[] { new HeldPremium(10, 720), new HeldPremium(11, 720), new HeldPremium(12, 720) }, list.Held);
        Assert.Equal(11, list.InUse);
        Assert.Empty(PremiumWire.ReadBonuses(p));
    }

    [Fact]
    public void ASwitchTooSoonCarriesTheSecondsToWait()
    {
        var p = new Packet(GameOpcodes.GS_PREMIUM);
        p.WriteByte(4);
        p.WriteByte(11);
        p.WriteShort(-1);
        p.WriteUShort(20);
        p.ResetOffset();

        Assert.Equal(PremiumSubOpcode.Switch, (PremiumSubOpcode)p.ReadByte());
        Assert.Equal(new PremiumSwitchReply(11, PremiumWire.SwitchTooSoon, 20), PremiumWire.ReadSwitch(p));
    }

    [Fact]
    public void ADoneSwitchHasNoWait()
    {
        var p = new Packet(GameOpcodes.GS_PREMIUM);
        p.WriteByte(4);
        p.WriteByte(12);
        p.WriteShort(1);
        p.ResetOffset();

        p.ReadByte();
        Assert.Equal(new PremiumSwitchReply(12, PremiumWire.SwitchDone, 0), PremiumWire.ReadSwitch(p));
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
