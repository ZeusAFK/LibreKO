using System.Text;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
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
    IUserSessionCharacterMapper characterMapper,
    IUserNotificationService userNotificationService,
    ILogger<NationTransferService> logger) : INationTransferService
{
    public const int NationTransferItem = 810096000;
    private const byte Moradon = (byte)ZoneId.Moradon;
    private const float MoradonTownX = 816f;
    private const float MoradonTownZ = 532f;
    private const int RequestHeadLength = sizeof(short) * 2;
    private const int RequestTailLength = 6;
    private const int BagEnd = InventoryConstants.InventoryStart + InventoryConstants.HaveMax;

    private sealed record Request(short Slot, string Name, byte Race, byte Face, int Hair);

    private sealed record HeldItem(int Index, int ItemId, short Durability, ushort Count, byte Flag, long ExpiresAt, int UniqueId)
    {
        public bool IsSameItem(ItemSlot slot) =>
            slot.ItemId == ItemId && slot.UniqueId == UniqueId && slot.Flag == Flag && slot.ExpiresAt == ExpiresAt;

        public void CopyTo(ItemSlot slot)
        {
            slot.ItemId = ItemId;
            slot.Durability = Durability;
            slot.Count = Count;
            slot.Flag = Flag;
            slot.ExpiresAt = ExpiresAt;
            slot.UniqueId = UniqueId;
        }
    }

    private sealed record SpentItem(HeldItem Held, bool Removed, short Durability, ushort Count);

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
        if (count == 0)
            return null;
        var requests = new List<Request>(count);
        for (var index = 0; index < count; index++)
        {
            if (packet.RemainingBytes < RequestHeadLength)
                return null;
            var slot = packet.ReadShort();
            var nameLength = packet.ReadShort();
            if (nameLength <= 0 || packet.RemainingBytes < nameLength + RequestTailLength)
                return null;
            var name = Encoding.ASCII.GetString(packet.ReadBytes(nameLength));
            requests.Add(new Request(slot, name, packet.ReadByte(), packet.ReadByte(), packet.ReadInt()));
        }
        return packet.RemainingBytes == 0 ? requests : null;
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
        if (refusal == NationTransferPacketWriter.InClan)
            refusal = NationTransferPacketWriter.Failed;
        if (refusal == NationTransferPacketWriter.Accepted && !Matches(characters, requests))
            refusal = NationTransferPacketWriter.WrongCharacter;
        if (refusal != NationTransferPacketWriter.Accepted)
        {
            await session.Client.SendPacket(NationTransferPacketWriter.Result(NationTransferPacketWriter.Submit, refusal));
            return;
        }

        var oldNation = session.Nation;
        var newNation = NationTransferRules.OtherNation(oldNation);
        var picks = requests!;
        var own = picks.Single(request => request.Slot == characters.Single(character => character.Id == session.CharacterId).Slot);
        var result = NationTransferPacketWriter.Failed;

        await session.CharacterPersistenceGate.WaitAsync();
        try
        {
            if (session.NationTransferCommitted || sessionManager.GetByClientId(session.Client.Id) != session)
                return;
            var held = HeldCertificates(session);
            List<SpentItem>? spent = null;
            try
            {
                var consumed = await magicItemUsageService.TryConsumeItemAsync(session, NationTransferItem);
                spent = SpentCertificates(session, held);
                if (!consumed)
                    result = NationTransferPacketWriter.NoItem;
                else if (await PersistAsync(session, newNation, characters, picks))
                {
                    session.WithLock(active =>
                    {
                        active.Nation = newNation;
                        active.Class = NationTransferRules.NewClass(active.Class);
                        active.Race = own.Race;
                        active.Face = own.Face;
                        active.Hair = own.Hair;
                        active.Quest.BindPoint = -1;
                        active.NationTransferCommitted = true;
                    });
                    result = NationTransferPacketWriter.Accepted;
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not persist the nation transfer of account {Account}", session.AccountId);
            }
            if (result != NationTransferPacketWriter.Accepted)
                await RestoreCertificatesAsync(session, spent ?? SpentCertificates(session, held));
        }
        finally
        {
            session.CharacterPersistenceGate.Release();
        }

        if (result != NationTransferPacketWriter.Accepted)
        {
            await session.Client.SendPacket(NationTransferPacketWriter.Result(NationTransferPacketWriter.Submit, result));
            return;
        }
        await sessionTermination.LogoutAsync(session.Client);
        await session.Client.SendPacket(NationTransferPacketWriter.Result(NationTransferPacketWriter.Submit, result));

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
        if (session.Hp <= 0 || session.Trade.IsTrading || session.Trade.IsMerchanting
            || session.Trade.IsMerchantPreparing || session.IsGathering || session.NationTransferCommitted)
            return (NationTransferPacketWriter.Failed, []);
        if (!magicItemUsageService.CanUseItem(session, NationTransferItem))
            return (NationTransferPacketWriter.NoItem, []);

        using var scope = scopeFactory.CreateScope();
        var characters = (await scope.ServiceProvider.GetRequiredService<ICharacterRepository>()
            .GetCharactersByAccount(session.AccountId)).ToList();
        if (characters.Count is 0 or > byte.MaxValue
            || characters.All(character => character.Id != session.CharacterId))
            return (NationTransferPacketWriter.NoCharacter, characters);
        if (session.KnightsId > 0 || characters.Any(character => character.KnightsId > 0))
            return (NationTransferPacketWriter.InClan, characters);
        var king = kingSystemRuntimeService.GetKingData(session.Nation)?.KingName?.Trim();
        if (!string.IsNullOrEmpty(king)
            && characters.Any(character => string.Equals(character.Name, king, StringComparison.OrdinalIgnoreCase)))
            return (NationTransferPacketWriter.IsKing, characters);
        return (NationTransferPacketWriter.Accepted, characters);
    }

    private async Task<bool> PersistAsync(UserSession session, AccountNation newNation,
        IReadOnlyList<Character> checkedCharacters, List<Request> requests)
    {
        using var scope = scopeFactory.CreateScope();
        var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var accountRepository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var account = await accountRepository.GetById(session.AccountId);
        var characters = (await characterRepository.GetCharactersByAccount(session.AccountId)).ToList();
        var active = characters.SingleOrDefault(character => character.Id == session.CharacterId);
        if (account == null || active == null || account.Nation != session.Nation
            || !Unchanged(checkedCharacters, characters) || !Matches(characters, requests))
            return false;

        characterMapper.ApplyToCharacter(session, active);
        var bySlot = requests.ToDictionary(request => request.Slot);
        var start = gameDataService.GetStartPosition(Moradon);
        foreach (var character in characters)
        {
            var request = bySlot[character.Slot];
            var (x, z) = start?.RandomSpawn(newNation) ?? (MoradonTownX, MoradonTownZ);
            character.Class = NationTransferRules.NewClass(character.Class);
            character.Race = request.Race;
            character.Face = request.Face;
            character.Hair = request.Hair;
            character.MapId = Moradon;
            character.X = x;
            character.Z = z;
            character.Bind = -1;
        }
        account.Nation = newNation;
        await accountRepository.UpdateWithCharactersAsync(account, characters);
        return true;
    }

    private static bool Unchanged(IReadOnlyList<Character> checkedCharacters, IReadOnlyList<Character> characters) =>
        characters.Count == checkedCharacters.Count
        && characters.All(character => character.KnightsId == 0
                                       && checkedCharacters.Any(listed => listed.Id == character.Id
                                                                          && listed.Slot == character.Slot
                                                                          && listed.Class == character.Class
                                                                          && listed.Name == character.Name));

    private static List<HeldItem> HeldCertificates(UserSession session)
    {
        var held = new List<HeldItem>();
        for (var index = InventoryConstants.InventoryStart; index < session.Inventory.Length; index++)
        {
            var slot = session.Inventory[index];
            if (slot.ItemId == NationTransferItem)
                held.Add(new HeldItem(index, slot.ItemId, slot.Durability, slot.Count, slot.Flag, slot.ExpiresAt, slot.UniqueId));
        }
        return held;
    }

    private static List<SpentItem> SpentCertificates(UserSession session, List<HeldItem> held)
    {
        var spent = new List<SpentItem>();
        foreach (var item in held)
        {
            var slot = session.Inventory[item.Index];
            if (!item.IsSameItem(slot))
                spent.Add(new SpentItem(item, true, item.Durability, item.Count));
            else if (slot.Durability != item.Durability || slot.Count != item.Count)
                spent.Add(new SpentItem(item, false, (short)(item.Durability - slot.Durability), (ushort)(item.Count - slot.Count)));
        }
        return spent;
    }

    private async Task RestoreCertificatesAsync(UserSession session, List<SpentItem> spent)
    {
        if (spent.Count == 0)
            return;
        foreach (var item in spent)
        {
            if (RestoreSlot(session, item) is not { } index)
            {
                SkipRestore(session, item);
                continue;
            }
            var slot = session.Inventory[index];
            if (item.Removed)
                item.Held.CopyTo(slot);
            else
            {
                slot.Durability += item.Durability;
                slot.Count += item.Count;
            }
            await NotifySlotAsync(session, index);
        }
        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);
        await userNotificationService.SendWeightChangeAsync(session);
    }

    private static int? RestoreSlot(UserSession session, SpentItem item)
    {
        var slot = session.Inventory[item.Held.Index];
        if (!item.Removed)
            return item.Held.IsSameItem(slot) ? item.Held.Index : null;
        if (slot.IsEmpty)
            return item.Held.Index;
        for (var index = InventoryConstants.InventoryStart; index < BagEnd; index++)
        {
            if (session.Inventory[index].IsEmpty)
                return index;
        }
        return null;
    }

    private Task NotifySlotAsync(UserSession session, int index)
    {
        var slot = session.Inventory[index];
        return userNotificationService.SendStackChangeAsync(session, (byte)index, slot.ItemId, slot.Count, slot.Durability);
    }

    private void SkipRestore(UserSession session, SpentItem item) =>
        logger.LogWarning("Could not return item {Item} with {Durability} durability and {Count} count from slot {Slot} of account {Account} after a failed nation transfer",
            item.Held.ItemId, item.Durability, item.Count, item.Held.Index, session.AccountId);
}
