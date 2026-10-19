namespace LibreKO.Network.Readers;

internal sealed class ItemSmashReader : IPacketReader
{
    private readonly ItemSmashSession itemSmash;

    public ItemSmashReader(ItemSmashSession itemSmash)
    {
        this.itemSmash = itemSmash;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_ITEM_UPGRADE, ItemUpgradeSubOpcode.Smash, HandleItemSmashReply);
    }

    private void HandleItemSmashReply(Packet p)
    {
        if (ItemSmashReply.Read(p) is { } reply)
            itemSmash.RaiseItemSmashReply(reply);
    }
}
