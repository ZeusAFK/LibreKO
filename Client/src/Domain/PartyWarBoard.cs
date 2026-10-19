using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public readonly record struct WarBoardShare(int MemberId, int Share);

public readonly record struct WarBoardReply(PartyWarBoardTarget Target, PartyBoardMeterMode Mode, IReadOnlyList<WarBoardShare> Shares);

public static class PartyWarBoard
{
    public const float MinAlpha = 0.25f;

    public static PartyBoardMeterMode SavedMode(PartyBoardMeterMode value, PartyBoardMeterMode fallback) =>
        value is PartyBoardMeterMode.Off or PartyBoardMeterMode.Damage or PartyBoardMeterMode.Healing ? value : fallback;

    public static float SavedAlpha(float value) => float.IsFinite(value) ? System.Math.Clamp(value, MinAlpha, 1f) : 1f;
    public const int ShareScale = 1000;
    public const int MaxRows = 8;
    public const double RowToggleSeconds = 1.0;
    public const double BoardTabSeconds = 5.0;
    public const int TextAvailableIn = 16820;
    private const int PriestFamily = 4;
    private const int TenthsPerPercent = 10;

    public static PartyBoardMeterMode NextRowMode(PartyBoardMeterMode mode) => mode switch
    {
        PartyBoardMeterMode.Off => PartyBoardMeterMode.Damage,
        PartyBoardMeterMode.Damage => PartyBoardMeterMode.Healing,
        _ => PartyBoardMeterMode.Off,
    };

    public static float Fill(int share) => System.Math.Clamp(share / (float)ShareScale, 0f, 1f);

    public static string Percent(int share) =>
        share <= 0 ? "0.0%" : $"{share / TenthsPerPercent}.{share % TenthsPerPercent}%";

    public static string RowName(int place, string name) => $"{place}. {name}";

    public static IReadOnlyList<WarBoardShare> Ranked(IEnumerable<WarBoardShare> shares) =>
        shares.OrderByDescending(s => s.Share).Take(MaxRows).ToList();

    public static PartyBoardMeterMode DefaultBoardMode(int classCode) =>
        CharacterClassCatalog.Family(classCode) == PriestFamily ? PartyBoardMeterMode.Healing : PartyBoardMeterMode.Damage;

    public static double SecondsLeft(double lastSent, double now, double limit) =>
        System.Math.Max(0, lastSent + limit - now);

    public static int CooldownShown(bool pending, double secondsLeft) =>
        pending && secondsLeft > 0 ? (int)System.Math.Ceiling(secondsLeft) : 0;
}
