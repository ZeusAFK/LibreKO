using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ItemUpgradePacketWriter
{

    public enum BifrostResult : byte
    {
        Failed = 0,
        Succeeded = 1,
        Rejected = 2,
        NoPiece = 4,
    }

    public enum BifrostRarity : byte
    {
        None = 0,
        Red = 1,
        Green = 2,
        White = 3,
    }

    public const byte ResultFailed = 0;

    public const int UpgradeSlotCount = 10;
    public const byte EmptyPosition = byte.MaxValue;

    public static Packet AnvilOpen(int anvilId)
    {
        var packet = Sub(ItemUpgradeSubOpcode.AnvilOpen);
        packet.WriteInt(anvilId);
        return packet;
    }

    public static Packet BifrostRequest(int npcId)
    {
        var packet = Sub(ItemUpgradeSubOpcode.BifrostRequest);
        packet.WriteInt(npcId);
        return packet;
    }

    public static Packet BifrostExchangeResult(BifrostResult result)
    {
        var packet = Sub(ItemUpgradeSubOpcode.BifrostExchange);
        packet.WriteByte((byte)result);
        return packet;
    }

    public static Packet BifrostExchangeSucceeded(
        int rewardItemId, byte grantedPosition, int pieceItemId, byte sourcePosition, BifrostRarity effect)
    {
        var packet = BifrostExchangeResult(BifrostResult.Succeeded);
        packet.WriteInt(rewardItemId);
        packet.WriteByte(grantedPosition);
        packet.WriteInt(pieceItemId);
        packet.WriteByte(sourcePosition);
        packet.WriteByte((byte)effect);
        return packet;
    }

    public static Packet UpgradeResult(ItemUpgradeSubOpcode sub, byte upgradeType, byte resultCode, int[] itemIds, sbyte[] positions)
    {
        var packet = Sub(sub);
        packet.WriteByte(upgradeType);
        packet.WriteByte(resultCode);

        for (var index = 0; index < UpgradeSlotCount; index++)
        {
            packet.WriteInt(index < itemIds.Length ? itemIds[index] : 0);
            packet.WriteByte(index < positions.Length ? unchecked((byte)positions[index]) : EmptyPosition);
        }

        return packet;
    }

    public static Packet ItemSealResult(
        ItemSealType sealType, ItemSealResult result, int itemId, byte position)
    {
        var packet = Sub(ItemUpgradeSubOpcode.ItemSeal);
        packet.WriteByte((byte)sealType);
        packet.WriteByte((byte)result);
        packet.WriteInt(itemId);
        packet.WriteByte(position);
        return packet;
    }

    private static Packet Sub(ItemUpgradeSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
