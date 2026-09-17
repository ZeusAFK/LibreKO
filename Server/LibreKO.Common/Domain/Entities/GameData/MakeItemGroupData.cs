using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MakeItemGroupData
{
    public int GroupNum { get; set; }
    public string Items { get; set; } = string.Empty;

    public int[] GetItems()
    {
        if (string.IsNullOrWhiteSpace(Items))
            return [];

        var parts = Items.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var value) && value > 0)
                result.Add(value);
        }
        return [.. result];
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<MakeItemGroupData>
    {
        public void Configure(EntityTypeBuilder<MakeItemGroupData> builder)
        {
            builder.HasKey(p => p.GroupNum);
            builder.Property(p => p.GroupNum).ValueGeneratedNever();
            builder.Property(p => p.Items).HasColumnType("text");
        }
    }
}
