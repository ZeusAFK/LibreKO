using System.Collections.Concurrent;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public sealed class InstanceRoom(ushort id, byte zoneId, short set, DateTime expiresAt, bool endsOnBossKill = false)
{
    public ushort Id { get; } = id;
    public byte ZoneId { get; } = zoneId;
    public short Set { get; } = set;
    public DateTime ExpiresAt { get; private set; } = expiresAt;
    public bool EndsOnBossKill { get; } = endsOnBossKill;
    public bool Finishing { get; private set; }
    public ConcurrentDictionary<int, byte> Members { get; } = new();
    public List<NpcInstance> Npcs { get; } = [];

    public bool Finish(DateTime closesAt)
    {
        if (Finishing || closesAt >= ExpiresAt)
            return false;

        Finishing = true;
        ExpiresAt = closesAt;
        return true;
    }
}

public sealed class InstanceRoomRegistry(SessionManager sessionManager, ILogger<InstanceRoomRegistry> logger)
{
    private readonly ConcurrentDictionary<ushort, InstanceRoom> _rooms = new();
    private int _nextRoom;

    public IEnumerable<InstanceRoom> Rooms => _rooms.Values;

    public InstanceRoom Open(byte zoneId, short set, TimeSpan duration, bool endsOnBossKill = false)
    {
        ushort id;
        do
        {
            id = (ushort)(Interlocked.Increment(ref _nextRoom) & 0xFFFF);
        } while (id == 0 || _rooms.ContainsKey(id));

        var room = new InstanceRoom(id, zoneId, set, DateTime.UtcNow + duration, endsOnBossKill);
        _rooms[id] = room;
        return room;
    }

    public InstanceRoom? Get(ushort id) => _rooms.GetValueOrDefault(id);

    public bool Holds(ushort roomId, byte zoneId) => _rooms.TryGetValue(roomId, out var room) && room.ZoneId == zoneId;

    public void Join(InstanceRoom room, UserSession session)
    {
        if (session.Room != 0 && session.Room != room.Id)
            Leave(session);
        room.Members[session.CharacterId] = 0;
        session.Room = room.Id;
    }

    public void Leave(UserSession session)
    {
        var roomId = session.Room;
        session.Room = 0;
        session.InstanceReturn = null;
        if (roomId == 0 || !_rooms.TryGetValue(roomId, out var room))
            return;

        room.Members.TryRemove(session.CharacterId, out _);
        if (room.Members.IsEmpty)
            Close(room);
    }

    public event Action<InstanceRoom>? RoomClosed;

    public void Close(InstanceRoom room)
    {
        if (!_rooms.TryRemove(room.Id, out _))
            return;

        foreach (var npc in room.Npcs)
            sessionManager.Regions.RemoveNpc(npc);
        room.Npcs.Clear();
        logger.LogInformation("Instance room {Room} in zone {Zone} closed", room.Id, room.ZoneId);
        RoomClosed?.Invoke(room);
    }
}

public interface IInstanceEntryService
{
    Task EnterAsync(UserSession session, byte zoneId, short set, float x, float z);
    Task EnterClanAsync(UserSession session, byte zoneId, short set, float x, float z);
    Task EnterAloneAsync(UserSession session, byte zoneId, short set, float x, float z, bool endsOnBossKill);
    void Populate(InstanceRoom room);
}

