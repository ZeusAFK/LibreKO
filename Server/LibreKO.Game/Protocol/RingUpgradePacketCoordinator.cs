using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IRingUpgradePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class RingUpgradePacketCoordinator(
    SessionManager sessionManager,
    ILogger<RingUpgradePacketCoordinator> logger) : IRingUpgradePacketCoordinator
{

    private const byte MaxPlus = 9;         // accessories cap at +9 in this implementation

    // Base success-rate percent indexed by the CURRENT +level (the level we are trying to leave).
    // +0->+1 is near-certain; each subsequent step is harder.
    private static readonly byte[] RateByCurrentPlus =
    {
        95, // +0 -> +1
        85, // +1 -> +2
        72, // +2 -> +3
        60, // +3 -> +4
        48, // +4 -> +5
        36, // +5 -> +6
        25, // +6 -> +7
        16, // +7 -> +8
        9,  // +8 -> +9
    };

    // charId -> (invSlot -> current +level). In-memory; resets on server restart.
    private static readonly Dictionary<int, Dictionary<byte, byte>> plusLevels = new();
    private static readonly System.Random rng = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = (RingUpgradeSubOpcode)packet.ReadByte();
        switch (sub)
        {
            case RingUpgradeSubOpcode.Status:
                await HandleStatusAsync(session, packet);
                break;
            case RingUpgradeSubOpcode.Upgrade:
                await HandleUpgradeAsync(session, packet);
                break;
        }
    }

    private async Task HandleStatusAsync(UserSession session, Packet packet)
    {
        byte invSlot = packet.RemainingBytes >= 1 ? packet.ReadByte() : (byte)0;
        byte cur = GetPlus(session.CharacterId, invSlot);

        await session.Client.SendPacket(
            RingUpgradePacketWriter.Status(RingUpgradeSubOpcode.Status, RateForPlus(cur)));
    }

    private async Task HandleUpgradeAsync(UserSession session, Packet packet)
    {
        byte invSlot = packet.RemainingBytes >= 1 ? packet.ReadByte() : (byte)0;
        var slots = GetSlots(session.CharacterId);
        slots.TryGetValue(invSlot, out byte cur);

        if (cur >= MaxPlus)
        {
            await session.Client.SendPacket(RingUpgradePacketWriter.UpgradeResult(
                RingUpgradeSubOpcode.Upgrade, RingUpgradeResult.NotUpgradeable, cur));
            return;
        }

        byte rate = RateForPlus(cur);
        bool success = rng.Next(100) < rate;
        if (success)
        {
            cur++;
            slots[invSlot] = cur;
        }

        logger.LogDebug("{Name} ring-upgrade slot {Slot}: {Outcome} -> +{Plus} (rate {Rate}%)",
            session.Name, invSlot, success ? "success" : "fail", cur, rate);

        await session.Client.SendPacket(RingUpgradePacketWriter.UpgradeResult(
            RingUpgradeSubOpcode.Upgrade, success ? RingUpgradeResult.Succeeded : RingUpgradeResult.Failed, cur));
    }

    private static byte RateForPlus(byte cur)
        => cur < RateByCurrentPlus.Length ? RateByCurrentPlus[cur] : (byte)0;

    private static byte GetPlus(int charId, byte invSlot)
    {
        GetSlots(charId).TryGetValue(invSlot, out byte cur);
        return cur;
    }

    private static Dictionary<byte, byte> GetSlots(int charId)
    {
        if (!plusLevels.TryGetValue(charId, out var slots))
        {
            slots = new Dictionary<byte, byte>();
            plusLevels[charId] = slots;
        }
        return slots;
    }
}
