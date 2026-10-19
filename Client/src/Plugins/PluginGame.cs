using System.Reflection;
using Godot;
using LibreKO.Domain;

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
    bool TwoHanded = false,
    long ExpiresAt = 0,
    int State = 0)
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
    bool Enough = true,
    bool SpShort = false)
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

public readonly record struct GameDrop(int ItemId, string Name, Texture2D? Icon, int Band);

public sealed record GameDropTable(int TargetId, int CoinMin, int CoinMax, int Experience, IReadOnlyList<GameDrop> Items);

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
    int Manner { get; }
    bool NoahKnight { get; }
    int MailUnread { get; }

    int Resist(int index);
    void AllocateStat(int statRow);

    int Sp => 0;
    int MaxSp => 0;
    int RebirthLevel => 0;
    int RebirthStat(int statRow) => 0;
    int StatBonus(int statRow) => 0;
    string ClanName => "";
    bool HasRankAura => false;
    bool RankAuraShown => true;
    void SetRankAuraShown(bool shown)
    {
    }

    Texture2D? Portrait { get; }
    void SetPortraitShown(bool shown);

    event Action? Changed;
}

public interface IGameInventory
{
    int Length { get; }
    int GridStart { get; }
    int GridCount { get; }

    GameItem At(int slot);
    void Move(int from, int to);
    void MoveAmount(int from, int to, int count);
    void Use(int slot);
    void Drop(int slot);
    void Arrange();
    void ShowTooltip(int slot);
    void HideTooltip();

    event Action? Changed;
}

public enum TargetHpLook
{
    Normal,
    Lasting,
    Drop,
    Slow,
    Undead,
}

public readonly record struct GameTargetDrop(Texture2D? Icon, string Line);

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
    TargetHpLook HpLook { get; }

    bool IsMonster => false;
    int NpcId => 0;
    int ClanId => 0;
    string ClanName => "";
    GameDropTable? Drops => null;
    IReadOnlyList<GameTargetDrop> QuestDrops => Array.Empty<GameTargetDrop>();

    void Clear();
    void RequestDrops()
    {
    }

    event Action? Changed;
}

public interface IGameWindows
{
    bool IsOpen(string id);
    void Open(string id);
    void Close(string id);
    void Toggle(string id);
    Rect2 TopRightRow { get; }
    event Action? TopRightRowMoved;
    int UiAlpha => Config.UiAlpha;
    void SetUiAlpha(int alpha) => Config.SetUiAlpha(alpha);
}

public readonly record struct GameChatLine(string BbCode, ChatCategory Category);

public interface IGameChat
{
    IReadOnlyList<string> History { get; }
    IReadOnlyList<GameChatLine> Lines { get; }

    void Send(string text);
    void SetTyping(bool typing);

    event Action<string>? LineAdded;
    event Action<GameChatLine>? LineArrived;
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

    int Selected => -1;
    void Select(int abs)
    {
    }

    event Action? Changed;
    event Action<IReadOnlyList<int>>? Ready { add { } remove { } }
}

public interface IGameCommands
{
    void GoTown();
    void ToggleSit();
    void TradeWithTarget();
    void InviteTarget();
    void ShowExitMenu();
    void OpenSettings() { }
    void Run(KeyAction action);
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
    int NpcKind { get; }

    IReadOnlyList<byte> MonumentOwners => Array.Empty<byte>();
    bool WantedUiOn => false;

    bool NpcKindPresent(int kind);
    void ToggleWantedUi() { }
    void ShowWantedList() { }
    void PickNpcKind(int kind);

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

public interface IGameVendor
{
    bool IsOpen { get; }
    string Title { get; }
    bool PaysInNp { get; }
    int Page { get; }
    int Pages { get; }
    IReadOnlyList<GameItem> Items { get; }

    int Price(int index);
    void Buy(int index);
    void Sell(int bagSlot);
    void SetPage(int page);
    void Search(string text);
    void ShowTooltip(int index);
    void HideTooltip();
    void Close();

    event Action? Changed;
}

public interface IGameWarehouse
{
    bool IsOpen { get; }
    int Page { get; }
    int Pages { get; }
    int Gold { get; }
    IReadOnlyList<GameItem> Items { get; }

