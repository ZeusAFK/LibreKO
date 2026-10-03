using System;

namespace LibreKO.Domain;

public static class Paging
{
    public static int Step(int page, int delta, int pages) => pages <= 0 ? 0 : Math.Clamp(page + delta, 0, pages - 1);

    public static string Caption(int page, int pages) => $"Page {page + 1} / {Math.Max(1, pages)}";
}
