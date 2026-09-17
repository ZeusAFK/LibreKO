using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IPetPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Task SendSatisfactionUpdateAsync(UserSession session);
    Task SendDeathAsync(UserSession session);
}

public class PetPacketCoordinator(
    SessionManager sessionManager,
    ILogger<PetPacketCoordinator> logger) : IPetPacketCoordinator
{

    private const byte NormalMode = 5;
    private const byte FoodMode = 16;

    private const byte CodeSatisfactionUpdate = 0x0F;
    private const byte CodeFood = 0x10;
    private const byte CodeDeath = 2;

    private const int FoodItem20Pct = 389570000;
    private const int FoodItem50Pct = 389580000;
    private const int FoodItem100Pct = 389590000;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 1)
            return;

        var subOpcode = (PetSubOpcode)packet.ReadByte();
        switch (subOpcode)
        {
            case PetSubOpcode.ModeFunction:
                await HandleModeFunctionAsync(session, packet);
                break;
            case PetSubOpcode.UseSkill:
                // we don't yet have NPC casting infrastructure for player pets.
                logger.LogDebug("WIZ_PET skill use from {Name} ignored (pet skills not implemented)", session.Name);
                break;
            default:
                logger.LogDebug("WIZ_PET unknown sub-opcode {Sub} from {Name}", subOpcode, session.Name);
                break;
        }
    }

    private async Task HandleModeFunctionAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;

        var subCode = packet.ReadByte();
        var mode = packet.ReadByte();

        switch (subCode)
        {
            case NormalMode:
                await HandleNormalModeAsync(session, mode, packet);
                break;
            case FoodMode:
                await HandleFoodModeAsync(session, slotIndex: mode, packet);
                break;
        }
    }

    private async Task HandleNormalModeAsync(UserSession session, byte mode, Packet packet)
    {
        if (session.Pet == null)
            return;

        if (mode == PetState.ModeAttack || mode == PetState.ModeDefence || mode == PetState.ModeLooting)
        {
            session.Pet.Mode = mode;
            await session.Client.SendPacket(
                PetPacketWriter.ModeChanged(PetSubOpcode.ModeFunction, NormalMode, mode));
            return;
        }

        if (mode == PetState.ModeChat)
        {
            var chatMessage = packet.RemainingBytes > 0 ? packet.ReadString() : string.Empty;
            await session.Client.SendPacket(PetPacketWriter.ChatChanged(
                PetSubOpcode.ModeFunction, NormalMode, PetState.ModeChat, chatMessage));
        }
    }

    private async Task HandleFoodModeAsync(UserSession session, byte slotIndex, Packet packet)
    {
        if (session.Pet == null || packet.RemainingBytes < 4)
            return;

        var itemId = packet.ReadInt();
        if (itemId != FoodItem20Pct && itemId != FoodItem50Pct && itemId != FoodItem100Pct)
            return;

        var inventoryStart = Common.Domain.Entities.GameData.InventoryConstants.InventoryStart;
        var slotPos = inventoryStart + slotIndex;
        if (slotPos < inventoryStart || slotPos >= inventoryStart + Common.Domain.Entities.GameData.InventoryConstants.HaveMax)
            return;

        var slot = session.Inventory[slotPos];
        if (slot.ItemId != itemId || slot.Count == 0)
            return;

        var oldSat = session.Pet.Satisfaction;
        var pct = itemId switch
        {
            FoodItem20Pct => 20,
            FoodItem50Pct => 50,
            _ => 100,
        };
        var newSat = (short)Math.Min(PetState.MaxSatisfaction, oldSat + (oldSat * pct / 100));
        session.Pet.Satisfaction = newSat;

        slot.Count -= 1;
        if (slot.Count == 0) slot.Clear();

        await session.Client.SendPacket(PetPacketWriter.Fed(
            PetSubOpcode.ModeFunction, CodeFood, (ushort)oldSat, itemId,
            (ushort)(PetState.MaxSatisfaction - oldSat)));

        await SendSatisfactionUpdateAsync(session);
    }

    public async Task SendSatisfactionUpdateAsync(UserSession session)
    {
        if (session.Pet == null) return;

        await session.Client.SendPacket(PetPacketWriter.SatisfactionUpdate(
            PetSubOpcode.ModeFunction, CodeSatisfactionUpdate,
            (ushort)session.Pet.Satisfaction, session.Pet.Nid));
    }

    public async Task SendDeathAsync(UserSession session)
    {
        await session.Client.SendPacket(PetPacketWriter.Death(
            PetSubOpcode.ModeFunction, NormalMode, CodeDeath, session.Pet?.Nid ?? 0));
    }
}
