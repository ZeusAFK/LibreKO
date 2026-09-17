namespace LibreKO.Quests.Localization;

public sealed record PoEntry(string MsgId, string MsgStr, IReadOnlyList<string> References);

public static class PoFile
{
    public static IReadOnlyList<PoEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<PoEntry>();
        var references = new List<string>();
        string? id = null;
        var text = new System.Text.StringBuilder();
        var reading = Reading.None;

        void Flush()
        {
            if (id is not null)
                entries.Add(new PoEntry(id, text.ToString(), [.. references]));
            id = null;
            text.Clear();
            references.Clear();
            reading = Reading.None;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (line.StartsWith("#:", StringComparison.Ordinal))
            {
                references.Add(line[2..].Trim());
                continue;
            }

            if (line.StartsWith('#'))
                continue;

            if (line.StartsWith("msgid ", StringComparison.Ordinal))
            {
                if (reading == Reading.MsgStr)
                    Flush();
                id = Unquote(line[6..]);
                reading = Reading.MsgId;
                continue;
            }

            if (line.StartsWith("msgstr ", StringComparison.Ordinal))
            {
                text.Clear();
                text.Append(Unquote(line[7..]));
                reading = Reading.MsgStr;
                continue;
            }

            if (!line.StartsWith('"'))
                continue;

            var continuation = Unquote(line);
            if (reading == Reading.MsgId)
                id += continuation;
            else if (reading == Reading.MsgStr)
                text.Append(continuation);
        }

        Flush();
        return entries;
    }

    public static string Render(IEnumerable<PoEntry> entries, string languageName)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("msgid \"\"");
        builder.AppendLine("msgstr \"\"");
        builder.AppendLine("\"Project-Id-Version: KO quest scripts\\n\"");
        builder.AppendLine($"\"Language: {languageName}\\n\"");
        builder.AppendLine("\"MIME-Version: 1.0\\n\"");
        builder.AppendLine("\"Content-Type: text/plain; charset=UTF-8\\n\"");
        builder.AppendLine("\"Content-Transfer-Encoding: 8bit\\n\"");

        foreach (var entry in entries)
        {
            builder.AppendLine();
            foreach (var reference in entry.References)
                builder.AppendLine($"#: {reference}");
            builder.AppendLine($"msgid {Quote(entry.MsgId)}");
            builder.AppendLine($"msgstr {Quote(entry.MsgStr)}");
        }

        return builder.ToString();
    }

    private static string Quote(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", string.Empty)
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
        return $"\"{escaped}\"";
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
            return value;

        var body = value[1..^1];
        var builder = new System.Text.StringBuilder(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '\\' || i + 1 >= body.Length)
            {
                builder.Append(body[i]);
                continue;
            }

            i++;
            builder.Append(body[i] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                '"' => '"',
                _ => body[i],
            });
        }
        return builder.ToString();
    }

    private enum Reading
    {
        None,
        MsgId,
        MsgStr,
    }
}
