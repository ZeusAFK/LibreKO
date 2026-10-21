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
    private const int RentalCapDays = 60;
    private const int UseKeyBytes = 11;
    internal const byte PasswordTriesAllowed = 3;
    private const int CoinMax = 2_100_000_000;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null) return;

        var sub = (VipWarehouseSubOpcode)packet.ReadByte();

        if (session.IsBusy)
        {
            await SendResult(session, sub, VipWarehouseResult.Failed);
            return;
        }

        if (PasswordPending(session) && sub is VipWarehouseSubOpcode.Input or VipWarehouseSubOpcode.Output
                or VipWarehouseSubOpcode.Store or VipWarehouseSubOpcode.InventoryMove)
        {
            await SendResult(session, sub, VipWarehouseResult.Failed);
            return;
        }

        if (PasswordPending(session) && sub is VipWarehouseSubOpcode.SetPassword
                or VipWarehouseSubOpcode.CancelPassword or VipWarehouseSubOpcode.ChangePassword)
        {
            await SendResult(session, sub, VipWarehouseResult.Rejected);
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
        if (PasswordPending(session))
        {
            await SendResult(session, VipWarehouseSubOpcode.EnterPassword, VipWarehouseResult.Succeeded);
            return;
        }

        await SendOpenResponseAsync(session);
    }

    private static bool PasswordPending(UserSession session) =>
        session.VipPasswordRequest != 0 && session.VipPassword.Length == VipVault.PinLength;

    private static async Task SendOpenResponseAsync(UserSession session)
    {
        var slots = new List<ItemSlot>(UserSession.VipWarehouseMax);
        for (var i = 0; i < UserSession.VipWarehouseMax; i++)
            slots.Add(session.VipWarehouse[i]);

        await session.Client.SendPacket(WarehousePacketWriter.VipContents(
            session.VipPassword.Length == VipVault.PinLength, session.VipVaultExpiry > DateTime.UtcNow,
            VipVault.ExpiresAt(session.VipVaultExpiry), slots));
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
            || page != VipVault.OwnStorage
            || dstPos >= UserSession.VipWarehouseMax
            || !WarehousePacketCoordinator.Storable(itemData)
            || count <= 0
            || (itemData.Countable == 0 && count != 1))
        {
            await SendResult(session, VipWarehouseSubOpcode.Input, VipWarehouseResult.Failed);
            return;
        }

        var absSrc = InventoryConstants.SlotMax + srcPos;
        var realDst = dstPos;

        var success = session.WithLock(s =>
        {
            var source = s.Inventory[absSrc];
            if (source.ItemId != itemId || source.Count < count || source.IsLinked)
                return false;

            var destination = s.VipWarehouse[realDst];
            if (!ItemTransfer.Fits(source, destination, itemData, count))
                return false;

            ItemTransfer.Move(source, destination, count);

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
        if (session.VipVaultExpiry <= DateTime.UtcNow)
        {
            await SendResult(session, VipWarehouseSubOpcode.Output, VipWarehouseResult.Expired);
            return;
        }

        _ = packet.ReadInt();
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();
        var count = packet.ReadInt();

        if (page != VipVault.OwnStorage || srcPos >= UserSession.VipWarehouseMax
            || dstPos >= InventoryConstants.HaveMax || count <= 0)
        {
            await SendResult(session, VipWarehouseSubOpcode.Output, VipWarehouseResult.Failed);
            return;
        }

        var realSrc = srcPos;

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
            if (!ItemTransfer.Fits(source, destination, itemData, count))
                return false;

            ItemTransfer.Move(source, destination, count);

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

        if (page != VipVault.OwnStorage || srcPos >= UserSession.VipWarehouseMax
            || dstPos >= UserSession.VipWarehouseMax)
        {
            await SendResult(session, VipWarehouseSubOpcode.Store, VipWarehouseResult.Failed);
            return;
        }

        var realSrc = srcPos;
        var realDst = dstPos;

        var moved = session.WithLock(s =>
        {
            var source = s.VipWarehouse[realSrc];
            var destination = s.VipWarehouse[realDst];
            if (source.ItemId != itemId || !destination.IsEmpty)
                return false;

            ItemTransfer.Move(source, destination, source.Count);
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

            ItemTransfer.Move(source, destination, source.Count);
            return true;
        });

        await SendResult(session, VipWarehouseSubOpcode.InventoryMove, moved ? VipWarehouseResult.Succeeded : VipWarehouseResult.Failed);
    }

    private async Task UseVaultAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < UseKeyBytes)
        {
            await SendResult(session, VipWarehouseSubOpcode.UseVault, VipWarehouseResult.Rejected);
            return;
        }

        _ = packet.ReadInt();
        var itemId = packet.ReadInt();
        _ = packet.ReadByte();
        var bagPos = packet.ReadByte();
        var storage = packet.ReadByte();
        var days = VipVault.KeyDays(itemId);
        if (days <= 0)
        {
            await SendResult(session, VipWarehouseSubOpcode.UseVault, VipWarehouseResult.NotAVaultKey);
            return;
        }
        if (storage != VipVault.OwnStorage || bagPos >= InventoryConstants.HaveMax)
        {
            await SendResult(session, VipWarehouseSubOpcode.UseVault, VipWarehouseResult.Rejected);
            return;
        }

        var keySlot = InventoryConstants.SlotMax + bagPos;
        var outcome = session.WithLock(s =>
        {
            var now = DateTime.UtcNow;
            if (s.VipVaultExpiry - now > TimeSpan.FromDays(RentalCapDays))
                return VipWarehouseResult.RentalAtCap;

            var slot = s.Inventory[keySlot];
            if (slot.ItemId != itemId || slot.Count == 0)
                return VipWarehouseResult.Rejected;

            var basis = s.VipVaultExpiry > now ? s.VipVaultExpiry : now;
            s.VipVaultExpiry = basis.AddDays(days);
            s.VipVaultExpiryAnnounced = false;

            slot.Count--;
            if (slot.Count == 0) slot.Clear();
            return VipWarehouseResult.Succeeded;
        });

        if (outcome != VipWarehouseResult.Succeeded)
        {
            await SendResult(session, VipWarehouseSubOpcode.UseVault, outcome);
            return;
        }

        var newExpiry = session.VipVaultExpiry;
        var key = session.Inventory[keySlot];
        await PersistAccountAsync(session);
        await userNotificationService.SendStackChangeAsync(
            session, (byte)keySlot, key.IsEmpty ? 0 : key.ItemId, key.Count, key.Durability);
        await session.Client.SendPacket(WarehousePacketWriter.VipVaultExtended(
            VipVault.OwnStorage, VipVault.ExpiresAt(newExpiry)));

        logger.LogInformation("{Name} activated VIP vault with key {ItemId}: expires {Expiry:u}",
            session.Name, itemId, newExpiry);
    }

    private static async Task SendResult(UserSession session, VipWarehouseSubOpcode sub, VipWarehouseResult result)
    {
        await session.Client.SendPacket(WarehousePacketWriter.VipResult(sub, result));
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
        if (session.VipPasswordFailures >= PasswordTriesAllowed)
        {
            await session.Client.SendPacket(WarehousePacketWriter.VipPasswordRejected(session.VipPasswordFailures));
            return;
        }

        if (!IsValidPin(pin) || pin != session.VipPassword)
        {
            session.VipPasswordFailures++;
            await session.Client.SendPacket(WarehousePacketWriter.VipPasswordRejected(session.VipPasswordFailures));
            return;
        }

        session.VipPasswordFailures = 0;
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
        if (pin.Length != VipVault.PinLength) return false;
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
