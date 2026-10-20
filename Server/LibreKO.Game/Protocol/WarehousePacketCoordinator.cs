using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IWarehousePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class WarehousePacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    ILogger<WarehousePacketCoordinator> logger) : IWarehousePacketCoordinator
{
    private const int NonStorableRace = 73;
    private const int VaultTicketRaceFirst = 77;
    private const int VaultTicketRaceLast = 79;
    private const int NonStorableIdFirst = 900_000_000;
    private const int NonStorableIdLast = 999_999_999;
    private const int CoinMax = 2_100_000_000;
    private const int WarehousePageSize = 24;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var sub = (WarehouseSubOpcode)packet.ReadByte();
        logger.LogDebug("Warehouse operation {Sub} by {Name}", sub, session.Name);

        if ((session.IsBusy && sub is WarehouseSubOpcode.Input or WarehouseSubOpcode.Output or WarehouseSubOpcode.InventoryMove)
            || !AtWarehouseKeeper(session))
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(sub, WarehousePacketWriter.Failed));
            return;
        }

        switch (sub)
        {
            case WarehouseSubOpcode.Open:
                await OpenAsync(session);
                break;

            case WarehouseSubOpcode.Input:
                await InputAsync(session, packet);
                break;

            case WarehouseSubOpcode.Output:
                await OutputAsync(session, packet);
                break;

            case WarehouseSubOpcode.Move:
                await MoveAsync(session, packet);
                break;

            case WarehouseSubOpcode.InventoryMove:
                await InventoryMoveAsync(session, packet);
                break;
        }
    }

    private static async Task OpenAsync(UserSession session)
    {
        var slots = new List<ItemSlot>(UserSession.WarehouseMax);
        for (var i = 0; i < UserSession.WarehouseMax; i++)
            slots.Add(session.Warehouse[i]);

        await session.Client.SendPacket(WarehousePacketWriter.Contents(
            WarehouseSubOpcode.Open, session.WarehouseMoney, slots));
    }

    private async Task InputAsync(UserSession session, Packet packet)
    {
        _ = packet.ReadInt(); // NPC unique ID (unused, validated via quest event NPC)
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();
        var count = packet.ReadInt();


        if (itemId == InventoryConstants.ItemGold)
        {
            var goldOk = session.WithLock(s =>
            {
                if (count <= 0 || count > s.Money || s.WarehouseMoney > CoinMax - count)
                    return false;
                s.Money -= count;
                s.WarehouseMoney += count;
                return true;
            });
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Input, goldOk ? WarehousePacketWriter.Succeeded : WarehousePacketWriter.Failed));
            return;
        }

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null
            || srcPos >= InventoryConstants.HaveMax
            || dstPos >= WarehousePageSize
            || !Storable(itemData)
            || count <= 0
            || (itemData.Countable == 0 && count != 1))
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Input, WarehousePacketWriter.Failed));
            return;
        }

        var absSrc = InventoryConstants.SlotMax + srcPos;
        var realDst = page * WarehousePageSize + dstPos;
        if (realDst >= UserSession.WarehouseMax)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Input, WarehousePacketWriter.Failed));
            return;
        }

        var duplicate = false;
        var success = session.WithLock(s =>
        {
            var source = s.Inventory[absSrc];
            if (source.ItemId != itemId || source.Count < count || source.IsLinked)
                return false;
            if (source.State == ItemFlag.Duplicate)
            {
                duplicate = true;
                return false;
            }

            var destination = s.Warehouse[realDst];
            if (!ItemTransfer.Fits(source, destination, itemData, count))
                return false;

            ItemTransfer.Move(source, destination, count);

            var coefficient = gameDataService.GetCoefficient(s.Class);
            if (coefficient != null)
                s.RecalculateStats(coefficient, gameDataService);
            return true;
        });

        await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Input,
            success ? WarehousePacketWriter.Succeeded
            : duplicate ? WarehousePacketWriter.DuplicateItem
            : WarehousePacketWriter.Failed));
        if (success)
            await userNotificationService.SendWeightChangeAsync(session);
    }

    private async Task OutputAsync(UserSession session, Packet packet)
    {
        _ = packet.ReadInt(); // NPC unique ID (unused, validated via quest event NPC)
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();
        var count = packet.ReadInt();


        if (itemId == InventoryConstants.ItemGold)
        {
            var goldOk = session.WithLock(s =>
            {
                if (count <= 0 || count > s.WarehouseMoney || s.Money > CoinMax - count)
                    return false;
                s.WarehouseMoney -= count;
                s.Money += count;
                return true;
            });
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Output, goldOk ? WarehousePacketWriter.Succeeded : WarehousePacketWriter.Failed));
            return;
        }

        if (srcPos >= WarehousePageSize || dstPos >= InventoryConstants.HaveMax || count <= 0)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Output, WarehousePacketWriter.Failed));
            return;
        }

        var realSrc = page * WarehousePageSize + srcPos;
        if (realSrc >= UserSession.WarehouseMax)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Output, WarehousePacketWriter.Failed));
            return;
        }

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Output, WarehousePacketWriter.Failed));
            return;
        }

        var absDst = InventoryConstants.SlotMax + dstPos;

        var success = session.WithLock(s =>
        {
            var source = s.Warehouse[realSrc];
            if (source.ItemId != itemId || source.Count < count || (itemData.Countable == 0 && count != 1))
                return false;

            var destination = s.Inventory[absDst];
            if (!ItemTransfer.Fits(source, destination, itemData, count))
                return false;

            ItemTransfer.Move(source, destination, count);

            var coefficient = gameDataService.GetCoefficient(s.Class);
            if (coefficient != null)
                s.RecalculateStats(coefficient, gameDataService);
            return true;
        });

        await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Output, success ? WarehousePacketWriter.Succeeded : WarehousePacketWriter.Failed));
        if (success)
            await userNotificationService.SendWeightChangeAsync(session);
    }

    private static async Task MoveAsync(UserSession session, Packet packet)
    {
        _ = packet.ReadInt(); // NPC unique ID (unused, validated via quest event NPC)
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();
        var dstPage = packet.RemainingBytes >= 1 ? packet.ReadByte() : page;

        if (srcPos >= WarehousePageSize || dstPos >= WarehousePageSize)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Move, WarehousePacketWriter.Failed));
            return;
        }

        var realSrc = page * WarehousePageSize + srcPos;
        var realDst = dstPage * WarehousePageSize + dstPos;
        if (realSrc >= UserSession.WarehouseMax || realDst >= UserSession.WarehouseMax)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Move, WarehousePacketWriter.Failed));
            return;
        }

        var moved = session.WithLock(s =>
        {
            var source = s.Warehouse[realSrc];
            var destination = s.Warehouse[realDst];
            if (source.ItemId != itemId || !destination.IsEmpty)
                return false;

            ItemTransfer.Move(source, destination, source.Count);
            return true;
        });

        await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Move, moved ? WarehousePacketWriter.Succeeded : WarehousePacketWriter.Failed));
    }

    private static async Task InventoryMoveAsync(UserSession session, Packet packet)
    {
        _ = packet.ReadInt(); // NPC unique ID (unused, validated via quest event NPC)
        var itemId = packet.ReadInt();
        _ = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();


        if (srcPos >= InventoryConstants.HaveMax || dstPos >= InventoryConstants.HaveMax)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.InventoryMove, WarehousePacketWriter.Failed));
            return;
        }

        var absSrc = InventoryConstants.SlotMax + srcPos;
        var absDst = InventoryConstants.SlotMax + dstPos;

        var moved = session.WithLock(s =>
        {
            var source = s.Inventory[absSrc];
            var destination = s.Inventory[absDst];
            if (source.ItemId != itemId || !destination.IsEmpty)
                return false;

            ItemTransfer.Move(source, destination, source.Count);
            return true;
        });

        await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.InventoryMove, moved ? WarehousePacketWriter.Succeeded : WarehousePacketWriter.Failed));
    }

    private bool AtWarehouseKeeper(UserSession session)
    {
        if (IsKeeperBeside(session, session.Quest.EventNpcUniqueId))
            session.WarehouseKeeperId = session.Quest.EventNpcUniqueId;
        return IsKeeperBeside(session, session.WarehouseKeeperId);
    }

    private bool IsKeeperBeside(UserSession session, int npcUniqueId)
    {
        if (sessionManager.Regions.GetNpc(npcUniqueId) is not { NpcType: NpcData.TypeWarehouse } keeper
            || keeper.ZoneId != session.ZoneId)
            return false;

        var dx = session.X - keeper.X;
        var dz = session.Z - keeper.Z;
        return dx * dx + dz * dz <= GameConstants.MaxNpcInteractionRangeSq;
    }

    internal static bool Storable(ItemData itemData) =>
        itemData.Race != NonStorableRace
        && itemData.Race is < VaultTicketRaceFirst or > VaultTicketRaceLast
        && itemData.Num is < NonStorableIdFirst or > NonStorableIdLast;
}
