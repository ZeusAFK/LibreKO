using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IDisguisePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class DisguisePacketCoordinator(
    SessionManager sessionManager,
    ILogger<DisguisePacketCoordinator> logger) : IDisguisePacketCoordinator
{
    private const byte DisguiseSubList = 1;
    private const byte DisguiseSubApply = 2;
    private const byte DisguiseSubRemove = 3;

    // disguiseId -> display name. Static catalog of available costumes.
    private static readonly (int Id, string Name)[] DisguiseCatalog =
    {
        (1, "Skeleton Warrior"),
        (2, "Goblin"),
        (3, "Orc Pawn"),
        (4, "Bulkan Brute"),
        (5, "Snow Beast"),
        (6, "Death Knight"),
        (7, "Pumpkin Head"),
        (8, "Festival Costume"),
    };

    // charId -> currently-applied disguiseId (0 = none). In-memory; resets on server restart.
    private static readonly Dictionary<int, int> currentDisguise = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case DisguiseSubList:
                await SendDisguiseListAsync(session);
                break;
            case DisguiseSubApply:
                await HandleDisguiseApplyAsync(session, packet);
                break;
            case DisguiseSubRemove:
                await HandleDisguiseRemoveAsync(session);
                break;
        }
    }

    private async Task SendDisguiseListAsync(UserSession session)
    {
        var entries = DisguiseCatalog
            .Select(entry => new DisguisePacketWriter.Entry(entry.Id, entry.Name))
            .ToList();

        await session.Client.SendPacket(
            DisguisePacketWriter.DisguiseList(DisguiseSubList, entries));
    }

    private async Task HandleDisguiseApplyAsync(UserSession session, Packet packet)
    {

        int disguiseId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        var entry = System.Array.Find(DisguiseCatalog, d => d.Id == disguiseId);

        if (entry.Id == 0)
        {
            await session.Client.SendPacket(DisguisePacketWriter.Result(
                DisguiseSubApply, DisguisePacketWriter.Failed, disguiseId));
            return;
        }

        currentDisguise[session.CharacterId] = disguiseId;
        logger.LogDebug("{Name} applied disguise {Disguise}", session.Name, disguiseId);
        await session.Client.SendPacket(DisguisePacketWriter.Result(
            DisguiseSubApply, DisguisePacketWriter.Succeeded, disguiseId));
    }

    private async Task HandleDisguiseRemoveAsync(UserSession session)
    {

        int removed = currentDisguise.TryGetValue(session.CharacterId, out var cur) ? cur : 0;
        currentDisguise[session.CharacterId] = 0;
        logger.LogDebug("{Name} removed disguise (was {Disguise})", session.Name, removed);
        await session.Client.SendPacket(DisguisePacketWriter.Result(
            DisguiseSubRemove, DisguisePacketWriter.Succeeded, removed));
    }
}
