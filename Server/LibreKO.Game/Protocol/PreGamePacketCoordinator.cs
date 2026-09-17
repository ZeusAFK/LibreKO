using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IPreGamePacketCoordinator
{
    Task<Packet?> HandleAsync(IClient client, Packet packet, GameOpcodes opcode);
}

public class PreGamePacketCoordinator(
    IPreGameService preGameService,
    SessionManager sessionManager,
    IAccountLockService accountLockService,
    ISessionTerminationService sessionTerminationService,
    ILogger<PreGamePacketCoordinator> logger) : IPreGamePacketCoordinator
{
    public async Task<Packet?> HandleAsync(IClient client, Packet packet, GameOpcodes opcode)
    {
        return opcode switch
        {
            GameOpcodes.GS_NATION_SELECT => await preGameService.SelectNationAsync(client.AccountId, (AccountNation)packet.ReadByte()),
            GameOpcodes.GS_ALLCHAR_INFO_REQ => await HandleAllCharacterInfoAsync(packet, client),
            GameOpcodes.GS_LOADING_LOGIN => await HandleLoadingLoginAsync(packet),
            GameOpcodes.GS_CREATE_CHARACTER => await HandleCreateCharacterAsync(packet, client),
            GameOpcodes.GS_DELETE_CHARACTER => await HandleDeleteCharacterAsync(packet, client),
            GameOpcodes.GS_SELECT_CHARACTER => await HandleSelectCharacterAsync(packet, client),
            GameOpcodes.GS_CHANGE_HAIR => await HandleChangeHairAsync(packet, client),
            GameOpcodes.GS_KICKOUT => null,
            GameOpcodes.GS_SPEEDHACK_CHECK => null,
            GameOpcodes.GS_CAPTCHA => HandleCaptcha(),
            GameOpcodes.GS_HACKTOOL => null,
            GameOpcodes.GS_REPORT_BUG => null,
            _ => null,
        };
    }

    private async Task<Packet?> HandleAllCharacterInfoAsync(Packet packet, IClient client)
    {
        var subOpcode = packet.RemainingBytes > 0 ? packet.ReadByte() : (byte)0;
        return subOpcode switch
        {
            (byte)AllCharacterInfoOpcode.CharacterList => await preGameService.GetAllCharacterInfoAsync(client.AccountId),
            (byte)AllCharacterInfoOpcode.NameChange => await HandleAllCharacterNameChangeAsync(packet, client),
            (byte)AllCharacterInfoOpcode.ArrangeOpen => null,
            (byte)AllCharacterInfoOpcode.ArrangeReceive => null,
            _ => null,
        };
    }

    private async Task<Packet> HandleAllCharacterNameChangeAsync(Packet packet, IClient client)
    {
        var charRanking = packet.ReadUShort();
        var oldCharacterName = packet.ReadString();
        var newCharacterName = packet.ReadString();

        return await preGameService.ChangeSelectingCharacterNameAsync(client.AccountId, charRanking, oldCharacterName, newCharacterName);
    }

    private async Task<Packet> HandleCreateCharacterAsync(Packet packet, IClient client)
    {
        var slot = packet.ReadByte();
        var name = packet.ReadString();
        var race = packet.ReadByte();
        var @class = packet.ReadShort();
        var face = packet.ReadByte();
        var hair = packet.ReadInt();
        var strength = packet.ReadByte();
        var stamina = packet.ReadByte();
        var dexterity = packet.ReadByte();
        var intelligence = packet.ReadByte();
        var magic = packet.ReadByte();

        return await preGameService.CreateCharacterAsync(client.AccountId, slot, name, race, @class, face, hair, strength, stamina, dexterity, intelligence, magic);
    }

    private async Task<Packet> HandleLoadingLoginAsync(Packet packet)
    {
        var subOpcode = packet.RemainingBytes > 0 ? packet.ReadByte() : (byte)0;
        return await preGameService.LoadingLoginAsync(subOpcode);
    }

    private async Task<Packet> HandleDeleteCharacterAsync(Packet packet, IClient client)
    {
        var slot = packet.ReadByte();
        var name = packet.ReadString();
        var socNo = packet.ReadString();

        return await preGameService.DeleteCharacterAsync(client.AccountId, slot, name, socNo);
    }

    private async Task<Packet?> HandleSelectCharacterAsync(Packet packet, IClient client)
    {
        var accountName = packet.ReadString();
        var characterName = packet.ReadString();
        var init = packet.ReadByte();

        if (!accountLockService.Owns(client))
        {
            logger.LogWarning(
                "Refusing character select for client {ClientId}: its account claim was taken over",
                client.Id);

            return PreGamePacketWriter.SelectCharacterFailure(
                (byte)SelectCharacterResult.Failed);
        }

        var result = await preGameService.SelectCharacterAsync(client.AccountId, accountName, characterName, init);
        if (result.CharacterId == 0)
            return result.Packet;

        var existingSession = sessionManager.GetByCharacterId(result.CharacterId);
        if (existingSession != null && existingSession.Client.Id != client.Id)
        {
            // Character is already online on another connection — almost always a fast
            // reconnect whose previous session's async cleanup (a full save) hasn't run
            // yet. Evict the stale session synchronously and let this client take over,
            // instead of rejecting: under load the old session lingers long enough that
            // repeated rejects turn into an endless login loop.
            logger.LogInformation(
                "Character {Character} already online; taking over from previous session {SessionId}",
                characterName,
                existingSession.Client.Id);

            await sessionTerminationService.EvictForTakeoverAsync(existingSession);
            existingSession.Client.Disconnect();
        }

        client.CharacterId = result.CharacterId;
        return result.Packet;
    }

    private static Packet HandleCaptcha() => CaptchaPacketWriter.Skip();

    private async Task<Packet> HandleChangeHairAsync(Packet packet, IClient client)
    {
        var subOpcode = packet.ReadByte();
        var characterName = packet.ReadSByteString();
        var face = packet.ReadByte();
        var hair = packet.ReadInt();

        return await preGameService.ChangeHairAsync(client.AccountId, subOpcode, characterName, face, hair);
    }
}
