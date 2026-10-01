using System;

namespace LibreKO.Domain;

public enum BlindMode
{
    None,
    Blind,
    Unsight,
}

public readonly record struct BlindEffect(BlindMode Mode, double Until)
{
    public const int DisableTargetingBuffType = 20;
    public const int BlindBuffType = 21;
    public const int UnsightBuffType = 156;

    public const float HoldAlpha = 180f / 255f;
    public const double FadeSeconds = 2.0;
    public const float UnsightGrey = 200f / 255f;

    public static BlindMode ModeFor(int buffType) => buffType switch
    {
        BlindBuffType => BlindMode.Blind,
        DisableTargetingBuffType or UnsightBuffType => BlindMode.Unsight,
        _ => BlindMode.None,
    };

    public static BlindEffect Start(int buffType, double now, double seconds) =>
        new(ModeFor(buffType), now + Math.Max(0.0, seconds));

    public float Grey => Mode == BlindMode.Unsight ? UnsightGrey : 0f;

    public bool HidesOthers(double now) => Mode != BlindMode.None && now < Until;

    public float Alpha(double now)
    {
        if (Mode == BlindMode.None) return 0f;
        if (now < Until) return HoldAlpha;
        return (float)Math.Max(0.0, HoldAlpha * (1.0 - (now - Until) / FadeSeconds));
    }
}

public static class SecondaryBuff
{
    private const int ResultSlot = 1;
    private const int DurationSlot = 3;
    private const int SpeedSlot = 5;
    private const int NeutralSpeed = 100;
    private const int Applied = 1;

    public static bool IsEcho(SkillData.Skill s, short[] data) => IsEcho(s.Type1, s.Type2, s.Buff2Type, data);

    public static bool IsEcho(int type1, int type2, int buff2Type, short[] data) =>
        SkillData.HasSecondaryBuffFor(type1, type2, buff2Type) && data.Length > DurationSlot
        && data[ResultSlot] == Applied && data[DurationSlot] > 0;

    public static int Seconds(short[] data) => data.Length > DurationSlot ? data[DurationSlot] : 0;

    public static int SpeedPercent(short[] data) => data.Length > SpeedSlot ? data[SpeedSlot] : NeutralSpeed;
}
