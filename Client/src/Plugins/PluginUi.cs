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
    Party,
    WarBoard,
    GenieSub,
}

public readonly record struct PlayerMenuRow(string Key, string Caption, Action Run);

public sealed class PlayerMenuRequest
{
    public string Name { get; }
    public Vector2 Position { get; }
    public IReadOnlyList<PlayerMenuRow> Rows { get; }

    private readonly Action close;

    internal PlayerMenuRequest(string name, Vector2 position, IReadOnlyList<PlayerMenuRow> rows, Action close)
    {
        Name = name;
        Position = position;
        Rows = rows;
        this.close = close;
    }

    public void Close() => close();
}

public readonly record struct ExitMenuRow(string Key, string Caption, Action Run);

public readonly record struct ExitMenuItem(int ItemId, Texture2D? Icon, string Name, string Place, string TimeLeft, bool Urgent);

public sealed class ExitMenuRequest
{
    public string Notice { get; }
    public IReadOnlyList<ExitMenuItem> TimedItems { get; }
    public IReadOnlyList<ExitMenuRow> Rows { get; }

    private readonly Action close;

    internal ExitMenuRequest(string notice, IReadOnlyList<ExitMenuItem> timedItems, IReadOnlyList<ExitMenuRow> rows, Action close)
    {
        Notice = notice;
        TimedItems = timedItems;
        Rows = rows;
        this.close = close;
    }

    public void Close() => close();
}

public sealed class WindowHost
{
    public string Id { get; }
    public string Title { get; }
    public Control Window { get; }
    public Control? DragHandle { get; private set; }

    public event Action? Shown;
    public event Action? Hidden;

    private readonly Action close;

    internal WindowHost(string id, string title, Control window, Action close)
    {
        Id = id;
        Title = title;
        Window = window;
        this.close = close;
    }

    public void SetDragHandle(Control handle) => DragHandle = handle;

    public void Close() => close();

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

    private readonly Action confirm;
    private readonly Action cancel;
    private readonly Action dismiss;

    internal DialogRequest(string title, string message, string confirmText, string? cancelText, bool dismissable,
        Action confirm, Action cancel, Action dismiss)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        Dismissable = dismissable;
        this.confirm = confirm;
        this.cancel = cancel;
        this.dismiss = dismiss;
    }

    public void Confirm() => confirm();

    public void Cancel() => cancel();

    public void Dismiss() => dismiss();

    internal void SetMessage(string message)
    {
        Message = message;
        MessageChanged?.Invoke(message);
    }
}

public sealed class TextRequest
{
    public string Title { get; }
    public string Message { get; }
    public string Initial { get; }
    public string Placeholder { get; }
    public int MaxLength { get; }
    public bool Secret { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }

    private readonly Action<string> submit;
    private readonly Action cancel;

    internal TextRequest(string title, string message, string initial, string placeholder, int maxLength, bool secret,
        string confirmText, string cancelText, Action<string> submit, Action cancel)
    {
        Title = title;
        Message = message;
        Initial = initial;
        Placeholder = placeholder;
        MaxLength = maxLength;
        Secret = secret;
        ConfirmText = confirmText;
        CancelText = cancelText;
        this.submit = submit;
        this.cancel = cancel;
    }

    public void Submit(string text) => submit(text);

    public void Cancel() => cancel();
}

public sealed class WindowFrame
{
    public string Id { get; }
    public string Title { get; private set; }
    public Control Content { get; }
    public bool Closable { get; }
    public Control? DragHandle { get; private set; }

    public event Action<string>? TitleChanged;
    public event Action? Shown;
    public event Action? Hidden;

    private readonly Action close;

    internal WindowFrame(string id, string title, Control content, bool closable, Action close)
    {
        Id = id;
        Title = title;
        Content = content;
        Closable = closable;
        this.close = close;
    }

    public void SetDragHandle(Control handle) => DragHandle = handle;

    public void Close() => close();

    internal void SetTitle(string title)
    {
        if (title == Title) return;
        Title = title;
        TitleChanged?.Invoke(title);
    }

    internal void RaiseShown() => Shown?.Invoke();

    internal void RaiseHidden() => Hidden?.Invoke();
}

internal sealed class Owned<T>
{
    private readonly List<(string Owner, T Value)> entries = new();

    public bool Has => entries.Count > 0;

    public T? Current => entries.Count > 0 ? entries[^1].Value : default;

