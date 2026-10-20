using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IItemSmashService
{
    Task HandleAsync(UserSession session, Packet packet);
}

public class ItemSmashService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IItemGrantService itemGrantService,
    IUserNotificationService userNotificationService,
    ICharacterStatePersister statePersister,
    ILogger<ItemSmashService> logger) : IItemSmashService
{
    public const int HighFee = 100_000;
    public const int LowFee = 10_000;
    private const int RequestBytes = 4 + 1 + 4;
    private const int ItemNumberSpan = 1_000_000_000;
    private const int LastSmashableNumber = 900_000_000;
    private const byte HighFeeTypeUnique = 4;
    private const byte HighFeeTypeReverseUnique = 12;
    private static readonly (byte From, byte To)[] SmashableKinds =
    [
        (11, 80), (91, 94), (110, 110), (130, 130), (140, 140), (181, 181), (210, 240),
    ];
    private static readonly byte[] KeptRaces = [50, 69, 70, 71, 76];

    public async Task HandleAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < RequestBytes)
            return;
        var itemId = packet.ReadInt();
        var bagSlot = packet.ReadByte();
        var npcUniqueId = packet.ReadInt();

        var npc = sessionManager.Regions.GetNpc(npcUniqueId);
        if (npc == null || !CanSmashAt(session, npc))
        {
            await session.Client.SendPacket(ItemSmashPacketWriter.Refusal(ItemSmashPacketWriter.WrongNpc));
            return;
        }

        var item = gameDataService.GetItem(itemId);
        if (bagSlot >= InventoryConstants.HaveMax || item == null
            || session.Inventory[InventoryConstants.SlotMax + bagSlot] is not { } slot
            || slot.ItemId != itemId || !Smashable(slot, item) || ClassOf(item) is not { } smashClass
            || session.Money < FeeFor(item))
        {
            await session.Client.SendPacket(ItemSmashPacketWriter.Refusal(ItemSmashPacketWriter.CannotDisassemble));
            return;
        }

        var rewards = gameDataService.ItemSmashRewardsByBand[smashClass.Band].Where(r => r.Rate > 0).ToList();
        if (rewards.Count == 0 || FreeBagSlots(session) < smashClass.Rolls)
        {
            await session.Client.SendPacket(ItemSmashPacketWriter.Refusal(ItemSmashPacketWriter.InventoryFull));
            return;
        }

        var absolute = InventoryConstants.SlotMax + bagSlot;
        session.WithLock(s =>
        {
            var held = s.Inventory[absolute];
            if (held.Count <= 1)
                held.Clear();
            else
                held.Count--;
        });
        var fee = FeeFor(item);
        session.Money -= fee;
        await userNotificationService.SendGoldLossAsync(session, fee);

        var totalRate = rewards.Sum(r => r.Rate);
        var pieces = new List<ItemSmashPacketWriter.Piece>(smashClass.Rolls);
        for (var roll = 0; roll < smashClass.Rolls; roll++)
        {
            var reward = Pick(rewards, Random.Shared.Next(totalRate));
            if (gameDataService.GetItem(reward.ItemId) is not { } given)
                continue;
            var target = session.WithLock(s => s.FindSlotForItem(given.Num, gameDataService, (ushort)reward.Count));
            if (target < InventoryConstants.SlotMax
                || await itemGrantService.GrantAsync(session, given, reward.Count) < reward.Count)
                continue;
            pieces.Add(new ItemSmashPacketWriter.Piece(given.Num, (byte)(target - InventoryConstants.SlotMax), (ushort)reward.Count));
        }

        session.WithLock(s => s.RecalculateStatsWithBuffs(gameDataService));
        await session.Client.SendPacket(ItemSmashPacketWriter.Success(itemId, bagSlot, pieces));
        await userNotificationService.SendWeightChangeAsync(session);
        await statePersister.SaveAsync(session);
        logger.LogInformation("{Name} disassembled {Item} at {Npc} into {Pieces}",
            session.Name, itemId, npc.NpcId, string.Join(", ", pieces.Select(p => $"{p.ItemId}x{p.Count}")));
    }

    public static ItemSmashRewardData Pick(IReadOnlyList<ItemSmashRewardData> rewards, int roll)
    {
        foreach (var reward in rewards)
        {
            if (roll < reward.Rate)
                return reward;
            roll -= reward.Rate;
        }
        return rewards[^1];
    }

    public static int FeeFor(ItemData item) =>
        item.ItemType is HighFeeTypeUnique or HighFeeTypeReverseUnique ? HighFee : LowFee;

    public ItemSmashClassData? ClassOf(ItemData item) =>
        gameDataService.ItemSmashClassesByGrade.GetValueOrDefault(item.GradeClass(gameDataService.ItemGradeBonuses));

    public static bool Smashable(ItemSlot slot, ItemData item) =>
        item.Countable == 0
        && !slot.Expires
        && slot.State is not (ItemFlag.Rented or ItemFlag.CharacterSeal or ItemFlag.Duplicate or ItemFlag.Sealed
            or ItemFlag.Bound or ItemFlag.SealedPremium)
        && SmashableKinds.Any(k => item.Kind >= k.From && item.Kind <= k.To)
        && !KeptRaces.Contains(item.Race)
        && item.Num % ItemNumberSpan < LastSmashableNumber;

    private bool CanSmashAt(UserSession session, NpcInstance npc)
    {
        if (!npc.IsAlive || npc.ZoneId != session.ZoneId || session.Hp <= 0 || npc.NpcType != NpcData.TypeDisassembler
            || session.Trade.IsTrading || session.Trade.IsMerchanting || session.IsGathering)
            return false;
        var dx = session.X - npc.X;
        var dz = session.Z - npc.Z;
        return dx * dx + dz * dz <= GameConstants.MaxNpcInteractionRangeSq;
    }

    private static int FreeBagSlots(UserSession session)
    {
        var free = 0;
        for (var index = 0; index < InventoryConstants.HaveMax; index++)
            if (session.Inventory[InventoryConstants.SlotMax + index].IsEmpty)
                free++;
        return free;
    }
}
