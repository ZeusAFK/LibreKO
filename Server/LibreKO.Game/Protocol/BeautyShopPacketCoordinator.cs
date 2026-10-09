using System.Text;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IBeautyShopPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class BeautyShopPacketCoordinator(
    IServiceScopeFactory scopeFactory,
    SessionManager sessionManager,
    IUserSessionCharacterMapper characterMapper,
    IUserNotificationService userNotificationService,
    IGameDataService gameDataService,
    IWorldPacketCoordinator worldPacketCoordinator,
    ILogger<BeautyShopPacketCoordinator> logger) : IBeautyShopPacketCoordinator
{
    public const int MakeoverCoupon = 810340000;
    private const byte LegacyRequest = 0;
    private const byte ApplyRequest = 1;
    private const int NoCoupon = -1;
    private const int HeaderLength = sizeof(byte) + sizeof(byte);
    private const int AppearanceLength = sizeof(byte) + sizeof(int);

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

        var couponSlot = FindCoupon(session.Inventory);
        if (couponSlot == NoCoupon)
        {
            await RefuseAsync(client);
            return;
        }

        bool saved;
        try
        {
            saved = await SaveAsync(session, couponSlot, face, hair);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not save the beauty shop appearance of {Name}", session.Name);
            saved = false;
        }

        if (!saved)
        {
            await RefuseAsync(client);
            return;
        }

        session.Face = face;
        session.Hair = hair;
        await TakeCouponAsync(session, couponSlot);
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

    private async Task<bool> SaveAsync(UserSession session, int couponSlot, byte face, int hair)
    {
        using var scope = scopeFactory.CreateScope();
        var characters = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var character = await characters.GetById(session.CharacterId);
        if (character == null || character.AccountId != session.AccountId)
            return false;

        var paidInventory = session.Inventory.Select(CopyOf).ToArray();
        TakeOne(paidInventory[couponSlot]);

        characterMapper.ApplyToCharacter(session, character);
        character.Items = UserSessionBinaryState.SerializeItems(paidInventory);
        character.Face = face;
        character.Hair = hair;
        await characters.UpdateAsync(character);
        return true;
    }

    private async Task TakeCouponAsync(UserSession session, int couponSlot)
    {
        var slot = session.Inventory[couponSlot];
        TakeOne(slot);
        await userNotificationService.SendStackChangeAsync(session, (byte)couponSlot, slot.ItemId, slot.Count, slot.Durability);

        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);
        await userNotificationService.SendWeightChangeAsync(session);
    }

    private static void TakeOne(ItemSlot slot)
    {
        slot.Count--;
        if (slot.Count == 0)
            slot.Clear();
    }

    private static ItemSlot CopyOf(ItemSlot slot) => new()
    {
        ItemId = slot.ItemId,
        Durability = slot.Durability,
        Count = slot.Count,
        Flag = slot.Flag,
        ExpiresAt = slot.ExpiresAt,
        UniqueId = slot.UniqueId,
    };

    private static Task RefuseAsync(IClient client) =>
        client.SendPacket(PreGamePacketWriter.ChangeHairResult(PreGamePacketWriter.ChangeHairFailed));
}
