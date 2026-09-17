using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IWorldPacketCoordinator
{
    Task HandleMoveAsync(IClient client, Packet packet);
    Task HandleRotateAsync(IClient client, Packet packet);
    Task HandleStateChangeAsync(IClient client, Packet packet);
    Task HandleReqUserInAsync(IClient client, Packet packet);
    Task HandleHomeAsync(IClient client);
    Task WarpAsync(UserSession session, ushort posX, ushort posZ);
    Task HandleRecvWarpAsync(IClient client, Packet packet);
    Task HandleWarpListAsync(IClient client, Packet packet);
    Task HandleZoneChangeAsync(IClient client, Packet packet);
    Task SendNearbyUsersToClientAsync(UserSession session);
    Task SendRegionUserListAsync(UserSession session);
    Task SendNpcRegionListAsync(UserSession session);
    Task HandleReqNpcInAsync(IClient client, Packet packet);
    Task HandleRegionChangeAsync(IClient client);
    Task HandleNpcRegionAsync(IClient client);
    Task HandleBottomUserListAsync(IClient client, Packet packet);
    Task HandleStealthAsync(IClient client, Packet packet);
    Task HandleSpeedHackCheckAsync(IClient client, Packet packet);
    Task HandleObjectEventAsync(IClient client, Packet packet);
    Task BroadcastUserLookChangeAsync(UserSession session, byte slot, int itemId, short durability);
    Task BroadcastDisplayTitleAsync(UserSession session);
    Task BroadcastUserInOutAsync(UserSession session, InOutType type);
    Task BroadcastRegionTransitionAsync(UserSession session, int oldRegionX, int oldRegionZ);
}

public class WorldPacketCoordinator(
    IWorldMovementService worldMovementService,
    IWorldObjectEventService worldObjectEventService,
    IWorldVisibilityService worldVisibilityService) : IWorldPacketCoordinator
{
    public Task HandleMoveAsync(IClient client, Packet packet) =>
        worldMovementService.HandleMoveAsync(client, packet);

    public Task HandleRotateAsync(IClient client, Packet packet) =>
        worldMovementService.HandleRotateAsync(client, packet);

    public Task HandleStateChangeAsync(IClient client, Packet packet) =>
        worldMovementService.HandleStateChangeAsync(client, packet);

    public Task HandleReqUserInAsync(IClient client, Packet packet) =>
        worldVisibilityService.HandleReqUserInAsync(client, packet);

    public Task HandleHomeAsync(IClient client) =>
        worldMovementService.HandleHomeAsync(client);

    public Task WarpAsync(UserSession session, ushort posX, ushort posZ) =>
        worldMovementService.WarpAsync(session, posX, posZ);

    public Task HandleRecvWarpAsync(IClient client, Packet packet) =>
        worldMovementService.HandleRecvWarpAsync(client, packet);

    public Task HandleWarpListAsync(IClient client, Packet packet) =>
        worldMovementService.HandleWarpListAsync(client, packet);

    public Task HandleZoneChangeAsync(IClient client, Packet packet) =>
        worldMovementService.HandleZoneChangeAsync(client, packet);

    public Task SendNearbyUsersToClientAsync(UserSession session) =>
        worldVisibilityService.SendNearbyUsersToClientAsync(session);

    public Task SendRegionUserListAsync(UserSession session) =>
        worldVisibilityService.SendRegionUserListAsync(session);

    public Task SendNpcRegionListAsync(UserSession session) =>
        worldVisibilityService.SendNpcRegionListAsync(session);

    public Task HandleReqNpcInAsync(IClient client, Packet packet) =>
        worldVisibilityService.HandleReqNpcInAsync(client, packet);

    public Task HandleRegionChangeAsync(IClient client) =>
        worldVisibilityService.HandleRegionChangeAsync(client);

    public Task HandleNpcRegionAsync(IClient client) =>
        worldVisibilityService.HandleNpcRegionAsync(client);

    public Task HandleBottomUserListAsync(IClient client, Packet packet) =>
        worldVisibilityService.HandleBottomUserListAsync(client, packet);

    public Task HandleStealthAsync(IClient client, Packet packet) =>
        worldMovementService.HandleStealthAsync(client, packet);

    public Task HandleSpeedHackCheckAsync(IClient client, Packet packet) =>
        worldMovementService.HandleSpeedHackCheckAsync(client, packet);

    public Task HandleObjectEventAsync(IClient client, Packet packet) =>
        worldObjectEventService.HandleObjectEventAsync(client, packet);

    public Task BroadcastUserLookChangeAsync(UserSession session, byte slot, int itemId, short durability) =>
        worldVisibilityService.BroadcastUserLookChangeAsync(session, slot, itemId, durability);

    public Task BroadcastDisplayTitleAsync(UserSession session) =>
        worldVisibilityService.BroadcastDisplayTitleAsync(session);

    public Task BroadcastUserInOutAsync(UserSession session, InOutType type) =>
        worldVisibilityService.BroadcastUserInOutAsync(session, type);

    public Task BroadcastRegionTransitionAsync(UserSession session, int oldRegionX, int oldRegionZ) =>
        worldVisibilityService.BroadcastRegionTransitionAsync(session, oldRegionX, oldRegionZ);
}
