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
    IPetSkillService petSkillService,
    ILogger<PetPacketCoordinator> logger) : IPetPacketCoordinator
{
    private const int ModeRequestBytes = 1;
    private const int FoodRequestBytes = 5;
    private const int SkillDataSlots = 5;
    private const int SkillRequestBytes = 1 + sizeof(int) * (3 + SkillDataSlots);

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
            case PetSubOpcode.UseSkill when packet.RemainingBytes >= SkillRequestBytes:
                await petSkillService.UseAsync(session, ReadSkillRequest(packet));
                break;
            default:
                logger.LogDebug("GS_PET sub-opcode {Sub} from {Name} ignored", subOpcode, session.Name);
                break;
        }
    }

    private static PetSkillRequest ReadSkillRequest(Packet packet)
    {
        var stage = packet.ReadByte();
        var skillId = packet.ReadInt();
        var casterId = packet.ReadInt();
        var targetId = packet.ReadInt();
        var data = new int[SkillDataSlots];
        for (var i = 0; i < data.Length; i++)
            data[i] = packet.ReadInt();
        return new PetSkillRequest(stage, skillId, casterId, targetId, data);
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
