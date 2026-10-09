using System.Text;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IBeautyShopPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class BeautyShopPacketCoordinator(
    SessionManager sessionManager,
    ICharacterStatePersister statePersister,
    IUserNotificationService userNotificationService,
    IGameDataService gameDataService,
    IWorldPacketCoordinator worldPacketCoordinator,
    ILogger<BeautyShopPacketCoordinator> logger) : IBeautyShopPacketCoordinator
{
    public const int MakeoverCoupon = 810340000;
    private const byte LegacyRequest = 0;
    private const byte ApplyRequest = 1;
    private const int NoCoupon = -1;
    private const ushort CouponsPerChange = 1;
    private const int HeaderLength = sizeof(byte) + sizeof(byte);
    private const int AppearanceLength = sizeof(byte) + sizeof(int);

    private sealed record Restyle(int CouponSlot, int ItemId, short Durability, byte Flag, long ExpiresAt, int UniqueId,
        byte OldFace, int OldHair);

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        if (packet.RemainingBytes < HeaderLength)
        {
            await RefuseAsync(client);
            return;
        }

        var subOpcode = packet.ReadByte();
        var nameLength = packet.ReadByte();
        if (packet.RemainingBytes != nameLength + AppearanceLength)
        {
            await RefuseAsync(client);
            return;
        }

        var characterName = Encoding.ASCII.GetString(packet.ReadBytes(nameLength));
        var face = packet.ReadByte();
        var hair = packet.ReadInt();

        if (subOpcode is not (LegacyRequest or ApplyRequest)
            || !string.Equals(characterName, session.Name, StringComparison.OrdinalIgnoreCase)
            || session.Hp <= 0 || session.Trade.IsTrading || session.Trade.IsMerchanting || session.IsGathering
            || !IsAtMakeupArtist(session)
            || !CharacterLookRules.Allows(session.Race, face, hair, session.Hair))
        {
            await RefuseAsync(client);
            return;
        }

        var restyle = session.WithLock(active => Apply(active, face, hair));
        if (restyle == null)
        {
            await RefuseAsync(client);
            return;
        }

        bool saved;
        try
        {
            saved = await statePersister.SaveAsync(session);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not save the beauty shop appearance of {Name}", session.Name);
            saved = false;
        }

        if (!saved)
        {
            session.WithLock(active => Undo(active, restyle));
            await RefuseAsync(client);
            return;
        }

        await NotifyCouponTakenAsync(session, restyle.CouponSlot);
        await client.SendPacket(PreGamePacketWriter.ChangeHairResult(PreGamePacketWriter.ChangeHairSucceeded));
        if (sessionManager.GetByClientId(client.Id) != session)
            return;

        await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.Out);
        await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.In);
    }

    private bool IsAtMakeupArtist(UserSession session)
    {
        var npc = session.Quest.EventNpcUniqueId > 0
            ? sessionManager.Regions.GetNpc(session.Quest.EventNpcUniqueId)
            : null;
        return npc is { IsAlive: true }
            && npc.NpcId == NpcData.MakeupArtist
            && npc.ZoneId == session.ZoneId
            && QuestNpcInteractionService.IsInNpcRange(session, npc);
    }

    private static int FindCoupon(ItemSlot[] inventory)
    {
        for (var index = InventoryConstants.InventoryStart; index < inventory.Length; index++)
        {
            if (inventory[index].ItemId == MakeoverCoupon && inventory[index].Count > 0)
                return index;
        }

        return NoCoupon;
    }

    private static Restyle? Apply(UserSession session, byte face, int hair)
    {
        var couponSlot = FindCoupon(session.Inventory);
        if (couponSlot == NoCoupon)
            return null;

        var slot = session.Inventory[couponSlot];
        var restyle = new Restyle(couponSlot, slot.ItemId, slot.Durability, slot.Flag, slot.ExpiresAt, slot.UniqueId,
            session.Face, session.Hair);
        TakeOne(slot);
        session.Face = face;
        session.Hair = hair;
        return restyle;
    }

    private void Undo(UserSession session, Restyle restyle)
    {
        session.Face = restyle.OldFace;
        session.Hair = restyle.OldHair;

        var slot = session.Inventory[restyle.CouponSlot];
        if (slot.IsEmpty)
        {
            slot.ItemId = restyle.ItemId;
            slot.Durability = restyle.Durability;
            slot.Count = CouponsPerChange;
            slot.Flag = restyle.Flag;
            slot.ExpiresAt = restyle.ExpiresAt;
            slot.UniqueId = restyle.UniqueId;
        }
        else if (slot.ItemId == restyle.ItemId && slot.UniqueId == restyle.UniqueId
                 && slot.Count + CouponsPerChange <= InventoryConstants.MaxStackCount)
            slot.Count += CouponsPerChange;
        else
            logger.LogWarning("Could not return the makeover coupon to slot {Slot} of {Name} after a failed save",
                restyle.CouponSlot, session.Name);
    }

    private async Task NotifyCouponTakenAsync(UserSession session, int couponSlot)
    {
        var slot = session.Inventory[couponSlot];
        await userNotificationService.SendStackChangeAsync(session, (byte)couponSlot, slot.ItemId, slot.Count, slot.Durability);

        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);
        await userNotificationService.SendWeightChangeAsync(session);
    }

    private static void TakeOne(ItemSlot slot)
    {
        if (slot.Count <= CouponsPerChange)
            slot.Clear();
        else
            slot.Count -= CouponsPerChange;
    }

    private static Task RefuseAsync(IClient client) =>
        client.SendPacket(PreGamePacketWriter.ChangeHairResult(PreGamePacketWriter.ChangeHairFailed));
}
