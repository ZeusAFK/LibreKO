using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public enum KeyAction
{
    MoveForward,
    MoveBackward,
    TurnLeft,
    TurnRight,
    AutoRun,
    ToggleRun,
    ToggleRunAlt,
    Sit,

    TargetHostile,
    TargetFriendly,
    AutoAttack,
    StealthCancel,

    HotSlot1, HotSlot2, HotSlot3, HotSlot4, HotSlot5, HotSlot6, HotSlot7, HotSlot8,
    HotPage1, HotPage2, HotPage3, HotPage4, HotPage5, HotPage6, HotPage7, HotPage8,

    Character,
    Inventory,
    Skills,
    Quests,
    Party,
    Friends,
    Clan,
    Messenger,
    MiniMap,
    ZoneMap,
    WorldMap,
    Helmet,
    Interact,
    MyShop,
    Pet,
    PowerUpStore,
    Rebirth,
    King,
    Siege,
    NameChange,
    ClanWarehouse,
    VipWarehouse,
    Report,
    Achievements,
    Mail,
    Lottery,
    Auction,
    Attendance,
    Bounty,
    Tournament,
    Disguise,
    Presets,
    NationForce,
    Instances,
    ChatRooms,
    Fortune,
    ItemCombine,
    Roulette,
    FishingHall,
    TradeBoard,
    Duel,
    ItemExchange,
    RingUpgrade,
    Inn,
    EventQuests,
    Genie,
    DailyQuests,
    Rentals,
    TownRecall,

    HotPageNext,
    PotionHp,
    PotionMp,
    CameraTurn,
    GameMenu,

    PerformanceOverlay,
    GmPanel,
    GmSpeed,
}

public enum BindGroup
{
    Movement,
    Combat,
    Hotbar,
    Windows,
    System,
}

