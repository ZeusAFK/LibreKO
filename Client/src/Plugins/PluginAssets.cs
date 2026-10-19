using System.Text.Json;
using Godot;

namespace LibreKO.Plugins;

public sealed class PluginAssets
{
    private readonly string root;
    private readonly PluginLog log;
    private readonly Dictionary<string, Texture2D?> textures = new(StringComparer.OrdinalIgnoreCase);

    internal PluginAssets(string root, PluginLog log)
    {
        this.root = Path.GetFullPath(root);
        this.log = log;
    }

    public string Root => root;

    public string PathOf(string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', '/')));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
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
            log.Error($"{relative}: {ex.Message}");
            return null;
        }
    }

    public Texture2D? Texture(string relative)
    {
        if (textures.TryGetValue(relative, out var cached)) return cached;
        var tex = LoadTexture(relative);
        textures[relative] = tex;
        return tex;
    }

    internal void Release()
    {
        foreach (var texture in textures.Values) texture?.Dispose();
        textures.Clear();
    }

    private Texture2D? LoadTexture(string relative)
    {
        string p = PathOf(relative);
        if (!File.Exists(p))
        {
            log.Warn($"missing texture {relative}");
            return null;
        }
        using var img = new Image();
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
            log.Error($"{relative}: {err}");
            return null;
        }
        return ImageTexture.CreateFromImage(img);
    }
}
