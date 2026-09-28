using System.Text.Json;
using System.Text.RegularExpressions;

namespace LibreKO.Plugins;

public enum PluginType { UiTheme, Extension }

public sealed class PluginManifestException : Exception
{
    public PluginManifestException(string message) : base(message)
    {
    }
}

public sealed partial class PluginManifest
{
    public const string FileName = "plugin.json";
    public const int IdMaxLength = 64;

    public string Id { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Version { get; private set; } = "0.0.0";
    public PluginType Type { get; private set; } = PluginType.Extension;
    public string Description { get; private set; } = "";
    public string Author { get; private set; } = "";
    public string Homepage { get; private set; } = "";
    public string Assembly { get; private set; } = "";
    public string Entry { get; private set; } = "";
    public System.Version? MinClientVersion { get; private set; }

    public bool HasCode => Assembly.Length > 0;

    public bool Exclusive => IsExclusive(Type);

    public static bool IsExclusive(PluginType type) => type == PluginType.UiTheme;

    public static string TypeName(PluginType type) => type switch
    {
        PluginType.UiTheme => "ui-theme",
        _ => "extension",
    };

    public static bool TryParseType(string name, out PluginType type)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "ui-theme":
            case "uitheme":
            case "theme":
                type = PluginType.UiTheme;
                return true;
            case "extension":
            case "":
                type = PluginType.Extension;
                return true;
            default:
                type = PluginType.Extension;
                return false;
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex IdPattern();

    public static PluginManifest Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            throw new PluginManifestException($"{FileName} is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new PluginManifestException($"{FileName} must contain an object");

            var m = new PluginManifest
            {
                Id = Str(root, "id").Trim(),
                Name = Str(root, "name").Trim(),
                Version = Str(root, "version", "0.0.0").Trim(),
                Description = Str(root, "description").Trim(),
                Author = Str(root, "author").Trim(),
                Homepage = Str(root, "homepage").Trim(),
                Assembly = Str(root, "assembly").Trim().Replace('\\', '/'),
                Entry = Str(root, "entry").Trim(),
            };

            if (m.Id.Length == 0)
                throw new PluginManifestException("\"id\" is required");
            if (m.Id.Length > IdMaxLength || !IdPattern().IsMatch(m.Id))
                throw new PluginManifestException($"\"id\" must be lowercase letters, digits, '.', '_' or '-' (got \"{m.Id}\")");
            if (m.Name.Length == 0)
                throw new PluginManifestException("\"name\" is required");

            string typeName = Str(root, "type");
            if (!TryParseType(typeName, out var type))
                throw new PluginManifestException($"\"type\" must be \"ui-theme\" or \"extension\" (got \"{typeName}\")");
            m.Type = type;

            if (m.Assembly.Length > 0)
            {
                if (m.Assembly.StartsWith('/') || m.Assembly.Contains("..") || m.Assembly.Contains(':'))
                    throw new PluginManifestException("\"assembly\" must be a path inside the plugin folder");
                if (!m.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    throw new PluginManifestException("\"assembly\" must point at a .dll");
            }
            else if (m.Entry.Length > 0)
            {
                throw new PluginManifestException("\"entry\" needs an \"assembly\"");
            }

            string min = Str(root, "minClientVersion").Trim();
            if (min.Length > 0)
            {
                if (!System.Version.TryParse(min, out var v))
                    throw new PluginManifestException($"\"minClientVersion\" is not a version (got \"{min}\")");
                m.MinClientVersion = v;
            }
            return m;
        }
    }

    private static string Str(JsonElement o, string key, string fallback = "")
    {
        if (!o.TryGetProperty(key, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? fallback,
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.Null => fallback,
            _ => throw new PluginManifestException($"\"{key}\" must be a string"),
        };
    }
}
