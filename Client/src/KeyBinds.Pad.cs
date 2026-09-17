using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public enum PadMod
{
    None,
    LeftTrigger,
    RightTrigger,
}

public readonly record struct PadChord(JoyButton Button, PadMod Mod)
{
    public const string Unassigned = "Unassigned";

    private const string LeftPrefix = "LT+";
    private const string RightPrefix = "RT+";

    public static readonly PadChord Unbound = new(JoyButton.Invalid, PadMod.None);

    public bool Assigned => Button != JoyButton.Invalid || Mod != PadMod.None;

    public bool TriggerOnly => Button == JoyButton.Invalid && Mod != PadMod.None;

    public static PadChord From(InputEventJoypadButton ev, PadMod mod) => new(ev.ButtonIndex, mod);

    private static readonly (JoyButton Button, string Name)[] Names =
    {
        (JoyButton.A, "A"),
        (JoyButton.B, "B"),
        (JoyButton.X, "X"),
        (JoyButton.Y, "Y"),
        (JoyButton.LeftShoulder, "LB"),
        (JoyButton.RightShoulder, "RB"),
        (JoyButton.LeftStick, "L3"),
        (JoyButton.RightStick, "R3"),
        (JoyButton.DpadUp, "D-Up"),
        (JoyButton.DpadDown, "D-Down"),
        (JoyButton.DpadLeft, "D-Left"),
        (JoyButton.DpadRight, "D-Right"),
        (JoyButton.Start, "Start"),
        (JoyButton.Back, "Back"),
        (JoyButton.Guide, "Guide"),
    };

    public static string ButtonName(JoyButton button)
    {
        foreach (var (candidate, name) in Names)
            if (candidate == button) return name;
        return button.ToString();
    }

    public string Text => TriggerOnly ? Prefix.TrimEnd('+')
        : Assigned ? Prefix + ButtonName(Button) : Unassigned;

    private string Prefix => Mod switch
    {
        PadMod.LeftTrigger => LeftPrefix,
        PadMod.RightTrigger => RightPrefix,
        _ => "",
    };

    public static PadChord Parse(string text)
    {
        string rest = text.Trim();
        if (rest.Equals("LT", StringComparison.OrdinalIgnoreCase))
            return new PadChord(JoyButton.Invalid, PadMod.LeftTrigger);
        if (rest.Equals("RT", StringComparison.OrdinalIgnoreCase))
            return new PadChord(JoyButton.Invalid, PadMod.RightTrigger);

        var mod = PadMod.None;
        if (Strip(ref rest, LeftPrefix)) mod = PadMod.LeftTrigger;
        else if (Strip(ref rest, RightPrefix)) mod = PadMod.RightTrigger;
        if (rest.Length == 0
            || rest.Equals(Unassigned, StringComparison.OrdinalIgnoreCase)
            || rest.Equals("None", StringComparison.OrdinalIgnoreCase))
            return Unbound;
        foreach (var (candidate, name) in Names)
            if (rest.Equals(name, StringComparison.OrdinalIgnoreCase))
                return new PadChord(candidate, mod);
        return Enum.TryParse<JoyButton>(rest, ignoreCase: true, out var button)
            ? new PadChord(button, mod)
            : Unbound;
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
    public const float TriggerThreshold = 0.5f;
    public const float StickDeadzone = 0.22f;

    public const string TouchLeft = "gko_move_left";
    public const string TouchRight = "gko_move_right";
    public const string TouchUp = "gko_move_up";
    public const string TouchDown = "gko_move_down";
    public const string TouchLookLeft = "gko_look_left";
    public const string TouchLookRight = "gko_look_right";
    public const string TouchLookUp = "gko_look_up";
    public const string TouchLookDown = "gko_look_down";

    private static PadChord Pad(JoyButton button) => new(button, PadMod.None);

    private static PadChord Lt(JoyButton button) => new(button, PadMod.LeftTrigger);

    private static PadChord Rt(JoyButton button) => new(button, PadMod.RightTrigger);

    private static PadChord Trigger(PadMod mod) => new(JoyButton.Invalid, mod);

    private static readonly Dictionary<KeyAction, PadChord> PadDefaults = new()
    {
        [KeyAction.HotSlot1] = Pad(JoyButton.X),
        [KeyAction.HotSlot2] = Pad(JoyButton.Y),
        [KeyAction.HotSlot3] = Pad(JoyButton.A),
        [KeyAction.HotSlot4] = Pad(JoyButton.B),
        [KeyAction.HotPageNext] = Pad(JoyButton.RightShoulder),
        [KeyAction.AutoAttack] = Pad(JoyButton.LeftShoulder),

        [KeyAction.PotionHp] = Trigger(PadMod.RightTrigger),
        [KeyAction.PotionMp] = Trigger(PadMod.LeftTrigger),

        [KeyAction.CameraTurn] = Pad(JoyButton.RightStick),
        [KeyAction.TargetHostile] = Pad(JoyButton.LeftStick),
        [KeyAction.GameMenu] = Pad(JoyButton.Start),
        [KeyAction.Interact] = Pad(JoyButton.Back),

        [KeyAction.Inventory] = Pad(JoyButton.DpadUp),
        [KeyAction.Skills] = Pad(JoyButton.DpadDown),
        [KeyAction.Character] = Pad(JoyButton.DpadLeft),
        [KeyAction.Quests] = Pad(JoyButton.DpadRight),
    };

    private static readonly Dictionary<KeyAction, PadChord> BoundPad = new();

    public static PadChord PadDefault(KeyAction action) =>
        PadDefaults.TryGetValue(action, out var chord) ? chord : PadChord.Unbound;

    public static PadChord GetPad(KeyAction action)
    {
        Load();
        return BoundPad.TryGetValue(action, out var chord) ? chord : PadChord.Unbound;
    }

    public static void SetPad(KeyAction action, PadChord chord)
    {
        Load();
        if (chord.Assigned)
            foreach (var entry in Table)
                if (entry.Action != action && GetPad(entry.Action) == chord)
                    StorePad(entry.Action, PadChord.Unbound);
        StorePad(action, chord);
        Changed?.Invoke();
    }

    private static void StorePad(KeyAction action, PadChord chord)
    {
        BoundPad[action] = chord;
        Config.SavePadBind(action.ToString(), chord.Text);
    }

    private static void LoadPad()
    {
        foreach (var entry in Table)
        {
            string saved = Config.GetPadBind(entry.Action.ToString());
            BoundPad[entry.Action] = saved.Length == 0 ? PadDefault(entry.Action) : PadChord.Parse(saved);
        }
    }

    public static int PadDevice()
    {
        var pads = Input.GetConnectedJoypads();
        return pads.Count > 0 ? pads[0] : -1;
    }

    public static bool PadConnected => PadDevice() >= 0;

    public static PadMod ActiveMod()
    {
        int device = PadDevice();
        if (device < 0) return PadMod.None;
        if (Input.GetJoyAxis(device, JoyAxis.TriggerLeft) > TriggerThreshold) return PadMod.LeftTrigger;
        if (Input.GetJoyAxis(device, JoyAxis.TriggerRight) > TriggerThreshold) return PadMod.RightTrigger;
        return PadMod.None;
    }

    public static bool PadHeld(PadChord chord)
    {
        int device = PadDevice();
        if (device < 0 || !chord.Assigned) return false;
        if (chord.TriggerOnly) return TriggerHeld(chord.Mod);
        return chord.Mod == ActiveMod() && Input.IsJoyButtonPressed(device, chord.Button);
    }

    public static bool TriggerHeld(PadMod mod)
    {
        int device = PadDevice();
        if (device < 0 || mod == PadMod.None) return false;
        var axis = mod == PadMod.LeftTrigger ? JoyAxis.TriggerLeft : JoyAxis.TriggerRight;
        return Input.GetJoyAxis(device, axis) > TriggerThreshold;
    }

    public static bool Held(KeyAction action)
    {
        var key = PolledKey(action);
        if (key != Key.None && Input.IsKeyPressed(key)) return true;
        return PadHeld(GetPad(action));
    }

    public static Vector2 Stick(JoyAxis horizontal, JoyAxis vertical)
    {
        int device = PadDevice();
        if (device < 0) return Vector2.Zero;
        var raw = new Vector2(Input.GetJoyAxis(device, horizontal), Input.GetJoyAxis(device, vertical));
        return raw.Length() < StickDeadzone ? Vector2.Zero : raw;
    }

    public static Vector2 MoveStick()
    {
        var pad = Stick(JoyAxis.LeftX, JoyAxis.LeftY);
        if (pad != Vector2.Zero) return pad;
        return (Input.GetVector(TouchLeft, TouchRight, TouchUp, TouchDown)
                * Config.MoveStickSensitivity).LimitLength(1f);
    }

    public static Vector2 LookStick()
    {
        var pad = Stick(JoyAxis.RightX, JoyAxis.RightY);
        if (pad != Vector2.Zero) return pad;
        return Input.GetVector(TouchLookLeft, TouchLookRight, TouchLookUp, TouchLookDown)
               * Config.LookStickSensitivity;
    }
}
