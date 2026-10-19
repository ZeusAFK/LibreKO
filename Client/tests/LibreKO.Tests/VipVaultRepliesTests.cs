using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class VipVaultRepliesTests
{
    private const int Slots = 48;
    private const uint ExpiresAt = 1_800_000_000;
    private const int StoredItem = 156_210_000;

    private static Packet OpenReply(bool pinSet, bool open, uint expiresAt)
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte((byte)(pinSet ? 1 : 0));
        for (int storage = 0; storage < VipVaultContents.Storages; storage++)
        {
            bool own = storage == VipVaultContents.OwnStorage;
            p.WriteByte((byte)(own && open ? 1 : 0));
            p.WriteUInt(own ? expiresAt : 0u);
        }
        for (int i = 0; i < Slots; i++)
        {
            p.WriteInt(i == 3 ? StoredItem : 0);
            p.WriteShort(i == 3 ? (short)50 : (short)0);
            p.WriteUShort(i == 3 ? (ushort)1 : (ushort)0);
            p.WriteByte(0);
            p.WriteUInt(0);
            p.WriteUInt(0);
        }
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheOpenReplyCarriesThePinFlagTheOwnStorageAndTheItems()
    {
        var p = OpenReply(pinSet: true, open: true, ExpiresAt);

        var contents = VipVaultContents.Read(p, Slots);

        Assert.NotNull(contents);
        Assert.True(contents.Value.PinSet);
        Assert.True(contents.Value.Open);
        Assert.Equal(ExpiresAt, contents.Value.ExpiresAt);
        Assert.Equal(StoredItem, contents.Value.Slots[3].ItemId);
        Assert.Equal(50, contents.Value.Slots[3].Durability);
        Assert.True(contents.Value.Slots[0].IsEmpty);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void AnExpiredStorageStillHandsOverItsItems()
    {
        var p = OpenReply(pinSet: false, open: false, ExpiresAt);

        var contents = VipVaultContents.Read(p, Slots);

        Assert.NotNull(contents);
        Assert.False(contents.Value.Open);
        Assert.False(contents.Value.PinSet);
        Assert.Equal(StoredItem, contents.Value.Slots[3].ItemId);
    }

    [Fact]
    public void AKeyReplyNamesTheStorageAndItsNewEnd()
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte((byte)VipWarehouseResult.Succeeded);
        p.WriteByte(VipVaultContents.OwnStorage);
        p.WriteUInt(ExpiresAt);
        p.ResetOffset();

        var reply = VipVaultKeyReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal(VipVaultContents.OwnStorage, reply.Value.Storage);
        Assert.Equal(ExpiresAt, reply.Value.ExpiresAt);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    public void AKeyRefusalIsJustItsCode(byte code)
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte(code);
        p.ResetOffset();

        var reply = VipVaultKeyReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal(code, (byte)reply.Value.Result);
    }
}
