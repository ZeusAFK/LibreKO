using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.World;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface ICombatRewardService
{
    Task AwardNpcKillAsync(NpcInstance npc, UserSession killer);
    Task AwardPlayerKillAsync(UserSession victim, UserSession? killer);
}

public class CombatRewardService(
    IOptions<GameServerSettings> settings,
    SessionManager sessionManager,
    IGameDataService gameDataService,
    TimeWeatherBroadcastService timeWeather,
    IPlayerProgressionService playerProgressionService,
    IQuestPacketCoordinator questPacketCoordinator,
    IAchievementProgressService achievementProgressService,
    ICollectionRaceService collectionRaceService,
    IJuraidMountainService juraidMountainService,
    IBorderDefenseWarService borderDefenseWarService,
    IMonsterStoneService monsterStoneService,
    IUnderTheCastleService underTheCastleService,
    ILogger<CombatRewardService> logger) : ICombatRewardService
{
    private const int CoinRollMinPercent = 70;
    private const int CoinRollMaxPercent = 100;

    public async Task AwardPlayerKillAsync(UserSession victim, UserSession? killer)
    {
        if (killer == null || killer == victim || killer.Nation == victim.Nation)
            return;

        killer.PlayersDefeated++;
        await questPacketCoordinator.CheckQuestKillAsync(killer, (int)victim.Nation);
        await achievementProgressService.ReportPlayerKillAsync(killer);
        await collectionRaceService.HandlePlayerKillAsync(victim, killer);

        if (victim.ZoneId == (byte)ZoneId.BorderDefenseWar)
            await borderDefenseWarService.OnPlayerKilledAsync(victim, killer);
    }

    public async Task AwardNpcKillAsync(NpcInstance npc, UserSession killer)
    {
        logger.LogDebug("NPC {NpcId} killed by {Name}: {Exp} exp, {Gold} gold", npc.NpcId, killer.Name, npc.Experience, npc.GoldDrop);

        var rewardRecipient = ResolveRewardRecipient(npc, killer);

        if (npc.Experience > 0)
        {
            if (rewardRecipient.IsInParty)
                await AwardPartyExpAsync(rewardRecipient, npc);
            else
                await AwardSoloExpAsync(rewardRecipient, npc);
        }

        await questPacketCoordinator.CheckQuestKillAsync(rewardRecipient, npc.NpcId);
        rewardRecipient.MonstersDefeated++;
        await achievementProgressService.ReportMonsterKillAsync(rewardRecipient, npc.NpcId);
        await collectionRaceService.HandleNpcKillAsync(npc, rewardRecipient);

        if (npc.ZoneId == (byte)ZoneId.JuradMountain)
            await juraidMountainService.OnNpcKilledAsync(npc, killer);

        if (npc.ZoneId == (byte)ZoneId.BorderDefenseWar)
            await borderDefenseWarService.OnNpcKilledAsync(npc, killer);

        if (MonsterStoneRules.IsNestZone(npc.ZoneId))
            await monsterStoneService.OnNpcKilledAsync(npc);

        if (npc.ZoneId == (byte)ZoneId.UnderCastle)
            await underTheCastleService.OnNpcKilledAsync(npc, killer);

        var damagerIds = npc.WithLock(n => n.DamageMap.Keys.ToArray());
        foreach (var charId in damagerIds)
        {
            var damager = sessionManager.GetByCharacterId(charId);
            if (damager != null && damager != rewardRecipient)
                await questPacketCoordinator.CheckQuestKillAsync(damager, npc.NpcId);
        }

        if (npc.DropItemGroup > 0 || npc.GoldDrop > 0)
            await CreateDropsAsync(npc, rewardRecipient);

        npc.WithLock(n =>
        {
            n.DamageMap.Clear();
            n.TopDamagerCharId = 0;
        });
    }

    private UserSession ResolveRewardRecipient(NpcInstance npc, UserSession killer)
    {
        if (npc.TopDamagerCharId > 0)
        {
            var topDamager = sessionManager.GetByCharacterId(npc.TopDamagerCharId);
            if (topDamager != null && topDamager.Hp > 0 && topDamager.ZoneId == npc.ZoneId)
                return topDamager;
        }

        return killer;
    }

    private async Task AwardSoloExpAsync(UserSession player, NpcInstance npc)
    {
        long expGain = npc.Experience;
        var killRate = settings.Value.Global.KillRate;
        if (killRate > 1)
            expGain *= killRate;

        var levelDiff = player.Level - npc.Level;
        if (levelDiff > 5)
            expGain = expGain * Math.Max(10, 100 - (levelDiff - 5) * 10) / 100;

        await playerProgressionService.AwardExperienceAsync(player, expGain);
    }

    private async Task AwardPartyExpAsync(UserSession killer, NpcInstance npc)
    {
        var nearbyMembers = GetNearbyPartyMembers(killer, npc);
        if (nearbyMembers.Count == 0)
        {
            await AwardSoloExpAsync(killer, npc);
            return;
        }

        var killRate = settings.Value.Global.KillRate;
        long baseExp = npc.Experience;
        if (killRate > 1)
            baseExp *= killRate;

        var partyBonus = 1.0 + (nearbyMembers.Count - 1) * 0.3;
        var totalExp = (long)(baseExp * partyBonus);

        var damageSnapshot = npc.WithLock(n =>
        {
            var snap = new Dictionary<int, int>();
            foreach (var member in nearbyMembers)
                if (n.DamageMap.TryGetValue(member.CharacterId, out var dmg))
                    snap[member.CharacterId] = dmg;
            return snap;
        });

        var totalPartyDamage = damageSnapshot.Values.Sum();

        foreach (var member in nearbyMembers)
        {
            long memberExp;
            if (totalPartyDamage > 0 && damageSnapshot.TryGetValue(member.CharacterId, out var memberDamage))
                memberExp = (long)(totalExp * ((double)memberDamage / totalPartyDamage));
            else
                memberExp = totalExp / nearbyMembers.Count;

            var levelDiff = member.Level - npc.Level;
            if (levelDiff > 5)
                memberExp = memberExp * Math.Max(10, 100 - (levelDiff - 5) * 10) / 100;

            await playerProgressionService.AwardExperienceAsync(member, memberExp);
        }
    }

    private ushort RollCoins(NpcInstance npc)
    {
        if (npc.GoldDrop <= 0)
            return 0;

        var rolled = (long)npc.GoldDrop * Random.Shared.Next(CoinRollMinPercent, CoinRollMaxPercent + 1) / 100;
        var coins = timeWeather.ApplyCoinBonus((int)Math.Min(rolled, int.MaxValue));
        return (ushort)Math.Clamp(coins, 0, ushort.MaxValue);
    }

    private int ResolveDropItem(int dropValue)
    {
        if (dropValue >= 100_000_000)
            return dropValue;

        if (dropValue >= 100)
        {
            if (!gameDataService.MakeItemGroupTable.TryGetValue(dropValue, out var pool) || pool.Length == 0)
                return 0;
            return pool[Random.Shared.Next(pool.Length)];
        }

        return 0;
    }

    private async Task CreateDropsAsync(NpcInstance npc, UserSession rewardRecipient)
    {
        var coins = RollCoins(npc);
        var droppedItems = new List<(int ItemId, ushort Count)>();
        var dropTable = npc.DropItemGroup > 0 ? gameDataService.GetNpcItem(npc.DropItemGroup, npc.IsMonster) : null;
        if (dropTable != null)
        {
            var dropBonus = gameDataService.GetPremiumProperty(rewardRecipient.PremiumType, PremiumPropertyType.Drop);
            foreach (var (itemId, percent) in dropTable.GetDrops())
            {
                if (itemId <= 0 || percent <= 0)
                    continue;

                var adjustedPercent = dropBonus > 0 ? percent + percent * dropBonus / 100 : percent;
                adjustedPercent = timeWeather.ApplyDropBonus(adjustedPercent);
                if (Random.Shared.Next(1, 10001) > adjustedPercent)
                    continue;

                var resolvedItemId = ResolveDropItem(itemId);
                if (resolvedItemId <= 0 || gameDataService.GetItem(resolvedItemId) == null)
                    continue;

                droppedItems.Add((resolvedItemId, 1));
            }
        }

        if (coins == 0 && droppedItems.Count == 0)
            return;

        var bundle = sessionManager.Regions.CreateBundle(npc.X, npc.Z, npc.Y);
        bundle.OwnerCharId = rewardRecipient.CharacterId;
        bundle.OwnerPartyIndex = rewardRecipient.IsInParty ? rewardRecipient.PartyIndex : -1;

        if (coins > 0)
            bundle.Items.Add(new LootItem { ItemId = InventoryConstants.ItemGold, Count = coins });

        foreach (var (itemId, count) in droppedItems.Take(LootBundle.MaxItems - bundle.Items.Count))
        {
            bundle.Items.Add(new LootItem
            {
                ItemId = itemId,
                Count = count
            });
        }

        var bundlePacket = ItemDropPacketWriter
            .Dropped(npc.UniqueId, bundle.BundleId, bundle.Items.Count > 0)
            ;

        if (!rewardRecipient.IsInParty)
        {
            await rewardRecipient.Client.SendPacket(bundlePacket);
            return;
        }

        var party = sessionManager.Parties.GetParty(rewardRecipient.PartyIndex);
        if (party == null)
        {
            await rewardRecipient.Client.SendPacket(bundlePacket);
            return;
        }

        for (var index = 0; index < PartyGroup.MaxMembers; index++)
        {
            var memberId = party.MemberIds[index];
            if (memberId < 0)
                continue;

            var member = sessionManager.GetByCharacterId(memberId);
            if (member == null)
                continue;

            await member.Client.SendPacket(bundlePacket);
        }
    }

    private List<UserSession> GetNearbyPartyMembers(UserSession killer, NpcInstance npc)
    {
        var result = new List<UserSession>();
        var party = sessionManager.Parties.GetParty(killer.PartyIndex);
        if (party == null)
            return result;

        const float maxDistSq = 50 * 50;

        for (var index = 0; index < PartyGroup.MaxMembers; index++)
        {
            if (party.MemberIds[index] < 0)
                continue;

            var member = sessionManager.GetByCharacterId(party.MemberIds[index]);
            if (member == null || member.Hp <= 0 || member.ZoneId != npc.ZoneId)
                continue;

            var dx = member.X - npc.X;
            var dz = member.Z - npc.Z;
            if (dx * dx + dz * dz <= maxDistSq)
                result.Add(member);
        }

        return result;
    }

}
