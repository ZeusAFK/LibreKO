using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class PetFoodWireTests
{
    private const int FoodItem = 810684000;
    private const byte FoodSlot = 3;

    private static Packet Food(byte result = PetWire.FoodSucceeded, byte slot = FoodSlot, int item = FoodItem, short count = 2, short increase = 1500)
    {
        var packet = new Packet((byte)GameOpcodes.GS_PET);
        packet.WriteByte(result); packet.WriteByte(slot); packet.WriteInt(item);
        if (result == PetWire.FoodSucceeded) { packet.WriteShort(count); packet.WriteShort(0); packet.WriteInt(0); packet.WriteShort(increase); }
        packet.ResetOffset(); return packet;
    }

    [Fact]
    public void EveryTruncatedSuccessIsIgnoredInsteadOfBecomingARefusal()
    {
        var complete = Food().GetData();
        for (int length = 0; length < complete.Length; length++)
        {
            var prefix = new Packet((byte)GameOpcodes.GS_PET); prefix.WriteBytes(complete[..length]);
            Assert.False(PetWire.TryReadFood(prefix, out _));
        }
    }

    [Fact]
    public void AuthoritativeFoodSuccessAndRefusalHaveDistinctRecords()
    {
        Assert.True(PetWire.TryReadFood(Food(), out var success));
        Assert.Equal(new PetFoodReply(true, FoodSlot, FoodItem, 2, 1500), success);
        Assert.True(PetWire.TryReadFood(Food(result: PetWire.FoodRefused), out var refusal));
        Assert.Equal(new PetFoodReply(false, FoodSlot, FoodItem, 0, 0), refusal);
    }

    [Theory]
    [InlineData(2, FoodSlot, FoodItem, 2, 1500)]
    [InlineData(1, InventoryConstants.HaveMax, FoodItem, 2, 1500)]
    [InlineData(1, FoodSlot, 0, 2, 1500)]
    [InlineData(1, FoodSlot, FoodItem, -1, 1500)]
    [InlineData(1, FoodSlot, FoodItem, 2, -1)]
    [InlineData(1, FoodSlot, FoodItem, 2, PetSheet.MaxSatisfaction + 1)]
    public void InvalidFoodRepliesCannotReachInventoryOrStatusUpdates(byte result, byte slot, int item, short count, short increase)
        => Assert.False(PetWire.TryReadFood(Food(result, slot, item, count, increase), out _));

    [Fact]
    public void AFoodReplyOnlyAnswersTheFeedThatWasSent()
    {
        var request = new PetFeedRequest(FoodSlot, FoodItem);
        Assert.True(request.Matches(new PetFoodReply(true, FoodSlot, FoodItem, 1, 1500)));
        Assert.True(request.Matches(new PetFoodReply(false, FoodSlot, FoodItem, 0, 0)));
        Assert.False(request.Matches(new PetFoodReply(true, FoodSlot + 1, FoodItem, 1, 1500)));
        Assert.False(request.Matches(new PetFoodReply(true, FoodSlot, FoodItem + 1, 1, 1500)));
    }
}
