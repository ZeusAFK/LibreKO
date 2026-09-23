using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public record GameLoginResult(int AccountId, AccountNation Nation, bool Success);
public record CharacterSelectResult(Packet Packet, int CharacterId);

public interface IPreGameService
{
    Task<GameLoginResult> LoginAsync(string login, string password);
    Task<Packet> SelectNationAsync(int accountId, AccountNation nation);
    Task<Packet> GetAllCharacterInfoAsync(int accountId);
    Task<Packet> ChangeSelectingCharacterNameAsync(int accountId, ushort charRanking, string oldCharacterName, string newCharacterName);
    Task<Packet> LoadingLoginAsync(byte subOpcode);
    Task<Packet> CreateCharacterAsync(int accountId, byte slot, string name, byte race, short @class, byte face, int hair, byte strength, byte stamina, byte dexterity, byte intelligence, byte magic);
    Task<Packet> DeleteCharacterAsync(int accountId, byte slot, string name, string socNo);
    Task<CharacterSelectResult> SelectCharacterAsync(int accountId, string accountName, string characterName, byte init);
    Task<Packet> ChangeHairAsync(int accountId, byte subOpcode, string characterName, byte face, int hair);
    Task<List<Packet>> GameStartAsync(int characterId, int accountId, byte subOpcode, UserSession? session = null);
    Task LogoutAsync(int characterId);
}

