using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class PetTransformData
{
    public int Id { get; set; }
    public int Material { get; set; }
    public int Result { get; set; }
    public short ModelId { get; set; }
    public short Size { get; set; }
    public short Weight { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<PetTransformData>
    {
        public void Configure(EntityTypeBuilder<PetTransformData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.HasIndex(p => p.Material);
        }
    }
}
