using Microsoft.Extensions.Hosting;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Configuration;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IAdminPanelPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Task SendGrantAsync(UserSession session);
}

public class AdminPanelPacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    ICombatNotificationService combatNotificationService,
    IZoneTransitionService zoneTransitionService,
    IWorldMovementService worldMovementService,
    INpcSpawnRowService spawnRows,
    INpcSpawnRowStore spawnStore,
    IHostEnvironment hostEnvironment,
    ICollectionRaceService collectionRaceService,
    IPlayerProgressionService playerProgressionService,
    ILoyaltyService loyaltyService,
    IItemGrantService itemGrantService,
    IServiceScopeFactory scopeFactory,
    IOptions<GameServerSettings> settings,
    ILogger<AdminPanelPacketCoordinator> logger) : IAdminPanelPacketCoordinator
{
    private const byte ReqState = 1;
    private const byte ReqCoins = 2;
    private const byte ReqStats = 3;
    private const byte ReqGiveItem = 4;
    private const byte ReqSetClass = 5;
    private const byte ReqZone = 6;
    private const byte ReqItemSearch = 7;
    private const byte ReqCollectionRaces = 8;
    private const byte ReqCollectionRaceStart = 9;
    private const byte ReqCollectionRaceClose = 10;
    private const byte ReqSetLevel = 11;
    private const byte ReqSetSkill = 12;
    private const byte ReqSetLook = 13;
    private const byte ReqFind = 14;
    private const byte ReqGo = 15;
    private const byte ReqSpawnRow = 16;
    private const byte ReqSpawnSet = 17;
    private const byte ReqSpawnPersist = 18;
    private const byte ReqCash = 19;

    private static readonly HashSet<byte> GameMasterOnlySubs =
    [
        ReqCollectionRaces, ReqCollectionRaceStart, ReqCollectionRaceClose,
        ReqFind, ReqGo, ReqSpawnRow, ReqSpawnSet, ReqSpawnPersist,
    ];

    private const byte KeepProgress = 0;
    private const byte ResetProgress = 1;
    private const int SkillEditBodySize = 2 + ProgressionTable.MasteryClassSlotCount;

    private const byte AckState = 0x10;
    private const byte AckResult = 0x11;
    private const byte AckGrant = 0x12;
    private const byte AckCollectionRaces = 0x14;
    private const byte AckFind = 0x15;
    private const byte AckSpawnRow = 0x16;

    private const int FindRowCap = 200;
    private const int SpawnCountCeiling = 50;
    private const int SpawnEditBodySize = 4 + 4 + 4 + 4 + 1 + 2 + 2;

    private const byte StatFloor = 1;
    private const byte StatCeiling = 255;
    private const short StatPointsCeiling = 10_000;
    private const int GiveCountCeiling = 9_999;

    private static readonly short[][] JobFamilies =
    [
        [1, 5, 6],
        [2, 7, 8],
        [3, 9, 10],
        [4, 11, 12],
        [13, 14, 15],
    ];

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();

        if (GrantFor(session) == AdminPanelGrant.None)
        {
            logger.LogWarning(
                "Admin-panel sub {Sub} refused for non-GM {Name} (character {CharacterId})",
                sub, session.Name, session.CharacterId);
            await SendStateAsync(session, granted: false);
            return;
        }

        if (!session.IsGM && GameMasterOnlySubs.Contains(sub))
        {
            logger.LogWarning(
                "Admin-panel sub {Sub} is GM-only, refused for public-demo grant {Name} (character {CharacterId})",
                sub, session.Name, session.CharacterId);
            return;
        }

        switch (sub)
        {
            case ReqState:
                await SendStateAsync(session, granted: true);
                break;

            case ReqCoins:
                await HandleCoinsAsync(session, packet);
                break;

            case ReqCash:
                await HandleCashAsync(session, packet);
                break;

            case ReqStats:
                await HandleStatsAsync(session, packet);
                break;

            case ReqGiveItem:
                await HandleGiveItemAsync(session, packet);
                break;

            case ReqSetClass:
                await HandleSetClassAsync(session, packet);
                break;

            case ReqSetLevel:
                await HandleSetLevelAsync(session, packet);
                break;

            case ReqSetSkill:
                await HandleSetSkillAsync(session, packet);
                break;

            case ReqSetLook:
                await HandleSetLookAsync(session, packet);
                break;

            case ReqZone:
                await HandleZoneAsync(session, packet);
                break;

            case ReqFind:
                await HandleFindAsync(session, packet);
                break;

            case ReqGo:
                await HandleGoAsync(session, packet);
                break;

            case ReqSpawnRow:
                await HandleSpawnRowAsync(session, packet);
                break;

            case ReqSpawnSet:
                await HandleSpawnEditAsync(session, packet, persist: false);
                break;

            case ReqSpawnPersist:
                await HandleSpawnEditAsync(session, packet, persist: true);
                break;

            case ReqCollectionRaces:
                await SendCollectionRacesAsync(session);
                break;

            case ReqCollectionRaceStart when packet.RemainingBytes >= 4:
                await collectionRaceService.StartRaceAsync(packet.ReadInt(), session);
                await SendCollectionRacesAsync(session);
                break;

            case ReqCollectionRaceClose when packet.RemainingBytes >= 4:
                await collectionRaceService.EndRaceAsync(packet.ReadInt(), forced: true, session);
                await SendCollectionRacesAsync(session);
                break;

            default:
                logger.LogDebug("Unhandled admin-panel sub {Sub} from {Name}", sub, session.Name);
                break;
        }
    }

    private async Task HandleCoinsAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4)
            return;

        var amount = packet.ReadInt();
        if (amount == 0)
            return;

        var total = (int)Math.Clamp((long)session.Money + amount, 0L, int.MaxValue);
        var delta = total - session.Money;
        session.Money = total;

        if (delta >= 0)
            await userNotificationService.SendGoldGainAsync(session, delta);
        else
            await userNotificationService.SendGoldLossAsync(session, -delta);

        PersistInBackground(session);
        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true, $"Gold {(delta >= 0 ? "+" : "")}{delta:n0} — now {total:n0}.");
        logger.LogInformation("GM {Name} adjusted own coins by {Delta} (now {Total})", session.Name, delta, total);
    }

    private async Task HandleCashAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4)
            return;

        var amount = packet.ReadInt();
        if (amount == 0)
            return;

        var total = (int)Math.Clamp((long)session.KnightCash + amount, 0L, int.MaxValue);
        var delta = total - session.KnightCash;
        session.KnightCash = total;

        _ = PersistCashAsync(session);
        await session.Client.SendPacket(ShoppingMallStoreService.BalancePacket(total));
        await SendResultAsync(session, true, $"Cash {(delta >= 0 ? "+" : "")}{delta:n0} — now {total:n0}.");
        logger.LogInformation("GM {Name} adjusted own cash by {Delta} (now {Total})", session.Name, delta, total);
    }

    private async Task PersistCashAsync(UserSession session)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = await db.Accounts.FindAsync(session.AccountId);
            if (account == null)
                return;

            account.KnightCash = session.KnightCash;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin-panel cash persist failed for {Name}", session.Name);
        }
    }

    private async Task HandleStatsAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 7)
            return;

        session.Strength = ClampStat(packet.ReadByte());
        session.Stamina = ClampStat(packet.ReadByte());
        session.Dexterity = ClampStat(packet.ReadByte());
        session.Intelligence = ClampStat(packet.ReadByte());
        session.Magic = ClampStat(packet.ReadByte());
        session.StatPoints = Math.Clamp(packet.ReadShort(), (short)0, StatPointsCeiling);
        if (packet.RemainingBytes >= sizeof(int))
            await loyaltyService.SetAsync(session, packet.ReadInt());

        Recalculate(session);
        await RefillVitalsAsync(session);

        PersistInBackground(session);
        await userNotificationService.SendStatUpdateAsync(session);
        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true,
            $"Stats set — STR {session.Strength} STA {session.Stamina} DEX {session.Dexterity} " +
            $"INT {session.Intelligence} MP {session.Magic}, {session.StatPoints} free, NP {session.Loyalty:n0}.");
        logger.LogInformation(
            "GM {Name} set own stats: str={Str} sta={Sta} dex={Dex} int={Int} mag={Mag} points={Points} np={Np}",
            session.Name, session.Strength, session.Stamina, session.Dexterity,
            session.Intelligence, session.Magic, session.StatPoints, session.Loyalty);
    }

    private async Task HandleGiveItemAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 6)
            return;

        var itemId = packet.ReadInt();
        var count = Math.Clamp((int)packet.ReadShort(), 1, GiveCountCeiling);

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
        {
            await SendResultAsync(session, false, $"Item {itemId} is not in the server's item table.");
            return;
        }

        var placed = await itemGrantService.GrantAsync(session, itemData, count);
        if (placed <= 0)
        {
            await SendResultAsync(session, false, "Inventory full.");
            return;
        }

        await SendResultAsync(session, true, $"Received {itemData.Name} x{placed}.");
        logger.LogInformation("GM {Name} granted self item {ItemId} x{Count}", session.Name, itemId, placed);
    }

    private async Task HandleSetClassAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;

        var target = packet.ReadShort();
        if (gameDataService.GetCoefficient(target) == null)
        {
            await SendResultAsync(session, false, $"Class {target} has no coefficient on this server.");
            return;
        }

        var previous = session.Class;
        session.Class = target;

        session.ResetMasteryPoints();

        Recalculate(session);
        await RefillVitalsAsync(session);

        PersistInBackground(session);
        await combatNotificationService.SendPartyClassUpdateAsync(session);
        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true,
            $"Class {previous} → {target}. Mastery points refunded and the skill bar cleared.");
        logger.LogInformation("GM {Name} changed own class {Previous} → {Target}", session.Name, previous, target);
    }

    private async Task HandleSetLevelAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;

        var level = packet.ReadByte();
        var reset = packet.ReadByte() == ResetProgress;

        if (!ProgressionTable.IsValidLevel(level))
        {
            await SendResultAsync(session, false,
                $"Level must be {ProgressionTable.MinLevel}-{ProgressionTable.MaxLevel}.");
            return;
        }

        if (reset)
            await playerProgressionService.ResetToLevelAsync(session, level);
        else
            await playerProgressionService.SetLevelAsync(session, level);

        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true, reset
            ? $"Level {level} — stats, mastery and skill bar reset."
            : $"Level set to {level}.");
        logger.LogInformation("GM {Name} set own level to {Level} (reset={Reset})",
            session.Name, level, reset);
    }

    private async Task HandleSetSkillAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < SkillEditBodySize)
            return;

        var pool = packet.ReadByte();
        var trees = new byte[ProgressionTable.MasteryClassSlotCount];
        for (var tree = 0; tree < trees.Length; tree++)
            trees[tree] = packet.ReadByte();
        var reset = packet.ReadByte() == ResetProgress;

        if (reset)
        {
            session.SkillData = [];
            session.ResetMasteryPoints();
        }
        else
        {
            session.SkillPoints[ProgressionTable.MasteryPoolSlot] = pool;
            for (var tree = 0; tree < trees.Length; tree++)
                session.SkillPoints[ProgressionTable.MasteryClassFirstSlot + tree] = trees[tree];
        }

        Recalculate(session);
        await RefillVitalsAsync(session);
        PersistInBackground(session);

        if (reset)
        {
            await session.Client.SendPacket(CharacterDevelopmentPacketMapper.CreateSkillResetSuccess(session));
            await session.Client.SendPacket(SkillDataPacketWriter.Cleared());
        }

        await userNotificationService.SendStatUpdateAsync(session);
        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true, reset
            ? $"Skills reset — {session.SkillPoints[ProgressionTable.MasteryPoolSlot]} mastery points in the pool."
            : $"Skill points updated (pool {pool}, trees {MasteryTrees(session)}).");
        logger.LogInformation(
            "GM {Name} edited skill points (reset={Reset}): pool={Pool} trees={Trees}",
            session.Name, reset, session.SkillPoints[ProgressionTable.MasteryPoolSlot], MasteryTrees(session));
    }

    private static string MasteryTrees(UserSession session) =>
        string.Join('/', session.SkillPoints
            .Skip(ProgressionTable.MasteryClassFirstSlot)
            .Take(ProgressionTable.MasteryClassSlotCount));

    private async Task HandleSetLookAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;

        var nationByte = packet.ReadByte();
        var race = packet.ReadByte();

        if (nationByte != (byte)AccountNation.Karus && nationByte != (byte)AccountNation.ElMorad)
        {
            await SendResultAsync(session, false, "Nation must be Karus or El Morad.");
            return;
        }

        var nation = (AccountNation)nationByte;
        if (!CharacterRaceNations.BelongsTo(race, nation))
        {
            await SendResultAsync(session, false, $"Appearance {race} is not a {nation} body.");
            return;
        }

        session.Nation = nation;
        session.Race = race;
        await PersistLookAsync(session, nation, race);

        await SendStateAsync(session, granted: true);
        await SendResultAsync(session, true,
            $"Nation → {nation}, appearance {race}. Log out to the character screen and back in to load the new body model.");
        logger.LogInformation("GM {Name} set nation={Nation} race={Race}", session.Name, nation, race);
    }

    private async Task PersistLookAsync(UserSession session, AccountNation nation, byte race)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            var characters = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

            var account = await accounts.GetById(session.AccountId);
            if (account != null)
            {
                account.Nation = nation;
                await accounts.UpdateAsync(account);
            }

            var character = await characters.GetById(session.CharacterId);
            if (character != null)
            {
                character.Race = race;
                await characters.UpdateAsync(character);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin-panel look persist failed for {Name}", session.Name);
        }
    }

    private async Task HandleZoneAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return;

        var target = packet.ReadShort();
        if (target is <= 0 or > byte.MaxValue
            || !gameDataService.ZoneInfoTable.TryGetValue(target, out var zoneInfo))
        {
            await SendResultAsync(session, false, $"Zone {target} is not on this server.");
            return;
        }

        var name = string.IsNullOrWhiteSpace(zoneInfo.MapName) ? $"zone {target}" : zoneInfo.MapName;

        if (target == session.ZoneId)
        {
            await SendResultAsync(session, false, $"You are already in {name}.");
            return;
        }

        if (session.IsWarping)
        {
            await SendResultAsync(session, false, "A zone change is already under way.");
            return;
        }

        var from = session.ZoneId;
        await SendResultAsync(session, true, $"Moving to {name}.");
        await zoneTransitionService.ChangeZoneAsync(session, (byte)target, 0f, 0f);
        logger.LogInformation(
            "GM {Name} used the panel to change zone {From} -> {To}", session.Name, from, target);
    }

    private async Task HandleFindAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 3)
            return;

        var kind = packet.ReadByte();
        var query = packet.ReadUtf8String().Trim();
        if (query.Length == 0)
        {
            await SendResultAsync(session, false, "Type a name or an id to search for.");
            return;
        }

        var byId = int.TryParse(query, out var wanted);
        IEnumerable<AdminPanelPacketWriter.FindRow> rows = kind == AdminPanelPacketWriter.FindPlayers
            ? sessionManager.GetAll()
                .Where(s => FindMatches(s.Name, s.CharacterId, query, byId, wanted))
                .Select(s => new AdminPanelPacketWriter.FindRow(
                    s.CharacterId, 0, s.Name, s.Level, s.ZoneId, WireCoordinate(s.X), WireCoordinate(s.Z), false, s.IsBot))
            : sessionManager.Regions.GetAllNpcs()
                .Where(n => n.Room == 0 && !n.IsDead && n.IsMonster == (kind == AdminPanelPacketWriter.FindMonsters)
                            && FindMatches(n.Name, n.NpcId, query, byId, wanted))
                .Select(n => new AdminPanelPacketWriter.FindRow(
                    n.NpcId, n.SpawnRow, n.Name, n.Level, n.ZoneId, WireCoordinate(n.X), WireCoordinate(n.Z), n.IsMonster, false));
        var found = rows
            .OrderBy(r => r.ZoneId)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.X)
            .ToList();
        await session.Client.SendPacket(AdminPanelPacketWriter.FindResults(
            AckFind, kind, found.Count, found.Take(FindRowCap).ToList()));
    }

    private static bool FindMatches(string name, int id, string query, bool byId, int wanted) =>
        (byId && id == wanted) || name.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static ushort WireCoordinate(float value) => (ushort)Math.Clamp(value, 0, ushort.MaxValue);

    private static ushort WarpUnits(ushort coordinate) => (ushort)Math.Min(coordinate * 10, ushort.MaxValue);

    private async Task HandleGoAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 5)
            return;

        var zone = packet.ReadByte();
        var x = packet.ReadUShort();
        var z = packet.ReadUShort();
        if (!gameDataService.ZoneInfoTable.TryGetValue(zone, out var zoneInfo))
        {
            await SendResultAsync(session, false, $"Zone {zone} is not on this server.");
            return;
        }

        if (session.IsWarping)
        {
            await SendResultAsync(session, false, "A zone change is already under way.");
            return;
        }

        var name = string.IsNullOrWhiteSpace(zoneInfo.MapName) ? $"zone {zone}" : zoneInfo.MapName;
        if (zone == session.ZoneId)
        {
            await SendResultAsync(session, true, $"Moving to {x}, {z}.");
            await worldMovementService.WarpAsync(session, WarpUnits(x), WarpUnits(z));
            logger.LogInformation("GM {Name} used the panel to warp to {X}, {Z} in zone {Zone}", session.Name, x, z, zone);
            return;
        }

        await SendResultAsync(session, true, $"Moving to {name} at {x}, {z}.");
        await zoneTransitionService.ChangeZoneAsync(session, zone, x, z);
        logger.LogInformation("GM {Name} used the panel to go to zone {Zone} at {X}, {Z}", session.Name, zone, x, z);
    }

    private async Task HandleSpawnRowAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4)
            return;

        var index = packet.ReadInt();
        var row = spawnRows.Find(index);
        if (row == null)
        {
            await SendResultAsync(session, false, $"No spawn row {index}.");
            return;
        }

        await SendSpawnRowAsync(session, row);
    }

    private async Task SendSpawnRowAsync(UserSession session, NpcPosData row)
    {
        var name = gameDataService.GetSpawnProto(row)?.Name ?? $"npc {row.NpcId}";
        var height = sessionManager.Maps?.GetHeight(row.ZoneId, row.LeftX, row.TopZ) ?? 0f;
        await session.Client.SendPacket(AdminPanelPacketWriter.SpawnRow(AckSpawnRow, new AdminPanelPacketWriter.SpawnRowInfo(
            CanPersist(row), row.Index, row.NpcId, name, (byte)row.ZoneId, row.ActType < NpcPosData.NpcSpawnActTypeBase,
            row.LeftX, row.TopZ, (int)MathF.Round(height * 10f), row.Direction, row.NumNPC, row.RegTime, row.SpawnRange,
            spawnRows.AliveCount(row))));
    }

    private bool CanPersist(NpcPosData row) =>
        hostEnvironment.IsDevelopment() && NpcPositionSeedFile.PathFor(hostEnvironment.ContentRootPath, row.ZoneId) != null;

    private async Task HandleSpawnEditAsync(UserSession session, Packet packet, bool persist)
    {
        if (packet.RemainingBytes < SpawnEditBodySize)
            return;

        var index = packet.ReadInt();
        var x = packet.ReadInt();
        var z = packet.ReadInt();
        var direction = packet.ReadInt();
        var count = packet.ReadByte();
        var respawn = packet.ReadShort();
        var range = packet.ReadShort();
        var row = spawnRows.Find(index);
        if (row == null)
        {
            await SendResultAsync(session, false, $"No spawn row {index}.");
            return;
        }

        if (x < 0 || z < 0 || direction < 0 || direction >= 360 || count < 1 || count > SpawnCountCeiling || respawn < 0 || range < 0)
        {
            await SendResultAsync(session, false, "Spawn values out of range.");
            return;
        }

        if (persist && !CanPersist(row))
        {
            await SendResultAsync(session, false, "Persisting is only available on a local development server.");
            return;
        }

        spawnRows.Edit(row, x, z, direction, count, respawn, range);
        var spawned = await spawnRows.RespawnRowAsync(row);

        if (!persist)
        {
            await SendResultAsync(session, true, $"Spawn row {index} set ({spawned} spawned); not persisted.");
            await SendSpawnRowAsync(session, row);
            return;
        }

        var path = NpcPositionSeedFile.PathFor(hostEnvironment.ContentRootPath, row.ZoneId)!;
        if (!NpcPositionSeedFile.TryUpdate(path, spawnRows.LastPersisted(row), row, out var error))
        {
            await SendResultAsync(session, false, $"Spawn row {index} set ({spawned} spawned) but not persisted: {error}");
            await SendSpawnRowAsync(session, row);
            return;
        }

        var stored = await spawnStore.UpdateAsync(row);
        spawnRows.MarkPersisted(row);
        await SendResultAsync(session, true,
            $"Spawn row {index} persisted to {Path.GetFileName(path)}{(stored ? " and the database" : "")} ({spawned} spawned).");
        logger.LogInformation("GM {Name} persisted spawn row {Index} ({NpcId} in zone {Zone} at {X}, {Z})",
            session.Name, index, row.NpcId, row.ZoneId, x, z);
        await SendSpawnRowAsync(session, row);
    }

    private async Task SendCollectionRacesAsync(UserSession session)
    {
        var active = collectionRaceService.ActiveRaces.ToDictionary(a => a.Race.Id);
        var rows = new List<AdminPanelPacketWriter.CollectionRaceRow>();
        foreach (var race in gameDataService.CollectionRaceTable.Values.OrderBy(r => r.ZoneId).ThenBy(r => r.Id))
        {
            active.TryGetValue(race.Id, out var running);
            rows.Add(new AdminPanelPacketWriter.CollectionRaceRow(
                race.Id,
                race.Name,
                race.ZoneId,
                race.MinLevel,
                race.MaxLevel,
                race.DurationMinutes,
                race.AutoStart,
                running != null,
                running?.RemainingSeconds ?? 0,
                running?.Winners ?? 0,
                race.MaxWinners,
                DescribeSchedule(gameDataService.CollectionRaceSchedulesByRace[race.Id]),
                DescribeObjectives(gameDataService.CollectionRaceObjectivesByRace[race.Id])));
        }

        await session.Client.SendPacket(AdminPanelPacketWriter.CollectionRaces(AckCollectionRaces, rows));
    }

    private static string DescribeSchedule(IEnumerable<CollectionRaceScheduleData> schedules)
    {
        var parts = schedules
            .OrderBy(s => s.Day.HasValue ? (int)s.Day.Value : -1)
            .ThenBy(s => s.Hour)
            .ThenBy(s => s.Minute)
            .Select(s => $"{(s.Day.HasValue ? s.Day.Value.ToString()[..3] : "Daily")} {s.Hour:D2}:{s.Minute:D2}")
            .ToList();
        return parts.Count == 0 ? "manual" : string.Join(", ", parts);
    }

    private string DescribeObjectives(IEnumerable<CollectionRaceObjectiveData> objectives)
    {
        var parts = objectives.OrderBy(o => o.Ordinal).Select(o => o.Kind switch
        {
            CollectionRaceObjectiveKind.EnemyPlayer => $"{o.Count} enemy players",
            CollectionRaceObjectiveKind.Item => $"{o.Count} x {gameDataService.GetItem(o.TargetId)?.Name ?? $"item {o.TargetId}"}",
            _ => $"{o.Count} x {(gameDataService.NpcTable.TryGetValue(o.TargetId, out var npc) ? npc.Name : $"monster {o.TargetId}")}",
        });
        return string.Join(", ", parts);
    }

    private List<short> ClassOptionsFor(UserSession session)
    {
        var options = new List<short>();
        var nationBase = (short)(session.Class / 100 * 100);
        if (nationBase <= 0)
            return options;

        var subtype = (short)ClassIdHelper.GetSubtype(session.Class);
        foreach (var family in JobFamilies)
        {
            if (Array.IndexOf(family, subtype) < 0)
                continue;

            foreach (var member in family)
            {
                var candidate = (short)(nationBase + member);
                if (candidate == session.Class)
                    continue;
                if (gameDataService.GetCoefficient(candidate) == null)
                    continue;
                options.Add(candidate);
            }
            break;
        }
        return options;
    }

    public async Task SendGrantAsync(UserSession session)
    {
        var grant = GrantFor(session);
        var speedGranted = SpeedGrantedFor(session);
        if (grant == AdminPanelGrant.None && !speedGranted)
            return;

        await session.Client.SendPacket(
            AdminPanelPacketWriter.Grant(AckGrant, (byte)grant, speedGranted));
        if (session.IsGM)
            await session.Client.SendPacket(AdminPanelPacketWriter.GmFx(session.CharacterId, session.GmModeEnabled));

        if (!session.IsGM)
            logger.LogInformation(
                "Public-demo grant to {Name} (character {CharacterId}): panel={Panel} speed={Speed}",
                session.Name, session.CharacterId, grant == AdminPanelGrant.PublicDemo, speedGranted);
    }

    private AdminPanelGrant GrantFor(UserSession session)
    {
        if (session.IsGM)
            return AdminPanelGrant.GameMaster;
        return settings.Value.PublicDemo.GrantGameMasterPanelToEveryone
            ? AdminPanelGrant.PublicDemo
            : AdminPanelGrant.None;
    }

    private bool SpeedGrantedFor(UserSession session) =>
        session.IsGM || settings.Value.PublicDemo.GrantGameMasterSpeedToEveryone;

    private async Task SendStateAsync(UserSession session, bool granted)
    {
        if (!granted)
        {
            await session.Client.SendPacket(AdminPanelPacketWriter.StateDenied(AckState));
            return;
        }

        var state = new AdminPanelPacketWriter.State(
            session.Class, session.Level,
            session.Strength, session.Stamina, session.Dexterity,
            session.Intelligence, session.Magic,
            session.StatPoints, session.MaxHp, session.MaxMp,
            (short)session.Stats.TotalHit, session.Stats.TotalAc,
            session.Money, session.SkillPoints, ClassOptionsFor(session),
            (byte)session.Nation, session.Race, session.Loyalty);

        await session.Client.SendPacket(AdminPanelPacketWriter.StateGranted(AckState, state));
    }

    private static async Task SendResultAsync(UserSession session, bool ok, string message)
    {
        await session.Client.SendPacket(AdminPanelPacketWriter.Result(AckResult, ok, message));
    }

    private static byte ClampStat(byte value) => Math.Clamp(value, StatFloor, StatCeiling);

    private void Recalculate(UserSession session)
    {
        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);
    }

    private async Task RefillVitalsAsync(UserSession session)
    {
        if (session.Hp > session.MaxHp) session.Hp = session.MaxHp;
        if (session.Mp > session.MaxMp) session.Mp = session.MaxMp;
        await combatNotificationService.SendHpChangeAsync(session);
        await combatNotificationService.SendMspChangeAsync(session);
    }

    private void PersistInBackground(UserSession session) => _ = PersistAsync(session);

    private async Task PersistAsync(UserSession session)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var characters = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
            var character = await characters.GetById(session.CharacterId);
            if (character == null)
                return;

            character.Class = session.Class;
            character.Strength = session.Strength;
            character.Stamina = session.Stamina;
            character.Dexterity = session.Dexterity;
            character.Intelligence = session.Intelligence;
            character.Magic = session.Magic;
            character.StatPoints = session.StatPoints;
            character.Money = session.Money;
            character.Loyalty = session.Loyalty;
            character.SkillPointData = session.SkillPoints;
            await characters.UpdateAsync(character);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin-panel persist failed for {Name}", session.Name);
        }
    }
}
