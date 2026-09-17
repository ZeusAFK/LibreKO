using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IGameSessionInitializer
{
    Task<UserSession?> InitializeAsync(IClient client);
}

public class GameSessionInitializer(
    ICharacterRepository characterRepository,
    IAccountRepository accountRepository,
    IWarehouseRepository warehouseRepository,
    IUserDailyOpRepository dailyOpRepository,
    IUserSessionCharacterMapper userSessionCharacterMapper,
    IGameDataService gameData,
    SessionManager sessionManager,
    ILogger<GameSessionInitializer> logger) : IGameSessionInitializer
{
    public async Task<UserSession?> InitializeAsync(IClient client)
    {
        var existingSession = sessionManager.GetByClientId(client.Id);
        if (existingSession != null)
            return existingSession;

        var character = await characterRepository.GetById(client.CharacterId);
        var account = await accountRepository.GetById(client.AccountId);
        if (character == null || account == null)
            return null;

        var originalZoneId = character.MapId;
        if (CharacterReconnectZoneRepair.TryRepair(character, account.Nation, gameData))
        {
            logger.LogWarning(
                "Relocating {Name} from unsupported reconnect zone {Zone} to safe zone {SafeZone} during session init",
                character.Name,
                originalZoneId,
                character.MapId);

            await characterRepository.UpdateAsync(character);
        }

        var warehouse = await warehouseRepository.GetOrCreateByAccountId(client.AccountId);

        var coefficient = gameData.GetCoefficient(character.Class);
        var maxHp = (short)Math.Max(1, coefficient != null
            ? AbilityCalculator.CalculateMaxHp(character, coefficient)
            : character.Hp);
        var maxMp = (short)Math.Max(0, coefficient != null
            ? AbilityCalculator.CalculateMaxMp(character, coefficient)
            : character.Mp);

        if (character.Hp <= 0)
        {
            logger.LogInformation(
                "Recovering dead login for {Name} (ID:{CharId}) with saved HP {SavedHp}; restoring to {RecoveredHp}",
                character.Name,
                character.Id,
                character.Hp,
                maxHp);

            character.Hp = maxHp;
            await characterRepository.UpdateAsync(character);
        }

        var session = sessionManager.CreateSession(client, client.CharacterId, client.AccountId);
        userSessionCharacterMapper.HydrateSession(session, character, account, warehouse, maxHp, maxMp, gameData);
        userSessionCharacterMapper.HydrateDailyOps(
            session, await dailyOpRepository.GetOrCreateByCharacterId(character.Id));
        session.IsGM = account.Authority == AccountAuthority.GameMaster;
        logger.LogDebug("Session created for {Name}: Authority={Authority}, IsGM={IsGM}", session.Name, account.Authority, session.IsGM);

        if (session.KnightsId > 0)
        {
            var clan = sessionManager.Knights.GetClan(session.KnightsId);
            if (clan != null)
            {
                session.KnightsName = clan.Name;
                if (clan.Chief == session.Name)
                    session.KnightsFame = 1;
                else
                    session.KnightsFame = session.Fame > 0 ? session.Fame : (byte)5;
            }
            else
            {
                logger.LogWarning(
                    "Clearing stale clan state for {Name}: character references missing clan {ClanId}",
                    session.Name,
                    session.KnightsId);

                character.KnightsId = 0;
                character.Fame = 0;
                await characterRepository.UpdateAsync(character);

                session.KnightsId = 0;
                session.KnightsFame = 0;
                session.KnightsName = string.Empty;
                session.Fame = 0;
            }
        }

        session.PremiumExpiry = account.PremiumDate;
        session.PremiumService = account.PremiumType;
        session.AccountStatus = session.PremiumTime > 0
            ? UserSession.AccountStatusPremium
            : UserSession.AccountStatusNone;

        if (coefficient != null)
            session.RecalculateStats(coefficient, gameData);

        sessionManager.Regions.AddToRegion(session);

        logger.LogDebug(
            "Player {Name} (ID:{CharId}) entered the game in zone {Zone} at ({X},{Z}) region ({RX},{RZ})",
            session.Name,
            session.CharacterId,
            session.ZoneId,
            session.X,
            session.Z,
            session.RegionX,
            session.RegionZ);

        return session;
    }
}
