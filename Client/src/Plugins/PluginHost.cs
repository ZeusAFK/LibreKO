using System.Reflection;
using System.Runtime.Loader;
using Godot;

namespace LibreKO.Plugins;

public partial class PluginHost : Node
{
    public const string FolderName = "plugins";
    public const string UserFolder = "user://plugins";
    public const string ThemeArgPrefix = "theme=";

    public static PluginUi Ui { get; } = new();
    public static PluginGame Game { get; } = new();
    public static IReadOnlyList<PluginInfo> All => all;
    public static bool RestartRequired { get; private set; }
    public static bool CodePluginsSupported => OS.GetName() != "Android" && OS.GetName() != "iOS";
    public static PluginInfo? ActiveTheme => all.FirstOrDefault(p => p.IsLoaded && p.Type == PluginType.UiTheme);

    public static event Action? ThemeChanged;
    public static bool PersistChoices { get; set; } = true;

    private static readonly List<PluginInfo> all = new();
    private static readonly List<string> resolveDirs = new();
    private static readonly Dictionary<string, Assembly> assemblies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<(string Id, Assembly Assembly)> unloadedPendingSweep = new();
    private static bool resolverInstalled;
    private static bool loadedOnce;
    private static bool themeArgSpent;

    public override void _Ready()
    {
        Config.Load();
        if (loadedOnce) return;
        loadedOnce = true;
        Discover();
        LoadEnabled();
    }

    public override void _ExitTree()
    {
        foreach (var p in all)
            ShutdownInstance(p);
    }

    private static Assembly? ShutdownInstance(PluginInfo p)
    {
        var assembly = p.Instance?.GetType().Assembly;
        if (p.Instance != null)
        {
            try { p.Instance.Shutdown(); }
            catch (Exception ex) { GD.PushWarning($"[plugins] {p.Id} shutdown: {ex.Message}"); }
            p.Instance = null;
        }
        p.Context?.Assets.Release();
        p.Context = null;
        return assembly;
    }

