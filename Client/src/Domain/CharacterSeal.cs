using System;
using LibreKO.Network;

namespace LibreKO.Domain;

public static class CharacterSeal
{
    public enum Mode { Seal, Unseal }

    public const int ScrollKind = 250;
    public const int RingKind = 160;
    public const int MaxCodeDigits = 8;
    public const ulong SheetRequestCooldownMs = 3000;

    public const int HelpText = 14030;
    public const int SealedText = 14031;
    public const int SealFailedText = 14032;
    public const int UnsealedText = 14033;
    public const int UnsealFailedText = 14034;
    public const int SealAskText = 14035;
    public const int UnsealAskText = 14036;
    public const int EmptySlotText = 14040;
    public const int CodeThreeText = 14043;
    public const int BoundGearText = 14044;
    public const int RentalGearText = 14045;
    public const int JuraidWarningText = 14050;
    public const int VoteWarningText = 43785;
    public const int JuraidText = 37026;
    public const int NoVaultPasswordText = 32150;
    public const int VaultLockedText = 1669;
    public const int WrongVaultPasswordText = 15113;
    public const int InvalidCharacterText = 1513;
    public const int TooSoonText = 19008;
    public const int DuplicateSerialText = 10760;
    public const int RingNotDestroyableText = 15104;
    public const int SheetFailedText = 7045;
    public const int RaceLabelText = 14028;
    public const int JobLabelText = 14029;

    public const int CodeThree = 3;
    public const int CodeJuraidVotes = 4;
    public const int CodeTooSoon = 7;
    public const int CodeNoVaultPassword = 20;
    public const int CodeVaultLocked = 21;
    public const int CodeWrongVaultPassword = 22;
    public const int RefusalBoundGear = -4;
    public const int RefusalRentalGear = -5;
    public const int RefusalJuraidWarning = -6;

    public static int KindFor(Mode mode) => mode == Mode.Seal ? ScrollKind : RingKind;

    public static bool Fits(Mode mode, int kind) => kind == KindFor(mode);

    public static bool SlotPickable(Mode mode, CharacterSummary slot, string selfName) => mode == Mode.Seal
        ? !slot.Empty && !string.Equals(slot.Name, selfName, StringComparison.OrdinalIgnoreCase)
        : slot.Empty;

    public static int SealRefusalText(CharacterSealResult result, int code) => result == CharacterSealResult.Extended
        ? code switch
        {
            RefusalBoundGear => BoundGearText,
            RefusalRentalGear => RentalGearText,
            RefusalJuraidWarning => JuraidWarningText,
            _ => SealFailedText,
        }
        : code switch
        {
            CodeThree => CodeThreeText,
            CodeJuraidVotes => VoteWarningText,
            CodeNoVaultPassword => NoVaultPasswordText,
            CodeVaultLocked => VaultLockedText,
            CodeWrongVaultPassword => WrongVaultPasswordText,
            _ => SealFailedText,
        };

    public static (string Title, string Body) HelpPanel(string help, Mode mode)
    {
        var sections = help.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int at = mode == Mode.Seal ? 0 : 2;
        return sections.Length >= 4
            ? (sections[at], sections[at + 1])
            : (mode == Mode.Seal ? "Seal" : "Unseal", "");
    }

    public static string SlotLine(CharacterSummary slot, string className, string emptyText) =>
        slot.Empty ? emptyText : $"{slot.Name}  LV{slot.Level}  {className}";

    public static string StatLine(int value, int bonus) => bonus != 0 ? $"{value}+{bonus}" : value.ToString();
}
