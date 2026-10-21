using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Network.Readers;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class ItemCountChangeReaderTests
{
    private const int Potion = 389010000;
    private const int OtherPotion = 389020000;
    private const byte BagCell = 3;
    private const int Expiry = 1_900_000_000;

    private sealed class NoConnection : IPacketSink
    {
        public bool Connected => false;

        public void Send(Packet packet) { }
    }

    private static (PacketDispatcher Dispatcher, GameSession Session) Readers()
    {
        var session = new GameSession();
        var dispatcher = new PacketDispatcher();
        PacketReaders.Register(dispatcher, session, new GameRequests(new NoConnection(), session, new LoginState()), () => 0);
        return (dispatcher, session);
    }

    private static Packet CountChange(int itemId, int count)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_COUNT_CHANGE);
        p.WriteShort(1);
        p.WriteByte((byte)ItemCountChangeKind.Update);
        p.WriteByte(BagCell);
        p.WriteInt(itemId);
        p.WriteInt(count);
        p.WriteByte(0);
        p.WriteShort(1);
        p.WriteInt(0);
        p.ResetOffset();
        return p;
    }

    private static int Abs => InventoryConstants.InventoryStart + BagCell;

    [Fact]
    public void ACountChangeKeepsTheStacksBindingAndExpiry()
    {
        var (dispatcher, session) = Readers();
        session.Self.SetInventorySlot(Abs, new ItemSlot { ItemId = Potion, Count = 10, Durability = 1, Flag = (byte)ItemFlag.Bound, ExpiresAt = Expiry });

        dispatcher.Dispatch(CountChange(Potion, 7));

        var slot = session.Self.Inventory[Abs];
        Assert.Equal(7, slot.Count);
        Assert.Equal(ItemFlag.Bound, slot.State);
        Assert.Equal(Expiry, slot.ExpiresAt);
    }

    [Fact]
    public void ACountChangeToAnotherItemStartsClean()
    {
        var (dispatcher, session) = Readers();
        session.Self.SetInventorySlot(Abs, new ItemSlot { ItemId = Potion, Count = 10, Durability = 1, Flag = (byte)ItemFlag.Bound });

        dispatcher.Dispatch(CountChange(OtherPotion, 2));

        var slot = session.Self.Inventory[Abs];
        Assert.Equal(OtherPotion, slot.ItemId);
        Assert.Equal(0, slot.Flag);
    }
}
