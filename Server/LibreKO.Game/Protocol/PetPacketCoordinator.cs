using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IPetPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class PetPacketCoordinator(
    SessionManager sessionManager,
    IPetService petService,
    ILogger<PetPacketCoordinator> logger) : IPetPacketCoordinator
{
    private const int ModeRequestBytes = 1;
    private const int FoodRequestBytes = 5;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var subOpcode = (PetSubOpcode)packet.ReadByte();
        switch (subOpcode)
        {
            case PetSubOpcode.ModeFunction when packet.RemainingBytes >= 1:
                await HandleFunctionAsync(session, (PetFunction)packet.ReadByte(), packet);
                break;
            case PetSubOpcode.UseSkill:
                logger.LogDebug("Familiar skill request from {Name} ignored", session.Name);
                break;
            default:
                logger.LogDebug("GS_PET sub-opcode {Sub} from {Name} ignored", subOpcode, session.Name);
                break;
        }
    }

    private async Task HandleFunctionAsync(UserSession session, PetFunction function, Packet packet)
    {
        switch (function)
        {
            case PetFunction.Mode when packet.RemainingBytes >= ModeRequestBytes:
                var mode = (PetMode)packet.ReadByte();
                if (mode is PetMode.Attack or PetMode.Defence or PetMode.Looting)
                    await petService.SetModeAsync(session, mode);
                break;
            case PetFunction.Food when packet.RemainingBytes >= FoodRequestBytes:
                var bagSlot = packet.ReadByte();
                var itemId = packet.ReadInt();
                await petService.FeedAsync(session, bagSlot, itemId);
                break;
            default:
                logger.LogDebug("Familiar function {Function} from {Name} ignored", function, session.Name);
                break;
        }
    }
}
