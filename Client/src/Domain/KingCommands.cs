using System;

namespace LibreKO.Domain;

public enum KingCommandKind
{
    RoyalOrder,
    Prize,
    ExperiencePoint,
    DropRate,
    Weather,
}

public readonly record struct KingCommand(KingCommandKind Kind, string Text, string Target, int Amount, byte Weather);

public static class KingCommands
{
    public const int RoyalOrderText = 9400;
    public const int PrizeText = 9401;
    public const int ExperienceText = 9402;
    public const int DropRateText = 9403;
    public const int RainText = 9404;
    public const int SnowText = 9405;
    public const int ClearText = 9406;

    public const int OnlyKingsText = 11300;
    public const int LimitText = 11304;
    public const int NoSuchIdText = 11305;
    public const int TooManyText = 11306;
    public const int TreasuryText = 11408;
    public const int RoyalOrderLineText = 19346;
    public const int KingTitleText = 6804;
    public const int RoyalOrderMaxLength = 255;

    public static KingCommand? Parse(string word, string args, Func<int, string, string> text)
    {
        bool Is(int id, string fallback) => string.Equals(word, text(id, fallback), StringComparison.OrdinalIgnoreCase);
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int Number(int index) => parts.Length > index && int.TryParse(parts[index], out int value) ? value : 0;

        if (Is(RoyalOrderText, "RoyalOrder"))
            return new KingCommand(KingCommandKind.RoyalOrder, args.Length > RoyalOrderMaxLength ? args[..RoyalOrderMaxLength] : args, "", 0, 0);
        if (Is(PrizeText, "Prize"))
            return new KingCommand(KingCommandKind.Prize, "", parts.Length > 0 ? parts[0] : "", Number(1), 0);
        if (Is(ExperienceText, "ExperiencePoint"))
            return new KingCommand(KingCommandKind.ExperiencePoint, "", "", Number(0), 0);
        if (Is(DropRateText, "DropRate"))
            return new KingCommand(KingCommandKind.DropRate, "", "", Number(0), 0);
        if (Is(RainText, "Rain"))
            return new KingCommand(KingCommandKind.Weather, "", "", Number(0), (byte)KingWeather.Rain);
        if (Is(SnowText, "Snow"))
            return new KingCommand(KingCommandKind.Weather, "", "", Number(0), (byte)KingWeather.Snow);
        if (Is(ClearText, "Clear"))
            return new KingCommand(KingCommandKind.Weather, "", "", Number(0), (byte)KingWeather.Clear);
        return null;
    }

    public static int ResultText(KingEventSubOpcode kingEvent, short result) => kingEvent switch
    {
        KingEventSubOpcode.Prize or KingEventSubOpcode.Fugitive => result switch
        {
            -1 => OnlyKingsText,
            -2 => NoSuchIdText,
            -3 => TooManyText,
            -4 => TreasuryText,
            _ => 0,
        },
        KingEventSubOpcode.Weather => result switch
        {
            -1 => OnlyKingsText,
            -3 => TreasuryText,
            _ => 0,
        },
        _ => result switch
        {
            -1 => OnlyKingsText,
            -2 => LimitText,
            -3 => TreasuryText,
            _ => 0,
        },
    };
}
