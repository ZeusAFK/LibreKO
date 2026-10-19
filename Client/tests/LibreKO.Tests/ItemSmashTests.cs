using System.Collections.Generic;
using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ItemSmashTests
{
    private const int Sword = 120050605;
    private const int SwordKind = 21;
    private const int UniqueGrade = 33;
    private const int Scrap = 810369000;
    private const int Gem = 900823000;

    [Theory]
    [InlineData(SwordKind, 0, UniqueGrade, ItemFlag.Unsealed, SmashCheck.Allowed)]
    [InlineData(235, 0, UniqueGrade, ItemFlag.Unsealed, SmashCheck.Allowed)]
    [InlineData(255, 0, UniqueGrade, ItemFlag.Unsealed, SmashCheck.NotGear)]
    [InlineData(SwordKind, 0, 2, ItemFlag.Unsealed, SmashCheck.EventItem)]
    [InlineData(SwordKind, 0, 7, ItemFlag.Unsealed, SmashCheck.EventItem)]
    [InlineData(SwordKind, 0, UniqueGrade, ItemFlag.Bound, SmashCheck.Protected)]
    [InlineData(SwordKind, 0, UniqueGrade, ItemFlag.SealedPremium, SmashCheck.Protected)]
    [InlineData(SwordKind, 70, UniqueGrade, ItemFlag.Unsealed, SmashCheck.Indestructible)]
    public void TheDropChecksFollowTheRetailOrder(int kind, int race, int grade, ItemFlag state, SmashCheck expected)
    {
        Assert.Equal(expected, ItemSmash.Check(Sword, kind, race, grade, state));
    }

    [Fact]
    public void TheHighItemNumbersAreQuietlyIgnored()
    {
        Assert.Equal(SmashCheck.Ignored, ItemSmash.Check(910_000_000, SwordKind, 0, UniqueGrade, ItemFlag.Unsealed));
        Assert.Null(ItemSmash.CheckText(SmashCheck.Ignored));
    }

    [Theory]
    [InlineData(ItemSmash.InventoryFull, 10714)]
    [InlineData(ItemSmash.BagFull, 7434)]
    [InlineData(ItemSmash.CannotDisassemble, 30503)]
    [InlineData(ItemSmash.WrongNpc, 30504)]
    [InlineData(ItemSmash.TooSoon, 30505)]
    public void EveryRefusalHasItsText(short result, int text)
    {
        Assert.Equal(text, ItemSmash.ReplyText(result));
    }

    [Fact]
    public void ASuccessReadsTheItemAndEveryPiece()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)ItemUpgradeSubOpcode.Smash);
        p.WriteShort(ItemSmash.Succeeded);
        p.WriteInt(Sword);
        p.WriteByte(4);
        p.WriteShort(2);
        p.WriteInt(Scrap);
        p.WriteByte(5);
        p.WriteUShort(3);
        p.WriteInt(Gem);
        p.WriteByte(6);
        p.WriteUShort(1);
        p.ResetOffset();
        p.ReadByte();

        var reply = ItemSmashReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal(ItemSmash.Succeeded, reply.Result);
        Assert.Equal(Sword, reply.ItemId);
        Assert.Equal(4, reply.BagSlot);
        Assert.Equal(new[] { new SmashPiece(Scrap, 5, 3), new SmashPiece(Gem, 6, 1) }, reply.Pieces);
    }

    [Fact]
    public void ARefusalCarriesOnlyItsCode()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)ItemUpgradeSubOpcode.Smash);
        p.WriteShort(ItemSmash.WrongNpc);
        p.ResetOffset();
        p.ReadByte();

        var reply = ItemSmashReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal(ItemSmash.WrongNpc, reply.Result);
        Assert.Empty(reply.Pieces);
    }

    [Fact]
    public void TheTableGivesTheBandAndItsRewards()
    {
        var table = new SmashTable(
            new Dictionary<int, SmashClass> { [UniqueGrade] = new(3, 3) },
            new Dictionary<int, IReadOnlyList<int>> { [3] = new[] { Scrap, Gem } });

        Assert.Equal(new SmashClass(3, 3), table.ClassOf(UniqueGrade));
        Assert.Null(table.ClassOf(6));
        Assert.Equal(new[] { Scrap, Gem }, table.RewardsOf(3));
        Assert.Empty(table.RewardsOf(1));
    }
}
