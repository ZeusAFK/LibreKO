using System.Reflection;
using System.Runtime.Loader;
using Godot;

namespace LibreKO.Plugins;

public partial class PluginHost : Node
{
    public const string FolderName = "plugins";
    public const string UserFolder = "user://plugins";

    public static PluginUi Ui { get; } = new();
    public static PluginGame Game { get; } = new();
    public static IReadOnlyList<PluginInfo> All => _all;
    public static bool RestartRequired { get; private set; }
    public static bool CodePluginsSupported => OS.GetName() != "Android" && OS.GetName() != "iOS";

    private static readonly List<PluginInfo> _all = new();
    private static readonly List<string> _resolveDirs = new();
    private static bool _resolverInstalled;
    private static bool _loadedOnce;

    public override void _Ready()
    {
        Config.Load();
        if (_loadedOnce) return;
        _loadedOnce = true;
        Discover();
        LoadEnabled();
    }

    public override void _ExitTree()
    {
        foreach (var p in _all)
        {
            if (p.Instance == null) continue;
            try { p.Instance.Shutdown(); }
            catch (Exception ex) { GD.PushWarning($"[plugins] {p.Id} shutdown: {ex.Message}"); }
            p.Instance = null;
        }
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
        _all.Clear();
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
                var info = Inspect(dir, manifestPath);
                if (info.Manifest != null && !seen.Add(info.Manifest.Id))
                {
                    info.State = PluginState.Invalid;
                    info.Message = $"duplicate id \"{info.Manifest.Id}\" (another folder already provides it)";
                }
                _all.Add(info);
            }
        }
        ApplyEnableStates();
        GD.Print($"[plugins] {_all.Count} candidate(s) in {string.Join(", ", Roots().Where(Directory.Exists))}");
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

    private static void ApplyEnableStates()
    {
        var exclusiveTaken = new Dictionary<PluginType, string>();
        foreach (var p in _all)
        {
            p.Enabled = p.Manifest != null && Config.PluginEnabled(p.Id);
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
        foreach (var p in _all)
        {
            if (!p.Enabled || p.State != PluginState.Disabled) continue;
            try
            {
                var ctx = new PluginContext(p, _all, Ui, Game);
                if (p.Manifest!.HasCode)
                {
                    p.Instance = Instantiate(p);
                    p.Instance.Initialize(ctx);
                }
                p.State = PluginState.Loaded;
                p.Message = "";
                GD.Print($"[plugins] {p.Id} {p.Version} loaded ({PluginManifest.TypeName(p.Type)})");
            }
            catch (Exception ex)
            {
                p.State = PluginState.Failed;
                p.Message = ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message;
                p.Instance = null;
                GD.PushError($"[plugins] {p.Id} failed to load: {ex}");
            }
        }
    }

    private static IPlugin Instantiate(PluginInfo info)
    {
        var manifest = info.Manifest!;
        string dll = Path.GetFullPath(Path.Combine(info.Directory, manifest.Assembly));
        var alc = AssemblyLoadContext.GetLoadContext(typeof(PluginHost).Assembly) ?? AssemblyLoadContext.Default;
        InstallResolver(alc, Path.GetDirectoryName(dll)!);

        Assembly asm;
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
        if (!_resolveDirs.Contains(dir, StringComparer.OrdinalIgnoreCase)) _resolveDirs.Add(dir);
        if (_resolverInstalled) return;
        _resolverInstalled = true;
        alc.Resolving += (ctx, name) =>
        {
            foreach (var d in _resolveDirs)
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
        var target = _all.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (target?.Manifest == null) return;
        if (enabled && target.Manifest.Exclusive)
            foreach (var other in _all)
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
