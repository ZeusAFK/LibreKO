using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.World;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IUserNotificationService
{
    Task SendGoldGainAsync(UserSession session, int amount);
    Task SendGoldLossAsync(UserSession session, int amount);
    Task SendStackChangeAsync(UserSession session, byte pos, int itemId, ushort count, short durability, bool isNewItem = false);
    Task SendWeightChangeAsync(UserSession session);
    Task SendStatUpdateAsync(UserSession session);
    Task SendUnreadNotificationAsync(UserSession session);
}

public class UserNotificationService : IUserNotificationService
{
    public async Task SendGoldGainAsync(UserSession session, int amount)
    {
        await session.Client.SendPacket(GoldChangePacketWriter.Change(
            GoldChangePacketWriter.Gained, amount, session.Money));
    }

    public async Task SendGoldLossAsync(UserSession session, int amount)
    {
        await session.Client.SendPacket(GoldChangePacketWriter.Change(
            GoldChangePacketWriter.Spent, amount, session.Money));
    }

    public async Task SendStackChangeAsync(
        UserSession session,
        byte pos,
        int itemId,
        ushort count,
        short durability,
        bool isNewItem = false)
    {
        var packet = new ItemCountChangePacketWriter()
            .Add(pos, itemId, count, durability, isNewItem)
            .Build();
        await session.Client.SendPacket(packet);
    }

    private const byte UnreadNotificationCount = 1;

    public async Task SendWeightChangeAsync(UserSession session)
    {
        var packet = ProgressionPacketWriter.WeightChange(session.Stats.ItemWeight);
        await session.Client.SendPacket(packet);
    }

    public async Task SendStatUpdateAsync(UserSession session)
    {
        await session.Client.SendPacket(ItemMovePacketMapper.BuildResponse(session, 1));
    }

    public async Task SendUnreadNotificationAsync(UserSession session)
    {
        await session.Client.SendPacket(ShoppingMallPacketWriter.UnreadCount(
            ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterUnread,
            UnreadNotificationCount));
    }

    private static byte NormalizeInventoryPosition(byte pos) =>
        pos >= InventoryConstants.InventoryStart
            ? (byte)(pos - InventoryConstants.InventoryStart)
            : pos;
}
