using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LibreKO.Domain;

public static class Vanguard
{
    public const int AppointedText = 43100;
    public const int FailedText = 43101;
    public const int ClosedText = 43102;
    public const int KarusSlainText = 43103;
    public const int ElMoradSlainText = 43104;
    public const double InviteSeconds = 10.0;
    public const int ListRows = 4;
    public const string ListColour = "adff2f";
    public const string AppointedColour = "ff0000";
    public const string EmptyRow = "-";

    private static readonly Regex FontOpen = new("<font color=@(#[0-9a-fA-F]{6})@>", RegexOptions.Compiled);
    private const string FontClose = "</font>";

    public static IReadOnlyList<string> ListLines(IReadOnlyList<string> names)
    {
        var lines = new List<string>(names.Count + 2) { "---Wanted List---" };
        for (int i = 0; i < names.Count; i++)
            lines.Add(RowLabel(i) + names[i]);
        lines.Add($"--- {OthersCount(names.Count)} user(s)---");
        return lines;
    }

    public static string RowLabel(int index) => index == 0 ? "A." : $"{index}.";

    public static int OthersCount(int count) => System.Math.Max(count - ListRows, 0);

    public static string GmLine(int index, string name, int x, int z) => $"{index}.{name}-{x}:{z}";

    public static int SlainText(int killerNation) => killerNation == Nations.ElMorad ? ElMoradSlainText : KarusSlainText;

    public static string Plain(string template) => FontOpen.Replace(template, "").Replace(FontClose, "");

    public static string Markup(string escapedTemplate) =>
        FontOpen.Replace(escapedTemplate, "[color=$1]").Replace(FontClose, "[/color]");

    public static bool ModeShown(bool uiOn, bool stallOpen) => uiOn && !stallOpen;
}
