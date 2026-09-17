using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class Patch : Entity
{
    public int FileId { get; set; }
    public string FileName { get; set; } = default!;
    public int FileSize { get; set; }
    public int FileChecksum { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<Patch>
    {
        public void Configure(EntityTypeBuilder<Patch> builder)
        {

            builder.HasKey(p => p.Id);

            builder.Property(p => p.FileId).IsRequired();
            builder.Property(p => p.FileName).IsRequired().HasMaxLength(255);
            builder.Property(p => p.FileSize).IsRequired();
            builder.Property(p => p.FileChecksum).IsRequired();
        }
    }
}
