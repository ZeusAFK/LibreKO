using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using static LibreKO.Game.Protocol.Writers.SiegeTowerPacketWriter;

namespace LibreKO.Game.World;

public interface ISiegeTowerService
{
    Task HandleAsync(IClient client, Packet packet);
    Task ReleaseAsync(UserSession session);
}

public sealed class SiegeTowerService : ISiegeTowerService
{
    private readonly SessionManager sessions;
    private readonly IWorldPacketCoordinator world;
    private readonly IMagicStatusEffectService magic;

    public SiegeTowerService(
        SessionManager sessions,
        IWorldPacketCoordinator world,
        IMagicStatusEffectService magic,
        IZoneTransitionService zones)
    {
        this.sessions = sessions;
        this.world = world;
        this.magic = magic;
        this.magic.TransformationEnded += OnTransformationEndedAsync;
        zones.PlayerLeavingZone += (session, _) => ReleaseAsync(session);
    }

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessions.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        switch (packet.ReadByte())
        {
            case SubBoard when packet.RemainingBytes >= sizeof(int):
                await BoardAsync(session, packet.ReadInt());
                break;
            case SubLeave when packet.RemainingBytes >= 2 * sizeof(ushort):
                var x = packet.ReadUShort();
                var z = packet.ReadUShort();
                await LeaveAsync(session, x, z);
                break;
            case SubUse when packet.RemainingBytes >= sizeof(int):
                await UseAsync(session, packet.ReadInt());
                break;
            case SubExit:
                await ExitAsync(session);
                break;
        }
    }

    public async Task ReleaseAsync(UserSession session)
    {
        session.TowerHostId = 0;
        if (session.GuardTowerNpcId != 0)
        {
            if (session.ActiveBuffs.ContainsKey(SiegeTowerRules.GuardTowerSkill))
                await magic.CancelAsync(session, SiegeTowerRules.GuardTowerSkill);
            await ReleaseGuardTowerAsync(session);
        }
        if (session.TransformId == SiegeTowerRules.MovingTowerLook)
            await DismissPassengersAsync(session);
    }

    private async Task BoardAsync(UserSession session, int hostId)
    {
        var refusal = BoardRefusal(session, hostId, out var host);
        if (refusal != Succeeded)
        {
            await session.Client.SendPacket(Result(SubBoard, refusal));
            return;
        }

        session.TowerHostId = host!.CharacterId;
        await session.Client.SendPacket(Board(SiegeTowerRules.Wire(host.X), SiegeTowerRules.Wire(host.Z), SiegeTowerRules.Wire(host.Y)));
        await world.WarpAsync(session, SiegeTowerRules.Wire(host.X), SiegeTowerRules.Wire(host.Z));
    }

    private byte BoardRefusal(UserSession session, int hostId, out UserSession? host)
    {
        host = sessions.GetByCharacterId(hostId);
        if (session.TowerHostId != 0 || session.GuardTowerNpcId != 0)
            return BoardAlreadyOnboard;
        if (host == null || host == session || host.ZoneId != session.ZoneId || host.Hp <= 0)
            return BoardNoUser;
        if (host.TransformId != SiegeTowerRules.MovingTowerLook)
            return BoardNotTower;
        if (SiegeTowerRules.InSiegeLook(session.TransformId) || !MayBoard(session, host))
            return Failed;
        return SiegeTowerRules.Within(session.X, session.Y, session.Z, host.X, host.Y, host.Z, SiegeTowerRules.BoardReach)
            ? Succeeded
            : BoardTooFar;
    }

    private bool MayBoard(UserSession session, UserSession host) =>
        session.ZoneId == (byte)ZoneId.Delos
            ? SiegeTowerRules.SameSide(session.KnightsId, AllianceOf(session), host.KnightsId, AllianceOf(host))
            : !PvpRules.IsEnemy(session, host);

    private int AllianceOf(UserSession session) =>
        session.KnightsId > 0 ? sessions.Knights.GetClan(session.KnightsId)?.AllianceId ?? 0 : 0;

    private async Task LeaveAsync(UserSession session, ushort x, ushort z)
    {
        if (session.TowerHostId == 0)
        {
            await session.Client.SendPacket(Result(SubLeave, LeaveNotOnboard));
            return;
        }

        var host = sessions.GetByCharacterId(session.TowerHostId);
        if (host != null && host.ZoneId == session.ZoneId
            && !SiegeTowerRules.WithinGround(host.X, host.Z, (float)x / SiegeTowerRules.WireScale,
                (float)z / SiegeTowerRules.WireScale, SiegeTowerRules.BoardReach))
        {
            await session.Client.SendPacket(Result(SubLeave, LeaveTooFar));
            return;
        }

        session.TowerHostId = 0;
        await session.Client.SendPacket(Result(SubLeave, Succeeded));
        await world.WarpAsync(session, x, z);
    }

    private async Task UseAsync(UserSession session, int npcId)
    {
        var npc = sessions.Regions.GetNpc(npcId);
        var refusal = UseRefusal(session, npc);
        if (refusal != Succeeded)
        {
            await session.Client.SendPacket(Result(SubUse, refusal));
            return;
        }

        npc!.TowerUserId = session.CharacterId;
        session.GuardTowerNpcId = npc.UniqueId;
        await sessions.Regions.BroadcastFromNpc(npc, NpcPacketMapper.BuildInOutPacket(npc, InOutType.Out));
        if (!await magic.TransformAsync(session, SiegeTowerRules.GuardTowerSkill))
        {
            await ReleaseGuardTowerAsync(session);
            return;
        }

        await sessions.Regions.SendToRegion(session,
            Use(session.CharacterId, npc.UniqueId, SiegeTowerRules.Wire(npc.X), SiegeTowerRules.Wire(npc.Z), SiegeTowerRules.Wire(npc.Y)),
            excludeSender: false);
        await world.WarpAsync(session, SiegeTowerRules.Wire(npc.X), SiegeTowerRules.Wire(npc.Z));
    }

    private static byte UseRefusal(UserSession session, NpcInstance? npc)
    {
        if (npc == null || npc.ZoneId != session.ZoneId || npc.NpcType != SiegeTowerRules.GuardTowerNpcType || npc.IsDead)
            return UseNotAvailable;
        if (npc.TowerUserId != 0)
            return UseInUse;
        if (session.TowerHostId != 0 || session.GuardTowerNpcId != 0 || session.IsTransformed || session.Hp <= 0)
            return UseNotAvailable;
        if (npc.IsNationOwned && (int)npc.Nation != (int)session.Nation)
            return UseNotAvailable;
        return SiegeTowerRules.Within(session.X, session.Y, session.Z, npc.X, npc.Y, npc.Z, SiegeTowerRules.BoardReach)
            ? Succeeded
            : UseNotAvailable;
    }

    private async Task ExitAsync(UserSession session)
    {
        if (session.GuardTowerNpcId == 0)
        {
            await session.Client.SendPacket(Result(SubExit, Failed));
            return;
        }

        await magic.CancelAsync(session, SiegeTowerRules.GuardTowerSkill);
        await ReleaseGuardTowerAsync(session);
    }

    private async Task OnTransformationEndedAsync(UserSession session, int skillId)
    {
        if (skillId == SiegeTowerRules.GuardTowerSkill)
            await ReleaseGuardTowerAsync(session);
        else if (skillId == SiegeTowerRules.MovingTowerSkill)
            await DismissPassengersAsync(session);
    }

    private async Task ReleaseGuardTowerAsync(UserSession session)
    {
        var npcId = session.GuardTowerNpcId;
        if (npcId == 0)
            return;

        session.GuardTowerNpcId = 0;
        if (sessions.Regions.GetNpc(npcId) is { } npc && npc.TowerUserId == session.CharacterId)
        {
            npc.TowerUserId = 0;
            await sessions.Regions.BroadcastFromNpc(npc, NpcPacketMapper.BuildInOutPacket(npc, InOutType.In));
        }
        await sessions.Regions.SendToRegion(session, Exit(session.CharacterId, npcId), excludeSender: false);
    }

    private async Task DismissPassengersAsync(UserSession host)
    {
        foreach (var passenger in sessions.GetAll().Where(s => s.TowerHostId == host.CharacterId).ToList())
        {
            passenger.TowerHostId = 0;
            await passenger.Client.SendPacket(Result(SubEnded, EndedDismissed));
        }
    }
}
