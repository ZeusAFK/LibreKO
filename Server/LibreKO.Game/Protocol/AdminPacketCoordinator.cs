using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence.Seed;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using LibreKO.Game.Configuration;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IAdminPacketCoordinator
{
    Task HandleOperatorAsync(IClient client, Packet packet);
    Task HandleGmCommandAsync(UserSession session, string command);
    bool IsOpenToEveryone(string command);
}

public class AdminPacketCoordinator(
    IServiceProvider serviceProvider,
    IOptions<GameServerSettings> settings,
    SessionManager sessionManager,
    ISessionTerminationService sessionTerminationService,
    IZoneTransitionService zoneTransitionService,
    IWorldPacketCoordinator worldPacketCoordinator,
    IPlayerProgressionService playerProgressionService,
    IEventSystemsPacketCoordinator eventSystemsPacketCoordinator,
    IMiscPacketCoordinator miscPacketCoordinator,
    IUserNotificationService userNotificationService,
    ICombatLifecycleService combatLifecycleService,
    ICombatNotificationService combatNotificationService,
    ILoyaltyService loyaltyService,
    IGameDataService gameDataService,
    TimeWeatherBroadcastService timeWeather,
    IBifrostEventService bifrostEventService,
    IMonsterAggressionPolicy monsterAggressionPolicy,
    EventSchedulerService eventSchedulerService,
    ICollectionRaceService collectionRaceService,
    INpcSummonService npcSummonService,
    ILogger<AdminPacketCoordinator> logger) : IAdminPacketCoordinator
{
    private const int MaxGmSummonCount = 50;

    public async Task HandleOperatorAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || !session.IsGM)
            return;

        var opcode = packet.ReadByte();
        var targetName = packet.ReadSByteString();
        if (string.IsNullOrEmpty(targetName) || targetName.Length > 20)
            return;

        var target = sessionManager.GetByName(targetName);

        switch (opcode)
        {
            case 1: // OPERATOR_ARREST - GM warps to target
                if (target != null)
                {
                    session.X = target.X;
                    session.Z = target.Z;
                    session.Y = target.Y;
                    if (session.ZoneId != target.ZoneId)
                        await zoneTransitionService.ChangeZoneAsync(session, target.ZoneId, target.X, target.Z);
                    else
                        await WarpToPositionAsync(session, target.X, target.Z);
                }
                break;

            case 5: // OPERATOR_CUTOFF - Disconnect target
                if (target != null)
                    await sessionTerminationService.LogoutAsync(target.Client);
                break;

            case 7: // OPERATOR_SUMMON - Summon target to GM
                if (target != null)
                {
                    if (target.ZoneId != session.ZoneId)
                        await zoneTransitionService.ChangeZoneAsync(target, session.ZoneId, session.X, session.Z);
                    else
                        await WarpToPositionAsync(target, session.X, session.Z);
                }
                break;

            default:
                logger.LogDebug("Unhandled operator command {Opcode} from GM {Name}", opcode, session.Name);
                break;
        }
    }

    public bool IsOpenToEveryone(string command) =>
        CommandWord(command) == "setlevel" && settings.Value.PublicDemo.GrantSetLevelToEveryone;

    private static string CommandWord(string command)
    {
        var parts = command.TrimStart('+').Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[0].ToLowerInvariant();
    }

    public async Task HandleGmCommandAsync(UserSession session, string command)
    {
        if (!session.IsGM && !IsOpenToEveryone(command))
            return;

        var parts = command.TrimStart('+').Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;

        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1] : string.Empty;

        switch (cmd)
        {
            case "gm":
                if (!session.IsGM) return;
                session.GmModeEnabled = !session.GmModeEnabled;
                await sessionManager.Regions.SendToRegion(session,
                    AdminPanelPacketWriter.GmFx(session.CharacterId, session.GmModeEnabled), excludeSender: false);
                await SendNoticeAsync(session, session.GmModeEnabled ? "GM mode enabled." : "GM mode disabled.");
                break;

            case "santa":
                await miscPacketCoordinator.SetSantaOrAngelStateAsync(1);
                break;

            case "angel":
                await miscPacketCoordinator.SetSantaOrAngelStateAsync(2);
                break;

            case "offsanta":
            case "offangel":
                await miscPacketCoordinator.SetSantaOrAngelStateAsync(0);
                break;

            case "notice":
                if (!string.IsNullOrEmpty(arg))
                    await BroadcastNoticeAsync(arg);
                break;

            case "cropen":
                if (int.TryParse(arg, out var crId))
                {
                    await collectionRaceService.StartRaceAsync(crId, session);
                }
                else
                {
                    await SendNoticeAsync(session, "Usage: +cropen <raceId>");
                }
                break;

            case "crclose":
                if (int.TryParse(arg, out var crCloseId))
                    await collectionRaceService.EndRaceAsync(crCloseId, forced: true, session);
                else
                    await collectionRaceService.EndAllAsync(session);
                break;

            case "crstatus":
                var activeRaces = collectionRaceService.ActiveRaces;
                if (activeRaces.Count == 0)
                {
                    await SendNoticeAsync(session, "No active Collection Race.");
                    break;
                }

                foreach (var active in activeRaces)
                {
                    await SendNoticeAsync(session,
                        $"Active CR: '{active.Race.Name}' (ID {active.Race.Id}) in Zone {active.Race.ZoneId}. Remaining: {active.RemainingSeconds}s.");
                }
                break;

            case "time":
                await HandleTimeAsync(session, arg);
                break;

            case "weather":
                await HandleWeatherAsync(session, arg);
                break;

            case "sealcode":
                await HandleSealCodeAsync(session, arg);
                break;

            case "exp":
                if (long.TryParse(arg, out var expAmount))
                    await playerProgressionService.AwardExperienceAsync(session, expAmount);
                break;

            case "gold":
                if (int.TryParse(arg, out var goldAmount))
                {
                    session.Money += goldAmount;
                    if (goldAmount >= 0)
                        await userNotificationService.SendGoldGainAsync(session, goldAmount);
                    else
                        await userNotificationService.SendGoldLossAsync(session, -goldAmount);
                }
                break;

            case "setlevel":
                await HandleSetLevelAsync(session, arg);
                break;

            case "hp":
                session.Hp = session.MaxHp;
                session.Mp = session.MaxMp;
                await combatNotificationService.SendHpChangeAsync(session);
                await combatNotificationService.SendMspChangeAsync(session);
                break;

            case "online":
                await SendNoticeAsync(session, $"Online players: {sessionManager.GetAll().Count()}");
                break;

            case "waropen":
                if (byte.TryParse(arg, out var zoneId) && BattleZoneManager.IsBattleZone(zoneId))
                {
                    await eventSystemsPacketCoordinator.OpenBattleZoneAsync(BattleZoneManager.BATTLEZONE_OPEN, zoneId);
                    await BroadcastNoticeAsync($"Battle zone {zoneId} opened!");
                }
                break;

            case "warclose":
                await eventSystemsPacketCoordinator.CloseBattleZoneAsync();
                await BroadcastNoticeAsync("Battle zone closed.");
                break;

            case "snowwar":
                await eventSystemsPacketCoordinator.OpenBattleZoneAsync(BattleZoneManager.SNOW_BATTLEZONE_OPEN, BattleZoneManager.ZONE_SNOW_BATTLE);
                await BroadcastNoticeAsync("Snow battle zone opened!");
                break;

            case "jr":
            case "juraid":
                await HandleTempleEventCommandAsync(session, TempleEvent.JuraidMountain, ZoneId.JuradMountain, "Juraid Mountain", arg);
                break;

            case "bdw":
                await HandleTempleEventCommandAsync(session, TempleEvent.BorderDefenseWar, ZoneId.BorderDefenseWar, "Border Defense War", arg);
                break;

            case "chaos":
                await HandleTempleEventCommandAsync(session, TempleEvent.Chaos, ZoneId.ChaosDungeon, "Chaos Dungeon", arg);
                break;

            case "templecancel":
            case "cancelevent":
                if (await eventSchedulerService.CancelTempleEventAsync())
                {
                    await SendNoticeAsync(session, "Temple event cancelled.");
                }
                else
                {
                    await SendNoticeAsync(session, "No temple event is currently active.");
                }
                break;

            case "?":
            case "help":
                await SendNoticeAsync(session, "GM Commands:");
                await SendNoticeAsync(session, "+give <itemId> [count] - Give item");
                await SendNoticeAsync(session, "+monsummon <npcId> [count] - Spawn monsters here once; they never respawn");
                await SendNoticeAsync(session, "+item <name> - Search items by name");
                await SendNoticeAsync(session, "+gold <amount> - Give/take gold");
                await SendNoticeAsync(session, "+kc <name> <amount> - Give/take Knight Cash");
                await SendNoticeAsync(session,
                    "+setlevel <1-83> - Set level; resets stats + mastery, clears the skill bar");
                await SendNoticeAsync(session, "+hp - Restore HP/MP");
                await SendNoticeAsync(session, "+gm - Toggle GM mode: the GM aura, one-hit kills, 1 damage taken");
                await SendNoticeAsync(session, "+exp <amount> - Give experience");
                await SendNoticeAsync(session, "+notice <text> - Server notice");
                await SendNoticeAsync(session, "+time <hh[:mm]> - Set the game time for everyone");
                await SendNoticeAsync(session, "+weather <clear|rain|snow|leaves> [0-100] - Set the weather for everyone");
                await SendNoticeAsync(session, "+sealcode <8 digits> - Set this account's item seal security code");
                await SendNoticeAsync(session, "+online - Show online count");
                await SendNoticeAsync(session, "+santa/+angel/+offsanta - Santa/Angel");
                await SendNoticeAsync(session, "+waropen <zoneId>/+warclose/+snowwar");
                await SendNoticeAsync(session, "+bifroststart [min] / +bifrostclose - Bifrost event");
                await SendNoticeAsync(session, "+jr [sec] / +bdw [sec] / +chaos [sec] / +templecancel - Temple Events");
                await SendNoticeAsync(session, "+cropen <eventIndex> / +crclose / +crstatus - Collection Race");
                await SendNoticeAsync(session, "+zone | +zone <id> - List zones / teleport to zone home");
                await SendNoticeAsync(session, "+reloadscripts - Reload quest scripts without restart");
                await SendNoticeAsync(session, "+reseed - Seed JSON to DB + reload (drops/NPCs/items)");
                break;

            case "give":
                await HandleGiveItemAsync(session, arg);
                break;

            case "item":
                await HandleItemSearchAsync(session, arg);
                break;

            case "mute":
                await HandleMuteAsync(session, arg, true);
                break;

            case "unmute":
                await HandleMuteAsync(session, arg, false);
                break;

            case "kick":
                await HandleKickAsync(session, arg);
                break;

            case "kill":
                await HandleKillAsync(session, arg);
                break;

            case "ban":
                await HandleBanAsync(session, arg, true);
                break;

            case "unban":
                await HandleBanAsync(session, arg, false);
                break;

            case "hapis":
            case "prison":
                await HandlePrisonAsync(session, arg);
                break;

            case "exp_add":
            case "expadd":
                await HandleEventRateAsync(session, arg, "EXP", v => timeWeather.ExpEventAmount = v);
                break;

            case "money_add":
            case "noahadd":
            case "coin_add":
                await HandleEventRateAsync(session, arg, "Coin", v => timeWeather.CoinEventAmount = v);
                break;

            case "np_add":
                await HandleEventRateAsync(session, arg, "NP", v => timeWeather.NpEventAmount = v);
                break;

            case "drop_add":
                await HandleEventRateAsync(session, arg, "Drop", v => timeWeather.DropEventAmount = v);
                break;

            case "goto":
                await HandleGotoAsync(session, arg);
                break;

            case "zone":
                await HandleZoneAsync(session, arg);
                break;

            case "questinfo":
            case "quest":
                await HandleQuestInfoAsync(session, arg);
                break;

            case "questreset":
                await HandleQuestResetAsync(session, arg);
                break;

            case "reloadscripts":
            case "reloadquests":
                await SendNoticeAsync(session, "Quest script cache cleared; scripts reload on next interaction.");
                logger.LogInformation("GM {Gm} cleared the quest script cache", session.Name);
                break;

            case "reloadgamedata":
            case "reloaddrops":
            case "reseed":
                await HandleReseedAsync(session);
                break;

            case "summonuser":
                await HandleSummonUserAsync(session, arg);
                break;

            case "monsummon":
                await HandleMonsterSummonAsync(session, arg);
                break;

            case "kill_all":
            case "killall":
                await HandleKillAllAsync(session);
                break;

            case "countzone":
                await HandleCountZoneAsync(session);
                break;

            case "countlevel":
                await HandleCountLevelAsync(session);
                break;

            case "exp_change":
            case "expchange":
                await HandleAdjustAsync(session, arg, kind: "exp");
                break;

            case "np_change":
            case "npchange":
                await HandleAdjustAsync(session, arg, kind: "np");
                break;

            case "mon":
            case "monster":
                await HandleSummonMonsterAsync(session, arg);
                break;

            case "kc":
            case "knightcash":
                await HandleKnightCashAsync(session, arg);
                break;

            case "bifroststart":
                await HandleBifrostStartAsync(session, arg);
                break;

            case "bifrostclose":
                await HandleBifrostCloseAsync(session);
                break;

            default:
                logger.LogDebug("Unknown GM command: {Command} from {Name}", cmd, session.Name);
                break;
        }
    }

    private async Task HandleSetLevelAsync(UserSession session, string arg)
    {
        if (!byte.TryParse(arg.Trim(), out var level) || !ProgressionTable.IsValidLevel(level))
        {
            await SendNoticeAsync(session,
                $"Usage: +setlevel <{ProgressionTable.MinLevel}-{ProgressionTable.MaxLevel}>");
            return;
        }

        await playerProgressionService.ResetToLevelAsync(session, level);

        var mastery = session.SkillPoints[ProgressionTable.MasteryPoolSlot];
        await SendNoticeAsync(session,
            $"Level {level}: {session.StatPoints} stat points, {mastery} mastery points, " +
            $"HP {session.MaxHp}, MP {session.MaxMp}. Stats, mastery and skill bar reset.");
        logger.LogInformation(
            "{Name} set own level to {Level} (stat points {StatPoints}, mastery {Mastery})",
            session.Name, level, session.StatPoints, mastery);
    }

    private async Task HandleKickAsync(UserSession session, string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            await SendNoticeAsync(session, "Usage: +kick <name>");
            return;
        }

        var target = sessionManager.GetByName(arg.Trim());
        if (target == null)
        {
            await SendNoticeAsync(session, $"Player not found: {arg}");
            return;
        }

        await sessionTerminationService.LogoutAsync(target.Client);
        await SendNoticeAsync(session, $"Kicked {target.Name}.");
        logger.LogInformation("GM {Gm} kicked {Target}", session.Name, target.Name);
    }

    private async Task HandleKillAsync(UserSession session, string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            await SendNoticeAsync(session, "Usage: +kill <name>");
            return;
        }

        var target = sessionManager.GetByName(arg.Trim());
        if (target == null || target.Hp <= 0)
        {
            await SendNoticeAsync(session, $"Target not found or already dead: {arg}");
            return;
        }

        target.Hp = 0;
        await combatNotificationService.SendHpChangeAsync(target, session.CharacterId);
        await combatLifecycleService.HandlePlayerDeathAsync(target, session);
        await SendNoticeAsync(session, $"Killed {target.Name}.");
        logger.LogInformation("GM {Gm} killed {Target}", session.Name, target.Name);
    }

    private const byte PrisonZoneId = (byte)ZoneId.Prison;
    private const float PrisonStartX = 215f;
    private const float PrisonStartZ = 158f;

    private async Task HandleKillAllAsync(UserSession session)
    {
        var killed = 0;
        foreach (var npc in sessionManager.Regions.GetAllNpcsInZone(session.ZoneId))
        {
            if (!npc.IsAlive) continue;
            if (!npc.IsAttackable) continue;

            npc.Hp = 0;
            await combatLifecycleService.HandleNpcDeathAsync(npc, session);
            killed++;
        }
        await SendNoticeAsync(session, $"Killed {killed} NPCs in zone {session.ZoneId}.");
        logger.LogInformation("GM {Gm} killed {Count} NPCs in zone {Zone}", session.Name, killed, session.ZoneId);
    }

    private async Task HandleCountZoneAsync(UserSession session)
    {
        var grouped = sessionManager.GetAll()
            .GroupBy(s => s.ZoneId)
            .OrderByDescending(g => g.Count())
            .Take(10);

        await SendNoticeAsync(session, "Zone populations (top 10):");
        foreach (var group in grouped)
            await SendNoticeAsync(session, $"  Zone {group.Key}: {group.Count()} players");
    }

    private async Task HandleCountLevelAsync(UserSession session)
    {
        var buckets = sessionManager.GetAll()
            .GroupBy(s => s.Level / 10)
            .OrderBy(g => g.Key);

        await SendNoticeAsync(session, "Level distribution:");
        foreach (var bucket in buckets)
            await SendNoticeAsync(session, $"  Lv {bucket.Key * 10}-{bucket.Key * 10 + 9}: {bucket.Count()}");
    }

    private async Task HandleAdjustAsync(UserSession session, string arg, string kind)
    {
        var parts = arg.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await SendNoticeAsync(session, $"Usage: +{kind}_change <name> <amount>");
            return;
        }

        var target = sessionManager.GetByName(parts[0]);
        if (target == null)
        {
            await SendNoticeAsync(session, $"Player not found: {parts[0]}");
            return;
        }

        if (kind == "exp")
        {
            if (!long.TryParse(parts[1], out var amount))
            {
                await SendNoticeAsync(session, "Amount must be an integer.");
                return;
            }
            await playerProgressionService.ChangeExperienceAsync(target, amount);
            await SendNoticeAsync(session, $"{target.Name} EXP {(amount >= 0 ? "+" : "")}{amount}");
            return;
        }

        if (kind == "np")
        {
            if (!int.TryParse(parts[1], out var amount))
            {
                await SendNoticeAsync(session, "Amount must be an integer.");
                return;
            }
            await loyaltyService.ChangeAsync(target, amount);
            await SendNoticeAsync(session, $"{target.Name} NP {(amount >= 0 ? "+" : "")}{amount}");
            return;
        }
    }

    private async Task HandleKnightCashAsync(UserSession session, string arg)
    {
        var parts = arg.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var amount))
        {
            await SendNoticeAsync(session, "Usage: +kc <name> <amount>");
            return;
        }

        var target = sessionManager.GetByName(parts[0]);
        if (target != null)
        {
            target.KnightCash = (int)Math.Clamp((long)target.KnightCash + amount, 0L, int.MaxValue);
        }

        // Persist to DB so it survives logout regardless of next auto-save.
        using var scope = serviceProvider.CreateScope();
        var accountRepo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var characterRepo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

        Common.Domain.Entities.Account? account;
        if (target != null)
        {
            account = await accountRepo.GetById(target.AccountId);
        }
        else
        {
            var character = await characterRepo.GetByName(parts[0]);
            account = character != null ? await accountRepo.GetById(character.AccountId) : null;
        }

        if (account == null)
        {
            await SendNoticeAsync(session, $"Account not found for: {parts[0]}");
            return;
        }

        account.KnightCash = (int)Math.Clamp((long)account.KnightCash + amount, 0L, int.MaxValue);
        await accountRepo.UpdateAsync(account);

        await SendNoticeAsync(session, $"Account {account.Login} KC {(amount >= 0 ? "+" : "")}{amount} (now {account.KnightCash})");
        logger.LogInformation("GM {Gm} adjusted KC of {Login} by {Amount}", session.Name, account.Login, amount);
    }

    private async Task HandleBifrostStartAsync(UserSession session, string arg)
    {
        int? minutes = null;
        var trimmed = arg.Trim();
        if (trimmed.Length > 0)
        {
            if (!int.TryParse(trimmed, out var parsed) || parsed <= 0 || parsed > 720)
            {
                await SendNoticeAsync(session, "Usage: +bifroststart [minutes 1-720]");
                return;
            }
            minutes = parsed;
        }
        bifrostEventService.Start(minutes);
        await SendNoticeAsync(session, $"Bifrost event started ({minutes ?? 120} min monument phase)");
    }

    private async Task HandleBifrostCloseAsync(UserSession session)
    {
        bifrostEventService.Close();
        await SendNoticeAsync(session, "Bifrost event closed");
    }

    private async Task HandleSummonMonsterAsync(UserSession session, string arg)
    {
        var parts = arg.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out var npcId))
        {
            await SendNoticeAsync(session, "Usage: +mon <npcId> [count]");
            return;
        }

        var count = 1;
        if (parts.Length > 1 && int.TryParse(parts[1], out var c))
            count = Math.Clamp(c, 1, 20);

        var npcData = gameDataService.GetNpc(npcId);
        if (npcData == null)
        {
            await SendNoticeAsync(session, $"NPC ID {npcId} not found in K_NPC.");
            return;
        }

        var pos = new Common.Domain.Entities.GameData.NpcPosData
        {
            NpcId = npcId,
            ZoneId = session.ZoneId,
            LeftX = (int)session.X,
            TopZ = (int)session.Z,
            // Small radius so multiple summons spread out around the GM rather than stack.
            SpawnRange = 3,
            ActType = npcData.ActType,
            NumNPC = (byte)Math.Min(count, byte.MaxValue),
        };

        var lifecycle = serviceProvider.GetRequiredService<INpcLifecycleService>();
        for (int i = 0; i < count; i++)
        {
            var npc = NpcInstance.FromData(npcData, pos, 0);
            monsterAggressionPolicy.Apply(npc);
            npc.Y = sessionManager.Maps?.GetHeight(session.ZoneId, npc.X, npc.Z) ?? session.Y;
            npc.SpawnY = npc.Y;
            await lifecycle.SpawnAsync(npc);
        }

        await SendNoticeAsync(session, $"Spawned {count} × {npcData.Name} (id {npcId}) at your position.");
        logger.LogInformation("GM {Gm} summoned {Count} of NPC {NpcId} in zone {Zone}", session.Name, count, npcId, session.ZoneId);
    }

    private const ushort WeatherAmountMax = 100;
    private const ushort WeatherAmountDefault = 60;
    private const int SealCodeLength = 8;

    private async Task HandleTimeAsync(UserSession session, string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            await SendNoticeAsync(session,
                $"Game time is {timeWeather.MinuteOfDay / 60:00}:{timeWeather.MinuteOfDay % 60:00}. "
                + "Usage: +time <hh> | +time <hh:mm>");
            return;
        }

        if (!TimeWeatherBroadcastService.TryParseTimeOfDay(arg, out var hour, out var minute))
        {
            await SendNoticeAsync(session, "Usage: +time <hh> | +time <hh:mm>");
            return;
        }

        timeWeather.SetTimeOfDay(hour, minute);
        await sessionManager.BroadcastToAll(timeWeather.BuildCurrentTimePacket());
        await SendNoticeAsync(session, $"Game time set to {hour:00}:{minute:00} for everyone.");
        logger.LogInformation("GM {Gm} set the game time to {Hour:00}:{Minute:00}", session.Name, hour, minute);
    }

    private async Task HandleWeatherAsync(UserSession session, string arg)
    {
        var parts = (arg ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var type = parts.Length > 0 ? ParseWeather(parts[0]) : null;

        if (type == null)
        {
            await SendNoticeAsync(session, "Usage: +weather <clear|rain|snow|leaves> [0-100]");
            return;
        }

        var amount = parts.Length > 1 && ushort.TryParse(parts[1], out var parsed)
            ? Math.Min(parsed, WeatherAmountMax)
            : WeatherAmountDefault;

        timeWeather.TrySetWeather((byte)type.Value, amount);
        await timeWeather.BroadcastWeatherAsync();
        await SendNoticeAsync(session, $"Weather set to {parts[0].ToLowerInvariant()} at {amount} for everyone.");
        logger.LogInformation("GM {Gm} set the weather to {Weather} at {Amount}", session.Name, type, amount);
    }

    private async Task HandleSealCodeAsync(UserSession session, string arg)
    {
        var code = (arg ?? string.Empty).Trim();
        if (code.Length != SealCodeLength || !code.All(char.IsAsciiDigit))
        {
            await SendNoticeAsync(session, $"Usage: +sealcode <{SealCodeLength} digits>");
            return;
        }

        session.SealCode = code;

        using var scope = serviceProvider.CreateScope();
        var accountRepo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var account = await accountRepo.GetById(session.AccountId);
        if (account == null)
        {
            await SendNoticeAsync(session, "Seal code kept for this session only.");
            return;
        }

        account.SealCode = code;
        await accountRepo.UpdateAsync(account);
        await SendNoticeAsync(session, "Seal code set.");
    }

    private static WeatherType? ParseWeather(string name) => name.ToLowerInvariant() switch
    {
        "clear" or "fine" or "sunny" => WeatherType.Fine,
        "rain" => WeatherType.Rain,
        "snow" => WeatherType.Snow,
        "leaves" or "leaf" => WeatherType.Leaves,
        _ => null,
    };

    private async Task HandleEventRateAsync(UserSession session, string arg, string label, Action<byte> apply)
    {
        if (string.IsNullOrWhiteSpace(arg) || !byte.TryParse(arg.Trim(), out var pct))
        {
            await SendNoticeAsync(session, $"Usage: +{label.ToLower()}_add <0-255 percent>");
            return;
        }

        apply(pct);
        await SendNoticeAsync(session, $"{label} event bonus = {pct}%");
        logger.LogInformation("GM {Gm} set {Label} event bonus to {Pct}%", session.Name, label, pct);
    }

    private async Task HandleGotoAsync(UserSession session, string arg)
    {
        // +goto <name> — warp the GM to the named player.
        // +goto <zone> <x> <z> — warp the GM to coords in a zone.
        if (string.IsNullOrWhiteSpace(arg))
        {
            await SendNoticeAsync(session, "Usage: +goto <name> | +goto <zone> <x> <z>");
            return;
        }

        var parts = arg.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            var target = sessionManager.GetByName(parts[0]);
            if (target == null)
            {
                await SendNoticeAsync(session, $"Player not found: {parts[0]}");
                return;
            }

            if (session.ZoneId != target.ZoneId)
                await zoneTransitionService.ChangeZoneAsync(session, target.ZoneId, target.X, target.Z);
            else
                await worldPacketCoordinator.WarpAsync(session, (ushort)(target.X * 10), (ushort)(target.Z * 10));
            return;
        }

        if (parts.Length >= 3
            && byte.TryParse(parts[0], out var zone)
            && float.TryParse(parts[1], out var x)
            && float.TryParse(parts[2], out var z))
        {
            await zoneTransitionService.ChangeZoneAsync(session, zone, x, z);
            return;
        }

        await SendNoticeAsync(session, "Usage: +goto <name> | +goto <zone> <x> <z>");
    }

    private async Task HandleReseedAsync(UserSession session)
    {
        await SendNoticeAsync(session, "Reseeding game data from JSON...");
        try
        {
            using (var scope = serviceProvider.CreateScope())
            {
                var seedRunner = scope.ServiceProvider.GetRequiredService<GameDataSeedRunner>();
                await seedRunner.SeedAllAsync(force: true);
            }

            await gameDataService.ReloadAsync();
            await SendNoticeAsync(session, "Done: JSON seeded to DB and reloaded into memory.");
            logger.LogInformation("GM {Gm} reseeded JSON to DB and reloaded game data", session.Name);
        }
        catch (Exception ex)
        {
            await SendNoticeAsync(session, $"Reseed failed: {ex.Message}");
            logger.LogError(ex, "GM {Gm} reseed/reload failed", session.Name);
        }
    }

    private async Task HandleZoneAsync(UserSession session, string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            await SendNoticeAsync(session, "Available zones:");
            foreach (var zone in gameDataService.ZoneInfoTable.Values.OrderBy(z => z.ZoneNo))
            {
                var name = string.IsNullOrWhiteSpace(zone.MapName) ? zone.SmdName : zone.MapName;
                await SendNoticeAsync(session, $"  {zone.ZoneNo}: {name}");
            }
            return;
        }

        if (!short.TryParse(arg.Trim(), out var zoneId))
        {
            await SendNoticeAsync(session, "Usage: +zone | +zone <id>");
            return;
        }

        var startPos = gameDataService.GetStartPosition(zoneId);
        if (startPos == null)
        {
            await SendNoticeAsync(session, $"Zone {zoneId}: no start position configured.");
            return;
        }

        float x, z;
        if (session.Nation == AccountNation.Karus)
        {
            x = startPos.KarusX;
            z = startPos.KarusZ;
        }
        else
        {
            x = startPos.ElmoradX;
            z = startPos.ElmoradZ;
        }

        if (x == 0 && z == 0)
        {
            await SendNoticeAsync(session, $"Zone {zoneId}: no nation-side start coords; use +goto {zoneId} <x> <z>.");
            return;
        }

        await zoneTransitionService.ChangeZoneAsync(session, (byte)zoneId, x, z);
        logger.LogInformation("GM {Gm} teleported to zone {Zone} ({X:0.#},{Z:0.#})", session.Name, zoneId, x, z);
    }

    private async Task HandleQuestResetAsync(UserSession session, string arg)
    {
        if (!short.TryParse(arg?.Trim(), out var questId))
        {
            await SendNoticeAsync(session, "Usage: +questreset <id>");
            return;
        }

        var removed = session.WithLock(s =>
        {
            var had = s.Quest.QuestMap.Remove(questId);
            s.Quest.RemoveQuestKillCounts(questId);
            if (s.Quest.ActiveQuestId == questId)
                s.Quest.ActiveQuestId = 0;
            return had;
        });

        session.Quest.SyncActiveQuestKillCounts();
        await serviceProvider.GetRequiredService<ICharacterStatePersister>().SaveQuestStateAsync(session);

        await session.Client.SendPacket(QuestPacketWriter.StateChange(questId, QuestStatus.NotStarted));
        await session.Client.SendPacket(QuestPacketWriter.QuestList(session.Quest.QuestMap
            .Select(entry => new QuestPacketWriter.QuestEntry(entry.Key, (QuestStatus)entry.Value))
            .ToList()));

        await SendNoticeAsync(session, removed
            ? $"Quest {questId} reset. Talk to the giver again."
            : $"Quest {questId} was not on your record.");
        logger.LogInformation("GM {Gm} reset quest {QuestId}", session.Name, questId);
    }

    private async Task HandleQuestInfoAsync(UserSession session, string arg)
    {
        var objectives = serviceProvider.GetRequiredService<IQuestDefinitionSource>();
        if (string.IsNullOrWhiteSpace(arg))
        {
            var active = session.WithLock(s => s.Quest.QuestMap
                .Where(kv => kv.Value == 1)
                .Select(kv => kv.Key)
                .OrderBy(id => id)
                .ToArray());

            if (active.Length == 0)
            {
                await SendNoticeAsync(session, "No active quests. Use +questinfo <id> for a specific quest.");
                return;
            }

            await SendNoticeAsync(session, $"Active quests ({active.Length}):");
            foreach (var qid in active)
            {
                var label = objectives.ObjectivesFor(qid) is { Groups.Count: > 0 } ? "kill quest" : "task";
                await SendNoticeAsync(session, $"  {qid} ({label})");
            }
            await SendNoticeAsync(session, "Use +questinfo <id> for required monsters and zones.");
            return;
        }

        if (!short.TryParse(arg.Trim(), out var questId))
        {
            await SendNoticeAsync(session, "Usage: +questinfo | +questinfo <id>");
            return;
        }

        var declared = objectives.ObjectivesFor(questId);
        if (declared is not { Groups.Count: > 0 })
        {
            await SendNoticeAsync(session, $"Quest {questId}: its script declares no kill objectives.");
            return;
        }

        var groups = declared.Groups.Select(group => group.Monsters.Select(id => (short)id).ToArray()).ToArray();
        var requiredCounts = declared.Groups.Select(group => (short)group.Count).ToArray();
        var killCounts = session.WithLock(s => s.Quest.GetQuestKillCounts(questId));

        await SendNoticeAsync(session, $"Quest {questId}:");
        for (var i = 0; i < groups.Length && i < killCounts.Length; i++)
        {
            if (requiredCounts[i] <= 0)
                continue;

            await SendNoticeAsync(session, $"  Group {i + 1}: {killCounts[i]}/{requiredCounts[i]}");

            foreach (var nid in groups[i])
            {
                if (nid <= 0)
                    continue;
                var npc = gameDataService.GetNpc(nid);
                var name = npc?.Name ?? "?";
                var zones = gameDataService.NpcPositions
                    .Where(p => p.NpcId == nid)
                    .Select(p => (int)p.ZoneId)
                    .Distinct()
                    .OrderBy(z => z)
                    .ToArray();
                var zonePart = zones.Length > 0 ? $"zones [{string.Join(",", zones)}]" : "not spawned";
                await SendNoticeAsync(session, $"    Required #{nid} \"{name}\" → {zonePart}");

                var sameName = gameDataService.MonsterTable.Values
                    .Concat(gameDataService.NpcTable.Values)
                    .Where(n => n.Id != nid && string.Equals(n.Name, name, StringComparison.Ordinal))
                    .OrderBy(n => n.Id)
                    .ToArray();
                foreach (var other in sameName)
                {
                    var otherZones = gameDataService.NpcPositions
                        .Where(p => p.NpcId == other.Id)
                        .Select(p => (int)p.ZoneId)
                        .Distinct()
                        .OrderBy(z => z)
                        .ToArray();
                    if (otherZones.Length == 0)
                        continue;
                    await SendNoticeAsync(session, $"    Also accepted #{other.Id} \"{other.Name}\" → zones [{string.Join(",", otherZones)}]");
                }
            }
        }
    }

    private async Task HandleSummonUserAsync(UserSession session, string arg)
    {
        // Summon target to the GM (mirror of OPERATOR_SUMMON opcode 7 but as chat command).
        if (string.IsNullOrWhiteSpace(arg))
        {
            await SendNoticeAsync(session, "Usage: +summonuser <name>");
            return;
        }

        var target = sessionManager.GetByName(arg.Trim());
        if (target == null)
        {
            await SendNoticeAsync(session, $"Player not found: {arg}");
            return;
        }

        if (target.ZoneId != session.ZoneId)
            await zoneTransitionService.ChangeZoneAsync(target, session.ZoneId, session.X, session.Z);
        else
            await worldPacketCoordinator.WarpAsync(target, (ushort)(session.X * 10), (ushort)(session.Z * 10));

        await SendNoticeAsync(session, $"Summoned {target.Name}.");
        logger.LogInformation("GM {Gm} summoned {Target}", session.Name, target.Name);
    }

    private async Task HandlePrisonAsync(UserSession session, string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            await SendNoticeAsync(session, "Usage: +hapis <name>");
            return;
        }

        var target = sessionManager.GetByName(arg.Trim());
        if (target == null)
        {
            await SendNoticeAsync(session, $"Player not found: {arg}");
            return;
        }

        await zoneTransitionService.ChangeZoneAsync(target, PrisonZoneId, PrisonStartX, PrisonStartZ);
        await SendNoticeAsync(session, $"{target.Name} sent to prison.");
        logger.LogInformation("GM {Gm} sent {Target} to prison", session.Name, target.Name);
    }

    private async Task HandleBanAsync(UserSession session, string arg, bool ban)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            await SendNoticeAsync(session, ban ? "Usage: +ban <name>" : "Usage: +unban <login>");
            return;
        }

        var trimmed = arg.Trim();
        var target = sessionManager.GetByName(trimmed);

        using var scope = serviceProvider.CreateScope();
        var accountRepo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var characterRepo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

        Common.Domain.Entities.Account? account = null;

        if (target != null)
        {
            account = await accountRepo.GetById(target.AccountId);
        }
        else if (!ban)
        {
            // Unban accepts the account login since the player is offline.
            account = await accountRepo.GetByLogin(trimmed);
        }
        else
        {
            // Ban offline player by their character name.
            var character = await characterRepo.GetByName(trimmed);
            if (character != null)
                account = await accountRepo.GetById(character.AccountId);
        }

        if (account == null)
        {
            await SendNoticeAsync(session, $"Account not found for: {arg}");
            return;
        }

        account.Authority = ban ? AccountAuthority.Banned : AccountAuthority.Normal;
        await accountRepo.UpdateAsync(account);

        if (ban && target != null)
            await sessionTerminationService.LogoutAsync(target.Client);

        await SendNoticeAsync(session, $"Account {account.Login} {(ban ? "banned" : "unbanned")}.");
        logger.LogInformation("GM {Gm} {Action} account {Login}", session.Name, ban ? "banned" : "unbanned", account.Login);
    }

    private async Task HandleMuteAsync(UserSession session, string arg, bool mute)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            await SendNoticeAsync(session, mute ? "Usage: +mute <name>" : "Usage: +unmute <name>");
            return;
        }

        var trimmed = arg.Trim();
        var target = sessionManager.GetByName(trimmed);

        using var scope = serviceProvider.CreateScope();
        var characterRepo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

        var character = target != null
            ? await characterRepo.GetById(target.CharacterId)
            : await characterRepo.GetByName(trimmed);

        if (character == null)
        {
            await SendNoticeAsync(session, $"Character not found: {arg}");
            return;
        }

        character.IsMuted = mute;
        await characterRepo.UpdateAsync(character);

        if (target != null)
            target.IsMuted = mute;

        await SendNoticeAsync(session, $"{character.Name} is now {(mute ? "muted" : "unmuted")}.");
        logger.LogInformation("GM {Gm} {Action} {Target}", session.Name, mute ? "muted" : "unmuted", character.Name);
    }

    private async Task HandleMonsterSummonAsync(UserSession session, string arg)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out var npcId))
        {
            await SendNoticeAsync(session, "Usage: +monsummon <npcId> [count]");
            return;
        }

        var count = 1;
        if (parts.Length > 1 && int.TryParse(parts[1], out var requested))
            count = Math.Clamp(requested, 1, MaxGmSummonCount);

        var npcData = gameDataService.GetNpc(npcId);
        if (npcData == null)
        {
            await SendNoticeAsync(session, $"NPC {npcId} not found");
            return;
        }

        var spawned = await npcSummonService.SummonAsync(
            npcId, session.ZoneId, session.Room, (int)session.X, (int)session.Z, count, session.Y);
        await SendNoticeAsync(session, $"Summoned {spawned.Count} x {npcData.Name} ({npcId}); they will not respawn");
        logger.LogInformation("GM {Name} summoned {Count} of NPC {NpcId} in zone {Zone} room {Room} at {X},{Z}",
            session.Name, spawned.Count, npcId, session.ZoneId, session.Room, (int)session.X, (int)session.Z);
    }

    private async Task HandleGiveItemAsync(UserSession session, string arg)
    {
        // /give <itemId> [count]
        var giveParts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (giveParts.Length == 0 || !int.TryParse(giveParts[0], out var itemId))
        {
            await SendNoticeAsync(session, "Usage: /give <itemId> [count]");
            return;
        }

        var count = 1;
        if (giveParts.Length > 1 && int.TryParse(giveParts[1], out var c))
            count = Math.Clamp(c, 1, 9999);

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
        {
            await SendNoticeAsync(session, $"Item {itemId} not found");
            return;
        }

        var outcome = session.WithLock(s =>
        {
            var slotIndex = s.FindSlotForItem(itemId, gameDataService, (ushort)count);
            if (slotIndex < 0)
                return (Success: false, SlotIndex: 0, ItemId: 0, Count: (ushort)0, Durability: (short)0, IsNew: false);

            var slot = s.Inventory[slotIndex];
            var isNew = slot.IsEmpty;
            if (isNew)
            {
                slot.ItemId = itemId;
                slot.Durability = itemData.Duration;
                slot.Count = 0;
            }
            slot.Count = (ushort)Math.Min(9999, slot.Count + count);
            if (isNew)
                slot.Durability = itemData.Duration;

            s.RecalculateStatsWithBuffs(gameDataService);
            return (Success: true, SlotIndex: slotIndex, ItemId: slot.ItemId, Count: slot.Count, Durability: slot.Durability, IsNew: isNew);
        });

        if (!outcome.Success)
        {
            await SendNoticeAsync(session, "Inventory full");
            return;
        }

        await userNotificationService.SendStackChangeAsync(session, (byte)outcome.SlotIndex, outcome.ItemId, outcome.Count, outcome.Durability, outcome.IsNew);
        await userNotificationService.SendWeightChangeAsync(session);
        await SendNoticeAsync(session, $"Given {itemData.Name} x{count}");
    }

    private async Task HandleItemSearchAsync(UserSession session, string arg)
    {
        // /item <name> — search items by name, show top 10 results
        if (string.IsNullOrWhiteSpace(arg))
        {
            await SendNoticeAsync(session, "Usage: /item <name>");
            return;
        }

        var searchTerm = arg.ToLowerInvariant();
        var results = gameDataService.ItemTable.Values
            .Where(i => i.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Damage + i.Ac)
            .Take(10)
            .ToList();

        if (results.Count == 0)
        {
            await SendNoticeAsync(session, $"No items found matching '{arg}'");
            return;
        }

        foreach (var item in results)
            await SendNoticeAsync(session, $"[{item.Num}] {item.Name} (K{item.Kind} D{item.Damage} AC{item.Ac})");
    }

    private async Task WarpToPositionAsync(UserSession session, float x, float z)
    {
        await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.Out);
        session.X = x;
        session.Z = z;
        sessionManager.Regions.UpdateRegion(session);

        await session.Client.SendPacket(MovementPacketWriter.Warp(
            (ushort)session.GetPosX, (ushort)session.GetPosZ));
        await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.Warp);
    }

    private static async Task SendNoticeAsync(UserSession session, string message)
    {
        var packet = ChatPacketWriter.SystemNotice((byte)session.Nation, message);
        await session.Client.SendPacket(packet);
    }

    private async Task BroadcastNoticeAsync(string message)
    {
        await sessionManager.BroadcastToAll(NoticePacketWriter.Broadcast(message));
    }

    private const int DefaultJoinWindowSeconds = 30;

    private async Task HandleTempleEventCommandAsync(UserSession session, TempleEvent contest, ZoneId zoneId, string eventName, string arg)
    {
        if (arg is "0" or "now")
        {
            await zoneTransitionService.ChangeZoneAsync(session, (byte)zoneId, 0f, 0f);
            await SendNoticeAsync(session, $"[{eventName}] Teleported directly to event map!");
            return;
        }

        int joinSec = int.TryParse(arg, out var s) && s > 0 ? s : DefaultJoinWindowSeconds;
        await eventSchedulerService.CallTempleEventAsync(contest, joinSec, session);
        var confirmPkt = EventPacketWriter.TempleEvent((byte)TempleSubOpcode.TempleEventJoin, 1, (short)zoneId);
        await session.Client.SendPacket(confirmPkt);
        await SendNoticeAsync(session, $"[{eventName}] Registration open ({joinSec}s). You are registered and will teleport automatically!");
    }
}
