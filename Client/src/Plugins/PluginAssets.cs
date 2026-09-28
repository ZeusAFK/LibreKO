using System.Text.Json;
using Godot;

namespace LibreKO.Plugins;

public sealed class PluginAssets
{
    private readonly string _root;
    private readonly PluginLog _log;
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.OrdinalIgnoreCase);

    internal PluginAssets(string root, PluginLog log)
    {
        _root = Path.GetFullPath(root);
        _log = log;
    }

    public string Root => _root;

    public string PathOf(string relative)
    {
        string full = Path.GetFullPath(Path.Combine(_root, relative.Replace('\\', '/')));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{relative}' leaves the plugin folder");
        return full;
    }

    public bool Exists(string relative) => File.Exists(PathOf(relative));

    public string? Text(string relative)
    {
        string p = PathOf(relative);
        return File.Exists(p) ? File.ReadAllText(p) : null;
    }

    public JsonDocument? Json(string relative)
    {
        string p = PathOf(relative);
        if (!File.Exists(p)) return null;
        try
        {
            return JsonDocument.Parse(File.ReadAllBytes(p), new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            _log.Error($"{relative}: {ex.Message}");
            return null;
        }
    }

    public Texture2D? Texture(string relative)
    {
        if (_textures.TryGetValue(relative, out var cached)) return cached;
        var tex = LoadTexture(relative);
        _textures[relative] = tex;
        return tex;
    }

    private Texture2D? LoadTexture(string relative)
    {
        string p = PathOf(relative);
        if (!File.Exists(p))
        {
            _log.Warn($"missing texture {relative}");
            return null;
        }
        var img = new Image();
        byte[] bytes = File.ReadAllBytes(p);
        Error err = Path.GetExtension(p).ToLowerInvariant() switch
        {
            ".png" => img.LoadPngFromBuffer(bytes),
            ".webp" => img.LoadWebpFromBuffer(bytes),
            ".jpg" or ".jpeg" => img.LoadJpgFromBuffer(bytes),
            _ => Error.FileUnrecognized,
        };
        if (err != Error.Ok)
        {
            _log.Error($"{relative}: {err}");
            return null;
        }
        return ImageTexture.CreateFromImage(img);
    }
}
