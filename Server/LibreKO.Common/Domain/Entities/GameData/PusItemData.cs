using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class PusItemData
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int Price { get; set; }
    public byte Category { get; set; }
    public bool Featured { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<PusItemData>
    {
        public void Configure(EntityTypeBuilder<PusItemData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}

public class PusDiscountData
{
    public const int NoEnd = 0;

    public int Id { get; set; }
    public int PusItemId { get; set; }
    public int Price { get; set; }
    public DateTime StartsAt { get; set; }
    public int Duration { get; set; }

    public DateTime? EndsAt => Duration == NoEnd ? null : StartsAt.AddHours(Duration);

    public bool ActiveAt(DateTime now) => now >= StartsAt && (EndsAt is not { } end || now < end);

    internal class EntityConfiguration : IEntityTypeConfiguration<PusDiscountData>
    {
        public void Configure(EntityTypeBuilder<PusDiscountData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.HasIndex(p => p.PusItemId);
        }
    }
}

public class PusCategoryData
{
    public byte Id { get; set; }
    public string Name { get; set; } = "";
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
