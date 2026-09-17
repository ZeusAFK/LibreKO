namespace LibreKO;

internal static class BbCode
{
    internal static string Esc(string s) => s.Replace("[", "[lb]");
}
