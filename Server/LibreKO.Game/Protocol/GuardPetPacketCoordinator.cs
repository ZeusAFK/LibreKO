using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IGuardPetPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class GuardPetPacketCoordinator(
    SessionManager sessionManager,
    ILogger<GuardPetPacketCoordinator> logger) : IGuardPetPacketCoordinator
{
    private const byte GuardPetSubStatus = 1;

    private struct GuardPetState
    {
        public bool Active;
        public int Hp;
        public int MaxHp;
    }

    // charId -> guard-pet state (in-memory; resets on server restart).
    private static readonly Dictionary<int, GuardPetState> guardPets = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case GuardPetSubStatus:
                await SendGuardPetStatusAsync(session);
                break;
        }
    }

    private async Task SendGuardPetStatusAsync(UserSession session)
    {
        var state = GetGuardPetState(session.CharacterId);
        logger.LogDebug("{Name} guard-pet status: active={Active} hp={Hp}/{MaxHp}",
            session.Name, state.Active, state.Hp, state.MaxHp);
        await session.Client.SendPacket(BuildGuardPetStatus(state));
    }

    private static Packet BuildGuardPetStatus(GuardPetState state)
    {
        var result = GuardPetPacketWriter.Status(
            GuardPetSubStatus, state.Active, state.Hp, state.MaxHp);
        return result;
    }

    private static GuardPetState GetGuardPetState(int charId)
    {
        if (!guardPets.TryGetValue(charId, out var state))
        {
            state = new GuardPetState { Active = false, Hp = 0, MaxHp = 0 };
            guardPets[charId] = state;
        }
        return state;
    }

    public static async Task UpdateGuardPetAsync(SessionManager sessionManager, int charId, bool active, int hp, int maxHp)
    {
        if (!active || hp <= 0 || maxHp <= 0)
            guardPets[charId] = new GuardPetState { Active = false, Hp = 0, MaxHp = 0 };
        else
            guardPets[charId] = new GuardPetState { Active = true, Hp = hp > maxHp ? maxHp : hp, MaxHp = maxHp };

        var session = sessionManager.GetByCharacterId(charId);
        if (session != null)
            await session.Client.SendPacket(BuildGuardPetStatus(guardPets[charId]));
    }
}
