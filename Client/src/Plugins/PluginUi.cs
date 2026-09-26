using Godot;

namespace LibreKO.Plugins;

public enum HudPart
{
    StatusBars,
    TargetFrame,
    MiniMap,
    Hotbar,
    Chat,
    Launcher,
    ExpBar,
    CombatLog,
    QuestTracker,
}

public sealed class WindowHost
{
    public string Id { get; }
    public string Title { get; }
    public Control Window { get; }
    public Control? DragHandle { get; private set; }

    public event Action? Shown;
    public event Action? Hidden;

    private readonly Action _close;

    internal WindowHost(string id, string title, Control window, Action close)
    {
        Id = id;
        Title = title;
        Window = window;
        _close = close;
    }

    public void SetDragHandle(Control handle) => DragHandle = handle;

    public void Close() => _close();

    internal void RaiseShown() => Shown?.Invoke();

    internal void RaiseHidden() => Hidden?.Invoke();
}

public sealed class DialogRequest
{
    public string Title { get; }
    public string Message { get; private set; }
    public string ConfirmText { get; }
    public string? CancelText { get; }
    public bool Dismissable { get; }
    public bool HasCancel => CancelText != null;

    public event Action<string>? MessageChanged;

    private readonly Action _confirm;
    private readonly Action _cancel;
    private readonly Action _dismiss;

    internal DialogRequest(string title, string message, string confirmText, string? cancelText, bool dismissable,
        Action confirm, Action cancel, Action dismiss)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        Dismissable = dismissable;
        _confirm = confirm;
        _cancel = cancel;
        _dismiss = dismiss;
    }

    public void Confirm() => _confirm();

    public void Cancel() => _cancel();

    public void Dismiss() => _dismiss();

    internal void SetMessage(string message)
    {
        Message = message;
        MessageChanged?.Invoke(message);
    }
}

internal sealed class WindowRule
{
    public bool Hidden;
    public Func<WindowHost, Control>? Replacement;
    public readonly List<Action<Control>> Extenders = new();
}

public sealed class PluginUi
{
    public static readonly string[] KnownWindowIds =
    {
        "achievements", "admin_panel", "anvil", "attendance", "auction", "bounty", "cape", "changehair",
        "character_info", "chatrooms", "clan", "clanwarehouse", "class_change", "collectionrace", "dailyquest",
        "disguise", "duel", "equipview", "eventquests", "exchange", "fishinghall", "forces", "fortune", "genie",
        "globalmap", "inn", "instance", "inventory", "itemcombine", "itemexchange", "king", "lottery", "mail",
        "mailcompose", "mailread", "marketbbs", "merchantmenu", "messenger", "namechange", "npc_dialog", "party",
        "pet", "piecechange", "presets", "quest_available", "quest_receipt", "quest_target", "quests", "rank",
        "rebirth", "rental", "repair", "report", "ring_upgrade", "roulette", "seal", "seek_party", "sellstall",
        "shop", "shoppingmall", "siege", "skills", "titles", "tournament", "userinfo", "vendor", "vipwarehouse",
        "wantedstall", "warehouse", "warp", "wishfind", "wishlist",
    };

    private readonly Dictionary<string, WindowRule> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<HudPart, (bool Hidden, Func<Control>? Replacement)> _hud = new();
    private readonly List<Func<Control>> _extraHud = new();

    internal Func<DialogRequest, Control>? DialogBuilder { get; private set; }

    internal IReadOnlyList<Func<Control>> ExtraHud => _extraHud;

    public void AddHud(Func<Control> build) => _extraHud.Add(build);

    public IReadOnlyList<string> WindowIds => KnownWindowIds;

    public void HideWindow(string id) => Rule(id).Hidden = true;

    public void ReplaceWindow(string id, Func<WindowHost, Control> build) => Rule(id).Replacement = build;

    public void ExtendWindow(string id, Action<Control> extend) => Rule(id).Extenders.Add(extend);

    public void HideHud(HudPart part) => _hud[part] = (true, null);

    public void ReplaceHud(HudPart part, Func<Control> build) => _hud[part] = (false, build);

    public void ReplaceDialogs(Func<DialogRequest, Control> build) => DialogBuilder = build;

    public bool IsWindowOverridden(string id) =>
        _windows.TryGetValue(id, out var r) && (r.Hidden || r.Replacement != null);

    internal WindowRule? RuleFor(string id) => _windows.TryGetValue(id, out var r) ? r : null;

    internal bool HudHidden(HudPart part) => _hud.TryGetValue(part, out var h) && (h.Hidden || h.Replacement != null);

    internal Func<Control>? HudReplacement(HudPart part) => _hud.TryGetValue(part, out var h) ? h.Replacement : null;

    internal void Reset()
    {
        _windows.Clear();
        _hud.Clear();
        _extraHud.Clear();
        DialogBuilder = null;
    }

    private WindowRule Rule(string id)
    {
        if (!_windows.TryGetValue(id, out var r))
            _windows[id] = r = new WindowRule();
        return r;
    }
}
