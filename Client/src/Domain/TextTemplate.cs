using System.Text;

namespace LibreKO.Domain;

public static class TextTemplate
{
    public static string Fill(string template, params object[] args)
    {
        var sb = new StringBuilder(template.Length + 16);
        int next = 0;
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c != '%' || i + 1 >= template.Length)
            {
                sb.Append(c);
                continue;
            }
            char spec = template[i + 1];
            if (spec == '%')
            {
                sb.Append('%');
                i++;
            }
            else if (spec is 's' or 'd' or 'u')
            {
                if (next < args.Length) sb.Append(args[next]);
                next++;
                i++;
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
