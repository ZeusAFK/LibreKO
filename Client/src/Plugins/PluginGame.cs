using Godot;

namespace LibreKO.Plugins;

public readonly record struct GameItem(
    int Slot,
    int ItemId,
    string Name,
    int Count,
    int Durability,
    int Grade,
    int Kind,
    Texture2D? Icon,
    bool TwoHanded = false)
{
    public bool IsEmpty => ItemId <= 0;

    public static GameItem Empty(int slot) => new(slot, 0, "", 0, 0, 0, 0, null);
}

public readonly record struct HotSlotInfo(
    int Abs,
    int Id,
    bool IsSkill,
    Texture2D? Icon,
    float Cooldown,
    string Name,
    string Tooltip,
    int Count = -1,
    bool Enough = true)
{
    public bool IsEmpty => Id <= 0;

    public static HotSlotInfo Empty(int abs) => new(abs, 0, false, null, 0f, "", "");
}

public enum GameLogKind { Status, Item, System }

public readonly record struct GameLogLine(GameLogKind Kind, string Text, Color Color);

public readonly record struct GameQuestTrack(int QuestId, string Title, bool ReadyToTurnIn, IReadOnlyList<string> Objectives);

public readonly record struct GameSkillTab(int Category, string Label);

public readonly record struct GameSkill(int Id, string Name, Texture2D? Icon, bool Available, string Tooltip);

public readonly record struct GameSkillInfo(
    string Description,
    int Mp,
    int RequiredLevel,
    int RequiredPoints,
    bool UsesPoints,
    string BasicItem,
    string RequiredItem,
    string ConsumedItem);

public readonly record struct GameMasteryTree(int Type, string Name, int Points, bool Shown, bool CanSpend, string Hint);

public interface IGameCharacter
{
    string Name { get; }
    int Class { get; }
    string ClassName { get; }
    int Race { get; }
    int Nation { get; }
    string NationName { get; }
    int Level { get; }
    int Hp { get; }
    int MaxHp { get; }
    int Mp { get; }
    int MaxMp { get; }
    long Exp { get; }
    long MaxExp { get; }
    double ExpPercent { get; }
    int Gold { get; }
    int KnightCash { get; }
    int Weight { get; }
    int MaxWeight { get; }
    int Str { get; }
    int Sta { get; }
    int Dex { get; }
    int Intel { get; }
    int Mag { get; }
    int Points { get; }
    int Ap { get; }
    int Ac { get; }
    int Np { get; }

    int Resist(int index);
    void AllocateStat(int statRow);

    event Action? Changed;
}

public interface IGameInventory
{
    int Length { get; }
    int GridStart { get; }
    int GridCount { get; }

    GameItem At(int slot);
    void Move(int from, int to);
    void Use(int slot);
    void Drop(int slot);
    void Arrange();
    void ShowTooltip(int slot);
    void HideTooltip();

    event Action? Changed;
}

public interface IGameTarget
{
    bool Has { get; }
    int Id { get; }
    string Name { get; }
    int Level { get; }
    int Hp { get; }
    int MaxHp { get; }
    bool Hostile { get; }
    bool IsPlayer { get; }

    void Clear();

    event Action? Changed;
}

public interface IGameWindows
{
    bool IsOpen(string id);
    void Open(string id);
    void Close(string id);
    void Toggle(string id);
}

public interface IGameChat
{
    IReadOnlyList<string> History { get; }

    void Send(string text);
    void SetTyping(bool typing);

    event Action<string>? LineAdded;
    event Action? InputRequested;
}

public interface IGameHotbar
{
    int Pages { get; }
    int SlotsPerPage { get; }
    int Page { get; }
    bool Locked { get; }

    HotSlotInfo Slot(int abs);
    void Activate(int slotInPage);
    void ActivateAbs(int abs);
    void SetPage(int page);
    void ChangePage(int delta);
    void Drop(int abs, int id, int fromAbs);
    void Clear(int abs);
    void SetLocked(bool locked);
    void ShowTooltip(int abs);
    void HideTooltip();

