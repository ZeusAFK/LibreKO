using System.Collections.Frozen;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IGlobalMapPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class GlobalMapPacketCoordinator(
    SessionManager sessionManager,
    ILogger<GlobalMapPacketCoordinator> logger) : IGlobalMapPacketCoordinator
{
    private const byte GlobalMapSubList = 1;
    private const int PopulationCacheSeconds = 10;

    // ownerNation: 0 = neutral/contested, 1 = Karus, 2 = ElMorad (matches AccountNation).
    // zoneId mirrors the server's ZoneId space so live population can be attributed by UserSession.ZoneId.
    private static readonly (byte ZoneId, string Name, byte OwnerNation)[] GlobalMapCatalog =
    [
        (BattleZoneManager.ZONE_KARUS,            "Karus",             (byte)AccountNation.Karus),
        (BattleZoneManager.ZONE_ELMORAD,          "El Morad",          (byte)AccountNation.ElMorad),
        (BattleZoneManager.ZONE_KARUS_ESLANT,     "Eslant (Karus)",    (byte)AccountNation.Karus),
        (BattleZoneManager.ZONE_ELMORAD_ESLANT,   "Eslant (El Morad)", (byte)AccountNation.ElMorad),
        (BattleZoneManager.ZONE_MORADON,          "Moradon",           0),
        (BattleZoneManager.ZONE_DELOS,            "Delos",             0),
        (BattleZoneManager.ZONE_BIFROST,          "Bifrost",           0),
        (BattleZoneManager.ZONE_ARENA,            "Battle Arena",      0),
        (BattleZoneManager.ZONE_RONARK_LAND,      "Ronark Land",       0),
        (BattleZoneManager.ZONE_ARDREAM,          "Ardream",           0),
        (BattleZoneManager.ZONE_RONARK_LAND_BASE, "Ronark Land Base",  0),
        (BattleZoneManager.ZONE_KROWAZ_DOMINION,  "Krowaz Dominion",   0),
        (BattleZoneManager.ZONE_JURAID_MOUNTAIN,  "Juraid Mountain",   0),
    ];

    private static readonly FrozenDictionary<byte, int> CatalogIndexByZone =
        GlobalMapCatalog
            .Select((entry, index) => KeyValuePair.Create(entry.ZoneId, index))
            .ToFrozenDictionary();

    private sealed record PopulationSnapshot(ushort[] Counts, long ExpiryTicks);

    private readonly Lock refreshLock = new();
    private PopulationSnapshot population = new(new ushort[GlobalMapCatalog.Length], 0);

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case GlobalMapSubList:
                await SendGlobalMapListAsync(session);
                break;
        }
    }

    private async Task SendGlobalMapListAsync(UserSession session)
    {
        var counts = GetPopulation();

        var zones = new List<GlobalMapPacketWriter.ZoneEntry>(GlobalMapCatalog.Length);
        for (int i = 0; i < GlobalMapCatalog.Length; i++)
        {
            var (zoneId, name, ownerNation) = GlobalMapCatalog[i];
            zones.Add(new GlobalMapPacketWriter.ZoneEntry(zoneId, name, counts[i], ownerNation));
        }

        var result = GlobalMapPacketWriter.ZoneList(GlobalMapSubList, zones);

        logger.LogDebug("{Name} requested the global map ({Zones} zones)", session.Name, GlobalMapCatalog.Length);
        await session.Client.SendPacket(result);
    }

    private ushort[] GetPopulation()
    {
        var snapshot = Volatile.Read(ref population);
        if (DateTime.UtcNow.Ticks < snapshot.ExpiryTicks)
            return snapshot.Counts;

        lock (refreshLock)
        {
            snapshot = population;
            if (DateTime.UtcNow.Ticks < snapshot.ExpiryTicks)
                return snapshot.Counts;

            var counts = new ushort[GlobalMapCatalog.Length];
            foreach (var s in sessionManager.GetAll())
            {
                if (CatalogIndexByZone.TryGetValue(s.ZoneId, out var index) && counts[index] < ushort.MaxValue)
                    counts[index]++;
            }

            var refreshed = new PopulationSnapshot(counts, DateTime.UtcNow.AddSeconds(PopulationCacheSeconds).Ticks);
            Volatile.Write(ref population, refreshed);
            return counts;
        }
    }
}
