using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public interface IKrowazTrapService
{
    Task TickAsync();
    Task OnMovedAsync(UserSession session);
    Task<bool> TryOpenKeyGateAsync(UserSession session, ObjectEvent gate);
}

public sealed class KrowazTrapService(
    SessionManager sessionManager,
    ITouchTrapService touchTrapService) : IKrowazTrapService
{
    public const byte KeyGateObject = 12;
    public const byte PoisonGasObject = 13;
    public const int KeyGateOpenSeconds = 25;
    public const int GasFlowMinTenths = 40;
    public const int GasFlowMaxTenths = 120;
    public const int GasRestMinTenths = 20;
    public const int GasRestMaxTenths = 60;
    public const float GasHalfWidthX = 7f;
    public const float GasHalfWidthZ = 3f;
    public const int GasDamage = 2000;

    private const double TenthsPerSecond = 10.0;

    private readonly object stateLock = new();
    private readonly Dictionary<int, DateTime> gateClosesAt = [];
    private readonly Dictionary<int, DateTime> gasSwitchAt = [];

    public static bool InGasCloud(float x, float z, float gasX, float gasZ) =>
        Math.Abs(x - gasX) <= GasHalfWidthX && Math.Abs(z - gasZ) <= GasHalfWidthZ;

    public Task TickAsync() => TickAtAsync(DateTime.UtcNow);

    public async Task TickAtAsync(DateTime now)
    {
        foreach (var npc in sessionManager.Regions.GetAllNpcsInZone(KrowazService.Zone).ToList())
        {
            if (npc.ObjectEventType == KeyGateObject)
                await TickGateAsync(npc, now);
            else if (npc.ObjectEventType == PoisonGasObject)
                await TickGasAsync(npc, now);
        }
    }

    private async Task TickGateAsync(NpcInstance gate, DateTime now)
    {
        if (gate.GateOpen == 0)
            return;

        lock (stateLock)
        {
            if (!gateClosesAt.TryGetValue(gate.UniqueId, out var closesAt))
                closesAt = now;
            if (now < closesAt)
                return;
            gateClosesAt.Remove(gate.UniqueId);
        }

        gate.GateOpen = 0;
        await BroadcastStateAsync(gate, KeyGateObject);
    }

    private async Task TickGasAsync(NpcInstance gas, DateTime now)
    {
        lock (stateLock)
        {
            if (gasSwitchAt.TryGetValue(gas.UniqueId, out var switchAt) && now < switchAt)
                return;
            bool flowing = gas.GateOpen == 0;
            gas.GateOpen = flowing ? 1 : 0;
            gasSwitchAt[gas.UniqueId] = now.AddSeconds(flowing
                ? Random.Shared.Next(GasFlowMinTenths, GasFlowMaxTenths + 1) / TenthsPerSecond
                : Random.Shared.Next(GasRestMinTenths, GasRestMaxTenths + 1) / TenthsPerSecond);
        }

        await BroadcastStateAsync(gas, PoisonGasObject);
    }

    public async Task OnMovedAsync(UserSession session)
    {
        if (session.ZoneId != KrowazService.Zone || session.Hp <= 0)
            return;

        foreach (var gas in sessionManager.Regions.GetAllNpcsInZone(KrowazService.Zone))
        {
            if (gas.ObjectEventType != PoisonGasObject || gas.GateOpen == 0
                || !InGasCloud(session.X, session.Z, gas.SpawnX, gas.SpawnZ))
                continue;
            await touchTrapService.HurtAsync(session, gas, GasDamage);
            return;
        }
    }

    public async Task<bool> TryOpenKeyGateAsync(UserSession session, ObjectEvent gate)
    {
        var npc = sessionManager.Regions.GetNpcByProtoId((byte)session.ZoneId, gate.ControlNpcId);
        if (npc == null || npc.GateOpen != 0)
            return false;

        lock (stateLock)
            gateClosesAt[npc.UniqueId] = DateTime.UtcNow.AddSeconds(KeyGateOpenSeconds);
        npc.GateOpen = 1;
        await BroadcastStateAsync(npc, KeyGateObject);
        return true;
    }

    private Task BroadcastStateAsync(NpcInstance npc, byte objectType) =>
        sessionManager.Regions.BroadcastFromNpc(npc, MiscPacketWriter.ObjectGateFlag(objectType, npc.UniqueId, npc.GateOpen != 0));
}