    public IEnumerable<T> All => entries.Select(e => e.Value);

    public void Set(string owner, T value)
    {
        entries.RemoveAll(e => e.Owner == owner);
        entries.Add((owner, value));
    }

    public void Add(string owner, T value) => entries.Add((owner, value));

    public bool Remove(string owner) => entries.RemoveAll(e => e.Owner == owner) > 0;

    public bool RemoveValue(T value) => entries.RemoveAll(e => EqualityComparer<T>.Default.Equals(e.Value, value)) > 0;

    public void Clear() => entries.Clear();
}

internal sealed class WindowRule
{
    public readonly Owned<bool> HiddenBy = new();
    public readonly Owned<Func<WindowHost, Control>> Replacements = new();
    public readonly Owned<Func<WindowFrame, Control>> Frames = new();
    public readonly Owned<Action<Control>> ExtendedBy = new();

    public bool Hidden => HiddenBy.Has;
    public Func<WindowHost, Control>? Replacement => Replacements.Current;
    public Func<WindowFrame, Control>? Frame => Frames.Current;
    public IEnumerable<Action<Control>> Extenders => ExtendedBy.All;
    public bool Empty => !HiddenBy.Has && !Replacements.Has && !Frames.Has && !ExtendedBy.Has;

    public bool Remove(string owner)
    {
        bool removed = HiddenBy.Remove(owner);
        removed |= Replacements.Remove(owner);
        removed |= Frames.Remove(owner);
        removed |= ExtendedBy.Remove(owner);
        return removed;
    }
}

public sealed class PluginUi
{
    public static readonly string[] KnownWindowIds =
    {
        "achievements", "admin_panel", "anvil", "attendance", "bounty", "cape", "changehair", "character_info",
        "chat_room", "clan", "clanwarehouse", "class_change", "collectionrace", "combinerecipes", "disguise",
        "equipview", "exchange", "fishinghall", "forces", "fortune", "genie", "globalmap",
        "inventory", "itemcombine", "kingballot", "kingelection", "kingnominate", "kingplan", "kingvote", "lottery",
        "mail", "mailcompose", "mailread", "marketprice", "merchantmenu", "messenger", "namechange", "nation_chat",
        "nation_chat_create", "nation_chat_password", "nationintro",
        "nationtax", "nationtaxrate", "npc_dialog", "party", "pet", "piecechange", "presets", "quest_available",
        "quest_receipt", "quest_target", "quests", "rank", "rebirth", "repair", "report", "repurchase", "roulette", "seal",
        "seek_party", "sellstall", "shop", "shoppingmall", "siegechallengers", "siegedefenders", "siegeguard",
        "siegeoffice", "siegeschedule", "siegetaxlist", "siegetaxrate", "skills", "specialauction", "titles",
        "tournament", "user_vote", "user_vote_ask", "user_vote_notice", "userinfo", "vendor", "vipwarehouse", "wantedstall", "warehouse", "warp", "whisper_*",
        "wishfind", "wishlist",
    };

    public const string EveryWindow = "*";

    private sealed class Registry
    {
        public readonly Dictionary<string, WindowRule> Windows = new(StringComparer.OrdinalIgnoreCase);
        public readonly Owned<Func<WindowFrame, Control>> EveryFrame = new();
        public readonly Dictionary<HudPart, Owned<Func<Control>?>> Hud = new();
        public readonly Owned<Func<Control>> ExtraHud = new();
        public readonly Owned<Func<DialogRequest, Control>> Dialogs = new();
        public readonly Owned<Func<PlayerMenuRequest, Control>> PlayerMenus = new();
        public readonly Owned<Func<ExitMenuRequest, Control>> ExitMenus = new();
        public readonly Owned<Func<TextRequest, Control>> TextInputs = new();
        public readonly Owned<IEscapable> EscapePanels = new();
    }

    private readonly Registry store;
    private readonly string owner;

    public PluginUi() : this(new Registry(), "")
    {
    }

    private PluginUi(Registry store, string owner)
    {
        this.store = store;
        this.owner = owner;
    }

    internal PluginUi ForOwner(string owner) => new(store, owner);

    internal Func<DialogRequest, Control>? DialogBuilder => store.Dialogs.Current;

    internal Func<PlayerMenuRequest, Control>? PlayerMenuBuilder => store.PlayerMenus.Current;

    internal Func<ExitMenuRequest, Control>? ExitMenuBuilder => store.ExitMenus.Current;

