using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface INationTransferService
{
    Task OpenAsync(UserSession session);
    Task HandleAsync(IClient client, Packet packet);
}

public class NationTransferService(
    IServiceScopeFactory scopeFactory,
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IMagicItemUsageService magicItemUsageService,
    IKingSystemRuntimeService kingSystemRuntimeService,
    ISessionTerminationService sessionTermination,
    ILogger<NationTransferService> logger) : INationTransferService
{
    public const int NationTransferItem = 810096000;
    private const byte Moradon = (byte)ZoneId.Moradon;
    private const float MoradonTownX = 816f;
    private const float MoradonTownZ = 532f;
    private const int RequestTailLength = 6;

    private sealed record Request(short Slot, string Name, byte Race, byte Face, int Hair);

    public async Task OpenAsync(UserSession session)
    {
        if (await RefusedDuringWarAsync(session))
            return;

        var (refusal, characters) = await CheckAsync(session);
        if (refusal != NationTransferPacketWriter.Accepted)
        {
            await session.Client.SendPacket(NationTransferPacketWriter.Result(NationTransferPacketWriter.OpenBox, refusal));
            return;
        }

        var newNation = NationTransferRules.OtherNation(session.Nation);
        var candidates = characters
            .OrderBy(character => character.Slot)
            .Select(character => new NationTransferCandidate(
                character.Slot,
                character.Name,
                NationTransferRules.ProposedRace(character.Race, character.Class),
                newNation,
                NationTransferRules.NewClass(character.Class),
                character.Face,
                character.Hair))
            .ToList();
        await session.Client.SendPacket(NationTransferPacketWriter.Candidates(candidates));
    }

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        switch (packet.ReadByte())
        {
            case NationTransferPacketWriter.OpenBox:
                await OpenAsync(session);
                break;
            case NationTransferPacketWriter.Submit when packet.RemainingBytes >= 2
                                                       && packet.ReadByte() == NationTransferPacketWriter.Accepted:
                await SubmitAsync(session, ReadRequests(packet));
                break;
        }
    }

    private static List<Request>? ReadRequests(Packet packet)
    {
        var count = packet.ReadByte();
        var requests = new List<Request>(count);
        for (var index = 0; index < count; index++)
        {
            if (packet.RemainingBytes < 2)
                return null;
            var slot = packet.ReadShort();
            var name = packet.ReadString();
            if (packet.RemainingBytes < RequestTailLength)
                return null;
            requests.Add(new Request(slot, name, packet.ReadByte(), packet.ReadByte(), packet.ReadInt()));
        }
        return requests;
    }

    private async Task<bool> RefusedDuringWarAsync(UserSession session)
    {
        var battle = sessionManager.Battle;
        if (!battle.IsBattleActive)
            return false;
        await session.Client.SendPacket(NationTransferPacketWriter.DuringWar(battle.KilledElmoNpc, battle.KilledKarusNpc));
        return true;
    }

    private async Task SubmitAsync(UserSession session, List<Request>? requests)
    {
        if (await RefusedDuringWarAsync(session))
            return;

        var (refusal, characters) = await CheckAsync(session);
        if (refusal == NationTransferPacketWriter.Accepted && !Matches(characters, requests))
            refusal = NationTransferPacketWriter.WrongCharacter;
        if (refusal != NationTransferPacketWriter.Accepted)
        {
            await session.Client.SendPacket(NationTransferPacketWriter.Result(NationTransferPacketWriter.Submit, refusal));
            return;
        }

        var oldNation = session.Nation;
        var newNation = NationTransferRules.OtherNation(oldNation);
        var bySlot = requests!.ToDictionary(request => request.Slot);
        var playing = characters.FirstOrDefault(character => character.Id == session.CharacterId);

        await magicItemUsageService.TryConsumeItemAsync(session, NationTransferItem);
        session.Nation = newNation;
        session.Class = NationTransferRules.NewClass(session.Class);
        if (playing != null && bySlot.TryGetValue(playing.Slot, out var own))
        {
            session.Race = own.Race;
            session.Face = own.Face;
            session.Hair = own.Hair;
        }
        session.Quest.BindPoint = -1;

        await session.Client.SendPacket(NationTransferPacketWriter.Result(NationTransferPacketWriter.Submit, NationTransferPacketWriter.Accepted));
        await sessionTermination.LogoutAsync(session.Client);
        await PersistAsync(session.AccountId, newNation, characters, bySlot);

        logger.LogInformation("Nation transfer for account {Account}: {Count} characters {Old}→{New}",
            session.AccountId, characters.Count, oldNation, newNation);
    }

    private static bool Matches(IReadOnlyList<Character> characters, List<Request>? requests) =>
        requests != null
        && requests.Count == characters.Count
        && requests.Select(request => request.Slot).Distinct().Count() == requests.Count
        && requests.All(request => characters.FirstOrDefault(character => character.Slot == request.Slot) is { } character
                                   && string.Equals(character.Name, request.Name, StringComparison.Ordinal)
                                   && NationTransferRules.Allows(character.Class, request.Race));

    private async Task<(byte Refusal, IReadOnlyList<Character> Characters)> CheckAsync(UserSession session)
    {
        if (!magicItemUsageService.CanUseItem(session, NationTransferItem))
            return (NationTransferPacketWriter.NoItem, []);

        using var scope = scopeFactory.CreateScope();
        var characters = (await scope.ServiceProvider.GetRequiredService<ICharacterRepository>()
            .GetCharactersByAccount(session.AccountId)).ToList();
        if (characters.Count == 0)
            return (NationTransferPacketWriter.NoCharacter, characters);
        if (session.KnightsId > 0 || characters.Any(character => character.KnightsId > 0))
            return (NationTransferPacketWriter.InClan, characters);
        var king = kingSystemRuntimeService.GetKingData(session.Nation)?.KingName?.Trim();
        if (!string.IsNullOrEmpty(king)
            && characters.Any(character => string.Equals(character.Name, king, StringComparison.OrdinalIgnoreCase)))
            return (NationTransferPacketWriter.IsKing, characters);
        return (NationTransferPacketWriter.Accepted, characters);
    }

    private async Task PersistAsync(int accountId, AccountNation newNation, IReadOnlyList<Character> characters,
        IReadOnlyDictionary<short, Request> bySlot)
    {
        using var scope = scopeFactory.CreateScope();
        var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var accountRepository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var start = gameDataService.GetStartPosition(Moradon);

        foreach (var listed in characters)
        {
            var character = await characterRepository.GetById(listed.Id);
            if (character == null || !bySlot.TryGetValue(character.Slot, out var request))
                continue;
            var (x, z) = start?.RandomSpawn(newNation) ?? (MoradonTownX, MoradonTownZ);
            character.Class = NationTransferRules.NewClass(listed.Class);
            character.Race = request.Race;
            character.Face = request.Face;
            character.Hair = request.Hair;
            character.MapId = Moradon;
            character.X = x;
            character.Z = z;
            character.Bind = -1;
            await characterRepository.UpdateAsync(character);
        }

        var account = await accountRepository.GetById(accountId);
        if (account != null)
        {
            account.Nation = newNation;
            await accountRepository.UpdateAsync(account);
        }
    }
}
