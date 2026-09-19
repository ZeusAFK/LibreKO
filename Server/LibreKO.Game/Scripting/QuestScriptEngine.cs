using System.Collections.Concurrent;
using LibreKO.Common.Domain.Services;
using System.Text.Json;
using LibreKO.Game.Configuration;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Catalog;
using LibreKO.Quests.Localization;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.Scripting;

public sealed partial class QuestScriptEngine : IQuestDefinitionSource
{
    public const string Extension = ".quest";

    private readonly ConcurrentDictionary<string, CachedProgram?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _scriptDirectories;
    private readonly IGameDataService _gameData;
    private readonly SessionManager _sessionManager;
    private readonly IScriptEffectApplier _effects;
    private readonly IQuestTranslations _translations;
    private readonly ILogger<QuestScriptEngine> _logger;
    private readonly GameServerSettings _settings;
    private readonly TimeProvider _clock;
    private DateTimeOffset _nextFileCheck;

    private sealed record CachedProgram(QuestProgram Program, DateTime WrittenAt, string Path);

    private readonly Lock _indexGate = new();
    private Dictionary<(int Npc, int Zone), List<string>>? _npcIndex;
    private readonly Dictionary<string, CachedProgram> _composites = new(StringComparer.OrdinalIgnoreCase);
    private string[]? _files;
    private FileStamp[]? _fileStamps;

    private sealed record FileStamp(string Path, DateTime WrittenAt, long Length);

    public QuestScriptEngine(
        IGameDataService gameData,
        SessionManager sessionManager,
        IScriptEffectApplier effects,
        IQuestTranslations translations,
        IHostEnvironment hostEnvironment,
        IOptions<GameServerSettings> settings,
        ILogger<QuestScriptEngine> logger,
        TimeProvider? clock = null)
    {
        _gameData = gameData;
        _sessionManager = sessionManager;
        _effects = effects;
        _translations = translations;
        _logger = logger;
        _settings = settings.Value;
        _clock = clock ?? TimeProvider.System;
        _scriptDirectories = GameAssetPathResolver.GetCandidateDirectories(
            _settings.QuestsDirectory, hostEnvironment.ContentRootPath, "Quests");
    }

    public bool Handles(string scriptName) => Resolve(scriptName) is not null;

    private Dictionary<int, QuestObjectives>? _objectiveIndex;
    private Dictionary<(int Quest, int Nation, int ClassGroup), QuestText>? _textIndex;
    private Dictionary<int, QuestProgram>? _flowIndex;
    private Dictionary<int, List<(string File, int Event)>>? _abandonIndex;

    public void ClearCache()
    {
        _cache.Clear();
        lock (_indexGate)
        {
            _npcIndex = null;
            _composites.Clear();
            _objectiveIndex = null;
            _textIndex = null;
            _flowIndex = null;
            _abandonIndex = null;
            _files = null;
            _fileStamps = null;
        }
    }

    public QuestObjectives? ObjectivesFor(int questId) =>
        ObjectiveIndex().GetValueOrDefault(questId);

    public ObjectiveRule ObjectiveRuleFor(int questId) =>
        ObjectivesFor(questId)?.Rule ?? ObjectiveRule.All;

    public QuestText? TextFor(int questId, int nation = 0, int classGroup = 0)
    {
        var index = TextIndex();
        if (nation != 0 && classGroup != 0 && index.TryGetValue((questId, nation, classGroup), out var both))
            return both;
        if (classGroup != 0 && index.TryGetValue((questId, 0, classGroup), out var classed))
            return classed;
        if (nation != 0 && index.TryGetValue((questId, nation, 0), out var national))
            return national;
        if (index.TryGetValue((questId, 0, 0), out var shared))
            return shared;
        foreach (var entry in index)
            if (entry.Key.Quest == questId)
                return entry.Value;
        return null;
    }

