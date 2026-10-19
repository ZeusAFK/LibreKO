using System.Text;

namespace LibreKO.Domain;

public static class ItemText
{
    public static IEnumerable<string> DescriptionLines(string desc)
    {
        desc = StripTags(desc).Replace('|', '\n').Replace("\r", "");
        foreach (var raw in desc.Split('\n'))
        {
            string s = raw.Trim();
            if (s.Length > 0) yield return s;
        }
    }

    public static string StripTags(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        bool inTag = false;
        foreach (char ch in value)
        {
            if (ch == '<') { inTag = true; continue; }
            if (ch == '>' && inTag) { inTag = false; continue; }
            if (!inTag) sb.Append(ch);
        }
        return sb.ToString().Replace("@#", "#").Replace("@", "");
    }
}
