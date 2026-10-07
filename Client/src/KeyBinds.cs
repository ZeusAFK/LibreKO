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

    HotSlot1, HotSlot2, HotSlot3, HotSlot4, HotSlot5, HotSlot6, HotSlot7, HotSlot8, HotSlot9, HotSlot10,
    HotPage1, HotPage2, HotPage3, HotPage4, HotPage5, HotPage6, HotPage7, HotPage8,

    Character,
    Inventory,
    Skills,
    Quests,
    Party,
    Friends,
    Messenger,
    MiniMap,
    ZoneMap,
    WorldMap,
    Helmet,
    Interact,
    MyShop,
    Pet,
    PowerUpStore,
    ClanWarehouse,
    VipWarehouse,
    Achievements,
    Mail,
    Lottery,
    Attendance,
    Bounty,
    Tournament,
    Presets,
    NationForce,
    ChatRooms,
    Roulette,
    FishingHall,
    Genie,
    TownRecall,

    HotPageNext,
    PotionHp,
    PotionMp,
    CameraTurn,
    GameMenu,

    PerformanceOverlay,
    PerfSkipPoses,
    PerfSkipMove,
    PerfSkipPlates,
    PerfSkipEmitters,
    PerfSkipAudio,
    PerfSkipFx,
    PerfSkipLamps,
    PerfCensus,
    PerfAutoBisect,
    PerfEngineProfile,
    PerfHudSweep,
    PerfSceneSweep,
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
        new(KeyAction.HotSlot9, BindGroup.Hotbar, "Slot 9", Chord(Key.Key9)),
        new(KeyAction.HotSlot10, BindGroup.Hotbar, "Slot 10", Chord(Key.Key0)),
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
        new(KeyAction.Messenger, BindGroup.Windows, "Messenger", Chord(Key.Minus)),
        new(KeyAction.MiniMap, BindGroup.Windows, "Mini Map", Chord(Key.N)),
        new(KeyAction.ZoneMap, BindGroup.Windows, "Zone Map", Chord(Key.M)),
        new(KeyAction.WorldMap, BindGroup.Windows, "World Map", Chord(Key.KpSubtract)),
        new(KeyAction.Helmet, BindGroup.Windows, "Show / Hide Helmet", Chord(Key.Q)),
        new(KeyAction.MyShop, BindGroup.Windows, "My Shop", Chord(Key.Y)),
        new(KeyAction.Pet, BindGroup.Windows, "Pet", Chord(Key.Bracketleft)),
        new(KeyAction.PowerUpStore, BindGroup.Windows, "Power-Up Store", Chord(Key.Bracketright)),
        new(KeyAction.ClanWarehouse, BindGroup.Windows, "Clan Warehouse", Chord(Key.Slash)),
        new(KeyAction.VipWarehouse, BindGroup.Windows, "VIP Vault", Chord(Key.Home)),
        new(KeyAction.Achievements, BindGroup.Windows, "Achievements", Chord(Key.F11)),
        new(KeyAction.Mail, BindGroup.Windows, "Mail", Chord(Key.L)),
        new(KeyAction.Lottery, BindGroup.Windows, "Lottery Event", Ctrl(Key.L)),
        new(KeyAction.Attendance, BindGroup.Windows, "Attendance", Chord(Key.Pageup)),
        new(KeyAction.Bounty, BindGroup.Windows, "Bounty Board", Chord(Key.Pagedown)),
        new(KeyAction.Tournament, BindGroup.Windows, "Arena Tournament", Chord(Key.Delete)),
        new(KeyAction.Presets, BindGroup.Windows, "Presets", Chord(Key.Equal)),
        new(KeyAction.NationForce, BindGroup.Windows, "Nation Force", Chord(Key.Kp1)),
        new(KeyAction.ChatRooms, BindGroup.Windows, "Chat Rooms", Chord(Key.Kp3)),
        new(KeyAction.Roulette, BindGroup.Windows, "Event Roulette", Chord(Key.Kp6)),
        new(KeyAction.FishingHall, BindGroup.Windows, "Fishing Hall of Fame", Chord(Key.Kp7)),
        new(KeyAction.Genie, BindGroup.Windows, "Genie", Chord(Key.KpAdd)),
        new(KeyAction.TownRecall, BindGroup.Windows, "Town Recall", Ctrl(Key.H)),

        new(KeyAction.PerformanceOverlay, BindGroup.System, "Performance Overlay", Shift(Key.F3)),
        new(KeyAction.PerfSkipPoses, BindGroup.System, "Perf bisect: crowd poses", Shift(Key.F5)),
        new(KeyAction.PerfSkipMove, BindGroup.System, "Perf bisect: entity movement", Shift(Key.F6)),
        new(KeyAction.PerfSkipPlates, BindGroup.System, "Perf bisect: name plates", Shift(Key.F7)),
        new(KeyAction.PerfSkipEmitters, BindGroup.System, "Perf bisect: new emitters", Shift(Key.F8)),
        new(KeyAction.PerfSkipAudio, BindGroup.System, "Perf bisect: world audio", Shift(Key.F9)),
        new(KeyAction.PerfSkipFx, BindGroup.System, "Perf bisect: new effects", Shift(Key.F10)),
        new(KeyAction.PerfSkipLamps, BindGroup.System, "Perf bisect: lamp lights", Shift(Key.F11)),
        new(KeyAction.PerfCensus, BindGroup.System, "Perf: write a scene census", Shift(Key.F12)),
        new(KeyAction.PerfAutoBisect, BindGroup.System, "Perf: run the automatic bisect", new KeyChord(Key.F12, true, true, false)),
        new(KeyAction.PerfEngineProfile, BindGroup.System, "Perf: engine pass timings", new KeyChord(Key.F11, true, true, false)),
        new(KeyAction.PerfHudSweep, BindGroup.System, "Perf: sweep the HUD layers", new KeyChord(Key.F10, true, true, false)),
        new(KeyAction.PerfSceneSweep, BindGroup.System, "Perf: sweep the scene and render features", new KeyChord(Key.F9, true, true, false)),
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
        foreach (var (action, chord) in Resolve(Table, SavedChord))
            Bound[action] = chord;
        LoadPad();
    }

    private static KeyChord? SavedChord(KeyAction action)
    {
        string text = Config.GetKeyBind(action.ToString());
        return text.Length == 0 ? null : KeyChord.Parse(text);
    }

    internal static Dictionary<KeyAction, KeyChord> Resolve(IReadOnlyList<Entry> table, Func<KeyAction, KeyChord?> saved)
    {
        var bound = new Dictionary<KeyAction, KeyChord>();
        var chosen = new HashSet<KeyChord>();
        foreach (var entry in table)
        {
            if (saved(entry.Action) is not { } chord) continue;
            bound[entry.Action] = chord;
            if (chord.Assigned) chosen.Add(chord);
        }
        foreach (var entry in table)
            if (!bound.ContainsKey(entry.Action))
                bound[entry.Action] = chosen.Contains(entry.Default) ? KeyChord.Unbound : entry.Default;
        return bound;
    }
}
