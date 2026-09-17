using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LibreKO.Domain;

public static class QuestMarkup
{
    private static readonly Regex FontTags = new(@"</?font\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Color = new("\\bcolor\\s*=\\s*[\"']?@?#([0-9a-f]{6})(?=[@\"'\\s>])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Plain(string text, string selfName) =>
        Substitute(FontTags.Replace(text, ""), selfName);

    public static string Rich(string text, string selfName)
    {
        var output = new StringBuilder();
        var colors = new Stack<bool>();
        int offset = 0;
        foreach (Match tag in FontTags.Matches(text))
        {
            output.Append(Escape(Substitute(text[offset..tag.Index], selfName)));
            if (tag.Value.StartsWith("</"))
            {
                if (colors.TryPop(out bool colored) && colored) output.Append("[/color]");
            }
            else
            {
                var color = Color.Match(tag.Value);
                colors.Push(color.Success);
                if (color.Success) output.Append("[color=#").Append(color.Groups[1].Value).Append(']');
            }
            offset = tag.Index + tag.Length;
        }
        output.Append(Escape(Substitute(text[offset..], selfName)));
        while (colors.TryPop(out bool colored))
            if (colored) output.Append("[/color]");
        return output.ToString();
    }

    private static string Substitute(string text, string selfName) =>
        text.Replace("<selfname>", selfName).Replace('|', '\n');

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
