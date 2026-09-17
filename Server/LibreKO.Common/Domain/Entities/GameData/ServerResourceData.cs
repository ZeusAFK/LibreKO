using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ServerResourceData
{
    public int ResourceId { get; set; }
    public string Resource { get; set; } = string.Empty;

    internal class EntityConfiguration : IEntityTypeConfiguration<ServerResourceData>
    {
        public void Configure(EntityTypeBuilder<ServerResourceData> builder)
        {
            builder.HasKey(p => p.ResourceId);
            builder.Property(p => p.ResourceId).ValueGeneratedNever();
        }
    }
}
