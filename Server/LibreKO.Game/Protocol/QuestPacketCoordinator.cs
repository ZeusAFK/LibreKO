using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IQuestPacketCoordinator
{
    Task HandleSelectMsgAsync(IClient client, Packet packet);
    Task HandleClientEventAsync(IClient client, Packet packet);
    Task HandleNpcEventAsync(IClient client, Packet packet);
    Task HandleQuestAsync(IClient client, Packet packet);
    Task CheckQuestKillAsync(UserSession killer, int npcId);
}

public class QuestPacketCoordinator(
    IQuestNpcInteractionService questNpcInteractionService,
    IQuestProgressionService questProgressionService) : IQuestPacketCoordinator
{
    public Task HandleSelectMsgAsync(IClient client, Packet packet) =>
        questNpcInteractionService.HandleSelectMsgAsync(client, packet);

    public Task HandleClientEventAsync(IClient client, Packet packet) =>
        questNpcInteractionService.HandleClientEventAsync(client, packet);

    public Task HandleNpcEventAsync(IClient client, Packet packet) =>
        questNpcInteractionService.HandleNpcEventAsync(client, packet);

    public Task HandleQuestAsync(IClient client, Packet packet) =>
        questProgressionService.HandleQuestAsync(client, packet);

    public Task CheckQuestKillAsync(UserSession killer, int npcId) =>
        questProgressionService.CheckQuestKillAsync(killer, npcId);
}