public class PreGameService(
    IAccountRepository accountRepository,
    ICharacterRepository characterRepository,
    IKnightsRepository knightsRepository,
    IGameDataService gameData,
    SessionManager sessionManager,
    TimeWeatherBroadcastService timeWeather,
    ILogger<PreGameService> logger) : IPreGameService
{
    private const byte NewCharacterStartZone = (byte)ZoneId.Moradon;
    private const int MaxCharacterNameLength = 20;
    private const int MaxSocialNumberLength = 15;

    private const byte ChangeHairSucceeded = 0;
    private const byte ChangeHairFailed = 1;

    public async Task<GameLoginResult> LoginAsync(string login, string password)
    {
        var account = await accountRepository.GetByLogin(login);
        if (account == null || !PasswordHasher.Verify(password, account.Password))
        {
            logger.LogWarning("Login failed for {Login}: invalid credentials", login);
            return new(0, 0, false);
        }

        if (account.Authority == AccountAuthority.Banned)
        {
            logger.LogWarning("Login rejected for {Login}: account banned", login);
            return new(0, 0, false);
        }

        logger.LogDebug("Login succeeded for {Login} (accountId={AccountId})", login, account.Id);
        return new(account.Id, account.Nation, true);
    }

    public async Task<Packet> SelectNationAsync(int accountId, AccountNation nation)
    {
        var account = await accountRepository.GetById(accountId);

        if (account == null
            || account.Nation != AccountNation.None
            || nation is not (AccountNation.Karus or AccountNation.ElMorad))
        {
            return PreGamePacketWriter.NationSelect(PreGamePacketWriter.NationRejected);
        }

        account.Nation = nation;
        await accountRepository.UpdateAsync(account);
        return PreGamePacketWriter.NationSelect((byte)nation);
    }

    public async Task<Packet> GetAllCharacterInfoAsync(int accountId)
    {
        var characters = (await characterRepository.GetCharactersByAccount(accountId)).ToList();
        return CharacterPacketMapper.BuildAllCharacterInfo(characters);
    }

    public async Task<Packet> ChangeSelectingCharacterNameAsync(int accountId, ushort charRanking, string oldCharacterName, string newCharacterName)
    {
        _ = charRanking;

        if (string.IsNullOrWhiteSpace(oldCharacterName)
            || string.IsNullOrWhiteSpace(newCharacterName)
            || oldCharacterName.Length > MaxCharacterNameLength
            || newCharacterName.Length > MaxCharacterNameLength
            || string.Equals(oldCharacterName, newCharacterName, StringComparison.OrdinalIgnoreCase))
        {
            return PreGamePacketWriter.NameChangeRefused();
        }

        var character = await characterRepository.GetByName(oldCharacterName);
        if (character == null || character.AccountId != accountId || await characterRepository.IsNameTaken(newCharacterName))
        {
            return PreGamePacketWriter.NameChangeRefused();
        }

        character.Name = newCharacterName;
        await characterRepository.UpdateAsync(character);

        return PreGamePacketWriter.NameChanged((ushort)character.Slot, newCharacterName);
    }

    public Task<Packet> LoadingLoginAsync(byte subOpcode)
    {
        // Character select requests the current login queue count via sub-opcode 1.
        // We do not implement a queue yet, so we always report zero waiting players.
        _ = subOpcode;
        return Task.FromResult(
            PreGamePacketWriter.LoginQueue(PreGamePacketWriter.LoginQueueReady, 0));
    }

    public async Task<Packet> CreateCharacterAsync(
        int accountId, byte slot, string name, byte race, short @class, byte face, int hair,
        byte strength, byte stamina, byte dexterity, byte intelligence, byte magic)
    {
        var account = await accountRepository.GetById(accountId);
        if (account == null)
            return CreateCharacterPacket(CreateCharacterResult.ServerError);

        if (account.Nation is not (AccountNation.Karus or AccountNation.ElMorad))
            return CreateCharacterPacket(CreateCharacterResult.InvalidClass);

        if (slot >= GameConstants.MaxAccountCharacters)
            return CreateCharacterPacket(CreateCharacterResult.SlotFull);

        var existingCharacters = (await characterRepository.GetCharactersByAccount(accountId)).ToList();
        if (existingCharacters.Any(character => character.Slot == slot))
            return CreateCharacterPacket(CreateCharacterResult.SlotFull);

        if (!CharacterRaceNations.BelongsTo(race, account.Nation))
            return CreateCharacterPacket(CreateCharacterResult.InvalidRace);

        if (!IsValidStarterClass(@class, account.Nation) || gameData.GetCoefficient(@class) == null)
            return CreateCharacterPacket(CreateCharacterResult.InvalidClass);

        if (!IsValidStarterRaceClass(race, @class))
            return CreateCharacterPacket(CreateCharacterResult.InvalidClass);

        var totalStatPoints = strength + stamina + dexterity + intelligence + magic;
        if (totalStatPoints > 300)
            return CreateCharacterPacket(CreateCharacterResult.StatError);
        if (totalStatPoints < 300)
            return CreateCharacterPacket(CreateCharacterResult.PointsRemaining);
        if (strength < 50 || stamina < 50 || dexterity < 50 || intelligence < 50 || magic < 50)
            return CreateCharacterPacket(CreateCharacterResult.StatTooLow);

        if (string.IsNullOrWhiteSpace(name) || name.Length > 20)
            return CreateCharacterPacket(CreateCharacterResult.InvalidName);

        if (await characterRepository.IsNameTaken(name))
            return CreateCharacterPacket(CreateCharacterResult.NameAlreadyExists);

        var startZone = NewCharacterStartZone;
        var startPosition = gameData.GetStartPosition(startZone);
        var (spawnX, spawnZ) = ResolveSpawnPosition(startPosition, account.Nation);

        var character = new Character
        {
            AccountId = accountId,
            Slot = slot,
            Name = name,
            Race = race,
            Class = @class,
            Face = face,
            Hair = hair,
            Strength = strength,
            Stamina = stamina,
            Dexterity = dexterity,
            Intelligence = intelligence,
            Magic = magic,
            Level = 1,
            Experience = 0,
            Loyalty = 20,
            Money = 0,
            Hp = 100,
            Mp = 100,
            MapId = startZone,
            X = spawnX,
            Y = 0,
            Z = spawnZ,
            Items = StarterCharacterLoadout.CreateInitialItems(@class)
        };

        await characterRepository.CreateAsync(character);

        return CreateCharacterPacket(CreateCharacterResult.Success);
    }

    public async Task<Packet> DeleteCharacterAsync(int accountId, byte slot, string name, string socNo)
    {
        if (slot >= GameConstants.MaxAccountCharacters
            || string.IsNullOrEmpty(name) || name.Length > MaxCharacterNameLength
            || string.IsNullOrEmpty(socNo) || socNo.Length > MaxSocialNumberLength)
        {
            return DeleteRejected();
        }

        var character = await characterRepository.GetByName(name);
        if (character == null || character.AccountId != accountId || character.Slot != slot)
        {
            return DeleteRejected();
        }

        if (character.KnightsId > 0)
        {
            var clan = await knightsRepository.FindAsync(character.KnightsId);
            if (clan != null && string.Equals(clan.Chief, character.Name, StringComparison.OrdinalIgnoreCase))
            {
                return DeleteRejected();
            }
        }

        character.DeletionTime = DateTime.UtcNow;
        await characterRepository.UpdateAsync(character);

        return PreGamePacketWriter.DeleteCharacter(DeleteCharacterResult.Succeeded, slot);
    }

    public async Task<CharacterSelectResult> SelectCharacterAsync(int accountId, string accountName, string characterName, byte init)
    {
        var account = await accountRepository.GetById(accountId);
        if (account == null || account.Login != accountName)
        {
            return new CharacterSelectResult(SelectFailed(), 0);
        }

        var character = await characterRepository.GetByName(characterName);
        if (character == null || character.AccountId != account.Id)
        {
            return new CharacterSelectResult(SelectFailed(), 0);
        }

        await RepairReconnectZoneIfNeededAsync(character, account.Nation);

        var (zoneId, posX, posZ, posY) = ResolveSelectionPosition(character, account.Nation);
        var result = CharacterPacketMapper.BuildSelectCharacterSuccess(new SelectCharacterPacketContext(
            zoneId, posX, posZ, posY, (byte)account.Nation));

        return new CharacterSelectResult(result, character.Id);
    }

    public async Task<Packet> ChangeHairAsync(int accountId, byte subOpcode, string characterName, byte face, int hair)
    {
        if (subOpcode is not 0 and not 1)
        {
            return PreGamePacketWriter.ChangeHairResult(ChangeHairFailed);
        }

        var character = await characterRepository.GetByName(characterName);
        if (character == null || character.AccountId != accountId)
        {
            return PreGamePacketWriter.ChangeHairResult(ChangeHairFailed);
        }

        character.Face = face;
        character.Hair = hair;
        await characterRepository.UpdateAsync(character);

        return PreGamePacketWriter.ChangeHairResult(ChangeHairSucceeded);
    }

    public async Task<List<Packet>> GameStartAsync(int characterId, int accountId, byte subOpcode, UserSession? session = null)
    {
        var packets = new List<Packet>();

        if (subOpcode == 1)
        {
            var myInfo = await BuildMyInfoPacket(characterId, accountId);
            if (myInfo != null)
                packets.Add(myInfo);

            packets.Add(BuildStoryPacket());
            packets.Add(timeWeather.BuildCurrentTimePacket());
            packets.Add(timeWeather.BuildCurrentWeatherPacket());

            if (session != null)
            {
                packets.Add(BuildQuestClockPacket());
                packets.Add(BuildQuestStatePacket(session));
            }

            packets.Add(PreGamePacketWriter.GameStart());
        }
        else if (subOpcode == 2)
        {
            var character = await characterRepository.GetById(characterId);
            if (character != null)
            {
                character.IsOnline = true;
                character.LastOnlineTime = DateTime.UtcNow;
                await characterRepository.UpdateAsync(character);
            }
        }

        return packets;
    }

    private static Packet DeleteRejected() =>
        PreGamePacketWriter.DeleteCharacter(
            DeleteCharacterResult.Refused, PreGamePacketWriter.DeleteFailedSlot);

    private static Packet SelectFailed() =>
        PreGamePacketWriter.SelectCharacterFailure((byte)SelectCharacterResult.Failed);

    public async Task LogoutAsync(int characterId)
    {
        var character = await characterRepository.GetById(characterId);
        if (character == null)
            return;

        character.IsOnline = false;
        character.LastOnlineTime = DateTime.UtcNow;
        await characterRepository.UpdateAsync(character);
    }

    // --- Private helpers ---

    private async Task<Packet?> BuildMyInfoPacket(int characterId, int accountId)
    {
        var character = await characterRepository.GetById(characterId);
        var account = await accountRepository.GetById(accountId);
        if (character == null || account == null)
            return null;

        await RepairReconnectZoneIfNeededAsync(character, account.Nation);

        var inventory = DeserializeInventory(character.Items);
        var coefficient = gameData.GetCoefficient(character.Class);
        var stats = coefficient != null
            ? AbilityCalculator.Calculate(
                character.Level, character.Strength, character.Stamina,
                character.Dexterity, character.Intelligence,
                character.Class, coefficient, inventory, gameData, null, null,
                new RebirthBonus(character.RebStr, character.RebSta, character.RebDex,
                    character.RebIntel, character.RebMagic))
            : new DerivedStats();

        var clan = character.KnightsId > 0 ? sessionManager.Knights.GetClan(character.KnightsId) : null;
        var clanFame = ResolveClanFame(character, clan);
        var (zoneId, posX, posZ, posY) = ResolveLoginPosition(character, account.Nation);
        var premiumHours = account.RemainingPremiumHours;
        var allianceId = clan != null
            ? (short)(sessionManager.Knights.GetAllianceForClan(character.KnightsId)?.MainClanId ?? 0)
            : (short)0;

        return CharacterPacketMapper.BuildMyInfo(new MyInfoPacketContext(
            character, account, gameData.GetMaxExpForLevel(character.Level),
            stats, clan, allianceId, clanFame, zoneId, posX, posZ, posY, premiumHours));
    }

    private async Task RepairReconnectZoneIfNeededAsync(Character character, AccountNation nation)
    {
        var originalZoneId = character.MapId;
        if (!CharacterReconnectZoneRepair.TryRepair(character, nation, gameData))
            return;

        logger.LogWarning(
            "Relocating {Name} from unsupported reconnect zone {Zone} to safe zone {SafeZone}",
            character.Name,
            originalZoneId,
            character.MapId);

        await characterRepository.UpdateAsync(character);
    }

    private static ItemSlot[] DeserializeInventory(byte[] data)
    {
        var inventory = new ItemSlot[InventoryConstants.InventoryTotal];
        for (var i = 0; i < inventory.Length; i++)
            inventory[i] = new ItemSlot();

        UserSessionBinaryState.LoadSlots(inventory, data);
        return inventory;
    }

    private static byte ResolveClanFame(Character character, KnightsEntity? clan)
    {
        if (clan == null || character.KnightsId <= 0)
            return character.Fame;

        if (string.Equals(clan.Chief, character.Name, StringComparison.OrdinalIgnoreCase))
            return 1;

        return character.Fame > 0 ? character.Fame : (byte)5;
    }

    private (short ZoneId, short PosX, short PosZ, short PosY) ResolveLoginPosition(
        Character character, AccountNation nation)
    {
        if (character.MapId != 0 && (character.GetPosX != 0 || character.GetPosZ != 0))
            return (character.MapId, character.GetPosX, character.GetPosZ, character.GetPosY);

        short zoneId = character.MapId != 0
            ? character.MapId
            : (short)(nation == AccountNation.Karus ? 1 : 2);

        return ResolveSpawnForZone(zoneId, nation, character);
    }

    private (short ZoneId, short PosX, short PosZ, short PosY) ResolveSelectionPosition(
        Character character, AccountNation nation)
    {
        if (character.MapId != 0 && (character.GetPosX != 0 || character.GetPosZ != 0))
            return (character.MapId, character.GetPosX, character.GetPosZ, character.GetPosY);

        short zoneId = character.MapId != 0
            ? character.MapId
            : (short)(nation == AccountNation.Karus ? 1 : 2);

        return ResolveSpawnForZone(zoneId, nation, character);
    }

    private (short ZoneId, short PosX, short PosZ, short PosY) ResolveSpawnForZone(
        short zoneId, AccountNation nation, Character character)
    {
        var startPosition = gameData.GetStartPosition(zoneId);
        if (startPosition == null)
            return (zoneId, character.GetPosX, character.GetPosZ, character.GetPosY);

        var (spawnX, spawnZ) = startPosition.RandomSpawn(nation);

        return (zoneId, (short)(spawnX * 10), (short)(spawnZ * 10), 0);
    }

    private static (float X, float Z) ResolveSpawnPosition(StartPositionData? startPosition, AccountNation nation)
    {
        if (startPosition == null)
            return (0, 0);

        var (x, z) = startPosition.RandomSpawn(nation);

        return (x, z);
    }

    private static Packet CreateCharacterPacket(CreateCharacterResult code) =>
        PreGamePacketWriter.CreateCharacterResult((byte)code);

    private const short KarusKurianClass = 113;
    private const short ElMoradPorutuClass = 213;

    private static bool IsValidStarterClass(short classId, AccountNation nation) =>
        nation switch
        {
            AccountNation.Karus => classId is >= 101 and <= 104 or KarusKurianClass,
            AccountNation.ElMorad => classId is >= 201 and <= 204 or ElMoradPorutuClass,
            _ => false,
        };

    private static bool IsValidStarterRaceClass(byte race, short classId) =>
        (CharacterRace)race switch
        {
            CharacterRace.KarusArchTuarek => classId == 101,
            CharacterRace.KarusTuarek => classId is 102 or 104,
            CharacterRace.KarusWrinkleTuarek => classId == 103,
            CharacterRace.KarusPuriTuarek => classId is 103 or 104,
            CharacterRace.KarusKurian => classId == KarusKurianClass,
            CharacterRace.ElMoradBarbarian => classId == 201,
            CharacterRace.ElMoradMale or CharacterRace.ElMoradFemale
                => classId is 201 or 202 or 203 or 204,
            CharacterRace.ElMoradPorutu => classId == ElMoradPorutuClass,
            _ => false,
        };

    private static Packet BuildStoryPacket() =>
        PreGamePacketWriter.Story(PreGamePacketWriter.NoStory, 0);

    private static Packet BuildQuestClockPacket()
    {
        return QuestPacketWriter.Clock(DateTime.Now);
    }

    private static Packet BuildQuestStatePacket(UserSession session)
    {
        return QuestPacketWriter.QuestList(
            session.Quest.QuestMap
                .Select(entry => new QuestPacketWriter.QuestEntry(entry.Key, (QuestStatus)entry.Value))
                .ToList());
    }
}