public sealed class InstanceEntryService(
    SessionManager sessionManager,
    IGameDataService gameData,
    IMonsterAggressionPolicy aggression,
    IZoneTransitionService zoneTransition,
    InstanceRoomRegistry rooms,
    ILogger<InstanceEntryService> logger) : IInstanceEntryService
{
    public Task EnterAsync(UserSession session, byte zoneId, short set, float x, float z)
    {
        (byte ZoneId, float X, float Z)? returnPoint = (session.ZoneId, session.X, session.Z);
        return OpenAsync(session, Participants(session), zoneId, set, x, z, _ => returnPoint, endsOnBossKill: false);
    }

    public Task EnterAloneAsync(UserSession session, byte zoneId, short set, float x, float z, bool endsOnBossKill) =>
        OpenAsync(session, [session], zoneId, set, x, z, _ => null, endsOnBossKill);

    public Task EnterClanAsync(UserSession session, byte zoneId, short set, float x, float z)
    {
        if (session.KnightsId <= 0)
        {
            logger.LogInformation("{Name} is in no clan, so no clan room of zone {Zone} opens", session.Name, zoneId);
            return Task.CompletedTask;
        }

        return OpenAsync(session, ClanMembers(session), zoneId, set, x, z,
            member => (member.ZoneId, member.X, member.Z), endsOnBossKill: false);
    }

    private async Task OpenAsync(
        UserSession session, IEnumerable<UserSession> members, byte zoneId, short set, float x, float z,
        Func<UserSession, (byte ZoneId, float X, float Z)?> returnPoint, bool endsOnBossKill)
    {
        var duration = TimeSpan.FromMinutes(GameConstants.InstanceRoomMinutes);
        var room = rooms.Open(zoneId, set, duration, endsOnBossKill);
        Populate(room);

        foreach (var member in members.ToList())
        {
            rooms.Join(room, member);
            member.InstanceReturn = member.ZoneId == zoneId ? member.InstanceReturn : returnPoint(member);
            await zoneTransition.ChangeZoneAsync(member, zoneId, x, z);
            await member.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)member.Nation,
                $"The dungeon closes in {GameConstants.InstanceRoomMinutes} minutes."));
        }

        logger.LogInformation("Instance room {Room}: zone {Zone} set {Set} opened by {Name} with {Count} monsters",
            room.Id, zoneId, set, session.Name, room.Npcs.Count);
    }

    private IEnumerable<UserSession> ClanMembers(UserSession session)
    {
        yield return session;
        foreach (var member in sessionManager.GetAll())
        {
            if (member.CharacterId == session.CharacterId || member.KnightsId != session.KnightsId)
                continue;
            if (member.Room != 0 || member.IsWarping || ZoneRules.IsTempleEvent(member.ZoneId))
                continue;
            yield return member;
        }
    }

    private IEnumerable<UserSession> Participants(UserSession session)
    {
        yield return session;
        if (!session.IsInParty)
            yield break;

        var party = sessionManager.Parties.GetParty(session.PartyIndex);
        if (party == null)
            yield break;

        foreach (var memberId in party.MemberIds)
        {
            if (memberId < 0 || memberId == session.CharacterId)
                continue;

            var member = sessionManager.GetByCharacterId(memberId);
            if (member != null && member.ZoneId == session.ZoneId && !member.IsWarping)
                yield return member;
        }
    }

    public void Populate(InstanceRoom room)
    {
        var nestLevel = MonsterStoneRules.FamilyLevel(room.ZoneId, room.Set);
        foreach (var pos in gameData.NpcPositions)
        {
            if (pos.ZoneId != room.ZoneId || pos.Room != room.Set)
                continue;

            var npcData = gameData.GetSpawnProto(pos);
            if (npcData == null)
            {
                logger.LogWarning("Instance set {Set} of zone {Zone} names NPC {NpcId} which has no data", room.Set, room.ZoneId, pos.NpcId);
                continue;
            }

            var count = pos.NumNPC > 1 ? pos.NumNPC : 1;
            for (var i = 0; i < count; i++)
            {
                var npc = NpcInstance.FromData(npcData, pos, 0);
                npc.Room = room.Id;
                npc.RespawnType = NpcRespawnType.Never;
                if (!npc.IsMonster && npc.IsNationOwned)
                    npc.Nation = EntityNation.None;
                NestBalance.Apply(npc, nestLevel, npcData.IsBoss);
                aggression.Apply(npc);
                var height = sessionManager.Maps?.GetHeight(npc.ZoneId, npc.X, npc.Z) ?? 0f;
                npc.Y = height;
                npc.SpawnY = height;
                sessionManager.Regions.SpawnNpc(npc);
                room.Npcs.Add(npc);
            }
        }
    }
}

public sealed class InstanceRoomExpiryService(
    InstanceRoomRegistry rooms,
    SessionManager sessionManager,
    IZoneTransitionService zoneTransition,
    IDrakiTowerService drakiTowerService,
    ILogger<InstanceRoomExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await drakiTowerService.CheckTimeoutsAsync();

            var now = DateTime.UtcNow;
            foreach (var room in rooms.Rooms.Where(r => r.ExpiresAt <= now).ToList())
            {
                foreach (var characterId in room.Members.Keys.ToList())
                {
                    var session = sessionManager.GetByCharacterId(characterId);
                    if (session == null)
                    {
                        room.Members.TryRemove(characterId, out _);
                        continue;
                    }

                    if (room.ZoneId == DrakiTowerRules.ZoneIdValue)
                    {
                        var state = drakiTowerService.GetRoomState(room.Id);
                        var elapsed = state != null ? (uint)(DateTime.UtcNow - state.StartTime).TotalSeconds : 0u;
                        var stage = state?.CurrentStage ?? session.DrakiStage;
                        var subStage = state?.CurrentSubStage ?? session.DrakiSubStage;
                        await session.Client.SendPacket(EventPacketWriter.DrakiLeaveFirst());
                        await session.Client.SendPacket(EventPacketWriter.DrakiLeaveSecond(stage, subStage, elapsed));
                    }

                    var (zone, x, z) = session.InstanceReturn ?? ((byte)ZoneId.Moradon, 0f, 0f);
                    try
                    {
                        await zoneTransition.ChangeZoneAsync(session, zone, x, z);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not send {Name} out of instance room {Room}", session.Name, room.Id);
                        rooms.Leave(session);
                    }
                }

                rooms.Close(room);
            }
        }
    }
}