    event Action? Changed;
}

public interface IGameCommands
{
    void GoTown();
    void ToggleSit();
    void TradeWithTarget();
}

public interface IGameMap
{
    int Zone { get; }
    string ZoneName { get; }
    float X { get; }
    float Z { get; }
    float HeadingDegrees { get; }
    float WorldExtent { get; }
    Texture2D? MapTexture { get; }
    IReadOnlyList<MiniMap.Blip> Blips { get; }

    event Action? Changed;
}

public interface IGameLog
{
    IReadOnlyList<GameLogLine> History { get; }

    event Action<GameLogLine>? LineAdded;
}

public interface IGameQuests
{
    IReadOnlyList<GameQuestTrack> Tracked { get; }

    event Action? Changed;
}

public interface IGameSkills
{
    IReadOnlyList<GameSkillTab> Tabs { get; }
    int MasteryPool { get; }
    IReadOnlyList<GameMasteryTree> Trees { get; }

    IReadOnlyList<GameSkill> Skills(int category);
    GameSkillInfo Info(int skillId);
    void SpendMastery(int type);
    void AddToHotbar(int skillId);

    event Action? Changed;
}

public sealed class PluginGame
{
    public bool Available { get; private set; }

    public IGameCharacter Character => _character;
    public IGameInventory Inventory => _inventory;
    public IGameTarget Target => _target;
    public IGameWindows Windows => _windows;
    public IGameChat Chat => _chat;
    public IGameHotbar Hotbar => _hotbar;
    public IGameMap Map => _map;
    public IGameCommands Commands => _commands;
    public IGameLog Log => _log;
    public IGameQuests Quests => _quests;
    public IGameSkills Skills => _skills;

    public event Action? BecameAvailable;
    public event Action? BecameUnavailable;

    private readonly CharacterProxy _character = new();
    private readonly InventoryProxy _inventory = new();
    private readonly TargetProxy _target = new();
    private readonly WindowsProxy _windows = new();
    private readonly ChatProxy _chat = new();
    private readonly HotbarProxy _hotbar = new();
    private readonly MapProxy _map = new();
    private readonly CommandsProxy _commands = new();
    private readonly LogProxy _log = new();
    private readonly QuestsProxy _quests = new();
    private readonly SkillsProxy _skills = new();

    internal void Attach(IGameCharacter character, IGameInventory inventory, IGameTarget target,
        IGameWindows windows, IGameChat chat, IGameHotbar hotbar, IGameMap map, IGameCommands commands,
        IGameLog log, IGameQuests quests, IGameSkills skills)
    {
        _skills.Source = skills;
        _character.Source = character;
        _inventory.Source = inventory;
        _target.Source = target;
        _windows.Source = windows;
        _chat.Source = chat;
        _hotbar.Source = hotbar;
        _map.Source = map;
        _commands.Source = commands;
        _log.Source = log;
        _quests.Source = quests;
        Available = true;
        BecameAvailable?.Invoke();
        RaiseCharacter();
        RaiseInventory();
        RaiseTarget();
        RaiseHotbar();
        RaiseMap();
        RaiseQuests();
        RaiseSkills();
    }

    internal void Detach()
    {
        if (!Available) return;
        Available = false;
        BecameUnavailable?.Invoke();
        _character.Source = null;
        _inventory.Source = null;
        _target.Source = null;
        _windows.Source = null;
        _chat.Source = null;
        _hotbar.Source = null;
        _map.Source = null;
        _commands.Source = null;
        _log.Source = null;
        _quests.Source = null;
        _skills.Source = null;
    }