    public static IEnumerable<string> Roots()
    {
        yield return Path.Combine(Config.InstallDir(), FolderName);
        yield return ProjectSettings.GlobalizePath(UserFolder);
        foreach (var d in Config.PluginDirs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return d;
    }

    public static string PrimaryRoot => Path.Combine(Config.InstallDir(), FolderName);

    private const string GodotIgnoreFile = ".gdignore";

    public static void Discover()
    {
        var running = all.Where(p => p.State is PluginState.Loaded or PluginState.Failed)
            .ToDictionary(p => Path.GetFullPath(p.Directory), StringComparer.OrdinalIgnoreCase);
        all.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots())
        {
            if (!Directory.Exists(root)) continue;
            if (OS.HasFeature("editor") && root == PrimaryRoot && !File.Exists(Path.Combine(root, GodotIgnoreFile)))
                File.WriteAllText(Path.Combine(root, GodotIgnoreFile), "");
            foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string manifestPath = Path.Combine(dir, PluginManifest.FileName);
                if (!File.Exists(manifestPath)) continue;
                var info = running.TryGetValue(Path.GetFullPath(dir), out var kept) ? kept : Inspect(dir, manifestPath);
                if (info.Manifest != null && !seen.Add(info.Manifest.Id))
                {
                    info.State = PluginState.Invalid;
                    info.Message = $"duplicate id \"{info.Manifest.Id}\" (another folder already provides it)";
                }
                all.Add(info);
            }
        }
        ApplyEnableStates();
        GD.Print($"[plugins] {all.Count} candidate(s) in {string.Join(", ", Roots().Where(Directory.Exists))}");
    }

    private static PluginInfo Inspect(string dir, string manifestPath)
    {
        PluginManifest manifest;
        try
        {
            manifest = PluginManifest.Parse(File.ReadAllText(manifestPath));
        }
        catch (PluginManifestException ex)
        {
            return new PluginInfo(dir, null, PluginState.Invalid, ex.Message);
        }
        catch (IOException ex)
        {
            return new PluginInfo(dir, null, PluginState.Invalid, ex.Message);
        }

        if (manifest.HasCode && !CodePluginsSupported)
            return new PluginInfo(dir, manifest, PluginState.Unsupported, "Code plugins are not supported on this platform");
        if (manifest.HasCode && !File.Exists(Path.Combine(dir, manifest.Assembly)))
            return new PluginInfo(dir, manifest, PluginState.Invalid, $"assembly not found: {manifest.Assembly}");
        if (manifest.MinClientVersion != null && System.Version.TryParse(Build.Version, out var client)
            && client < manifest.MinClientVersion)
            return new PluginInfo(dir, manifest, PluginState.Incompatible,
                $"Needs client {manifest.MinClientVersion} or newer (this is {Build.Version})");
        return new PluginInfo(dir, manifest, PluginState.Disabled);
    }

    public static string ThemeOverride() => themeArgSpent ? "" :
        OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(ThemeArgPrefix, StringComparison.Ordinal))?[ThemeArgPrefix.Length..] ?? "";

    private static void ApplyEnableStates()
    {
        var exclusiveTaken = new Dictionary<PluginType, string>();
        string themeOverride = ThemeOverride();
        foreach (var p in all)
        {
            p.Enabled = p.Manifest != null
                        && (themeOverride.Length > 0 && p.Manifest.Exclusive ? p.Id == themeOverride : Config.PluginEnabled(p.Id));
            if (!p.CanEnable || p.State == PluginState.Loaded || p.State == PluginState.Failed) continue;
            if (!p.Enabled)
            {
                p.State = PluginState.Disabled;
                continue;
            }
            if (p.Manifest!.Exclusive)
            {
                if (exclusiveTaken.TryGetValue(p.Type, out var other))
                {
                    p.State = PluginState.Conflict;
                    p.Message = $"\"{other}\" is already the enabled {PluginManifest.TypeName(p.Type)}";
                    p.Enabled = false;
                    continue;
                }
                exclusiveTaken[p.Type] = p.Id;
            }
            p.State = PluginState.Disabled;
            p.Message = "";
        }
    }

    private static void LoadEnabled()
    {
        foreach (var p in all)
            if (p.Enabled && p.State == PluginState.Disabled)
                Load(p);
    }

    private static void Load(PluginInfo p)
    {
        try
        {
            var pluginContext = new PluginContext(p, all, Ui.ForOwner(p.Id), Game);
            p.Context = pluginContext;
            if (p.Manifest!.HasCode)
            {
                p.Instance = Instantiate(p);
                DropPendingSweep(p.Instance.GetType().Assembly);
                p.Instance.Initialize(pluginContext);
            }
            p.State = PluginState.Loaded;
            p.Message = "";
            GD.Print($"[plugins] {p.Id} {p.Version} loaded ({PluginManifest.TypeName(p.Type)})");
        }
        catch (Exception ex)
        {
            p.State = PluginState.Failed;
            p.Message = ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message;
            ShutdownInstance(p);
            Ui.RemoveOwner(p.Id);
            GD.PushError($"[plugins] {p.Id} failed to load: {ex}");
        }
    }

    private static void Unload(PluginInfo p)
    {
        var assembly = ShutdownInstance(p);
        Ui.RemoveOwner(p.Id);
        if (assembly != null) unloadedPendingSweep.Add((p.Id, assembly));
        p.State = PluginState.Disabled;
        p.Message = "";
        GD.Print($"[plugins] {p.Id} unloaded");
    }

    private static void DropPendingSweep(Assembly assembly)
    {
        if (unloadedPendingSweep.RemoveAll(entry => entry.Assembly == assembly) > 0) Game.DropHandlersFrom(assembly);
    }

    public static void SweepUnloaded()
    {
        foreach (var (id, assembly) in unloadedPendingSweep)
        {
            int dropped = Game.DropHandlersFrom(assembly);
            if (dropped > 0) GD.PushWarning($"[plugins] {id} left {dropped} game handler(s) subscribed; removed");
        }
        unloadedPendingSweep.Clear();
    }

    public static void SwitchTheme(string? id)
    {
        var target = id == null ? null : all.FirstOrDefault(p => p.Type == PluginType.UiTheme && p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (id != null && (target == null || !target.CanEnable)) return;
        var current = ActiveTheme;
        if (current == target) return;
        themeArgSpent = true;
        if (current != null) Unload(current);
        foreach (var p in all)
            if (p.Manifest != null && p.Type == PluginType.UiTheme)
            {
                if (PersistChoices) Config.SetPluginEnabled(p.Id, p == target);
                else p.Enabled = p == target;
            }
        if (PersistChoices) ApplyEnableStates();
        if (target is { Enabled: true, State: PluginState.Disabled }) Load(target);
        ThemeChanged?.Invoke();
    }

    private static IPlugin Instantiate(PluginInfo info)
    {
        var manifest = info.Manifest!;
        string dll = Path.GetFullPath(Path.Combine(info.Directory, manifest.Assembly));
        if (!assemblies.TryGetValue(dll, out var asm))
        {
            var alc = AssemblyLoadContext.GetLoadContext(typeof(PluginHost).Assembly) ?? AssemblyLoadContext.Default;
            InstallResolver(alc, Path.GetDirectoryName(dll)!);
            using (var dllStream = File.OpenRead(dll))
            {
                string pdb = Path.ChangeExtension(dll, ".pdb");
                if (File.Exists(pdb))
                {
                    using var pdbStream = File.OpenRead(pdb);
                    asm = alc.LoadFromStream(dllStream, pdbStream);
                }
                else
                {
                    asm = alc.LoadFromStream(dllStream);
                }
            }
            RegisterGodotScripts(asm);
            assemblies[dll] = asm;
        }

        Type? type = null;
        if (manifest.Entry.Length > 0)
        {
            type = asm.GetType(manifest.Entry, throwOnError: false);
            if (type == null)
                throw new InvalidOperationException($"entry type \"{manifest.Entry}\" not found in {manifest.Assembly}");
        }
        else
        {
            var candidates = asm.GetTypes().Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract).ToList();
            if (candidates.Count != 1)
                throw new InvalidOperationException(
                    $"{manifest.Assembly} has {candidates.Count} IPlugin types; set \"entry\" in {PluginManifest.FileName}");
            type = candidates[0];
        }
        if (!typeof(IPlugin).IsAssignableFrom(type))
            throw new InvalidOperationException($"{type.FullName} does not implement IPlugin");
        return (IPlugin)(Activator.CreateInstance(type)
                         ?? throw new InvalidOperationException($"could not construct {type.FullName}"));
    }

    private static void InstallResolver(AssemblyLoadContext alc, string dir)
    {
        if (!resolveDirs.Contains(dir, StringComparer.OrdinalIgnoreCase)) resolveDirs.Add(dir);
        if (resolverInstalled) return;
        resolverInstalled = true;
        alc.Resolving += (ctx, name) =>
        {
            foreach (var d in resolveDirs)
            {
                string candidate = Path.Combine(d, name.Name + ".dll");
                if (File.Exists(candidate)) return ctx.LoadFromAssemblyPath(candidate);
            }
            return null;
        };
    }

    private static void RegisterGodotScripts(Assembly asm)
    {
        var bridge = typeof(GodotObject).Assembly.GetType("Godot.Bridge.ScriptManagerBridge");
        var lookup = bridge?.GetMethod("LookupScriptsInAssembly",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Assembly) }, null);
        try { lookup?.Invoke(null, new object[] { asm }); }
        catch (Exception ex) { GD.PushWarning($"[plugins] script registration for {asm.GetName().Name}: {ex.Message}"); }
    }

    public static void SetEnabled(string id, bool enabled)
    {
        var target = all.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (target?.Manifest == null) return;
        if (target.Type == PluginType.UiTheme && CodePluginsSupported)
        {
            SwitchTheme(enabled ? target.Id : null);
            return;
        }
        if (enabled && target.Manifest.Exclusive)
            foreach (var other in all)
                if (other != target && other.Manifest != null && other.Type == target.Type && Config.PluginEnabled(other.Id))
                    Config.SetPluginEnabled(other.Id, false);
        Config.SetPluginEnabled(id, enabled);
        RestartRequired = true;
        ApplyEnableStates();
    }

    public static void OpenFolder()
    {
        string root = PrimaryRoot;
        Directory.CreateDirectory(root);
        OS.ShellOpen(root);
    }
}
