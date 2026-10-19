using System.Linq;
using System.Text.RegularExpressions;

namespace LibreKO.Domain;

public static class RequiredNameChange
{
    public const int MaxLength = 20;
    public const int PromptText = 1918;
    public const string PromptFallback = "Duplicated ID in joint server.\n Change your ID to enter";
    public const int ConfirmText = 1919;
    public const string ConfirmFallback = "Would you like to change your ID to %s?";
    public const int NotAlphanumericText = 7698;
    public const string NotAlphanumericFallback = "Invalid Character. Non Alpha-Numeric characters are not allowed in the name.";
    public const int ChangedText = 1917;
    public const string ChangedFallback = "Change ID has been successful";
    public const int RefusedText = 1906;
    public const string RefusedFallback = "You cannot use this character ID.";
    public const int InvalidCharactersText = 7654;
    public const string InvalidCharactersFallback = "There are characters that cannot be used in the ID you entered. You can only use alphabet and numbers.";

    private static readonly Regex FontTag = new("</?font[^>]*>", RegexOptions.Compiled);

    public static bool IsAlphanumeric(string name) => name.Length > 0 && name.All(char.IsAsciiLetterOrDigit);

    public static string ConfirmLine(string template, string name) => FontTag.Replace(template, "").Replace("%s", name);

    public static (int Id, string Fallback)? ResultText(SelectingCharacterNameChangeResult result) => result switch
    {
        SelectingCharacterNameChangeResult.Success => (ChangedText, ChangedFallback),
        SelectingCharacterNameChangeResult.Failed => (RefusedText, RefusedFallback),
        SelectingCharacterNameChangeResult.InvalidCharacters => (InvalidCharactersText, InvalidCharactersFallback),
        _ => null,
    };
}
