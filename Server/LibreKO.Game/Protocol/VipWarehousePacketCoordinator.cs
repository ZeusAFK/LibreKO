using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IVipWarehousePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class VipWarehousePacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    IServiceScopeFactory scopeFactory,
    ILogger<VipWarehousePacketCoordinator> logger) : IVipWarehousePacketCoordinator
{


    private const int VipVaultKey = 800_442_000;
    private const int VipSafeKey1 = 810_442_000;
    private const int VipSafeKey7 = 998_019_000;

    // Vault extension durations.
    private const int VaultDurationDaysDefault = 7;
    private const int VaultDurationDaysSafe1 = 1;
    private const int VaultDurationDaysSafe7 = 7;
    private const int VipWarehousePageSize = 12;
    private const int ItemCountMax = 9999;
    private const int ItemNoTradeMin = 900_000_001;
    private const int ItemNoTradeMax = 999_999_999;
    private const int CoinMax = 2_100_000_000;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null) return;

        var sub = (VipWarehouseSubOpcode)packet.ReadByte();

        if (session.Hp <= 0 || session.Trade.IsTrading || session.Trade.IsMerchanting
            || session.IsGathering)
        {
            await SendResult(session, sub, VipWarehouseResult.Failed);
            return;
        }

        switch (sub)
        {
            case VipWarehouseSubOpcode.Open: await OpenAsync(session); break;
            case VipWarehouseSubOpcode.Input: await InputAsync(session, packet); break;
            case VipWarehouseSubOpcode.Output: await OutputAsync(session, packet); break;
            case VipWarehouseSubOpcode.Store: await MoveAsync(session, packet); break;
            case VipWarehouseSubOpcode.InventoryMove: await InventoryMoveAsync(session, packet); break;
            case VipWarehouseSubOpcode.UseVault: await UseVaultAsync(session, packet); break;
            case VipWarehouseSubOpcode.SetPassword: await SetPasswordAsync(session, packet); break;
            case VipWarehouseSubOpcode.CancelPassword: await CancelPasswordAsync(session); break;
            case VipWarehouseSubOpcode.ChangePassword: await ChangePasswordAsync(session, packet); break;
            case VipWarehouseSubOpcode.EnterPassword: await EnterPasswordAsync(session, packet); break;
            default:
                logger.LogDebug("Unknown VIP warehouse sub-opcode {Sub:X2}", sub);
                break;
        }
    }

    private async Task OpenAsync(UserSession session)
    {
        if (session.VipVaultExpiry <= DateTime.UtcNow)
        {
            await SendResult(session, VipWarehouseSubOpcode.Open, VipWarehouseResult.Expired);
            return;
        }

        // hasn't entered it yet, prompt instead of opening the panel.
        if (session.VipPasswordRequest != 0 && session.VipPassword.Length == 4)
        {
            await SendResult(session, VipWarehouseSubOpcode.EnterPassword, VipWarehouseResult.Succeeded);
            return;
        }

        await SendOpenResponseAsync(session);
    }

    private static async Task SendOpenResponseAsync(UserSession session)
    {
        // Remaining seconds — client uses this for "expires in X" display.
        var remaining = (long)Math.Max(0, (session.VipVaultExpiry - DateTime.UtcNow).TotalSeconds);

        var slots = new List<ItemSlot>(UserSession.VipWarehouseMax);
        for (var i = 0; i < UserSession.VipWarehouseMax; i++)
            slots.Add(session.VipWarehouse[i]);

        await session.Client.SendPacket(WarehousePacketWriter.VipContents(
            VipWarehouseSubOpcode.Open, VipWarehouseResult.Succeeded, (int)Math.Min(remaining, int.MaxValue), slots));
    }

    private async Task InputAsync(UserSession session, Packet packet)
    {
        if (session.VipVaultExpiry <= DateTime.UtcNow)
        {
            await SendResult(session, VipWarehouseSubOpcode.Input, VipWarehouseResult.Expired);
            return;
        }

        _ = packet.ReadInt(); // npc id (ignored)
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();
        var count = packet.ReadInt();

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null
            || srcPos >= InventoryConstants.HaveMax
            || dstPos >= VipWarehousePageSize
            || (itemId >= ItemNoTradeMin && itemId <= ItemNoTradeMax)
            || count <= 0
            || (itemData.Countable == 0 && count != 1))
        {
            await SendResult(session, VipWarehouseSubOpcode.Input, VipWarehouseResult.Failed);
            return;
        }

        var absSrc = InventoryConstants.SlotMax + srcPos;
        var realDst = page * VipWarehousePageSize + dstPos;
        if (realDst >= UserSession.VipWarehouseMax)
        {
            await SendResult(session, VipWarehouseSubOpcode.Input, VipWarehouseResult.Failed);
            return;
        }

        var success = session.WithLock(s =>
        {
            var source = s.Inventory[absSrc];
            if (source.ItemId != itemId || source.Count < count)
                return false;

            var destination = s.VipWarehouse[realDst];
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

        if (!success)
        {
            await SendResult(session, VipWarehouseSubOpcode.Input, VipWarehouseResult.Failed);
            return;
        }

        await PersistAccountAsync(session);
        await SendResult(session, VipWarehouseSubOpcode.Input, VipWarehouseResult.Succeeded);
        await userNotificationService.SendWeightChangeAsync(session);
    }

    private async Task OutputAsync(UserSession session, Packet packet)
    {
        _ = packet.ReadInt();
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();
        var count = packet.ReadInt();

        if (srcPos >= VipWarehousePageSize || dstPos >= InventoryConstants.HaveMax || count <= 0)
        {
            await SendResult(session, VipWarehouseSubOpcode.Output, VipWarehouseResult.Failed);
            return;
        }

        var realSrc = page * VipWarehousePageSize + srcPos;
        if (realSrc >= UserSession.VipWarehouseMax)
        {
            await SendResult(session, VipWarehouseSubOpcode.Output, VipWarehouseResult.Failed);
            return;
        }

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
        {
            await SendResult(session, VipWarehouseSubOpcode.Output, VipWarehouseResult.Failed);
            return;
        }

        var absDst = InventoryConstants.SlotMax + dstPos;

        var success = session.WithLock(s =>
        {
            var source = s.VipWarehouse[realSrc];
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

        if (!success)
        {
            await SendResult(session, VipWarehouseSubOpcode.Output, VipWarehouseResult.Failed);
            return;
        }

        await PersistAccountAsync(session);
        await SendResult(session, VipWarehouseSubOpcode.Output, VipWarehouseResult.Succeeded);
        await userNotificationService.SendWeightChangeAsync(session);
    }

    private async Task MoveAsync(UserSession session, Packet packet)
    {
        if (session.VipVaultExpiry <= DateTime.UtcNow)
        {
            await SendResult(session, VipWarehouseSubOpcode.Store, VipWarehouseResult.Expired);
            return;
        }

        _ = packet.ReadInt();
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();

        if (srcPos >= VipWarehousePageSize || dstPos >= VipWarehousePageSize)
        {
            await SendResult(session, VipWarehouseSubOpcode.Store, VipWarehouseResult.Failed);
            return;
        }

        var realSrc = page * VipWarehousePageSize + srcPos;
        var realDst = page * VipWarehousePageSize + dstPos;
        if (realSrc >= UserSession.VipWarehouseMax || realDst >= UserSession.VipWarehouseMax)
        {
            await SendResult(session, VipWarehouseSubOpcode.Store, VipWarehouseResult.Failed);
            return;
        }

        var moved = session.WithLock(s =>
        {
            var source = s.VipWarehouse[realSrc];
            var destination = s.VipWarehouse[realDst];
            if (source.ItemId != itemId || !destination.IsEmpty)
                return false;

            destination.ItemId = source.ItemId;
            destination.Durability = source.Durability;
            destination.Count = source.Count;
            destination.Flag = source.Flag;
            source.Clear();
            return true;
        });

        if (!moved)
        {
            await SendResult(session, VipWarehouseSubOpcode.Store, VipWarehouseResult.Failed);
            return;
        }

        await PersistAccountAsync(session);
        await SendResult(session, VipWarehouseSubOpcode.Store, VipWarehouseResult.Succeeded);
    }

    private async Task InventoryMoveAsync(UserSession session, Packet packet)
    {
        _ = packet.ReadInt();
        var itemId = packet.ReadInt();
        _ = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();

        if (srcPos >= InventoryConstants.HaveMax || dstPos >= InventoryConstants.HaveMax)
        {
            await SendResult(session, VipWarehouseSubOpcode.InventoryMove, VipWarehouseResult.Failed);
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

        await SendResult(session, VipWarehouseSubOpcode.InventoryMove, moved ? VipWarehouseResult.Succeeded : VipWarehouseResult.Failed);
    }

    private async Task UseVaultAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4)
        {
            await SendResult(session, VipWarehouseSubOpcode.UseVault, VipWarehouseResult.Failed);
            return;
        }

        var itemId = packet.ReadInt();
        var days = itemId switch
        {
            VipVaultKey => VaultDurationDaysDefault,
            VipSafeKey1 => VaultDurationDaysSafe1,
            VipSafeKey7 => VaultDurationDaysSafe7,
            _ => 0
        };
        if (days <= 0)
        {
            await SendResult(session, VipWarehouseSubOpcode.UseVault, VipWarehouseResult.Failed);
            return;
        }

        var newExpiry = session.WithLock(s =>
        {
            var keySlot = FindKeyItemSlot(s, itemId);
            if (keySlot < 0)
                return (DateTime?)null;

            var basis = s.VipVaultExpiry > DateTime.UtcNow ? s.VipVaultExpiry : DateTime.UtcNow;
            s.VipVaultExpiry = basis.AddDays(days);

            var slot = s.Inventory[keySlot];
            slot.Count--;
            if (slot.Count == 0) slot.Clear();
            return s.VipVaultExpiry;
        });

        if (newExpiry == null)
        {
            await SendResult(session, VipWarehouseSubOpcode.UseVault, VipWarehouseResult.Failed);
            return;
        }

        await PersistAccountAsync(session);

        await session.Client.SendPacket(WarehousePacketWriter.VipVaultExtended(
            VipWarehouseSubOpcode.UseVault, VipWarehouseResult.Succeeded, (int)(newExpiry.Value - DateTime.UtcNow).TotalSeconds));

        logger.LogInformation("{Name} activated VIP vault with key {ItemId}: expires {Expiry:u}",
            session.Name, itemId, newExpiry.Value);
    }

    private static int FindKeyItemSlot(UserSession session, int itemId)
    {
        for (var i = InventoryConstants.InventoryStart;
             i < InventoryConstants.InventoryStart + InventoryConstants.HaveMax;
             i++)
        {
            if (session.Inventory[i].ItemId == itemId && session.Inventory[i].Count > 0)
                return i;
        }
        return -1;
    }

    private static async Task SendResult(UserSession session, VipWarehouseSubOpcode sub, VipWarehouseResult result)
    {
        await session.Client.SendPacket(WarehousePacketWriter.VipResult(sub, result));
    }

    private static bool CanMergeOrPlace(ItemData itemData, ItemSlot destination, int itemId, int count)
    {
        if (destination.IsEmpty) return true;
        if (destination.ItemId != itemId || itemData.Countable == 0) return false;
        return destination.Count + count <= ItemCountMax;
    }

    private async Task SetPasswordAsync(UserSession session, Packet packet)
    {
        var pin = ReadPin(packet);
        if (!IsValidPin(pin))
        {
            await SendResult(session, VipWarehouseSubOpcode.SetPassword, VipWarehouseResult.Rejected);
            return;
        }

        // succeeds and rewrites. We mirror that.
        session.VipPassword = pin;
        session.VipPasswordRequest = 1;
        await PersistAccountAsync(session);
        await SendResult(session, VipWarehouseSubOpcode.SetPassword, VipWarehouseResult.Succeeded);
    }

    private async Task CancelPasswordAsync(UserSession session)
    {
        session.VipPassword = string.Empty;
        session.VipPasswordRequest = 0;
        await PersistAccountAsync(session);
        await SendResult(session, VipWarehouseSubOpcode.CancelPassword, VipWarehouseResult.Succeeded);
    }

    private async Task ChangePasswordAsync(UserSession session, Packet packet)
    {
        var pin = ReadPin(packet);
        if (!IsValidPin(pin))
        {
            await SendResult(session, VipWarehouseSubOpcode.ChangePassword, VipWarehouseResult.Rejected);
            return;
        }

        session.VipPassword = pin;
        // request stays 1; if not, it stays 0 (effectively making this a Set).
        await PersistAccountAsync(session);
        await SendResult(session, VipWarehouseSubOpcode.ChangePassword, VipWarehouseResult.Succeeded);
    }

    private async Task EnterPasswordAsync(UserSession session, Packet packet)
    {
        var pin = ReadPin(packet);
        if (!IsValidPin(pin) || pin != session.VipPassword)
        {
            await SendResult(session, VipWarehouseSubOpcode.EnterPassword, VipWarehouseResult.Rejected);
            return;
        }

        session.VipPasswordRequest = 0;

        await session.Client.SendPacket(WarehousePacketWriter.VipPasswordAccepted(
            VipWarehouseSubOpcode.EnterPassword, VipWarehouseResult.Succeeded));

        if (session.VipVaultExpiry > DateTime.UtcNow)
            await SendOpenResponseAsync(session);
    }

    private static string ReadPin(Packet packet)
    {
        // PINs are sent as sbyte-prefixed strings on the wire.
        try { return packet.ReadSByteString() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static bool IsValidPin(string pin)
    {
        if (pin.Length != 4) return false;
        for (var i = 0; i < pin.Length; i++)
            if (pin[i] < '0' || pin[i] > '9') return false;
        return true;
    }

    private async Task PersistAccountAsync(UserSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var accountRepo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var account = await accountRepo.GetById(session.AccountId);
        if (account == null) return;
        account.VipWarehouseItems = session.SerializeVipWarehouse();
        account.VipVaultExpiry = session.VipVaultExpiry;
        account.VipPassword = session.VipPassword;
        await accountRepo.UpdateAsync(account);
    }

}
