using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
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
    IUserNotificationService notifications) : ICharacterSealService
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
                await session.Client.SendPacket(await SheetAsync(packet.ReadInt()));
                break;
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

    private async Task<Packet> SealAsync(UserSession session, byte bagPos, int scrollId, string name, string code)
    {
        var scroll = BagSlot(session, bagPos);
        if (scroll == null || scroll.ItemId != scrollId || gameData.GetItem(scrollId)?.Kind != ScrollKind
            || scroll.Expires || scroll.State == ItemFlag.Rented
            || string.Equals(name, session.Name, StringComparison.OrdinalIgnoreCase))
            return CharacterSealPacketWriter.SealFailed(GenericFailure);
        if (ItemUpgradeService.VaultPasswordRefusal(session, code) is { } refusal)
            return CharacterSealPacketWriter.SealFailed((byte)refusal);

        using var scope = scopes.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var target = await repository.GetByName(name);
        if (target == null || target.AccountId != session.AccountId || target.Sealed || target.IsOnline
            || (target.KnightsId > 0 && target.Fame == ClanRules.FameChief))
            return CharacterSealPacketWriter.SealFailed(GenericFailure);
        if (CarriedRefusal(target) is { } carried)
            return CharacterSealPacketWriter.SealRefused(carried);

        target.Sealed = true;
        await repository.UpdateAsync(target);
        scroll.ItemId = CypherRing;
        scroll.Count = 1;
        scroll.Durability = gameData.GetItem(CypherRing)?.Duration ?? 0;
        scroll.UniqueId = target.Id;
        await notifications.SendStackChangeAsync(session, (byte)(InventoryConstants.InventoryStart + bagPos), scroll.ItemId, scroll.Count, scroll.Durability);
        return CharacterSealPacketWriter.Sealed(bagPos, CypherRing, target.Id, Summary(target));
    }

    private short? CarriedRefusal(Character character)
    {
        var items = new ItemSlot[InventoryConstants.InventoryTotal];
        for (int i = 0; i < items.Length; i++) items[i] = new ItemSlot();
        UserSessionBinaryState.LoadItems(items, character.Items);
        if (items.Any(i => i.Flag == (byte)ItemFlag.Bound))
            return BoundItemRefusal;
        if (items.Any(i => i.Flag == (byte)ItemFlag.Rented))
            return RentalItemRefusal;
        return null;
    }

    private async Task<Packet> UnsealAsync(UserSession session, byte bagPos, int ringId, byte slot)
    {
        var ring = BagSlot(session, bagPos);
        if (ring == null || ring.ItemId != ringId || !IsRing(ringId) || ring.UniqueId == 0 || slot >= GameConstants.MaxAccountCharacters)
            return CharacterSealPacketWriter.UnsealFailed();

        using var scope = scopes.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var sealedCharacter = await repository.GetById(ring.UniqueId);
        var owned = await repository.GetCharactersByAccount(session.AccountId);
        if (sealedCharacter == null || !sealedCharacter.Sealed || owned.Any(c => c.Slot == slot)
            || !CharacterRaceNations.BelongsTo(sealedCharacter.Race, session.Nation))
            return CharacterSealPacketWriter.UnsealFailed();

        sealedCharacter.Sealed = false;
        sealedCharacter.AccountId = session.AccountId;
        sealedCharacter.Slot = slot;
        await repository.UpdateAsync(sealedCharacter);
        var uniqueId = ring.UniqueId;
        ring.Clear();
        await notifications.SendStackChangeAsync(session, (byte)(InventoryConstants.InventoryStart + bagPos), 0, 0, 0);
        return CharacterSealPacketWriter.Unsealed(bagPos, uniqueId);
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
