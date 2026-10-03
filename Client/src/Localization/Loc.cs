using System.Collections.Generic;
using Godot;

namespace LibreKO.Localization;

/// <summary>
/// Client-side localization layer.
///
/// When Config.Language is Chinese, every user-visible string funnels through
/// the lookups below and returns the simplified-Chinese dictionary value;
/// English (and Spanish, which the client has never localized) paths are
/// returned unchanged, so the existing English/Spanish experience is untouched.
///
/// Dictionaries are loaded lazily (first use) from res://assets/localization/:
///   zh_ui.json      { "English literal": "中文", ... }
///   zh_items.json   { "100010000": {"name": "...", "desc": "..."}, ... }
///   zh_quests.json  { "menu": {id: "..."}, "talk": {id: "..."} }
///   zh_mobs.json    { "100": "中文名", ... }      (monsters and NPCs by id)
///   zh_zones.json   { "21": "莫拉登", ... }
///   zh_texts.json   { "123": "中文", ... }         (items.json _texts overlay)
///   zh_skills.json  { "101001": {"name": "...", "desc": "..."}, ... }
/// </summary>
public static class Loc
{
    public static bool IsChinese => Config.Language == LibreKO.Network.GameLanguage.Chinese;

    private static readonly Dictionary<string, string> Ui = new(System.StringComparer.Ordinal);
    private static readonly Dictionary<int, (string Name, string Desc)> Items = new();
    private static readonly Dictionary<int, string> QuestMenu = new();
    private static readonly Dictionary<int, string> QuestTalk = new();
    private static readonly Dictionary<int, string> Mobs = new();
    private static readonly Dictionary<int, string> Zones = new();
    private static readonly Dictionary<int, string> Texts = new();
    private static readonly Dictionary<int, (string Name, string Desc)> Skills = new();

    private static bool _ui, _items, _quests, _mobs, _zones, _texts, _skills;

    /// <summary>Raised after Config.Language changes; UI panels subscribe to rebuild.</summary>
    public static event System.Action? LanguageChanged;

    public static void Refresh() => LanguageChanged?.Invoke();

    private static bool _fontReady;

    /// <summary>
    /// Installs a CJK-capable system font as the theme fallback so Chinese text never
    /// renders as missing-glyph boxes. Called from Boot before any UI is built.
    /// </summary>
    public static void EnsureCjkFont()
    {
        if (_fontReady) return;
        _fontReady = true;
        try
        {
            var sys = new SystemFont
            {
                FontNames = new[] { "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun", "Noto Sans CJK SC", "PingFang SC" },
            };
            ThemeDB.FallbackFont = sys;
        }
        catch (System.Exception)
        {
            // Keep the engine default; OS-level glyph fallback still applies.
        }
    }

    // ------------------------------------------------------------------ UI

    public static string Tr(string en)
    {
        if (!IsChinese || en.Length == 0) return en;
        EnsureUi();
        return Ui.TryGetValue(en, out var zh) && zh.Length > 0 ? zh : en;
    }

    // ---------------------------------------------------------- Chinese IDs

    private static readonly char[] Digit = { '〇', '一', '二', '三', '四', '五', '六', '七', '八', '九' };

    /// <summary>Renders an ID/number with Chinese numerals, e.g. 100010000 -&gt; 一〇〇〇一〇〇〇〇.</summary>
    public static string Num(long n)
    {
        if (n < 0) return "负" + Num(-n);
        var s = n.ToString();
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s) sb.Append(Digit[c - '0']);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ data

    public static string ItemName(int id, string fallback)
    {
        EnsureItems();
        if (!IsChinese) return fallback;
        return PickItem(id, fallback, static e => e.Name);
    }

    public static string ItemDesc(int id, string fallback)
    {
        EnsureItems();
        if (!IsChinese) return fallback;
        return PickItem(id, fallback, static e => e.Desc);
    }

    private static string PickItem(int id, string fallback, System.Func<(string Name, string Desc), string> sel)
    {
        if (Items.TryGetValue(id, out var exact) && sel(exact).Length > 0) return sel(exact);
        int baseId = id / 1000 * 1000;
        if (baseId != id && Items.TryGetValue(baseId, out var b) && sel(b).Length > 0) return sel(b);
        return fallback;
    }

    public static string ItemText(int id, string fallback)
    {
        EnsureTexts();
        if (!IsChinese) return fallback;
        return Texts.TryGetValue(id, out var zh) && zh.Length > 0 ? zh : fallback;
    }

    public static string MobName(int id, string fallback)
    {
        EnsureMobs();
        if (!IsChinese) return fallback;
        return Mobs.TryGetValue(id, out var zh) && zh.Length > 0 ? zh : fallback;
    }

