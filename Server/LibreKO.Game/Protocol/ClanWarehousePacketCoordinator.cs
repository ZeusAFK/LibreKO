using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IClanWarehousePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class ClanWarehousePacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    IKnightsRuntimeService knightsRuntime,
    IServiceScopeFactory scopeFactory,
    ILogger<ClanWarehousePacketCoordinator> logger) : IClanWarehousePacketCoordinator
{


    private const int ItemNoTradeMin = 900_000_001;
    private const int ItemNoTradeMax = 999_999_999;
    private const int CoinMax = 2_100_000_000;
    private const int WarehousePageSize = 24;
    private const int ItemCountMax = 9999;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null) return;

        var sub = (WarehouseSubOpcode)packet.ReadByte();

        if (session.KnightsId <= 0 || sessionManager.Knights.GetClan(session.KnightsId) == null)
        {
            await SendResult(session, sub, ClanWarehouseResult.Failed);
            return;
        }

        switch (sub)
        {
            case WarehouseSubOpcode.Open: await OpenAsync(session); break;
            case WarehouseSubOpcode.Input: await InputAsync(session, packet); break;
            case WarehouseSubOpcode.Output: await OutputAsync(session, packet); break;
            case WarehouseSubOpcode.Move: await MoveAsync(session, packet); break;
            case WarehouseSubOpcode.InventoryMove: await InventoryMoveAsync(session, packet); break;
            default:
                logger.LogDebug("Unknown clan warehouse sub-opcode {Sub:X2}", sub);
                break;
        }
    }

    private async Task OpenAsync(UserSession session)
    {
        var clan = sessionManager.Knights.GetClan(session.KnightsId)!;
        var slots = sessionManager.Knights.GetClanWarehouse(session.KnightsId);
        if (slots == null)
        {
            await SendResult(session, WarehouseSubOpcode.Open, ClanWarehouseResult.Failed);
            return;
        }

        var contents = new List<ItemSlot>(KnightsManager.ClanWarehouseSlots);
        for (var i = 0; i < KnightsManager.ClanWarehouseSlots; i++)
            contents.Add(slots[i]);

        await session.Client.SendPacket(ClanWarehousePacketWriter.Contents(
            WarehouseSubOpcode.Open, ClanWarehouseResult.Succeeded, clan.ClanWarehouseGold, contents));
    }

    private async Task InputAsync(UserSession session, Packet packet)
    {
        _ = packet.ReadInt();
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();
        var count = packet.ReadInt();

        if (itemId == InventoryConstants.ItemGold)
        {
            KnightsEntity? clanRefGold = null;
            var goldOk = sessionManager.Knights.WithClanWarehouse(session.KnightsId, (c, _) =>
            {
                clanRefGold = c;
                return session.WithLock(s =>
                {
                    if (count <= 0 || count > s.Money || c.ClanWarehouseGold > CoinMax - count)
                        return false;
                    s.Money -= count;
                    c.ClanWarehouseGold += count;
                    return true;
                });
            }, false);

            if (!goldOk || clanRefGold == null)
            {
                await SendResult(session, WarehouseSubOpcode.Input, ClanWarehouseResult.Failed);
                return;
            }
            await PersistClanAsync(clanRefGold, items: false);
            await SendResult(session, WarehouseSubOpcode.Input, ClanWarehouseResult.Succeeded);
            return;
        }

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null
            || srcPos >= InventoryConstants.HaveMax
            || dstPos >= WarehousePageSize
            || (itemId >= ItemNoTradeMin && itemId <= ItemNoTradeMax)
            || count <= 0
            || (itemData.Countable == 0 && count != 1))
        {
            await SendResult(session, WarehouseSubOpcode.Input, ClanWarehouseResult.Failed);
            return;
        }

        var absSrc = InventoryConstants.SlotMax + srcPos;
        var realDst = page * WarehousePageSize + dstPos;
        if (realDst >= KnightsManager.ClanWarehouseSlots)
        {
            await SendResult(session, WarehouseSubOpcode.Input, ClanWarehouseResult.Failed);
            return;
        }

        KnightsEntity? clanRef = null;
        var success = sessionManager.Knights.WithClanWarehouse(session.KnightsId, (c, slots) =>
        {
            clanRef = c;
            return session.WithLock(s =>
            {
                var source = s.Inventory[absSrc];
                if (source.ItemId != itemId || source.Count < count)
                    return false;

                var destination = slots[realDst];
                if (!CanMergeOrPlace(itemData, destination, itemId, count))
                    return false;

                var destinationWasEmpty = destination.IsEmpty;
                destination.ItemId = itemId;
                destination.Count += (ushort)count;
                destination.Flag = source.Flag;
                if (destinationWasEmpty) destination.Durability = source.Durability;
                if (destination.Count > ItemCountMax) destination.Count = ItemCountMax;

                source.Count -= (ushort)count;
                if (source.Count == 0) source.Clear();

                var coefficient = gameDataService.GetCoefficient(s.Class);
                if (coefficient != null)
                    s.RecalculateStats(coefficient, gameDataService);
                return true;
            });
        }, false);

        if (!success || clanRef == null)
        {
            await SendResult(session, WarehouseSubOpcode.Input, ClanWarehouseResult.Failed);
            return;
        }

        await PersistClanAsync(clanRef, items: true);
        await BroadcastDepositAsync(session, clanRef.Id, itemId, count, isDeposit: true);
        await SendResult(session, WarehouseSubOpcode.Input, ClanWarehouseResult.Succeeded);
        await userNotificationService.SendWeightChangeAsync(session);
    }

    private async Task OutputAsync(UserSession session, Packet packet)
    {
        if (!IsLeaderOrAssistant(session))
        {
            packet.ReadInt(); packet.ReadInt(); packet.ReadByte(); packet.ReadByte(); packet.ReadByte(); packet.ReadInt();
            await SendResult(session, WarehouseSubOpcode.Output, ClanWarehouseResult.Failed);
            return;
        }

        _ = packet.ReadInt();
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();
        var count = packet.ReadInt();

        if (itemId == InventoryConstants.ItemGold)
        {
            KnightsEntity? clanRefGold = null;
            var goldOk = sessionManager.Knights.WithClanWarehouse(session.KnightsId, (c, _) =>
            {
                clanRefGold = c;
                return session.WithLock(s =>
                {
                    if (count <= 0 || count > c.ClanWarehouseGold || s.Money > CoinMax - count)
                        return false;
                    c.ClanWarehouseGold -= count;
                    s.Money += count;
                    return true;
                });
            }, false);

            if (!goldOk || clanRefGold == null)
            {
                await SendResult(session, WarehouseSubOpcode.Output, ClanWarehouseResult.Failed);
                return;
            }
            await PersistClanAsync(clanRefGold, items: false);
            await SendResult(session, WarehouseSubOpcode.Output, ClanWarehouseResult.Succeeded);
            return;
        }

        if (srcPos >= WarehousePageSize || dstPos >= InventoryConstants.HaveMax || count <= 0)
        {
            await SendResult(session, WarehouseSubOpcode.Output, ClanWarehouseResult.Failed);
            return;
        }

        var realSrc = page * WarehousePageSize + srcPos;
        if (realSrc >= KnightsManager.ClanWarehouseSlots)
        {
            await SendResult(session, WarehouseSubOpcode.Output, ClanWarehouseResult.Failed);
            return;
        }

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
        {
            await SendResult(session, WarehouseSubOpcode.Output, ClanWarehouseResult.Failed);
            return;
        }

        var absDst = InventoryConstants.SlotMax + dstPos;

        KnightsEntity? clanRef = null;
        var success = sessionManager.Knights.WithClanWarehouse(session.KnightsId, (c, slots) =>
        {
            clanRef = c;
            return session.WithLock(s =>
            {
                var source = slots[realSrc];
                if (source.ItemId != itemId || source.Count < count
                    || (itemData.Countable == 0 && count != 1))
                    return false;

                var destination = s.Inventory[absDst];
                if (!CanMergeOrPlace(itemData, destination, itemId, count))
                    return false;

                var destinationWasEmpty = destination.IsEmpty;
                destination.ItemId = itemId;
                destination.Count += (ushort)count;
                destination.Flag = source.Flag;
                if (destinationWasEmpty) destination.Durability = source.Durability;

                source.Count -= (ushort)count;
                if (source.Count == 0) source.Clear();

                var coefficient = gameDataService.GetCoefficient(s.Class);
                if (coefficient != null)
                    s.RecalculateStats(coefficient, gameDataService);
                return true;
            });
        }, false);

        if (!success || clanRef == null)
        {
            await SendResult(session, WarehouseSubOpcode.Output, ClanWarehouseResult.Failed);
            return;
        }

        await PersistClanAsync(clanRef, items: true);
        await BroadcastDepositAsync(session, clanRef.Id, itemId, count, isDeposit: false);
        await SendResult(session, WarehouseSubOpcode.Output, ClanWarehouseResult.Succeeded);
        await userNotificationService.SendWeightChangeAsync(session);
    }

    private async Task MoveAsync(UserSession session, Packet packet)
    {
        if (!IsLeaderOrAssistant(session))
        {
            packet.ReadInt(); packet.ReadInt(); packet.ReadByte(); packet.ReadByte(); packet.ReadByte();
            await SendResult(session, WarehouseSubOpcode.Move, ClanWarehouseResult.Failed);
            return;
        }

        _ = packet.ReadInt();
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();

        if (srcPos >= WarehousePageSize || dstPos >= WarehousePageSize)
        {
            await SendResult(session, WarehouseSubOpcode.Move, ClanWarehouseResult.Failed);
            return;
        }

        var realSrc = page * WarehousePageSize + srcPos;
        var realDst = page * WarehousePageSize + dstPos;
        if (realSrc >= KnightsManager.ClanWarehouseSlots || realDst >= KnightsManager.ClanWarehouseSlots)
        {
            await SendResult(session, WarehouseSubOpcode.Move, ClanWarehouseResult.Failed);
            return;
        }

        KnightsEntity? clanRef = null;
        var moved = sessionManager.Knights.WithClanWarehouse(session.KnightsId, (c, slots) =>
        {
            clanRef = c;
            var source = slots[realSrc];
            var destination = slots[realDst];
            if (source.ItemId != itemId || !destination.IsEmpty)
                return false;

            destination.ItemId = source.ItemId;
            destination.Durability = source.Durability;
            destination.Count = source.Count;
            destination.Flag = source.Flag;
            source.Clear();
            return true;
        }, false);

        if (!moved || clanRef == null)
        {
            await SendResult(session, WarehouseSubOpcode.Move, ClanWarehouseResult.Failed);
            return;
        }

        await PersistClanAsync(clanRef, items: true);
        await SendResult(session, WarehouseSubOpcode.Move, ClanWarehouseResult.Succeeded);
    }

    private async Task InventoryMoveAsync(UserSession session, Packet packet)
    {
        if (!IsLeaderOrAssistant(session))
        {
            packet.ReadInt(); packet.ReadInt(); packet.ReadByte(); packet.ReadByte(); packet.ReadByte();
            await SendResult(session, WarehouseSubOpcode.InventoryMove, ClanWarehouseResult.Failed);
            return;
        }

        _ = packet.ReadInt();
        var itemId = packet.ReadInt();
        _ = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();

        if (srcPos >= InventoryConstants.HaveMax || dstPos >= InventoryConstants.HaveMax)
        {
            await SendResult(session, WarehouseSubOpcode.InventoryMove, ClanWarehouseResult.Failed);
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

            destination.ItemId = source.ItemId;
            destination.Durability = source.Durability;
            destination.Count = source.Count;
            destination.Flag = source.Flag;
            source.Clear();
            return true;
        });

        await SendResult(session, WarehouseSubOpcode.InventoryMove, moved ? ClanWarehouseResult.Succeeded : ClanWarehouseResult.Failed);
    }

    private static async Task SendResult(UserSession session, WarehouseSubOpcode sub, ClanWarehouseResult result)
    {
        await session.Client.SendPacket(ClanWarehousePacketWriter.Result(sub, result));
    }

    private static bool IsLeaderOrAssistant(UserSession session)
        => session.KnightsFame == 1 || session.KnightsFame == 2;

    private static bool CanMergeOrPlace(ItemData itemData, ItemSlot destination, int itemId, int count)
    {
        if (destination.IsEmpty) return true;
        if (destination.ItemId != itemId || itemData.Countable == 0) return false;
        return destination.Count + count <= ItemCountMax;
    }

    private async Task PersistClanAsync(KnightsEntity clan, bool items)
    {
        if (items)
            clan.ClanWarehouseItems = sessionManager.Knights.SerializeClanWarehouse(clan.Id);

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
        await repo.UpdateAsync(clan);
    }

    private async Task BroadcastDepositAsync(UserSession actor, short clanId, int itemId, int count, bool isDeposit)
    {
        var itemName = gameDataService.GetItem(itemId)?.Name ?? $"Item#{itemId}";
        var verb = isDeposit ? "deposited" : "withdrew";
        var message = count > 1
            ? $"### {actor.Name} {verb} {count}x {itemName} from clan bank ###"
            : $"### {actor.Name} {verb} {itemName} from clan bank ###";

        var pkt = ChatPacketWriter.SystemNotice((byte)actor.Nation, message);

        await knightsRuntime.NotifyOnlineClanMembersAsync(clanId, pkt);
    }
}
