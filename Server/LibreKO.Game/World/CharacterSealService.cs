using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibreKO.Game.World;

public interface ICharacterSealService
{
    Task HandleAsync(UserSession session, Packet packet);
    Task<IReadOnlyDictionary<int, CharacterSealPacketWriter.RingSummary>> DescribeAsync(IEnumerable<ItemSlot> slots);
}

public sealed class CharacterSealService(
    IServiceScopeFactory scopes,
    IGameDataService gameData,
    IUserNotificationService notifications,
    ICharacterStatePersister statePersister,
    SessionManager sessionManager,
    SealGates sealGates) : ICharacterSealService
{
    public const int CypherRing = InventoryConstants.CypherRing;
    public const byte ScrollKind = 250;
    public const byte RingKind = 160;
    public const byte NoBagPos = 255;
    public const short BoundItemRefusal = -4;
    public const short RentalItemRefusal = -5;
    public const byte GenericFailure = 1;
    private const int ExpScale = 10_000;
    private const int SkillTrees = 4;
    private const int SealRequestFixed = sizeof(int) + 1 + sizeof(int);
    private const int UnsealRequestBytes = sizeof(int) + 1 + sizeof(int) + 1;

    public static bool IsRing(int itemId) => itemId == CypherRing;

    public async Task HandleAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;
        switch (packet.ReadByte())
        {
            case CharacterSealPacketWriter.ListSub:
                await session.Client.SendPacket(await ListAsync(session));
                break;
            case CharacterSealPacketWriter.SealCharacterSub when packet.RemainingBytes >= SealRequestFixed:
            {
                packet.ReadInt();
                var bagPos = packet.ReadByte();
                var scrollId = packet.ReadInt();
                var name = packet.RemainingBytes >= sizeof(short) ? packet.ReadString() : string.Empty;
                var code = packet.RemainingBytes >= sizeof(short) ? packet.ReadString() : string.Empty;
                await session.Client.SendPacket(await SealAsync(session, bagPos, scrollId, name, code));
                break;
            }
            case CharacterSealPacketWriter.UnsealSub when packet.RemainingBytes >= UnsealRequestBytes:
            {
                packet.ReadInt();
                var bagPos = packet.ReadByte();
                var ringId = packet.ReadInt();
                var slot = packet.ReadByte();
                await session.Client.SendPacket(await UnsealAsync(session, bagPos, ringId, slot));
                break;
            }
            case CharacterSealPacketWriter.SheetSub when packet.RemainingBytes >= sizeof(int):
            {
                var uniqueId = packet.ReadInt();
                await session.Client.SendPacket(SeesRing(session, uniqueId)
                    ? await SheetAsync(uniqueId)
                    : CharacterSealPacketWriter.SheetRefused());
                break;
            }
            case CharacterSealPacketWriter.AchievementsSub:
                await session.Client.SendPacket(CharacterSealPacketWriter.NoAchievements());
                break;
        }
    }

    public async Task<IReadOnlyDictionary<int, CharacterSealPacketWriter.RingSummary>> DescribeAsync(IEnumerable<ItemSlot> slots)
    {
        var ids = slots.Where(s => IsRing(s.ItemId) && s.UniqueId != 0).Select(s => s.UniqueId).Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<int, CharacterSealPacketWriter.RingSummary>();
        using var scope = scopes.CreateScope();
        var characters = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetByIds(ids);
        return characters.Where(c => c.Sealed).ToDictionary(c => c.Id, Summary);
    }

    private CharacterSealPacketWriter.RingSummary Summary(Character character)
    {
        var max = gameData.GetMaxExpForLevel(character.Level);
        var exp = max > 0 ? (ushort)Math.Clamp(character.Experience * ExpScale / max, 0, ExpScale) : (ushort)0;
        return new CharacterSealPacketWriter.RingSummary(character.Name, (byte)character.Class, character.Level, exp, character.Race,
            (byte)Math.Clamp((int)character.RebirthLevel, 0, byte.MaxValue));
    }

    private async Task<Packet> ListAsync(UserSession session)
    {
        using var scope = scopes.CreateScope();
        var characters = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetCharactersByAccount(session.AccountId);
        var slots = new PreGamePacketWriter.CharacterSlot?[GameConstants.MaxAccountCharacters];
        foreach (var character in characters)
            if (character.Slot < slots.Length)
                slots[character.Slot] = CharacterPacketMapper.SlotOf(character);
        return CharacterSealPacketWriter.List(slots);
    }

    // The ring and the sealed character are saved together, from the scroll the bag holds once everything is read.
    private async Task<Packet> SealAsync(UserSession session, byte bagPos, int scrollId, string name, string code)
    {
        if (session.IsBusy || BagSlot(session, bagPos) == null || gameData.GetItem(scrollId)?.Kind != ScrollKind
            || string.Equals(name, session.Name, StringComparison.OrdinalIgnoreCase))
            return CharacterSealPacketWriter.SealFailed(GenericFailure);
        if (ItemUpgradeService.VaultPasswordRefusal(session, code) is { } refusal)
            return CharacterSealPacketWriter.SealFailed((byte)refusal);

        var targetId = await CharacterIdAsync(name);
        if (targetId == null)
            return CharacterSealPacketWriter.SealFailed(GenericFailure);

        using var turn = await sealGates.EnterAsync(targetId.Value);
        var position = InventoryConstants.InventoryStart + bagPos;
        var journal = new InventoryJournal(session);
        var answer = CharacterSealPacketWriter.SealFailed(GenericFailure);
        Character? sealedCharacter = null;
        var outcome = await statePersister.CommitAsync(session, async db =>
        {
            var target = await db.Characters.SingleOrDefaultAsync(character => character.Name == name);
            if (target == null || target.Id != targetId || target.AccountId != session.AccountId || target.Sealed || target.IsOnline
                || (target.KnightsId > 0 && target.Fame == ClanRules.FameChief) || await HoldsElsewhereAsync(db, target))
                return false;
            if (CarriedRefusal(target) is { } carried)
            {
                answer = CharacterSealPacketWriter.SealRefused(carried);
                return false;
            }

            if (!journal.Apply(_ => MakeRing(journal.Slot(position), scrollId, target.Id)))
                return false;

            target.Sealed = true;
            sealedCharacter = target;
            return true;
        }, () => session.WithLock(_ => journal.Restore()));

        if (outcome != StateCommit.Saved || sealedCharacter == null)
            return answer;

        var ring = session.Inventory[position];
        await notifications.SendStackChangeAsync(session, (byte)position, ring.ItemId, ring.Count, ring.Durability);
        return CharacterSealPacketWriter.Sealed(bagPos, CypherRing, sealedCharacter.Id, Summary(sealedCharacter));
    }

    private async Task<int?> CharacterIdAsync(string name)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters
            .Where(character => character.Name == name)
            .Select(character => (int?)character.Id)
            .FirstOrDefaultAsync();
    }

    private bool MakeRing(ItemSlot scroll, int scrollId, int characterId)
    {
        if (scroll.ItemId != scrollId || scroll.Count < 1 || scroll.Expires || scroll.State == ItemFlag.Rented)
            return false;

        scroll.ItemId = CypherRing;
        scroll.Count = 1;
        scroll.Durability = gameData.GetItem(CypherRing)?.Duration ?? 0;
        scroll.UniqueId = characterId;
        return true;
    }

    // Unclaimed mail and letters, open auction bids and a slave merchant would go into the ring with the character.
    private static async Task<bool> HoldsElsewhereAsync(AppDbContext db, Character character) =>
        await db.Mails.AnyAsync(mail => mail.RecipientCharacterId == character.Id && !mail.Deleted && mail.ClaimedAt == null && mail.Attachments.Any())
        || await db.MailBoxes.AnyAsync(letter => letter.RecipientId == character.Name && !letter.Deleted
            && letter.Status == ShoppingMallLetterProtocol.LetterStatusNew && letter.Type == ShoppingMallPacketWriter.LetterTypeWithItem)
        || await db.AuctionBids.AnyAsync(bid => bid.CharacterId == character.Id && bid.ClosedAt == null)
        || await db.SlaveMerchants.AnyAsync(slave => slave.OwnerCharacterId == character.Id);

    // A character goes into a ring only with what could change hands without one.
    private short? CarriedRefusal(Character character)
    {
        var items = new ItemSlot[InventoryConstants.InventoryTotal];
        for (int i = 0; i < items.Length; i++) items[i] = new ItemSlot();
        UserSessionBinaryState.LoadItems(items, character.Items);
        var carried = items.Where(item => !item.IsEmpty).ToList();
        if (carried.Any(item => item.Expires || item.State == ItemFlag.Rented))
            return RentalItemRefusal;
        if (carried.Any(item => gameData.GetItem(item.ItemId) is not { } data || !ExchangeTransferService.MayChangeHands(data, item)))
            return BoundItemRefusal;
        return null;
    }

    // The ring leaves the bag in the same save that hands the character over.
    private async Task<Packet> UnsealAsync(UserSession session, byte bagPos, int ringId, byte slot)
    {
        var ring = BagSlot(session, bagPos);
        if (session.IsBusy || ring == null || !IsRing(ringId) || ring.UniqueId == 0 || slot >= GameConstants.MaxAccountCharacters)
            return CharacterSealPacketWriter.UnsealFailed();

        var position = InventoryConstants.InventoryStart + bagPos;
        var uniqueId = ring.UniqueId;
        var journal = new InventoryJournal(session);
        var outcome = await statePersister.CommitAsync(session, async db =>
        {
            var sealedCharacter = await db.Characters.FindAsync(uniqueId);
            var slotTaken = await db.Characters.AnyAsync(character => character.AccountId == session.AccountId && character.Slot == slot);
            if (sealedCharacter == null || !sealedCharacter.Sealed || slotTaken
                || !CharacterRaceNations.BelongsTo(sealedCharacter.Race, session.Nation))
                return false;

            if (!journal.Apply(_ => TakeRing(journal.Slot(position), ringId, uniqueId)))
                return false;

            sealedCharacter.Sealed = false;
            sealedCharacter.AccountId = session.AccountId;
            sealedCharacter.Slot = slot;
            return true;
        }, () => session.WithLock(_ => journal.Restore()));

        if (outcome != StateCommit.Saved)
            return CharacterSealPacketWriter.UnsealFailed();

        await notifications.SendStackChangeAsync(session, (byte)position, 0, 0, 0);
        return CharacterSealPacketWriter.Unsealed(bagPos, uniqueId);
    }

    private static bool TakeRing(ItemSlot ring, int ringId, int uniqueId)
    {
        if (ring.ItemId != ringId || ring.UniqueId != uniqueId || ring.Count < 1)
            return false;

        ring.Clear();
        return true;
    }

    // A sheet is shown only for a ring in the player's bag or in their open trade.
    private bool SeesRing(UserSession session, int uniqueId)
    {
        bool Offered(UserSession trader) =>
            trader.WithLock(t => t.Trade.ExchangeItemList.Any(item => item.ItemId == CypherRing && item.UniqueId == uniqueId));

        return uniqueId != 0
            && (session.WithLock(s => s.Inventory.Any(slot => slot.IsSealedCharacter && slot.UniqueId == uniqueId))
                || Offered(session)
                || sessionManager.GetByCharacterId(session.Trade.ExchangeUser) is { } partner && TradeWith(session, partner) && Offered(partner));
    }

    private static bool TradeWith(UserSession session, UserSession partner)
    {
        var open = false;
        UserSession.WithBoth(session, partner, (s, p) => open = s.Trade.ExchangeAccepted && p.Trade.ExchangeAccepted
            && s.Trade.ExchangeUser == p.CharacterId && p.Trade.ExchangeUser == s.CharacterId);
        return open;
    }

    private async Task<Packet> SheetAsync(int uniqueId)
    {
        using var scope = scopes.CreateScope();
        var character = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetById(uniqueId);
        if (character == null || !character.Sealed)
            return CharacterSealPacketWriter.SheetRefused();

        var items = new ItemSlot[InventoryConstants.InventoryTotal];
        for (int i = 0; i < items.Length; i++) items[i] = new ItemSlot();
        UserSessionBinaryState.LoadItems(items, character.Items);
        var trees = new byte[SkillTrees];
        for (int i = 0; i < SkillTrees && i + 1 < character.SkillPointData.Length; i++)
            trees[i] = character.SkillPointData[i + 1];
        var free = character.SkillPointData.Length > 0 ? character.SkillPointData[0] : (byte)0;
        return CharacterSealPacketWriter.CharacterSheet(new CharacterSealPacketWriter.Sheet(
            character.Name, NationOfRace(character.Race), character.Race, character.Class, character.Level, character.Loyalty,
            character.Strength, character.Stamina, character.Dexterity, character.Intelligence, character.Magic, character.Money,
            free, trees, items.Take(InventoryConstants.InventoryStart + InventoryConstants.HaveMax).ToList()));
    }

    private static byte NationOfRace(byte race) =>
        CharacterRaceNations.BelongsTo(race, AccountNation.Karus) ? (byte)AccountNation.Karus : (byte)AccountNation.ElMorad;

    private static ItemSlot? BagSlot(UserSession session, byte bagPos) =>
        bagPos < InventoryConstants.HaveMax ? session.Inventory[InventoryConstants.InventoryStart + bagPos] : null;
}