    public bool TryGetAbandonEntry(int questId, out string? scriptName, out int eventId)
    {
        lock (_indexGate)
        {
            RefreshFiles();
            if (_abandonIndex is null)
            {
                _abandonIndex = [];
                foreach (var file in _files!)
                {
                    var cached = ResolveFile(file);
                    if (cached is null)
                        continue;
                    foreach (var (name, id) in cached.Program.EventNames)
                    {
                        if (!name.StartsWith(QuestProgram.AbandonEvent + "_", StringComparison.OrdinalIgnoreCase)
                            || !int.TryParse(name.AsSpan(QuestProgram.AbandonEvent.Length + 1), out var quest))
                            continue;
                        if (!_abandonIndex.TryGetValue(quest, out var entries))
                            _abandonIndex[quest] = entries = [];
                        entries.Add((file, id));
                    }
                }
            }

            scriptName = null;
            eventId = 0;
            if (!_abandonIndex.TryGetValue(questId, out var matches))
                return false;
            if (matches.Count == 1)
                (scriptName, eventId) = (ScriptName(matches[0].File), matches[0].Event);
            else
                _logger.LogWarning("Quest {QuestId} has multiple abandon handlers; abandonment was refused", questId);
            return true;
        }
    }

    private Dictionary<int, QuestObjectives> ObjectiveIndex()
    {
        lock (_indexGate)
        {
            RefreshFiles();
            if (_objectiveIndex is not null)
                return _objectiveIndex;

            var index = new Dictionary<int, QuestObjectives>();
            foreach (var file in _files!)
            {
                var cached = ResolveFile(file);
                foreach (var objectives in cached?.Program.Objectives ?? [])
                {
                    if (index.TryAdd(objectives.QuestId, objectives))
                        continue;
                    if (Agree(index[objectives.QuestId], objectives))
                        continue;
                    _logger.LogWarning(
                        "Quest {QuestId} lists different objectives in more than one script; keeping the first",
                        objectives.QuestId);
                }
            }

            return _objectiveIndex = index;
        }
    }

    private static bool Agree(QuestObjectives kept, QuestObjectives found) =>
        kept.Rule == found.Rule
        && kept.Groups.Count == found.Groups.Count
        && kept.Groups.Zip(found.Groups).All(pair =>
            pair.First.Count == pair.Second.Count
            && pair.First.Monsters.SequenceEqual(pair.Second.Monsters));

    private Dictionary<(int Quest, int Nation, int ClassGroup), QuestText> TextIndex()
    {
        lock (_indexGate)
        {
            RefreshFiles();
            if (_textIndex is not null)
                return _textIndex;

            var index = new Dictionary<(int Quest, int Nation, int ClassGroup), QuestText>();
            foreach (var file in _files!)
            {
                var cached = ResolveFile(file);
                foreach (var text in cached?.Program.Texts ?? [])
                {
                    var key = (text.QuestId, text.Nation, text.ClassGroup);
                    if (index.TryAdd(key, text))
                        continue;
                    if (index[key] == text)
                        continue;
                    _logger.LogWarning(
                        "Quest {QuestId} is named differently in {Kept} and {Ignored}; keeping the first",
                        text.QuestId, index[key].Title, text.Title);
                }
            }

            return _textIndex = index;
        }
    }

    private Dictionary<int, QuestProgram> FlowIndex()
    {
        lock (_indexGate)
        {
            RefreshFiles();
            if (_flowIndex is not null)
                return _flowIndex;
            var index = new Dictionary<int, QuestProgram>();
            foreach (var file in _files!)
            {
                var program = ResolveFile(file)?.Program;
                foreach (var flow in program?.Flows ?? [])
                    index.TryAdd(flow.QuestId, program!);
            }
            return _flowIndex = index;
        }
    }

    public bool IsAutoAccepted(int questId) => FlowIndex().TryGetValue(questId, out var program)
        && program.Flows.Any(f => f.QuestId == questId && f.AutoAccept);

    public bool HasAutomaticFlow(int questId) => FlowIndex().ContainsKey(questId);