    void Store(int bagSlot);
    void Take(int index);
    void SetPage(int page);
    void ShowTooltip(int index);
    void HideTooltip();
    void Close();

    event Action? Changed;
}

public interface IGameVipVault
{
    bool IsOpen { get; }
    string Expiry { get; }
    IReadOnlyList<GameItem> Items { get; }

    void Store(int bagSlot);
    void Take(int index);
    void ShowTooltip(int index);
    void HideTooltip();
    void Close();

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public bool Available { get; private set; }

    public IGameCharacter Character => character;
    public IGameInventory Inventory => inventory;
    public IGameTarget Target => target;
    public IGameWindows Windows => windows;
    public IGameChat Chat => chat;
    public IGameHotbar Hotbar => hotbar;
    public IGameMap Map => map;
    public IGameCommands Commands => commands;
    public IGameLog Log => log;
    public IGameQuests Quests => quests;
    public IGameSkills Skills => skills;
    public IGameVendor Vendor => vendor;
    public IGameWarehouse Warehouse => warehouse;
    public IGameVipVault VipVault => vipVault;

    public event Action? BecameAvailable;
    public event Action? BecameUnavailable;

    private readonly CharacterProxy character = new();
    private readonly InventoryProxy inventory = new();
    private readonly TargetProxy target = new();
    private readonly WindowsProxy windows = new();
    private readonly ChatProxy chat = new();
    private readonly HotbarProxy hotbar = new();
    private readonly MapProxy map = new();
    private readonly CommandsProxy commands = new();
    private readonly LogProxy log = new();
    private readonly QuestsProxy quests = new();
    private readonly SkillsProxy skills = new();
    private readonly VendorProxy vendor = new();
    private readonly WarehouseProxy warehouse = new();
    private readonly VipVaultProxy vipVault = new();

    internal void Attach(IGameCharacter characterSource, IGameInventory inventorySource, IGameTarget targetSource,
        IGameWindows windowsSource, IGameChat chatSource, IGameHotbar hotbarSource, IGameMap mapSource, IGameCommands commandsSource,
        IGameLog logSource, IGameQuests questsSource, IGameSkills skillsSource)
    {
        skills.Source = skillsSource;
        character.Source = characterSource;
        inventory.Source = inventorySource;
        target.Source = targetSource;
        windows.Source = windowsSource;
        chat.Source = chatSource;
        hotbar.Source = hotbarSource;
        map.Source = mapSource;
        commands.Source = commandsSource;
        log.Source = logSource;
        quests.Source = questsSource;
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
        character.Source = null;
        inventory.Source = null;
        target.Source = null;
        windows.Source = null;
        chat.Source = null;
        hotbar.Source = null;
        map.Source = null;
        commands.Source = null;
        log.Source = null;
        quests.Source = null;
        skills.Source = null;
        vendor.Source = null;
        warehouse.Source = null;
        vipVault.Source = null;
        attendance.Source = null;
        userInfo.Source = null;
        repurchase.Source = null;
        userVote.Source = null;
        equipView.Source = null;
        capeShop.Source = null;
        genie.Source = null;
        DetachSocial();
        DetachAppearance();
        friends.Source = null;
    }

    internal void AttachServices(IGameVendor vendorSource, IGameWarehouse warehouseSource, IGameVipVault vipVaultSource)
    {
        vendor.Source = vendorSource;
        warehouse.Source = warehouseSource;
        vipVault.Source = vipVaultSource;
        RaiseVendor();
        RaiseWarehouse();
        RaiseVipVault();
    }

    internal int DropHandlersFrom(Assembly owner)
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        int dropped = 0;
        var holders = new List<object> { this };
        foreach (var field in typeof(PluginGame).GetFields(Instance))
            if (field.FieldType.DeclaringType == typeof(PluginGame) && field.GetValue(this) is { } proxy)
                holders.Add(proxy);
        foreach (var holder in holders)
            foreach (var field in holder.GetType().GetFields(Instance))
            {
                if (!typeof(Delegate).IsAssignableFrom(field.FieldType) || field.GetValue(holder) is not Delegate handlers)
                    continue;
                Delegate? kept = null;
                foreach (var handler in handlers.GetInvocationList())
                {
                    if (handler.Method.Module.Assembly == owner) dropped++;
                    else kept = Delegate.Combine(kept, handler);
                }
                field.SetValue(holder, kept);
            }
        return dropped;
    }