    internal void RaiseCharacter() => _character.Raise();
    internal void RaiseInventory() => _inventory.Raise();
    internal void RaiseTarget() => _target.Raise();
    internal void RaiseHotbar() => _hotbar.Raise();
    internal void RaiseMap() => _map.Raise();
    internal void RaiseQuests() => _quests.Raise();
    internal void RaiseSkills() => _skills.Raise();
    internal void RaiseChatLine(string bbcode) => _chat.RaiseLine(bbcode);
    internal void RaiseChatInputRequested() => _chat.RaiseInputRequested();
    internal void RaiseLogLine(GameLogLine line) => _log.RaiseLine(line);

    private sealed class CharacterProxy : IGameCharacter
    {
        public IGameCharacter? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public string Name => Source?.Name ?? "";
        public int Class => Source?.Class ?? 0;
        public string ClassName => Source?.ClassName ?? "";
        public int Race => Source?.Race ?? 0;
        public int Nation => Source?.Nation ?? 0;
        public string NationName => Source?.NationName ?? "";
        public int Level => Source?.Level ?? 0;
        public int Hp => Source?.Hp ?? 0;
        public int MaxHp => Source?.MaxHp ?? 0;
        public int Mp => Source?.Mp ?? 0;
        public int MaxMp => Source?.MaxMp ?? 0;
        public long Exp => Source?.Exp ?? 0;
        public long MaxExp => Source?.MaxExp ?? 0;
        public double ExpPercent => Source?.ExpPercent ?? 0;
        public int Gold => Source?.Gold ?? 0;
        public int KnightCash => Source?.KnightCash ?? 0;
        public int Weight => Source?.Weight ?? 0;
        public int MaxWeight => Source?.MaxWeight ?? 0;
        public int Str => Source?.Str ?? 0;
        public int Sta => Source?.Sta ?? 0;
        public int Dex => Source?.Dex ?? 0;
        public int Intel => Source?.Intel ?? 0;
        public int Mag => Source?.Mag ?? 0;
        public int Points => Source?.Points ?? 0;
        public int Ap => Source?.Ap ?? 0;
        public int Ac => Source?.Ac ?? 0;
        public int Np => Source?.Np ?? 0;
        public int Resist(int index) => Source?.Resist(index) ?? 0;
        public void AllocateStat(int statRow) => Source?.AllocateStat(statRow);
    }

    private sealed class InventoryProxy : IGameInventory
    {
        public IGameInventory? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public int Length => Source?.Length ?? InventoryConstants.InventoryTotal;
        public int GridStart => Source?.GridStart ?? InventoryConstants.InventoryStart;
        public int GridCount => Source?.GridCount ?? InventoryConstants.HaveMax;
        public GameItem At(int slot) => Source?.At(slot) ?? GameItem.Empty(slot);
        public void Move(int from, int to) => Source?.Move(from, to);
        public void Use(int slot) => Source?.Use(slot);
        public void Drop(int slot) => Source?.Drop(slot);
        public void Arrange() => Source?.Arrange();
        public void ShowTooltip(int slot) => Source?.ShowTooltip(slot);
        public void HideTooltip() => Source?.HideTooltip();
    }

    private sealed class TargetProxy : IGameTarget
    {
        public IGameTarget? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool Has => Source?.Has ?? false;
        public int Id => Source?.Id ?? -1;
        public string Name => Source?.Name ?? "";
        public int Level => Source?.Level ?? 0;
        public int Hp => Source?.Hp ?? 0;
        public int MaxHp => Source?.MaxHp ?? 0;
        public bool Hostile => Source?.Hostile ?? false;
        public bool IsPlayer => Source?.IsPlayer ?? false;
        public void Clear() => Source?.Clear();
    }

    private sealed class WindowsProxy : IGameWindows
    {
        public IGameWindows? Source;

        public bool IsOpen(string id) => Source?.IsOpen(id) ?? false;
        public void Open(string id) => Source?.Open(id);
        public void Close(string id) => Source?.Close(id);
        public void Toggle(string id) => Source?.Toggle(id);
    }