    internal Func<TextRequest, Control>? TextInputBuilder => store.TextInputs.Current;

    internal IReadOnlyList<Func<Control>> ExtraHud => store.ExtraHud.All.ToList();

    internal IEnumerable<IEscapable> EscapePanels => store.EscapePanels.All;

    public void AddHud(Func<Control> build) => store.ExtraHud.Add(owner, build);

    public IReadOnlyList<string> WindowIds => KnownWindowIds;

    public void HideWindow(string id) => Rule(id).HiddenBy.Set(owner, true);

    public void ReplaceWindow(string id, Func<WindowHost, Control> build) => Rule(id).Replacements.Set(owner, build);

    public void ExtendWindow(string id, Action<Control> extend) => Rule(id).ExtendedBy.Add(owner, extend);

    public void FrameWindow(string id, Func<WindowFrame, Control> build)
    {
        if (id == EveryWindow) store.EveryFrame.Set(owner, build);
        else Rule(id).Frames.Set(owner, build);
    }

    public void HideHud(HudPart part) => HudEntry(part).Set(owner, null);

    public void ReplaceHud(HudPart part, Func<Control> build) => HudEntry(part).Set(owner, build);

    public void ReplaceDialogs(Func<DialogRequest, Control> build) => store.Dialogs.Set(owner, build);

    public void ReplacePlayerMenu(Func<PlayerMenuRequest, Control> build) => store.PlayerMenus.Set(owner, build);

    public void ReplaceExitMenu(Func<ExitMenuRequest, Control> build) => store.ExitMenus.Set(owner, build);

    public void ReplaceTextInput(Func<TextRequest, Control> build) => store.TextInputs.Set(owner, build);

    public IDisposable CloseOnEscape(Func<bool> isOpen, Action close)
    {
        var panel = new ClosablePanel(isOpen, close);
        store.EscapePanels.Add(owner, panel);
        return new Registration(() => store.EscapePanels.RemoveValue(panel));
    }

    private sealed class Registration(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    public bool IsWindowOverridden(string id) =>
        RuleFor(id) is { } r && (r.Hidden || r.Replacement != null);

    internal WindowRule? RuleFor(string id)
    {
        if (store.Windows.TryGetValue(id, out var r)) return r;
        int cut = id.IndexOf(FamilySeparator);
        return cut > 0 && store.Windows.TryGetValue(id[..(cut + 1)] + FamilyWildcard, out var family) ? family : null;
    }

    internal Func<WindowFrame, Control>? FrameFor(string id) => RuleFor(id)?.Frame ?? store.EveryFrame.Current;

    private const char FamilySeparator = '_';
    private const string FamilyWildcard = "*";

    internal bool HudHidden(HudPart part) => store.Hud.TryGetValue(part, out var h) && h.Has;

    internal Func<Control>? HudReplacement(HudPart part) => store.Hud.TryGetValue(part, out var h) ? h.Current : null;

    internal bool RemoveOwner(string owner)
    {
        bool removed = false;
        foreach (var (id, rule) in store.Windows.ToList())
        {
            removed |= rule.Remove(owner);
            if (rule.Empty) store.Windows.Remove(id);
        }
        foreach (var entry in store.Hud.Values) removed |= entry.Remove(owner);
        removed |= store.EveryFrame.Remove(owner);
        removed |= store.ExtraHud.Remove(owner);
        removed |= store.Dialogs.Remove(owner);
        removed |= store.PlayerMenus.Remove(owner);
        removed |= store.ExitMenus.Remove(owner);
        removed |= store.TextInputs.Remove(owner);
        removed |= store.EscapePanels.Remove(owner);
        return removed;
    }

    internal void Reset()
    {
        store.Windows.Clear();
        store.Hud.Clear();
        store.EveryFrame.Clear();
        store.ExtraHud.Clear();
        store.Dialogs.Clear();
        store.PlayerMenus.Clear();
        store.ExitMenus.Clear();
        store.TextInputs.Clear();
        store.EscapePanels.Clear();
    }

    private WindowRule Rule(string id)
    {
        if (!store.Windows.TryGetValue(id, out var r))
            store.Windows[id] = r = new WindowRule();
        return r;
    }

    private Owned<Func<Control>?> HudEntry(HudPart part)
    {
        if (!store.Hud.TryGetValue(part, out var entry))
            store.Hud[part] = entry = new Owned<Func<Control>?>();
        return entry;
    }
}
