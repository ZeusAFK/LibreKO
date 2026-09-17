using System.Collections.Generic;
using System.Linq;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IFishingHallPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class FishingHallPacketCoordinator(
    SessionManager sessionManager,
    ILogger<FishingHallPacketCoordinator> logger) : IFishingHallPacketCoordinator
{
    private const byte FishingHallSubList = 1;

    // How many top anglers the leaderboard reports.
    private const int FishingHallTopCount = 10;

    // Angler name -> accumulated fishing score (in-memory; resets on server restart). Seeded with a few
    // entries so the hall of fame is never empty; real entries are added/updated via FishingHallReport.
    private static readonly Dictionary<string, int> FishingHallScores = new()
    {
        ["Gillbert"] = 48200,
        ["ReelDeal"] = 39500,
        ["CaptAhab"] = 31750,
        ["BaitMaster"] = 22100,
        ["Minnow"] = 9800,
    };

    private static readonly object FishingHallLock = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case FishingHallSubList:
                await SendFishingHallListAsync(session);
                break;
        }
    }

    private async Task SendFishingHallListAsync(UserSession session)
    {
        List<KeyValuePair<string, int>> top;
        lock (FishingHallLock)
        {
            top = FishingHallScores
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Take(FishingHallTopCount)
                .ToList();
        }

        var anglers = new List<FishingHallPacketWriter.Entry>(top.Count);
        for (int i = 0; i < top.Count; i++)
            anglers.Add(new FishingHallPacketWriter.Entry((ushort)(i + 1), top[i].Key, top[i].Value));

        var result = FishingHallPacketWriter.Rankings(FishingHallSubList, anglers);

        logger.LogDebug("{Name} viewed fishing hall of fame ({Count} anglers)", session.Name, top.Count);
        await session.Client.SendPacket(result);
    }

    public static void FishingHallReport(string anglerName, int scoreDelta)
    {
        if (string.IsNullOrEmpty(anglerName) || scoreDelta <= 0)
            return;

        lock (FishingHallLock)
        {
            FishingHallScores.TryGetValue(anglerName, out int current);
            FishingHallScores[anglerName] = current + scoreDelta;
        }
    }
}
