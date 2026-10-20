using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface ILootPacketCoordinator
{
    Task HandleItemDropAsync(IClient client, Packet packet);
    Task HandleBundleOpenAsync(IClient client, Packet packet);
    Task HandleItemGetAsync(IClient client, Packet packet);
}

public class LootPacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    ICollectionRaceService collectionRaceService,
    IKingEventState kingEventState,
    ILogger<LootPacketCoordinator> logger,
    Microsoft.Extensions.Options.IOptions<LibreKO.Game.Configuration.GameServerSettings>? settings = null) : ILootPacketCoordinator
{
    private const byte LootSuccess = 1;
    private const int CoinMax = 2_100_000_000;
    private const float PartyCoinRange = 50f;
    private const int PercentScale = 100;

    public async Task HandleItemDropAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 7)
            return;

        var pos = packet.ReadByte();
        var itemId = packet.ReadInt();
        var count = packet.ReadUShort();
        if (pos >= InventoryConstants.HaveMax || count == 0)
            return;

        var absPos = InventoryConstants.SlotMax + pos;

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
            return;

        var dropped = session.WithLock(s =>
        {
            var slot = s.Inventory[absPos];
            if (slot.ItemId != itemId || slot.Count < count)
                return false;
            slot.Count -= count;
            if (slot.Count == 0)
                slot.Clear();
            var coefficient = gameDataService.GetCoefficient(s.Class);
            if (coefficient != null)
                s.RecalculateStats(coefficient, gameDataService);
            return true;
        });

        if (!dropped)
            return;

        var bundle = sessionManager.Regions.CreateBundle(session.X, session.Z, session.Y);
        bundle.Items.Add(new LootItem { ItemId = itemId, Count = count });
        logger.LogDebug("{Name} dropped item {ItemId} x{Count}", session.Name, itemId, count);

        var result = ItemDropPacketWriter
            .Dropped(session.CharacterId, bundle.BundleId, hasItems: true)
            ;
        await session.Client.SendPacket(result);

        await userNotificationService.SendWeightChangeAsync(session);
    }

    public async Task HandleBundleOpenAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 4)
            return;

        var bundleId = packet.ReadInt();
        var bundle = sessionManager.Regions.GetBundle(bundleId);
        if (bundle == null)
        {
            await session.Client.SendPacket(BundleOpenPacketWriter.Refused(bundleId, BundleOpenPacketWriter.ResultGone));
            return;
        }

        if (bundle.DistanceSquaredTo(session.X, session.Z) > LootBundle.MaxLootRange)
            return;

        if (!bundle.CanLoot(session.CharacterId, session.IsInParty ? session.PartyIndex : -1, DateTime.UtcNow.Ticks))
        {
            await session.Client.SendPacket(BundleOpenPacketWriter.Refused(bundleId, BundleOpenPacketWriter.ResultNotYours));
            return;
        }

        var snapshot = bundle.SnapshotItems();

        var writer = new BundleOpenPacketWriter { BundleId = bundleId };
        for (var index = 0; index < snapshot.Count; index++)
            writer.Add(snapshot[index].ItemId, snapshot[index].Count);

        await session.Client.SendPacket(writer.Build());
    }

    public async Task HandleItemGetAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 10)
            return;

        var bundleId = packet.ReadInt();
        var itemId = packet.ReadInt();
        var slotId = packet.ReadUShort();
        var bundle = sessionManager.Regions.GetBundle(bundleId);
        if (bundle == null)
        {
            await SendItemGetErrorAsync(session, bundleId);
            return;
        }

        if (bundle.DistanceSquaredTo(session.X, session.Z) > LootBundle.MaxLootRange)
        {
            await SendItemGetErrorAsync(session, bundleId);
            return;
        }

        if (!bundle.CanLoot(session.CharacterId, session.IsInParty ? session.PartyIndex : -1, DateTime.UtcNow.Ticks))
        {
            await SendItemGetErrorAsync(session, bundleId);
            return;
        }

        if (itemId == InventoryConstants.ItemGold)
        {
            if (!sessionManager.Regions.TryClaimBundleSlot(bundleId, slotId, itemId, out var claimedGold) || claimedGold == null)
            {
                await SendItemGetErrorAsync(session, bundleId);
                return;
            }

            var party = session.IsInParty ? sessionManager.Parties.GetParty(session.PartyIndex) : null;
            if (party == null)
            {
                int coins = claimedGold.Count;
                int times = JackpotRules.Roll(session, settings?.Value.Jackpot.CoinRates ?? []);
                if (times > JackpotRules.None)
                    coins = (int)Math.Min((long)coins * times, int.MaxValue);
                var newMoney = GainCoins(session, coins);
                if (times > JackpotRules.None)
                    await sessionManager.Regions.SendToRegion(session,
                        GoldChangePacketWriter.Multiplied(coins, newMoney, times, session.CharacterId), excludeSender: false);
                await session.Client.SendPacket(ItemGetPacketWriter.LootedGold(bundleId, itemId, (ushort)Math.Min(coins, ushort.MaxValue), newMoney, slotId));
                return;
            }

            await ShareCoinsAsync(party, bundle, session.ZoneId, itemId, claimedGold.Count);
            return;
        }

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
        {
            await SendItemGetErrorAsync(session, bundleId);
            return;
        }

        var snapshot = bundle.SnapshotItems();
        if (slotId >= snapshot.Count || snapshot[slotId].ItemId != itemId)
        {
            await SendItemGetErrorAsync(session, bundleId);
            return;
        }
        var peekedCount = snapshot[slotId].Count;

        var slotIndex = session.WithLock(s => s.FindSlotForItem(itemId, gameDataService, peekedCount));
        if (slotIndex < 0)
        {
            var noRoom = ItemGetPacketWriter.NoRoom(bundleId);
            await session.Client.SendPacket(noRoom);
            return;
        }

        if (!sessionManager.Regions.TryClaimBundleSlot(bundleId, slotId, itemId, out var claimed) || claimed == null)
        {
            await SendItemGetErrorAsync(session, bundleId);
            return;
        }

        logger.LogDebug("{Name} picked up item {ItemId} x{Count}", session.Name, itemId, claimed.Count);

        var picked = session.WithLock(s =>
        {
            var dst = s.Inventory[slotIndex];
            dst.ItemId = itemId;
            dst.Count += claimed.Count;
            if (dst.Count > 9999)
                dst.Count = 9999;
            if (dst.Durability == 0)
                dst.Durability = itemData.Duration;
            var coefficient = gameDataService.GetCoefficient(s.Class);
            if (coefficient != null)
                s.RecalculateStats(coefficient, gameDataService);
            return (Count: dst.Count, Money: s.Money);
        });

        var success = ItemGetPacketWriter.Looted(
            bundleId,
            (byte)(slotIndex - InventoryConstants.SlotMax),
            itemId,
            picked.Count,
            picked.Money,
            slotId);
        await session.Client.SendPacket(success);
        await TellPartyAsync(session, bundleId, itemId, slotId);

        await userNotificationService.SendWeightChangeAsync(session);
        await collectionRaceService.HandleItemGainAsync(session, itemId);
    }

    private async Task TellPartyAsync(UserSession looter, int bundleId, int itemId, ushort slotId)
    {
        if (!looter.IsInParty || sessionManager.Parties.GetParty(looter.PartyIndex) is not { } party)
            return;
        var notice = ItemGetPacketWriter.PartyMemberLooted(bundleId, itemId, looter.Name, slotId);
        for (var index = 0; index < PartyGroup.MaxMembers; index++)
        {
            if (party.MemberIds[index] < 0 || party.MemberIds[index] == looter.CharacterId)
                continue;
            if (sessionManager.GetByCharacterId(party.MemberIds[index]) is { } member && member.ZoneId == looter.ZoneId)
                await member.Client.SendPacket(notice);
        }
    }

    private async Task ShareCoinsAsync(PartyGroup party, LootBundle bundle, byte zoneId, int itemId, int coins)
    {
        var members = new List<UserSession>();
        var levelSum = 0;
        for (var index = 0; index < PartyGroup.MaxMembers; index++)
        {
            if (party.MemberIds[index] < 0)
                continue;
            var member = sessionManager.GetByCharacterId(party.MemberIds[index]);
            if (member == null)
                continue;
            levelSum += member.Level;
            members.Add(member);
        }

        if (levelSum <= 0)
            return;

        foreach (var member in members)
        {
            if (member.Hp <= 0 || member.ZoneId != zoneId
                || bundle.DistanceSquaredTo(member.X, member.Z) > PartyCoinRange * PartyCoinRange)
                continue;

            var share = (int)((long)coins * member.Level / levelSum);
            var newMoney = GainCoins(member, share);
            await member.Client.SendPacket(ItemGetPacketWriter.PartyCoins(bundle.BundleId, itemId, newMoney));
        }
    }

    private int GainCoins(UserSession session, int coins) => session.WithLock(s =>
    {
        long gained = coins;
        var premiumPercent = gameDataService.GetPremiumProperty(s.PremiumType, PremiumPropertyType.Noah);
        if (premiumPercent > 0)
            gained = gained * (PercentScale + premiumPercent) / PercentScale;

        var kingPercent = kingEventState.GetNoahBonus(s.Nation);
        if (kingPercent > 0)
            gained = gained * (PercentScale + kingPercent) / PercentScale;

        var noahGainPercent = s.NoahGainAmount + s.Stats.ItemCoinBonusPercent;
        if (noahGainPercent > PercentScale)
            gained = gained * noahGainPercent / PercentScale;

        s.Money = (int)Math.Min(s.Money + gained, CoinMax);
        return s.Money;
    });

    private static async Task SendItemGetErrorAsync(UserSession session, int bundleId)
    {
        var result = ItemGetPacketWriter.Failed(bundleId);
        await session.Client.SendPacket(result);
    }
}
