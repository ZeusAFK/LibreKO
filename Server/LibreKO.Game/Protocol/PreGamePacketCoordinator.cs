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
    private const int NameChangeRequestMinBytes = 2 + 2;

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
            (byte)AllCharacterInfoOpcode.NameChange => await HandleRequiredNameChangeAsync(packet, client),
            (byte)AllCharacterInfoOpcode.ArrangeOpen => null,
            (byte)AllCharacterInfoOpcode.ArrangeReceive => null,
            _ => null,
        };
    }


    private async Task<Packet> HandleRequiredNameChangeAsync(Packet packet, IClient client)
    {
        if (packet.RemainingBytes < NameChangeRequestMinBytes)
            return PreGamePacketWriter.NameChangeRefused();
        var slot = packet.ReadShort();
        var currentName = packet.ReadString();
        var newName = packet.RemainingBytes >= 2 ? packet.ReadString() : string.Empty;
        return await preGameService.ChangeRequiredNameAsync(client.AccountId, slot, currentName, newName);
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

        // An earlier session of this account is saved and gone before this one loads, whichever character it played.
        var earlierSessions = sessionManager.GetAll()
            .Where(other => other.AccountId == client.AccountId && other.Client.Id != client.Id && !other.IsBot)
            .ToList();
        foreach (var earlier in earlierSessions)
        {
            logger.LogInformation(
                "Account {AccountId} selecting {Character}; taking over from previous session {SessionId} ({Earlier})",
                client.AccountId,
                characterName,
                earlier.Client.Id,
                earlier.Name);

            await sessionTerminationService.EvictForTakeoverAsync(earlier);
            earlier.Client.Disconnect();
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
