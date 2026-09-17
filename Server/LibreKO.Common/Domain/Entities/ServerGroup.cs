using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class ServerGroup : Entity
{
    public string Name { get; set; } = default!;

    public ICollection<Server> Servers { get; set; } = [];

    internal class ServerGroupConfiguration : IEntityTypeConfiguration<ServerGroup>
    {
        public void Configure(EntityTypeBuilder<ServerGroup> builder)
        {
            builder.HasKey(group => group.Id);
            builder.Property(group => group.Name).IsRequired().HasMaxLength(50);
        }
    }
}
