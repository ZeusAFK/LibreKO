using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public interface INereidMonumentService
{
    Task OnNpcKilledAsync(NpcInstance npc, UserSession killer);
    Task AnnounceResultAsync(byte winner);
    Packet PointsPacket();
}

public sealed class NereidMonumentService : INereidMonumentService
{
    private const byte Nereid = BattleZoneManager.ZONE_BATTLE4;

    private readonly SessionManager sessions;

    public NereidMonumentService(SessionManager sessions, IZoneTransitionService zones)
    {
        this.sessions = sessions;
        zones.ZoneAbilitySent += OnZoneAbilitySentAsync;
    }

    private BattleZoneManager Battle => sessions.Battle;

    private bool WarHere => Battle.IsBattleActive && Battle.BattleZone == Nereid;

    public Packet PointsPacket() => MapEventPacketWriter.Points(Battle.KarusMonumentPoint, Battle.ElmoMonumentPoint);

    public async Task OnNpcKilledAsync(NpcInstance npc, UserSession killer)
    {
        if (npc.ZoneId != Nereid || npc.NpcType != NpcData.TypeBattleMonument || !WarHere)
            return;

        int index = BattleZoneManager.MonumentIndexOfTrap(npc.TrapNumber);
        if (!Battle.CaptureMonumentAt(index, (byte)killer.Nation))
            return;

        await BroadcastAsync(MapEventPacketWriter.Captured((byte)index, (byte)killer.Nation));
        await BroadcastAsync(PointsPacket());
    }

    public async Task AnnounceResultAsync(byte winner)
    {
        if (winner != 0 && WarHere)
            await BroadcastAsync(MapEventPacketWriter.Result(winner));
    }

    private async Task OnZoneAbilitySentAsync(UserSession session)
    {
        if (session.ZoneId != Nereid)
            return;
        await session.Client.SendPacket(MapEventPacketWriter.Owners(Battle.MonumentOwners));
        await session.Client.SendPacket(PointsPacket());
    }

    private async Task BroadcastAsync(Packet packet)
    {
        foreach (var session in sessions.GetAll().Where(s => s.ZoneId == Nereid).ToList())
            await session.Client.SendPacket(packet);
    }
}
