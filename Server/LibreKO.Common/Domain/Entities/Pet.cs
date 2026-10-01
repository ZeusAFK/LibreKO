using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class Pet
{
    public const int NameMaxLength = 15;
    public const int InventorySize = 4;
    public const byte StartLevel = 1;
    public const short MaxSatisfaction = 10000;
    public const short HatchedSatisfaction = 9000;

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Level { get; set; } = StartLevel;
    public short Hp { get; set; }
    public short Mp { get; set; }
    public short Satisfaction { get; set; } = HatchedSatisfaction;
    public long Exp { get; set; }
    public short ModelId { get; set; }
    public short Size { get; set; }
    public byte Class { get; set; }
    public byte[] Items { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    internal class EntityConfiguration : IEntityTypeConfiguration<Pet>
    {
        public void Configure(EntityTypeBuilder<Pet> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Name).HasMaxLength(NameMaxLength).IsRequired();
            builder.HasIndex(p => p.Name).IsUnique();
        }
    }
}
