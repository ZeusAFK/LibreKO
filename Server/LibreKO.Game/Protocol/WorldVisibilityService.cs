using LibreKO.Common.Enums;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IWorldVisibilityService
{
    Task HandleReqUserInAsync(IClient client, Packet packet);
    Task SendNearbyUsersToClientAsync(UserSession session);
    Task SendRegionUserListAsync(UserSession session);
    Task SendNpcRegionListAsync(UserSession session);
    Task HandleReqNpcInAsync(IClient client, Packet packet);
    Task HandleRegionChangeAsync(IClient client);
    Task HandleNpcRegionAsync(IClient client);
    Task HandleBottomUserListAsync(IClient client, Packet packet);
    Task BroadcastUserLookChangeAsync(UserSession session, byte slot, int itemId, short durability);
    Task BroadcastDisplayTitleAsync(UserSession session);
    Task BroadcastUserInOutAsync(UserSession session, InOutType type);
    Task BroadcastRegionTransitionAsync(UserSession session, int oldRegionX, int oldRegionZ);
}

public class WorldVisibilityService(
    SessionManager sessionManager,
    IPlayerInspectService playerInspectService,
    IMerchantLifecycleService merchantLifecycleService,
    ILogger<WorldVisibilityService> logger) : IWorldVisibilityService
{
    public async Task HandleReqUserInAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var nearbyUsers = sessionManager.Regions.GetNearbyUsers(session).ToList();
        var payloadBytes = packet.RemainingBytes;

        if (packet.RemainingBytes < 2)
        {
            logger.LogDebug("REQ_USER_IN from {Name}: no count, sending {N} nearby", session.Name, nearbyUsers.Count);
            await SendUserSnapshotAsync(session, nearbyUsers);
            return;
        }

        var requestedCount = packet.ReadUShort();
        logger.LogDebug(
            "REQ_USER_IN from {Name}: count={Count} payloadBytes={PayloadBytes} nearbyCount={NearbyCount} nearbyIds=[{NearbyIds}]",
            session.Name,
            requestedCount,
            payloadBytes,
            nearbyUsers.Count,
            string.Join(",", nearbyUsers.Select(u => u.CharacterId)));

        if (requestedCount == 0)
        {
            await SendUserSnapshotAsync(session, []);
            return;
        }

        if (requestedCount > 1000)
            requestedCount = 1000;

        var nearbyUsersById = new Dictionary<int, UserSession>(nearbyUsers.Count);
        foreach (var user in nearbyUsers)
            nearbyUsersById[user.CharacterId] = user;

        var requestedUsers = new List<UserSession>();
        var requestedIds = new List<int>();
        for (var index = 0; index < requestedCount && packet.RemainingBytes >= 4; index++)
        {
            var userId = packet.ReadInt();
            requestedIds.Add(userId);
            if (nearbyUsersById.TryGetValue(userId, out var nearby))
                requestedUsers.Add(nearby);
        }

        logger.LogDebug(
            "REQ_USER_IN from {Name}: requestedIds=[{ReqIds}] matched={Matched}/{Requested} remainingAfterRead={Rem}",
            session.Name,
            string.Join(",", requestedIds),
            requestedUsers.Count,
            requestedCount,
            packet.RemainingBytes);

        await SendUserSnapshotAsync(session, requestedUsers);
    }

    public async Task SendNearbyUsersToClientAsync(UserSession session)
    {
        logger.LogDebug("SendNearbyUsers for {Name} zone={Zone} pos=({X},{Z}) region=({RX},{RZ})",
            session.Name, session.ZoneId, session.X, session.Z, session.RegionX, session.RegionZ);

        var nearbyUsers = sessionManager.Regions.GetNearbyUsers(session).ToList();
        var nearbyNpcs = sessionManager.Regions.GetNearbyNpcs(session)
            .Where(npc => npc.IsAlive)
            .ToList();

        await SendUserSnapshotAsync(session, nearbyUsers);
        await SendNpcSnapshotAsync(session, nearbyNpcs);

        logger.LogDebug("Sent {UserCount} users and {NpcCount} NPCs to {Name}", nearbyUsers.Count, nearbyNpcs.Count, session.Name);
    }

    private async Task SendStallsInViewAsync(UserSession session, IReadOnlyCollection<UserSession> nearbyUsers)
    {
        var stalls = nearbyUsers
            .Where(user => user.Trade.IsMerchanting)
            .Select(merchantLifecycleService.StallOwnerOf)
            .ToList();

        if (stalls.Count == 0)
            return;

        await session.Client.SendPacket(
            MerchantPacketWriter.StallsInView(MerchantInOut.StallsInView, stalls));
    }

    public async Task HandleReqNpcInAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var nearbyNpcs = sessionManager.Regions.GetNearbyNpcs(session)
            .Where(npc => npc.IsAlive)
            .ToList();

        if (packet.RemainingBytes < 2)
        {
            await SendNpcSnapshotAsync(session, nearbyNpcs);
            return;
        }

        var count = packet.ReadUShort();
        logger.LogDebug("REQ_NPC_IN from {Name}: count={Count}", session.Name, count);
        if (count == 0)
        {
            await SendNpcSnapshotAsync(session, []);
            return;
        }

        if (count > 1000)
            count = 1000;

        var nearbyNpcsById = new Dictionary<int, NpcInstance>(nearbyNpcs.Count);
        foreach (var nearbyNpc in nearbyNpcs)
            nearbyNpcsById[nearbyNpc.UniqueId] = nearbyNpc;

        var requestedNpcs = new List<NpcInstance>();
        for (var index = 0; index < count && packet.RemainingBytes >= 4; index++)
        {
            var npcUniqueId = packet.ReadInt();
            if (nearbyNpcsById.TryGetValue(npcUniqueId, out var npc))
                requestedNpcs.Add(npc);
        }

        await SendNpcSnapshotAsync(session, requestedNpcs);
        logger.LogDebug("  Sent {Sent}/{Count} NPC_IN responses", requestedNpcs.Count, count);
    }

    public async Task HandleRegionChangeAsync(IClient client)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        await SendRegionUserListAsync(session);
    }

    public async Task HandleNpcRegionAsync(IClient client)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        await SendNpcRegionListAsync(session);
    }

    public async Task SendNpcRegionListAsync(UserSession session)
    {
        var npcs = sessionManager.Regions.GetNearbyNpcs(session)
            .Where(n => n.IsAlive)
            .ToList();

        logger.LogDebug("NPC_REGION for {Name}: {Count} nearby NPCs in zone {Zone} region ({RX},{RZ})",
            session.Name, npcs.Count, session.ZoneId, session.RegionX, session.RegionZ);

        if (npcs.Count > 0 && npcs.Count <= 10)
        {
            foreach (var n in npcs)
                logger.LogDebug("  NPC uid={UniqueId} npcId={NpcId} '{Name}' at ({X},{Z}) region ({RX},{RZ})",
                    n.UniqueId, n.NpcId, n.Name, n.X, n.Z, n.RegionX, n.RegionZ);
        }

        if (npcs.Count > VisibilityPacketWriter.RegionListMax)
            logger.LogWarning(
                "NPC_REGION for {Name} in zone {Zone} has {Count} NPCs; only the first {Max} are listed",
                session.Name, session.ZoneId, npcs.Count, VisibilityPacketWriter.RegionListMax);

        var result = VisibilityPacketWriter.NpcRegion(
            npcs.Select(npc => npc.UniqueId).ToList());

        await session.Client.SendPacket(result.CompressIfNeeded());
    }

    public async Task SendRegionUserListAsync(UserSession session)
    {
        var nearbyUsers = sessionManager.Regions.GetNearbyUsers(session).ToList();

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug(
                "REGIONCHANGE → {Name}: {Count} nearby users [{Names}] in zone {Zone} region ({RX},{RZ}) at pos ({X},{Z})",
                session.Name, nearbyUsers.Count,
                string.Join(",", nearbyUsers.Select(u => $"{u.Name}#{u.CharacterId}")),
                session.ZoneId, session.RegionX, session.RegionZ, session.X, session.Z);

        if (nearbyUsers.Count > VisibilityPacketWriter.RegionListMax)
            logger.LogWarning(
                "REGIONCHANGE for {Name} in zone {Zone} has {Count} users; only the first {Max} are listed",
                session.Name, session.ZoneId, nearbyUsers.Count, VisibilityPacketWriter.RegionListMax);

        await session.Client.SendPacket(VisibilityPacketWriter.RegionChangeClear());

        var result = VisibilityPacketWriter.RegionChangeList(
            nearbyUsers.Select(nearby => nearby.CharacterId).ToList());
        await session.Client.SendPacket(result.CompressIfNeeded());

        await session.Client.SendPacket(VisibilityPacketWriter.RegionChangeEnd());
    }

    private const byte BottomUserListHeaderSub = 1;

    private const float BottomUserListMaxDistanceSq = 300f;
    private const int BottomUserListMaxResults = 800;

    public async Task HandleBottomUserListAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null) return;

        var subOpcode = packet.ReadByte();

        switch ((PlayerInspectSubOpcode)subOpcode)
        {
            case PlayerInspectSubOpcode.Detail:
                await playerInspectService.HandleUserInformationAsync(session, packet);
                return;

            case PlayerInspectSubOpcode.Equipment:
                await playerInspectService.HandleEquipmentViewAsync(session, packet);
                return;

            case PlayerInspectSubOpcode.Nearby:
            case PlayerInspectSubOpcode.NearbyAll:
                break;

            default:
                return;
        }

        await SendBottomUserListHeaderAsync(session);
        await SendBottomUserListAsync(session, subOpcode);
    }

    private static Task SendBottomUserListHeaderAsync(UserSession session)
    {
        var header = VisibilityPacketWriter.BottomUserListHeader(
            BottomUserListHeaderSub, session.ZoneId, 0);
        return session.Client.SendPacket(header);
    }

    private async Task SendBottomUserListAsync(UserSession session, byte subOpcode)
    {
        var nearbyUsers = GetBottomUserListCandidates(session);

        var entries = nearbyUsers
            .Select(user => new VisibilityPacketWriter.BottomUserEntry(
                user.Name, (byte)user.Nation,
                (short)(user.X * 10), (short)(user.Z * 10),
                user.KnightsId))
            .ToList();

        var result = VisibilityPacketWriter.BottomUserList(subOpcode, session.ZoneId, entries);
        await session.Client.SendPacket(result.CompressIfNeeded());
    }

    private List<UserSession> GetBottomUserListCandidates(UserSession session)
    {
        return sessionManager.GetAll()
            .Where(u => u.ZoneId == session.ZoneId
                        && u.CharacterId != session.CharacterId
                        && (session.IsGM || !u.IsGM))
            .Select(u => (User: u, DistSq: SquaredDistance(session, u)))
            .Where(x => session.IsGM || x.DistSq <= BottomUserListMaxDistanceSq)
            .OrderBy(x => x.DistSq)
            .Take(BottomUserListMaxResults)
            .Select(x => x.User)
            .ToList();
    }

    private static float SquaredDistance(UserSession a, UserSession b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    public async Task BroadcastUserLookChangeAsync(UserSession session, byte slot, int itemId, short durability)
    {
        // Allow real equipment slots (0..SlotMax-1) and cospre visual slots
        // (CospreStart..CospreStart+CospreMax-1). Other inventory positions
        // are not visually represented and shouldn't trigger a look-change broadcast.
        var inEquipment = slot < InventoryConstants.SlotMax;
        var inCospre = slot >= InventoryConstants.CospreStart
            && slot < InventoryConstants.CospreStart + InventoryConstants.CospreMax;
        if (!inEquipment && !inCospre)
            return;

        var result = VisibilityPacketWriter.LookChange(
            session.CharacterId, slot, itemId, (ushort)durability);
        await sessionManager.Regions.SendToRegion(session, result);
    }

    public async Task BroadcastDisplayTitleAsync(UserSession session)
    {
        var packet = AchievementPacketWriter.TitleChanged(
            session.CharacterId, session.DisplayTitleId);
        await session.Client.SendPacket(packet);
        await sessionManager.Regions.SendToRegion(session, packet);
    }

    public async Task BroadcastUserInOutAsync(UserSession session, InOutType type)
    {
        var result = BuildUserInOutPacket(session, type);
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var nearby = sessionManager.Regions.GetNearbyUsers(session).ToList();
            logger.LogDebug(
                "INOUT broadcast: {Sender} (id={Id}) type={Type} → {Count} nearby [{Names}] in zone {Zone} region ({RX},{RZ})",
                session.Name, session.CharacterId, type, nearby.Count,
                string.Join(",", nearby.Select(u => u.Name)),
                session.ZoneId, session.RegionX, session.RegionZ);
        }
        await sessionManager.Regions.SendToRegion(session, result);

        if (type != InOutType.Out && session.IsGM)
            await sessionManager.Regions.SendToRegion(session,
                AdminPanelPacketWriter.GmFx(session.CharacterId, session.GmModeEnabled));
        if (type != InOutType.Out && session.InCombatStance)
            await sessionManager.Regions.SendToRegion(session, BuildCombatStancePacket(session));
    }

    public async Task BroadcastRegionTransitionAsync(UserSession session, int oldRegionX, int oldRegionZ)
    {
        var oldNearby = sessionManager.Regions
            .GetNearbyUsersAt(session.Room, session.ZoneId, oldRegionX, oldRegionZ, session.CharacterId)
            .ToHashSet();
        var newNearby = sessionManager.Regions.GetNearbyUsers(session).ToHashSet();

        var gainedSight = newNearby.Where(u => !oldNearby.Contains(u)).ToList();
        var lostSight = oldNearby.Where(u => !newNearby.Contains(u)).ToList();

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug(
                "Region transition: {Name} ({OldRX},{OldRZ})→({NewRX},{NewRZ}) gainedSight=[{Gained}] lostSight=[{Lost}]",
                session.Name, oldRegionX, oldRegionZ, session.RegionX, session.RegionZ,
                string.Join(",", gainedSight.Select(u => u.Name)),
                string.Join(",", lostSight.Select(u => u.Name)));

        if (gainedSight.Count > 0)
        {
            var inPacket = BuildUserInOutPacket(session, InOutType.In);
            await Task.WhenAll(gainedSight.Select(u => u.Client.SendPacket(inPacket)));
            if (session.IsGM)
            {
                var gmFx = AdminPanelPacketWriter.GmFx(session.CharacterId, session.GmModeEnabled);
                await Task.WhenAll(gainedSight.Select(u => u.Client.SendPacket(gmFx)));
            }
            if (session.InCombatStance)
            {
                var stance = BuildCombatStancePacket(session);
                await Task.WhenAll(gainedSight.Select(u => u.Client.SendPacket(stance)));
            }
        }

        if (lostSight.Count > 0)
        {
            var outPacket = BuildUserInOutPacket(session, InOutType.Out);
            await Task.WhenAll(lostSight.Select(u => u.Client.SendPacket(outPacket)));
        }
    }

    private Packet BuildUserInOutPacket(UserSession session, InOutType type) =>
        UserInfoPacketWriter.InOut(
            (byte)type,
            session.CharacterId,
            type == InOutType.Out ? null : UserStateOf(session));


    private async Task SendUserSnapshotAsync(UserSession session, List<UserSession> users)
    {
        var result = VisibilityPacketWriter.SnapshotHeader(
            GameOpcodes.GS_REQ_USERIN, (short)users.Count);
        foreach (var user in users)
        {
            VisibilityPacketWriter.BeginUserRecord(result, user.CharacterId);
            WriteUserInfo(result, user);
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug(
                "REQ_USERIN response → {Recipient}: {Count} users [{Names}]",
                session.Name, users.Count, string.Join(",", users.Select(u => u.Name)));

        await session.Client.SendPacket(result.CompressIfNeeded());

        foreach (var user in users)
        {
            if (user.InCombatStance)
                await session.Client.SendPacket(BuildCombatStancePacket(user));
            if (user.IsGM)
                await session.Client.SendPacket(AdminPanelPacketWriter.GmFx(user.CharacterId, user.GmModeEnabled));
        }

        await SendStallsInViewAsync(session, users);
    }

    private static Packet BuildCombatStancePacket(UserSession session)
    {
        return VisibilityPacketWriter.CombatStance(
            session.CharacterId,
            (byte)StateChangeType.CombatStance,
            (byte)(session.InCombatStance ? CombatStanceState.Ready : CombatStanceState.Relaxed));
    }

    private static async Task SendNpcSnapshotAsync(UserSession session, List<NpcInstance> npcs)
    {
        var result = VisibilityPacketWriter.SnapshotHeader(
            GameOpcodes.GS_REQ_NPCIN, (short)npcs.Count);
        foreach (var npc in npcs)
        {
            VisibilityPacketWriter.BeginNpcRecord(result, npc.UniqueId);
            NpcPacketMapper.WriteNpcInfo(result, npc);
        }

        await session.Client.SendPacket(result.CompressIfNeeded());
    }

    private void WriteUserInfo(Packet packet, UserSession session) =>
        UserInfoPacketWriter.WriteRecord(packet, UserStateOf(session));

    private UserInfoPacketWriter.UserState UserStateOf(UserSession session)
    {
        UserInfoPacketWriter.ClanState? clanState = null;
        if (session.KnightsId > 0)
        {
            var clan = sessionManager.Knights.GetClan(session.KnightsId);
            var alliance = sessionManager.Knights.GetAllianceForClan(session.KnightsId);
            clanState = new UserInfoPacketWriter.ClanState(
                (short)(alliance?.MainClanId ?? 0),
                session.KnightsName,
                clan?.Grade ?? 0,
                0,
                clan?.Cape ?? 0,
                clan?.CapeR ?? 0,
                clan?.CapeG ?? 0,
                clan?.CapeB ?? 0,
                clan?.Flag ?? 0);
        }

        var visuals = new List<UserInfoPacketWriter.VisualItem>(InventoryConstants.VisualSlotCount);
        foreach (var slot in InventoryConstants.VisualSlots)
        {
            var item = session.GetEquippedItem(slot);
            visuals.Add(new UserInfoPacketWriter.VisualItem(item.ItemId, item.Durability, item.Flag));
        }

        return new UserInfoPacketWriter.UserState(
            session.Name,
            (byte)session.Nation,
            session.KnightsId,
            session.KnightsFame,
            clanState,
            GetNoClanNationCode(session),
            session.Level,
            session.Race,
            session.Class,
            session.GetPosX,
            session.GetPosZ,
            session.GetPosY,
            session.Face,
            session.Hair,
            PoseOf(session),
            NeedParty: false,
            session.IsGM,
            session.IsPartyLeader,
            session.IsInvisible,
            session.Direction,
            session.ZoneId,
            session.IsHidingHelmet,
            session.DisplayTitleId,
            visuals);
    }

    private static byte PoseOf(UserSession session) => (byte)(
        session.Hp <= 0 ? UserPoseState.Dead
        : session.IsMining ? UserPoseState.Mining
        : session.IsFishing ? UserPoseState.Fishing
        : session.IsSitting ? UserPoseState.Sitting
        : UserPoseState.Standing);

    private static ushort GetNoClanNationCode(UserSession session)
    {
        // No-clan branch: king > GM > nation-war-zone > default-by-nation
        if (session.IsGM) return 99;
        return session.Nation switch
        {
            AccountNation.Karus => 93,
            AccountNation.ElMorad => 94,
            _ => 0,
        };
    }
}
