using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class Server : Entity
{
    public string Name { get; set; } = default!;
    public int? GroupId { get; set; }
    public ServerCategory Category { get; set; }
    public string IpAddress { get; set; } = default!;
    public string LanIpAddress { get; set; } = default!;
    public int Port { get; set; }
    public int OnlinePlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int FreePlayerCap { get; set; }
    public string Status { get; set; } = default!;

    public ServerGroup? Group { get; set; }

    internal class ServerListConfiguration : IEntityTypeConfiguration<Server>
    {
        public void Configure(EntityTypeBuilder<Server> builder)
        {

            builder.HasKey(s => s.Id);

            builder.Property(s => s.Name).IsRequired().HasMaxLength(100);
            builder.Property(s => s.Category).IsRequired();
            builder.Property(s => s.FreePlayerCap);
            builder.Property(s => s.IpAddress).IsRequired().HasMaxLength(15);
            builder.Property(s => s.LanIpAddress).IsRequired().HasMaxLength(15);
            builder.Property(s => s.Port).IsRequired();
            builder.Property(s => s.OnlinePlayers);
            builder.Property(s => s.MaxPlayers);
            builder.Property(s => s.Status).HasMaxLength(50);

            builder.HasOne(s => s.Group)
                .WithMany(group => group.Servers)
                .HasForeignKey(s => s.GroupId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

}