    internal void RaiseVendor() => vendor.Raise();
    internal void RaiseWarehouse() => warehouse.Raise();
    internal void RaiseVipVault() => vipVault.Raise();

    internal void RaiseCharacter() => character.Raise();
    internal void RaiseInventory() => inventory.Raise();
    internal void RaiseTarget() => target.Raise();
    internal void RaiseHotbar() => hotbar.Raise();
    internal void RaiseHotbarReady(IReadOnlyList<int> skillIds) => hotbar.RaiseReady(skillIds);
    internal void RaiseMap() => map.Raise();
    internal void RaiseTopRightRow() => windows.RaiseRow();
    internal void RaiseQuests() => quests.Raise();
    internal void RaiseSkills() => skills.Raise();
    internal void RaiseChatLine(GameChatLine line) => chat.RaiseLine(line);
    internal void RaiseChatInputRequested() => chat.RaiseInputRequested();
    internal void RaiseLogLine(GameLogLine line) => log.RaiseLine(line);

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
        public int Manner => Source?.Manner ?? 0;
        public bool NoahKnight => Source?.NoahKnight ?? false;
        public int MailUnread => Source?.MailUnread ?? 0;
        public int Resist(int index) => Source?.Resist(index) ?? 0;
        public void AllocateStat(int statRow) => Source?.AllocateStat(statRow);
        public int Sp => Source?.Sp ?? 0;
        public int MaxSp => Source?.MaxSp ?? 0;
        public int RebirthLevel => Source?.RebirthLevel ?? 0;
        public int RebirthStat(int statRow) => Source?.RebirthStat(statRow) ?? 0;
        public int StatBonus(int statRow) => Source?.StatBonus(statRow) ?? 0;
        public string ClanName => Source?.ClanName ?? "";
        public bool HasRankAura => Source?.HasRankAura ?? false;
        public bool RankAuraShown => Source?.RankAuraShown ?? true;
        public void SetRankAuraShown(bool shown) => Source?.SetRankAuraShown(shown);
        public Texture2D? Portrait => Source?.Portrait;
        public void SetPortraitShown(bool shown) => Source?.SetPortraitShown(shown);
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
        public void MoveAmount(int from, int to, int count) => Source?.MoveAmount(from, to, count);
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
        public TargetHpLook HpLook => Source?.HpLook ?? TargetHpLook.Normal;
        public bool IsMonster => Source?.IsMonster ?? false;
        public int NpcId => Source?.NpcId ?? 0;
        public int ClanId => Source?.ClanId ?? 0;
        public string ClanName => Source?.ClanName ?? "";
        public GameDropTable? Drops => Source?.Drops;
        public IReadOnlyList<GameTargetDrop> QuestDrops => Source?.QuestDrops ?? Array.Empty<GameTargetDrop>();
        public void Clear() => Source?.Clear();
        public void RequestDrops() => Source?.RequestDrops();
    }

    private sealed class WindowsProxy : IGameWindows
    {
        public IGameWindows? Source;

        public bool IsOpen(string id) => Source?.IsOpen(id) ?? false;
        public void Open(string id) => Source?.Open(id);
        public void Close(string id) => Source?.Close(id);
        public void Toggle(string id) => Source?.Toggle(id);
        public int UiAlpha => Config.UiAlpha;
        public void SetUiAlpha(int alpha) => Config.SetUiAlpha(alpha);
        public Rect2 TopRightRow => Source?.TopRightRow ?? default;
        public event Action? TopRightRowMoved;
        internal void RaiseRow() => TopRightRowMoved?.Invoke();
    }

    private sealed class ChatProxy : IGameChat
    {
        public IGameChat? Source;
        public event Action<string>? LineAdded;
        public event Action<GameChatLine>? LineArrived;
        public event Action? InputRequested;
        internal void RaiseLine(GameChatLine line)
        {
            LineAdded?.Invoke(line.BbCode);
            LineArrived?.Invoke(line);
        }
        internal void RaiseInputRequested() => InputRequested?.Invoke();

        public IReadOnlyList<string> History => Source?.History ?? Array.Empty<string>();
        public IReadOnlyList<GameChatLine> Lines => Source?.Lines ?? Array.Empty<GameChatLine>();
        public void Send(string text) => Source?.Send(text);
        public void SetTyping(bool typing) => Source?.SetTyping(typing);
    }

    private sealed class HotbarProxy : IGameHotbar
    {
        public IGameHotbar? Source;
        public event Action? Changed;
        public event Action<IReadOnlyList<int>>? Ready;
        internal void Raise() => Changed?.Invoke();
        internal void RaiseReady(IReadOnlyList<int> skillIds) => Ready?.Invoke(skillIds);

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
        public int Selected => Source?.Selected ?? -1;
        public void Select(int abs) => Source?.Select(abs);
    }

    private sealed class CommandsProxy : IGameCommands
    {
        public IGameCommands? Source;

        public void GoTown() => Source?.GoTown();
        public void ToggleSit() => Source?.ToggleSit();
        public void TradeWithTarget() => Source?.TradeWithTarget();
        public void InviteTarget() => Source?.InviteTarget();
        public void ShowExitMenu() => Source?.ShowExitMenu();
        public void OpenSettings() => Source?.OpenSettings();
        public void Run(KeyAction action) => Source?.Run(action);
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
        public int NpcKind => Source?.NpcKind ?? 0;
        public IReadOnlyList<byte> MonumentOwners => Source?.MonumentOwners ?? Array.Empty<byte>();
        public bool WantedUiOn => Source?.WantedUiOn ?? false;
        public bool NpcKindPresent(int kind) => Source?.NpcKindPresent(kind) ?? false;
        public void PickNpcKind(int kind) => Source?.PickNpcKind(kind);
        public void ToggleWantedUi() => Source?.ToggleWantedUi();
        public void ShowWantedList() => Source?.ShowWantedList();
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

    private sealed class VendorProxy : IGameVendor
    {
        public IGameVendor? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public string Title => Source?.Title ?? "";
        public bool PaysInNp => Source?.PaysInNp ?? false;
        public int Page => Source?.Page ?? 0;
        public int Pages => Source?.Pages ?? 0;
        public IReadOnlyList<GameItem> Items => Source?.Items ?? Array.Empty<GameItem>();
        public int Price(int index) => Source?.Price(index) ?? 0;
        public void Buy(int index) => Source?.Buy(index);
        public void Sell(int bagSlot) => Source?.Sell(bagSlot);
        public void SetPage(int page) => Source?.SetPage(page);
        public void Search(string text) => Source?.Search(text);
        public void ShowTooltip(int index) => Source?.ShowTooltip(index);
        public void HideTooltip() => Source?.HideTooltip();
        public void Close() => Source?.Close();
    }

    private sealed class WarehouseProxy : IGameWarehouse
    {
        public IGameWarehouse? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public int Page => Source?.Page ?? 0;
        public int Pages => Source?.Pages ?? 0;
        public int Gold => Source?.Gold ?? 0;
        public IReadOnlyList<GameItem> Items => Source?.Items ?? Array.Empty<GameItem>();
        public void Store(int bagSlot) => Source?.Store(bagSlot);
        public void Take(int index) => Source?.Take(index);
        public void SetPage(int page) => Source?.SetPage(page);
        public void ShowTooltip(int index) => Source?.ShowTooltip(index);
        public void HideTooltip() => Source?.HideTooltip();
        public void Close() => Source?.Close();
    }

    private sealed class VipVaultProxy : IGameVipVault
    {
        public IGameVipVault? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public string Expiry => Source?.Expiry ?? "";
        public IReadOnlyList<GameItem> Items => Source?.Items ?? Array.Empty<GameItem>();
        public void Store(int bagSlot) => Source?.Store(bagSlot);
        public void Take(int index) => Source?.Take(index);
        public void ShowTooltip(int index) => Source?.ShowTooltip(index);
        public void HideTooltip() => Source?.HideTooltip();
        public void Close() => Source?.Close();
    }
}
