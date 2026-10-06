using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LibreKO.Domain;

public static class NationTreasury
{
    public const short KingView = 1;
    public const short CitizenView = 2;

    public const int KingsSceptre = 910074000;
    public const int TaxRateMin = 0;
    public const int TaxRateMax = 5;
    public const int IntroLimit = 200;

    public const int KarusKingText = 11399;
    public const int ElMoradKingText = 11400;
    public const int KarusCitizensText = 11403;
    public const int ElMoradCitizensText = 11404;
    public const int CitizenTreasuryText = 11405;
    public const int CheckTreasuryText = 11412;
    public const int ConfirmFundText = 11406;
    public const int FundCollectedText = 11407;
    public const int TaxRateSetText = 11359;
    public const int IntroWelcomeText = 11413;
    public const int IntroTooLongText = 11416;
    public const int IntroSavedText = 3429;
    public const int IntroFailedText = 6305;
    public const int TreasuryUsedText = 11417;
    public const int TreasuryLeftText = 11418;

    public static int Heading(short view, int nation) => (view, nation) switch
    {
        (KingView, Nations.Karus) => KarusKingText,
        (KingView, _) => ElMoradKingText,
        (_, Nations.Karus) => KarusCitizensText,
        _ => ElMoradCitizensText,
    };

    public static bool HasSceptre(IEnumerable<int> bagItems) => bagItems.Contains(KingsSceptre);

    public static int StepTaxRate(int rate, int delta) => Math.Clamp(rate + delta, TaxRateMin, TaxRateMax);

    public static string Coins(long value) => value.ToString("n0", CultureInfo.InvariantCulture);

    public static int IntroRefusal(string text) => text.Length > IntroLimit ? IntroTooLongText : 0;

    public static string IntroOrWelcome(string text, string welcome) => text.Length > 0 ? text : welcome;
}