    public static string ZoneName(int id, string fallback)
    {
        EnsureZones();
        if (!IsChinese) return fallback;
        return Zones.TryGetValue(id, out var zh) && zh.Length > 0 ? zh : fallback;
    }

    public static string SkillName(int id, string fallback)
    {
        EnsureSkills();
        if (!IsChinese) return fallback;
        return Skills.TryGetValue(id, out var e) && e.Name.Length > 0 ? e.Name : fallback;
    }

    public static string SkillDesc(int id, string fallback)
    {
        EnsureSkills();
        if (!IsChinese) return fallback;
        return Skills.TryGetValue(id, out var e) && e.Desc.Length > 0 ? e.Desc : fallback;
    }

    public static string QuestMenuText(int id, string fallback)
    {
        EnsureQuests();
        if (!IsChinese) return fallback;
        return QuestMenu.TryGetValue(id, out var zh) && zh.Length > 0 ? zh : fallback;
    }

    public static string QuestTalkText(int id, string fallback)
    {
        EnsureQuests();
        if (!IsChinese) return fallback;
        return QuestTalk.TryGetValue(id, out var zh) && zh.Length > 0 ? zh : fallback;
    }

    // -------------------------------------------------------------- loading

    private static void EnsureUi()
    {
        if (_ui) return;
        _ui = true;
        LoadStringMap("res://assets/localization/zh_ui.json", Ui);
    }

    private static void EnsureItems()
    {
        if (_items) return;
        _items = true;
        LoadPairMap("res://assets/localization/zh_items.json", Items);
    }

    private static void EnsureQuests()
    {
        if (_quests) return;
        _quests = true;
        LoadSections("res://assets/localization/zh_quests.json", QuestMenu, QuestTalk);
    }

    private static void EnsureMobs()
    {
        if (_mobs) return;
        _mobs = true;
        LoadStringMap("res://assets/localization/zh_mobs.json", Mobs, numericKeys: true);
    }

    private static void EnsureZones()
    {
        if (_zones) return;
        _zones = true;
        LoadStringMap("res://assets/localization/zh_zones.json", Zones, numericKeys: true);
    }

    private static void EnsureTexts()
    {
        if (_texts) return;
        _texts = true;
        LoadStringMap("res://assets/localization/zh_texts.json", Texts, numericKeys: true);
    }

    private static void EnsureSkills()
    {
        if (_skills) return;
        _skills = true;
        LoadPairMap("res://assets/localization/zh_skills.json", Skills);
    }

    private static void LoadStringMap(string path, Dictionary<string, string> into)
    {
        if (!Godot.FileAccess.FileExists(path)) return;
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var d = parsed.AsGodotDictionary();
        foreach (var k in d.Keys)
        {
            if (d[k].VariantType != Variant.Type.String) continue;
            string zh = d[k].AsString();
            if (zh.Length > 0) into[k.AsString()] = zh;
        }
    }

    private static void LoadStringMap(string path, Dictionary<int, string> into, bool numericKeys)
    {
        if (!Godot.FileAccess.FileExists(path)) return;
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var d = parsed.AsGodotDictionary();
        foreach (var k in d.Keys)
        {
            if (d[k].VariantType != Variant.Type.String) continue;
            if (!int.TryParse(k.AsString(), out int id)) continue;
            string zh = d[k].AsString();
            if (zh.Length > 0) into[id] = zh;
        }
    }

    private static void LoadPairMap(string path, Dictionary<int, (string Name, string Desc)> into)
    {
        if (!Godot.FileAccess.FileExists(path)) return;
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var d = parsed.AsGodotDictionary();
        foreach (var k in d.Keys)
        {
            if (!int.TryParse(k.AsString(), out int id)) continue;
            if (d[k].VariantType != Variant.Type.Dictionary) continue;
            var o = d[k].AsGodotDictionary();
            into[id] = (
                o.ContainsKey("name") && o["name"].VariantType == Variant.Type.String ? o["name"].AsString() : "",
                o.ContainsKey("desc") && o["desc"].VariantType == Variant.Type.String ? o["desc"].AsString() : "");
        }
    }

    private static void LoadSections(string path, Dictionary<int, string> menu, Dictionary<int, string> talk)
    {
        if (!Godot.FileAccess.FileExists(path)) return;
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var d = parsed.AsGodotDictionary();
        LoadSectionInto(d, "menu", menu);
        LoadSectionInto(d, "talk", talk);
    }

    private static void LoadSectionInto(Godot.Collections.Dictionary root, string key, Dictionary<int, string> into)
    {
        if (!root.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Dictionary) return;
        var map = v.AsGodotDictionary();
        foreach (var k in map.Keys)
        {
            if (map[k].VariantType != Variant.Type.String) continue;
            if (!int.TryParse(k.AsString(), out int id)) continue;
            string zh = map[k].AsString();
            if (zh.Length > 0) into[id] = zh;
        }
    }
}
