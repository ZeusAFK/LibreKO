using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class PusItemData
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Price { get; set; }
    public byte Category { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<PusItemData>
    {
        public void Configure(EntityTypeBuilder<PusItemData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}

public class PusCategoryData
{
    public byte Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public byte Status { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<PusCategoryData>
    {
        public void Configure(EntityTypeBuilder<PusCategoryData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}