public readonly record struct KeyChord(Key Key, bool Ctrl, bool Shift, bool Alt)
{
    public const string Unassigned = "Unassigned";

    public static readonly KeyChord Unbound = new(Key.None, false, false, false);

    public bool Assigned => Key != Key.None;

    public static KeyChord From(InputEventKey ev) => new(
        ev.Keycode != Key.None ? ev.Keycode : ev.PhysicalKeycode,
        ev.CtrlPressed, ev.ShiftPressed, ev.AltPressed);

    public string Text => Assigned
        ? (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + OS.GetKeycodeString(Key)
        : Unassigned;

    public static KeyChord Parse(string text)
    {
        string rest = text.Trim();
        bool ctrl = false, shift = false, alt = false;
        while (rest.Length > 1)
        {
            if (Strip(ref rest, "Ctrl+")) { ctrl = true; continue; }
            if (Strip(ref rest, "Shift+")) { shift = true; continue; }
            if (Strip(ref rest, "Alt+")) { alt = true; continue; }
            break;
        }
        if (rest.Length == 0
            || rest.Equals(Unassigned, StringComparison.OrdinalIgnoreCase)
            || rest.Equals("None", StringComparison.OrdinalIgnoreCase))
            return Unbound;
        var key = OS.FindKeycodeFromString(rest);
        return key == Key.None ? Unbound : new KeyChord(key, ctrl, shift, alt);
    }

    private static bool Strip(ref string text, string prefix)
    {
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        text = text[prefix.Length..].TrimStart();
        return true;
    }
}

public static partial class KeyBinds
{
    public sealed record Entry(KeyAction Action, BindGroup Group, string Label, KeyChord Default);

    private static KeyChord Chord(Key key) => new(key, false, false, false);

    private static KeyChord Ctrl(Key key) => new(key, true, false, false);

    private static KeyChord Shift(Key key) => new(key, false, true, false);

    private static readonly Entry[] Table =
    {
        new(KeyAction.MoveForward, BindGroup.Movement, "Move Forward", Chord(Key.W)),
        new(KeyAction.MoveBackward, BindGroup.Movement, "Move Backward", Chord(Key.S)),
        new(KeyAction.TurnLeft, BindGroup.Movement, "Turn Left", Chord(Key.A)),
        new(KeyAction.TurnRight, BindGroup.Movement, "Turn Right", Chord(Key.D)),
        new(KeyAction.AutoRun, BindGroup.Movement, "Auto Run", Chord(Key.E)),
        new(KeyAction.ToggleRun, BindGroup.Movement, "Walk / Run", Chord(Key.T)),
        new(KeyAction.ToggleRunAlt, BindGroup.Movement, "Walk / Run (alternate)", Chord(Key.Capslock)),
        new(KeyAction.Sit, BindGroup.Movement, "Sit / Stand", Chord(Key.C)),
        new(KeyAction.Interact, BindGroup.Movement, "Gather / Interact", Chord(Key.Space)),

        new(KeyAction.TargetHostile, BindGroup.Combat, "Target Nearest Enemy", Chord(Key.Z)),
        new(KeyAction.TargetFriendly, BindGroup.Combat, "Target Nearest Ally", Chord(Key.B)),
        new(KeyAction.AutoAttack, BindGroup.Combat, "Auto Attack", Chord(Key.R)),
        new(KeyAction.StealthCancel, BindGroup.Combat, "Cancel Stealth", Chord(Key.Insert)),
        new(KeyAction.PotionHp, BindGroup.Combat, "Health Potion", Chord(Key.None)),
        new(KeyAction.PotionMp, BindGroup.Combat, "Mana Potion", Chord(Key.None)),
        new(KeyAction.CameraTurn, BindGroup.Movement, "Face Camera Forward", Chord(Key.None)),
        new(KeyAction.GameMenu, BindGroup.System, "Game Menu", Chord(Key.None)),

        new(KeyAction.HotSlot1, BindGroup.Hotbar, "Slot 1", Chord(Key.Key1)),
        new(KeyAction.HotSlot2, BindGroup.Hotbar, "Slot 2", Chord(Key.Key2)),
        new(KeyAction.HotSlot3, BindGroup.Hotbar, "Slot 3", Chord(Key.Key3)),
        new(KeyAction.HotSlot4, BindGroup.Hotbar, "Slot 4", Chord(Key.Key4)),
        new(KeyAction.HotSlot5, BindGroup.Hotbar, "Slot 5", Chord(Key.Key5)),
        new(KeyAction.HotSlot6, BindGroup.Hotbar, "Slot 6", Chord(Key.Key6)),
        new(KeyAction.HotSlot7, BindGroup.Hotbar, "Slot 7", Chord(Key.Key7)),
        new(KeyAction.HotSlot8, BindGroup.Hotbar, "Slot 8", Chord(Key.Key8)),
        new(KeyAction.HotPageNext, BindGroup.Hotbar, "Next Page", Chord(Key.None)),
        new(KeyAction.HotPage1, BindGroup.Hotbar, "Page 1", Chord(Key.F1)),
        new(KeyAction.HotPage2, BindGroup.Hotbar, "Page 2", Chord(Key.F2)),
        new(KeyAction.HotPage3, BindGroup.Hotbar, "Page 3", Chord(Key.F3)),
        new(KeyAction.HotPage4, BindGroup.Hotbar, "Page 4", Chord(Key.F4)),
        new(KeyAction.HotPage5, BindGroup.Hotbar, "Page 5", Chord(Key.F5)),
        new(KeyAction.HotPage6, BindGroup.Hotbar, "Page 6", Chord(Key.F6)),
        new(KeyAction.HotPage7, BindGroup.Hotbar, "Page 7", Chord(Key.F7)),
        new(KeyAction.HotPage8, BindGroup.Hotbar, "Page 8", Chord(Key.F8)),

        new(KeyAction.Character, BindGroup.Windows, "Character", Chord(Key.U)),
        new(KeyAction.Inventory, BindGroup.Windows, "Inventory", Chord(Key.I)),
        new(KeyAction.Skills, BindGroup.Windows, "Skills", Chord(Key.K)),
        new(KeyAction.Quests, BindGroup.Windows, "Quest Journal", Chord(Key.F10)),
        new(KeyAction.Party, BindGroup.Windows, "Party", Chord(Key.P)),
        new(KeyAction.Friends, BindGroup.Windows, "Friends", Chord(Key.O)),
        new(KeyAction.Clan, BindGroup.Windows, "Clan", Chord(Key.X)),
        new(KeyAction.Messenger, BindGroup.Windows, "Messenger", Chord(Key.Minus)),
        new(KeyAction.MiniMap, BindGroup.Windows, "Mini Map", Chord(Key.N)),
        new(KeyAction.ZoneMap, BindGroup.Windows, "Zone Map", Chord(Key.M)),
        new(KeyAction.WorldMap, BindGroup.Windows, "World Map", Chord(Key.KpSubtract)),
        new(KeyAction.Helmet, BindGroup.Windows, "Show / Hide Helmet", Chord(Key.Q)),
        new(KeyAction.MyShop, BindGroup.Windows, "My Shop", Chord(Key.Y)),
        new(KeyAction.Pet, BindGroup.Windows, "Pet", Chord(Key.Bracketleft)),
        new(KeyAction.PowerUpStore, BindGroup.Windows, "Power-Up Store", Chord(Key.Bracketright)),
        new(KeyAction.Rebirth, BindGroup.Windows, "Master Rebirth", Ctrl(Key.Z)),
        new(KeyAction.King, BindGroup.Windows, "Nation King", Chord(Key.Period)),
        new(KeyAction.Siege, BindGroup.Windows, "Castle Siege War", Chord(Key.Comma)),
        new(KeyAction.NameChange, BindGroup.Windows, "Change Name", Chord(Key.Semicolon)),
        new(KeyAction.ClanWarehouse, BindGroup.Windows, "Clan Warehouse", Chord(Key.Slash)),
        new(KeyAction.VipWarehouse, BindGroup.Windows, "VIP Vault", Chord(Key.Home)),
        new(KeyAction.Report, BindGroup.Windows, "Sheriff Reports", Chord(Key.End)),
        new(KeyAction.Achievements, BindGroup.Windows, "Achievements", Chord(Key.F11)),
        new(KeyAction.Mail, BindGroup.Windows, "Mail", Chord(Key.L)),
        new(KeyAction.Lottery, BindGroup.Windows, "Lottery Event", Ctrl(Key.L)),
        new(KeyAction.Auction, BindGroup.Windows, "Auction House", Chord(Key.F12)),
        new(KeyAction.Attendance, BindGroup.Windows, "Attendance", Chord(Key.Pageup)),
        new(KeyAction.Bounty, BindGroup.Windows, "Bounty Board", Chord(Key.Pagedown)),
        new(KeyAction.Tournament, BindGroup.Windows, "Arena Tournament", Chord(Key.Delete)),
        new(KeyAction.Disguise, BindGroup.Windows, "Disguise", Chord(Key.Backspace)),
        new(KeyAction.Presets, BindGroup.Windows, "Presets", Chord(Key.Equal)),
        new(KeyAction.NationForce, BindGroup.Windows, "Nation Force", Chord(Key.Kp1)),
        new(KeyAction.Instances, BindGroup.Windows, "Instance Dungeons", Chord(Key.Kp2)),
        new(KeyAction.ChatRooms, BindGroup.Windows, "Chat Rooms", Chord(Key.Kp3)),
        new(KeyAction.Fortune, BindGroup.Windows, "Daily Fortune", Chord(Key.Kp4)),
        new(KeyAction.ItemCombine, BindGroup.Windows, "Item Combine", Chord(Key.Kp5)),
        new(KeyAction.Roulette, BindGroup.Windows, "Event Roulette", Chord(Key.Kp6)),
        new(KeyAction.FishingHall, BindGroup.Windows, "Fishing Hall of Fame", Chord(Key.Kp7)),
        new(KeyAction.TradeBoard, BindGroup.Windows, "Trade Board", Chord(Key.Quoteleft)),
        new(KeyAction.Duel, BindGroup.Windows, "Duel Lobby", Chord(Key.Kp8)),
        new(KeyAction.ItemExchange, BindGroup.Windows, "Item Exchange", Chord(Key.Kp9)),
        new(KeyAction.RingUpgrade, BindGroup.Windows, "Ring Upgrade", Chord(Key.Kp0)),
        new(KeyAction.Inn, BindGroup.Windows, "Inn", Chord(Key.KpMultiply)),
        new(KeyAction.EventQuests, BindGroup.Windows, "Event Quests", Chord(Key.KpDivide)),
        new(KeyAction.Genie, BindGroup.Windows, "Genie", Chord(Key.KpAdd)),
        new(KeyAction.DailyQuests, BindGroup.Windows, "Daily Quests", Chord(Key.KpPeriod)),
        new(KeyAction.Rentals, BindGroup.Windows, "Rentals", Chord(Key.Pause)),
        new(KeyAction.TownRecall, BindGroup.Windows, "Town Recall", Ctrl(Key.H)),

        new(KeyAction.PerformanceOverlay, BindGroup.System, "Performance Overlay", Shift(Key.F3)),
        new(KeyAction.GmPanel, BindGroup.System, "GM Panel", Chord(Key.Scrolllock)),
        new(KeyAction.GmSpeed, BindGroup.System, "GM Speed (hold)", Chord(Key.G)),
    };

    private static readonly Dictionary<KeyAction, KeyChord> Bound = new();
    private static bool _loaded;

    public static event Action? Changed;

    public static IReadOnlyList<Entry> All => Table;

    public static KeyChord Get(KeyAction action)
    {
        Load();
        return Bound.TryGetValue(action, out var chord) ? chord : KeyChord.Unbound;
    }

    public static Key PolledKey(KeyAction action) => Get(action).Key;

    public static void Set(KeyAction action, KeyChord chord)
    {
        Load();
        if (chord.Assigned)
            foreach (var entry in Table)
                if (entry.Action != action && Get(entry.Action) == chord)
                    Store(entry.Action, KeyChord.Unbound);
        Store(action, chord);
        Changed?.Invoke();
    }

    public static void Reset()
    {
        Load();
        foreach (var entry in Table) Bound[entry.Action] = entry.Default;
        foreach (var entry in Table) BoundPad[entry.Action] = PadDefault(entry.Action);
        Config.ClearKeyBinds();
        Config.ClearPadBinds();
        Changed?.Invoke();
    }

    private static void Store(KeyAction action, KeyChord chord)
    {
        Bound[action] = chord;
        Config.SaveKeyBind(action.ToString(), chord.Text);
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        foreach (var entry in Table)
        {
            string saved = Config.GetKeyBind(entry.Action.ToString());
            Bound[entry.Action] = saved.Length == 0 ? entry.Default : KeyChord.Parse(saved);
        }
        LoadPad();
    }
}
