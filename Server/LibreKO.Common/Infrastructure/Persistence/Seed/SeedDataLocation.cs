namespace LibreKO.Common.Infrastructure.Persistence.Seed;

public static class SeedDataLocation
{
    public static string Root { get; set; } = AppContext.BaseDirectory;

    public static string DataPath(string fileName) =>
        Path.Combine(Root, "Seed", "Data", fileName);
}
