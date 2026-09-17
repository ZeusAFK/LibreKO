using System.Buffers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace LibreKO.Common.Infrastructure.Persistence.Seed;

public static class SeedFingerprint
{
    private const int ReadBufferSize = 1 << 20;

    public static async Task<string> ComputeAsync<T>(IEntitySeed<T> entitySeed, IReadOnlyList<string> seedPaths)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(Encoding.UTF8.GetBytes(DescribeShape(entitySeed)));

        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        try
        {
            foreach (var seedPath in seedPaths)
            {
                await using var stream = new FileStream(
                    seedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    ReadBufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);

                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, ReadBufferSize))) > 0)
                    hasher.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Convert.ToHexString(hasher.GetHashAndReset());
    }

    private static string DescribeShape<T>(IEntitySeed<T> entitySeed)
    {
        var properties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => $"{p.Name}:{p.PropertyType.FullName}")
            .OrderBy(p => p, StringComparer.Ordinal);

        return string.Join('|',
            entitySeed.GetType().FullName,
            typeof(T).FullName,
            entitySeed.PerformInsert,
            entitySeed.PerformUpdate,
            entitySeed.PerformDelete,
            string.Join(',', entitySeed.PropertiesToExclude.OrderBy(p => p, StringComparer.Ordinal)),
            string.Join(',', entitySeed.PropertiesToUpdate.OrderBy(p => p, StringComparer.Ordinal)),
            string.Join(',', properties));
    }
}