    private sealed class ChatProxy : IGameChat
    {
        public IGameChat? Source;
        public event Action<string>? LineAdded;
        public event Action? InputRequested;
        internal void RaiseLine(string bbcode) => LineAdded?.Invoke(bbcode);
        internal void RaiseInputRequested() => InputRequested?.Invoke();

        public IReadOnlyList<string> History => Source?.History ?? Array.Empty<string>();
        public void Send(string text) => Source?.Send(text);
        public void SetTyping(bool typing) => Source?.SetTyping(typing);
    }

    private sealed class HotbarProxy : IGameHotbar
    {
        public IGameHotbar? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public int Pages => Source?.Pages ?? HotbarLayout.Pages;
        public int SlotsPerPage => Source?.SlotsPerPage ?? HotbarLayout.SlotsPerPage;
        public int Page => Source?.Page ?? 0;
        public bool Locked => Source?.Locked ?? false;
        public HotSlotInfo Slot(int abs) => Source?.Slot(abs) ?? HotSlotInfo.Empty(abs);
        public void Activate(int slotInPage) => Source?.Activate(slotInPage);
        public void ActivateAbs(int abs) => Source?.ActivateAbs(abs);
        public void SetPage(int page) => Source?.SetPage(page);
        public void ChangePage(int delta) => Source?.ChangePage(delta);
        public void Drop(int abs, int id, int fromAbs) => Source?.Drop(abs, id, fromAbs);
        public void Clear(int abs) => Source?.Clear(abs);
        public void SetLocked(bool locked) => Source?.SetLocked(locked);
        public void ShowTooltip(int abs) => Source?.ShowTooltip(abs);
        public void HideTooltip() => Source?.HideTooltip();
    }

    private sealed class CommandsProxy : IGameCommands
    {
        public IGameCommands? Source;

        public void GoTown() => Source?.GoTown();
        public void ToggleSit() => Source?.ToggleSit();
        public void TradeWithTarget() => Source?.TradeWithTarget();
    }

    private sealed class MapProxy : IGameMap
    {
        public IGameMap? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public int Zone => Source?.Zone ?? 0;
        public string ZoneName => Source?.ZoneName ?? "";
        public float X => Source?.X ?? 0f;
        public float Z => Source?.Z ?? 0f;
        public float HeadingDegrees => Source?.HeadingDegrees ?? 0f;
        public float WorldExtent => Source?.WorldExtent ?? 0f;
        public Texture2D? MapTexture => Source?.MapTexture;
        public IReadOnlyList<MiniMap.Blip> Blips => Source?.Blips ?? Array.Empty<MiniMap.Blip>();
    }

    private sealed class LogProxy : IGameLog
    {
        public IGameLog? Source;
        public event Action<GameLogLine>? LineAdded;
        internal void RaiseLine(GameLogLine line) => LineAdded?.Invoke(line);

        public IReadOnlyList<GameLogLine> History => Source?.History ?? Array.Empty<GameLogLine>();
    }

    private sealed class QuestsProxy : IGameQuests
    {
        public IGameQuests? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public IReadOnlyList<GameQuestTrack> Tracked => Source?.Tracked ?? Array.Empty<GameQuestTrack>();
    }

    private sealed class SkillsProxy : IGameSkills
    {
        public IGameSkills? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public IReadOnlyList<GameSkillTab> Tabs => Source?.Tabs ?? Array.Empty<GameSkillTab>();
        public int MasteryPool => Source?.MasteryPool ?? 0;
        public IReadOnlyList<GameMasteryTree> Trees => Source?.Trees ?? Array.Empty<GameMasteryTree>();
        public IReadOnlyList<GameSkill> Skills(int category) => Source?.Skills(category) ?? Array.Empty<GameSkill>();
        public GameSkillInfo Info(int skillId) => Source?.Info(skillId) ?? default;
        public void SpendMastery(int type) => Source?.SpendMastery(type);
        public void AddToHotbar(int skillId) => Source?.AddToHotbar(skillId);
    }
}
