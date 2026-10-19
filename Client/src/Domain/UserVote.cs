using System.Text;

namespace LibreKO.Domain;

public static class UserVote
{
    public const int Reasons = 4;
    public const byte ReasonOther = 4;
    public const int OtherTextMaxBytes = 60;
    public const int OtherTextMaxLetters = 30;
    public const double CooldownSeconds = 300;
    public const double AnswerSeconds = 30;

    public const int TitleText = 43781;
    public const int AskText = 43774;
    public const int ChooseReasonText = 43786;
    public const int CostText = 43787;
    public const int WarnedText = 43778;
    public const int VoidedText = 43779;
    public const int NoticeTitleText = 43770;
    public const int NoticeReceivedText = 43771;
    public const int NoticeCumulativeText = 43772;
    public const int NoticeLimitText = 43773;
    public const int EntryBarredText = 43784;
    public const int JuraidNameText = 37026;
    public const int NotHereText = 43775;
    public const int TextTooLongText = 10433;
    public const int AvailableInText = 16820;
    private const int FirstReasonText = 43790;

    public static int ReasonText(int reason) => FirstReasonText + reason - 1;

    public static bool TextFits(string text) => Encoding.UTF8.GetByteCount(text) <= OtherTextMaxBytes;

    public static double CooldownLeft(double sentAt, double now) => System.Math.Max(0, sentAt + CooldownSeconds - now);

    public static int RefusalText(short result) => result switch
    {
        -1 => NotHereText,
        -2 => 6518,
        -3 => 43776,
        -4 => 43783,
        -5 => 43777,
        -6 => 43780,
        -7 => TextTooLongText,
        -8 => 1702,
        _ => VoidedText,
    };
}
