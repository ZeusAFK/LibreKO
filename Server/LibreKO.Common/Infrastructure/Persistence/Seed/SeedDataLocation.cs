namespace LibreKO.Common.Infrastructure.Persistence.Seed;

public static class SeedDataLocation
{
    public static string Root { get; set; } = AppContext.BaseDirectory;

    public static string DataPath(string fileName) => DataPath(Root, fileName);

    public static string DataPath(string root, string fileName) =>
        Path.Combine(root, "Seed", "Data", fileName);
}