    public async Task SendViewsAsync(UserSession session, int? questId = null, bool changesOnly = false)
    {
        await session.Quest.ViewRefresh.WaitAsync();
        try
        {
            foreach (var (id, program) in FlowIndex())
            {
                if (questId is { } requested && id != requested)
                    continue;
                var context = new QuestScriptContext(session, null, _gameData, _sessionManager, _logger,
                    Math.Max(1, _settings.Global.ExpMultiplier));
                var host = new QuestScriptHost(session, context, _translations, _logger, program.FileName, this, _clock,
                    monster => _gameData.GetNpc(monster)?.Name ?? "Creature");
                session.WithLock(_ =>
                {
                    var interpreter = new QuestInterpreter(program, host);
                    var view = interpreter.BuildView(id);
                    var flow = program.Flows.Single(f => f.QuestId == id);
                    if (flow.AutoAccept && view.State == QuestViewState.Available && session.Hp > 0 && flow.Matches((int)session.Nation, session.ZoneId, ClassIdHelper.GroupOf(session.Class)))
                    {
                        session.Quest.StartedNotifications.Remove(id);
                        host.SetQuestState(id, 1);
                        view = interpreter.BuildView(id);
                    }
                    if (flow.AutoAccept && view.State == QuestViewState.InProgress)
                        Notify(session, context, program, id, "started", true, session.Quest.StartedNotifications);
                    if (flow.AutoComplete && view.State == QuestViewState.Claimable && session.Hp > 0
                        && flow.Matches((int)session.Nation, session.ZoneId, ClassIdHelper.GroupOf(session.Class)))
                    {
                        Notify(session, context, program, id, QuestProgram.ReadyEvent, true, session.Quest.ReadyNotifications);
                        if (program.TryGetEntry(QuestProgram.FulfilEvent, id, out var fulfil))
                            new QuestInterpreter(program, host).Run(fulfil);
                        view = interpreter.BuildView(id);
                    }
                    var version = new QuestState.ViewVersion(program, (byte)view.State, session.ZoneId, session.LanguageCode,
                        DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).DayNumber, string.Join(',', view.Counts));
                    if (!changesOnly || !session.Quest.ViewVersions.TryGetValue(id, out var previous) || previous != version)
                    {
                        session.Quest.ViewVersions[id] = version;
                        host.SendQuestView(view, flow.BindingFor((int)session.Nation, session.ZoneId, ClassIdHelper.GroupOf(session.Class))?.NpcId ?? program.NpcId, false);
                    }
                    Notify(session, context, program, id, "available", view.State == QuestViewState.Available,
                        session.Quest.AvailableNotifications);
                    Notify(session, context, program, id, QuestProgram.ReadyEvent,
                        view.State == QuestViewState.Claimable, session.Quest.ReadyNotifications);
                });
                await _effects.ApplyAsync(session, context, program.FileName);
            }
        }
        finally
        {
            session.Quest.ViewRefresh.Release();
        }
    }

    private void Notify(UserSession session, QuestScriptContext context, QuestProgram program, int questId,
        string role, bool eligible, HashSet<int> seen)
    {
        var fires = eligible && session.Hp > 0
            && program.Flows.Single(f => f.QuestId == questId).Matches((int)session.Nation, session.ZoneId);
        if (!fires)
        {
            if (seen.Remove(questId))
                session.Quest.NotificationReplies.Remove(questId);
            return;
        }
        if (!seen.Add(questId) || !program.TryGetEntry(role, questId, out var entry))
            return;
        var host = new QuestScriptHost(session, context, _translations, _logger, program.FileName,
            this, _clock, monster => _gameData.GetNpc(monster)?.Name ?? "Creature", program);
        new QuestInterpreter(program, host).Run(entry);
    }

    public async Task ShowObjectiveTargetAsync(UserSession session, int questId, int group)
    {
        if (!FlowIndex().TryGetValue(questId, out var program)
            || program.Objectives.FirstOrDefault(o => o.QuestId == questId) is not { } goals
            || group < 0 || group >= goals.Groups.Count
            || !program.TryGetLocation(goals.Groups[group].Target, out var location))
            return;
        var context = new QuestScriptContext(session, null, _gameData, _sessionManager, _logger,
            Math.Max(1, _settings.Global.ExpMultiplier));
        new QuestScriptHost(session, context, _translations, _logger, program.FileName, this, _clock,
            monster => _gameData.GetNpc(monster)?.Name ?? "Creature").ShowLocation(location, questId);
        await _effects.ApplyAsync(session, context, program.FileName);
    }

    public async Task ReplyToNotificationAsync(UserSession session, int questId, int choice)
    {
        if (!FlowIndex().TryGetValue(questId, out var program))
            return;
        var context = new QuestScriptContext(session, null, _gameData, _sessionManager, _logger,
            Math.Max(1, _settings.Global.ExpMultiplier));
        var host = new QuestScriptHost(session, context, _translations, _logger, program.FileName, this, _clock,
            monster => _gameData.GetNpc(monster)?.Name ?? "Creature", program);
        session.WithLock(_ =>
        {
            if (!session.Quest.NotificationReplies.Remove(questId, out var reply)
                || !ReferenceEquals(reply.Program, program) || choice < 0 || choice >= reply.Events.Length
                || session.Hp <= 0 || !program.Flows.Single(f => f.QuestId == questId).Matches((int)session.Nation, session.ZoneId, ClassIdHelper.GroupOf(session.Class)))
                return;
            var interpreter = new QuestInterpreter(program, host);
            if (interpreter.BuildView(questId).State == reply.State)
                interpreter.Run(reply.Events[choice]);
        });
        await _effects.ApplyAsync(session, context, program.FileName);
    }

    public bool TryGetGreeting(int npcId, int zoneId, out string scriptName, out int eventId) =>
        TryGetEntry(npcId, zoneId, QuestProgram.GreetingEvent, 0, out scriptName, out eventId);

    public bool TryGetEntry(
        int npcId,
        int zoneId,
        string role,
        int questId,
        out string scriptName,
        out int eventId)
    {
        scriptName = string.Empty;
        eventId = 0;

        lock (_indexGate)
        {
            var index = NpcIndex();
            var files = index.GetValueOrDefault((npcId, zoneId), []).Concat(
                zoneId == 0 ? [] : index.GetValueOrDefault((npcId, 0), [])).Distinct().ToArray();
            if (files.Length == 0)
                return false;
            var programs = files.Select(ResolveFile).OfType<CachedProgram>().ToArray();
            if (programs.Length == 0)
                return false;
            CachedProgram cached;
            if (programs.Length == 1 && !programs[0].Program.HasBinding)
            {
                cached = programs[0];
                scriptName = ScriptName(cached.Path);
            }
            else
            {
                scriptName = $"@npc_{npcId}_{zoneId}.quest";
                if (!_composites.TryGetValue(scriptName, out cached!))
                {
                    var quests = new HashSet<int>();
                    var chosen = programs.Where(p => p.Program.DefaultQuestId <= 0
                        || quests.Add(p.Program.DefaultQuestId)).Select(p => p.Program).ToArray();
                    cached = new CachedProgram(QuestProgramComposer.Compose(scriptName, npcId, zoneId, chosen),
                        DateTime.MinValue, scriptName);
                    _composites[scriptName] = cached;
                }
            }
            return cached.Program.TryGetEntry(role, questId, out eventId);
        }
    }

    private Dictionary<(int Npc, int Zone), List<string>> NpcIndex()
    {
        lock (_indexGate)
        {
            RefreshFiles();
            if (_npcIndex is not null)
                return _npcIndex;

            var index = new Dictionary<(int Npc, int Zone), List<string>>();
            foreach (var file in _files!)
            {
                var cached = ResolveFile(file);
                if (cached is null)
                    continue;
                var claimed = cached.Program.Bindings.Count > 0
                    ? cached.Program.Bindings.Select(b => (b.NpcId, b.ZoneId)).ToArray()
                    : (cached.Program.NpcIds.Count > 0 ? cached.Program.NpcIds : [cached.Program.NpcId])
                        .Select(npc => (npc, cached.Program.ZoneId)).ToArray();
                foreach (var (npc, zone) in claimed)
                {
                    if (npc == 0 && zone == 0)
                        continue;
                    (int Npc, int Zone) key = (npc, zone);
                    if (!index.TryGetValue(key, out var files))
                        index[key] = files = [];
                    if (!files.Contains(file))
                        files.Add(file);
                }
            }

            return _npcIndex = index;
        }
    }

    private IEnumerable<string> ScriptFiles()
    {
        foreach (var directory in _scriptDirectories)
        {
            if (!Directory.Exists(directory))
                continue;
            if (_settings.QuestManifest is { Length: > 0 } manifestName)
            {
                var manifest = Path.Combine(directory, manifestName);
                string[] names;
                try
                {
                    names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest)) ?? [];
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    _logger.LogWarning(exception, "Quest manifest could not be read at {Path}; no scripts will load from this directory", manifest);
                    names = [];
                }
                foreach (var name in names)
                    if (!string.IsNullOrEmpty(name) && Path.GetFileName(name) == name
                        && name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                        yield return Path.GetFullPath(Path.Combine(directory, name));
                yield break;
            }
            foreach (var file in Directory.EnumerateFiles(
                         directory, "*" + Extension, SearchOption.AllDirectories)
                         .Order(StringComparer.OrdinalIgnoreCase))
                yield return Path.GetFullPath(file);
        }
    }

    private void RefreshFiles()
    {
        var now = _clock.GetUtcNow();
        if (_files is not null && (!_settings.Global.HotReloadQuestScripts || now < _nextFileCheck))
            return;
        _nextFileCheck = now.AddSeconds(1);

        var stamps = ScriptFiles().Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .Select(file => new FileStamp(file.FullName, file.LastWriteTimeUtc, file.Length))
            .ToArray();
        if (_fileStamps is not null && _fileStamps.SequenceEqual(stamps))
            return;

        _fileStamps = stamps;
        _files = stamps.Select(stamp => stamp.Path)
            .DistinctBy(ScriptName, StringComparer.OrdinalIgnoreCase).ToArray();
        _cache.Clear();
        _npcIndex = null;
        _composites.Clear();
        _textIndex = null;
        _flowIndex = null;
        _objectiveIndex = null;
        _abandonIndex = null;
    }

    private string ScriptName(string path) => Path.GetRelativePath(
        _scriptDirectories.First(directory => Path.GetFullPath(path).StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)), path);

    public async Task<bool> ExecuteAsync(
        UserSession session,
        NpcInstance? npc,
        int eventId,
        sbyte selectedReward,
        string scriptName)
    {
        var cached = Resolve(scriptName);
        if (cached is null)
            return false;

        var talkingTo = npc?.NpcId ?? session.Quest.EventNpcId;
        if (cached.Program.HasBinding
            && (cached.Program.ZoneId > 0 && cached.Program.ZoneId != session.ZoneId
                || cached.Program.NpcId > 0 && talkingTo > 0 && cached.Program.NpcId != talkingTo))
            return false;

        if (!cached.Program.TryGetEvent(eventId, out _))
            return false;

        session.Quest.ActiveQuestScript = cached.Path.StartsWith("@") ? cached.Path : ScriptName(cached.Path);

        var context = new QuestScriptContext(
            session, npc, _gameData, _sessionManager, _logger,
            Math.Max(1, _settings.Global.ExpMultiplier));

        var host = new QuestScriptHost(session, context, _translations, _logger, cached.Program.FileName, this, _clock,
            monster => _gameData.GetNpc(monster)?.Name ?? "Creature");
        var result = cached.Program.Flows.Count > 0
            ? session.WithLock(_ => new QuestInterpreter(cached.Program, host, selectedReward).Run(eventId))
            : new QuestInterpreter(cached.Program, host, selectedReward).Run(eventId);

        if (!result.Handled)
        {
            _logger.LogDebug("Quest script {File} does not handle event {Event}", cached.Program.FileName, eventId);
            return false;
        }

        if (result.Failure is { } failure)
            _logger.LogWarning("Quest script {File} stopped: {Failure} (chain {Chain})",
                cached.Program.FileName, failure, string.Join(" -> ", result.EventChain));

        await _effects.ApplyAsync(session, context, cached.Program.FileName);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug(
                "Quest script {File} ran event {Event} for {Name}: {Statements} statement(s), {Packets} packet(s)",
                cached.Program.FileName, eventId, session.Name, result.StatementsExecuted, context.QueuedPackets.Count);

        return true;
    }

    private CachedProgram? Resolve(string scriptName)
    {
        lock (_indexGate)
        {
            var key = QuestScriptName(scriptName);
            if (key is null)
                return null;
            RefreshFiles();
            if (_composites.TryGetValue(key, out var composed))
                return composed;
            if (TryReadCompositeKey(key, out var compositeNpc, out var compositeZone))
            {
                TryGetEntry(compositeNpc, compositeZone, QuestProgram.GreetingEvent, 0, out _, out _);
                if (_composites.TryGetValue(key, out composed))
                    return composed;
            }
            var path = _files!.FirstOrDefault(file =>
                string.Equals(file, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ScriptName(file), key, StringComparison.OrdinalIgnoreCase));
            if (path is null && Path.GetFileName(key) == key)
                path = _files!.FirstOrDefault(file =>
                    string.Equals(Path.GetFileName(file), key, StringComparison.OrdinalIgnoreCase));
            return path is null ? null : ResolveFile(path);
        }
    }

    private CachedProgram? ResolveFile(string path)
    {
        if (_cache.TryGetValue(path, out var cached))
            return cached;
        var compiled = Compile(path);
        _cache[path] = compiled;
        return compiled;
    }

    private CachedProgram? Compile(string path)
    {
        var fileName = ScriptName(path);
        try
        {
            var compilation = QuestCompilation.CreateFromFile(path, BuildCatalog());
            foreach (var diagnostic in compilation.Diagnostics)
            {
                var level = diagnostic.Severity == DiagnosticSeverity.Error ? LogLevel.Error : LogLevel.Warning;
                _logger.Log(level, "{Diagnostic}", DiagnosticFormatter.Render(diagnostic, compilation.Source).TrimEnd());
            }

            if (!compilation.Succeeded)
            {
                _logger.LogError("Quest script {File} has errors and will not be used", fileName);
                return null;
            }

            _logger.LogInformation("Compiled quest script {File}: {Events} event(s) for NPC {Npc}",
                fileName, compilation.Program.Events.Count, compilation.Program.NpcId);
            return new CachedProgram(compilation.Program, LastWriteTime(path), path);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Could not read quest script {File}", fileName);
            return null;
        }
    }

    private IQuestCatalog BuildCatalog() => new GameDataQuestCatalog(_gameData);

    private static DateTime LastWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
    }

    private static bool TryReadCompositeKey(string key, out int npcId, out int zoneId)
    {
        npcId = zoneId = 0;
        if (!key.StartsWith("@npc_", StringComparison.Ordinal)
            || !key.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            return false;
        var parts = key[5..^Extension.Length].Split('_');
        return parts.Length == 2
            && int.TryParse(parts[0], out npcId)
            && int.TryParse(parts[1], out zoneId);
    }

    private static string? QuestScriptName(string scriptName)
    {
        scriptName = scriptName.Trim();
        if (scriptName.Length == 0)
            return null;
        if (scriptName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            return scriptName;
        return Path.ChangeExtension(scriptName, Extension);
    }
}
