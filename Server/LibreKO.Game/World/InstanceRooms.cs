using System.Collections.Concurrent;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public sealed class InstanceRoom(ushort id, byte zoneId, short set, DateTime expiresAt)
{
    public ushort Id { get; } = id;
    public byte ZoneId { get; } = zoneId;
    public short Set { get; } = set;
    public DateTime ExpiresAt { get; } = expiresAt;
    public ConcurrentDictionary<int, byte> Members { get; } = new();
    public List<NpcInstance> Npcs { get; } = [];
}

public sealed class InstanceRoomRegistry(SessionManager sessionManager, ILogger<InstanceRoomRegistry> logger)
{
    private readonly ConcurrentDictionary<ushort, InstanceRoom> _rooms = new();
    private int _nextRoom;

    public IEnumerable<InstanceRoom> Rooms => _rooms.Values;

    public InstanceRoom Open(byte zoneId, short set, TimeSpan duration)
    {
        ushort id;
        do
        {
            id = (ushort)(Interlocked.Increment(ref _nextRoom) & 0xFFFF);
        } while (id == 0 || _rooms.ContainsKey(id));

        var room = new InstanceRoom(id, zoneId, set, DateTime.UtcNow + duration);
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

    public void Close(InstanceRoom room)
    {
        if (!_rooms.TryRemove(room.Id, out _))
            return;

        foreach (var npc in room.Npcs)
            sessionManager.Regions.RemoveNpc(npc);
        room.Npcs.Clear();
        logger.LogInformation("Instance room {Room} in zone {Zone} closed", room.Id, room.ZoneId);
    }
}

public interface IInstanceEntryService
{
    Task EnterAsync(UserSession session, byte zoneId, short set, float x, float z);
}

public sealed class InstanceEntryService(
    SessionManager sessionManager,
    IGameDataService gameData,
    IMonsterAggressionPolicy aggression,
    IZoneTransitionService zoneTransition,
    InstanceRoomRegistry rooms,
    ILogger<InstanceEntryService> logger) : IInstanceEntryService
{
    public async Task EnterAsync(UserSession session, byte zoneId, short set, float x, float z)
    {
        var duration = TimeSpan.FromMinutes(GameConstants.InstanceRoomMinutes);
        var room = rooms.Open(zoneId, set, duration);
        Populate(room);

        var returnPoint = (session.ZoneId, session.X, session.Z);
        foreach (var member in Participants(session))
        {
            rooms.Join(room, member);
            member.InstanceReturn = member.ZoneId == zoneId ? member.InstanceReturn : returnPoint;
            await zoneTransition.ChangeZoneAsync(member, zoneId, x, z);
            await member.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)member.Nation,
                $"The dungeon closes in {GameConstants.InstanceRoomMinutes} minutes."));
        }

        logger.LogInformation("Instance room {Room}: zone {Zone} set {Set} opened by {Name} with {Count} monsters",
            room.Id, zoneId, set, session.Name, room.Npcs.Count);
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

    private void Populate(InstanceRoom room)
    {
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
    ILogger<InstanceRoomExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
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
