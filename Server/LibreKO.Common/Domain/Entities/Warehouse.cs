using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class Warehouse : Entity
{
    public int AccountId { get; set; }
    public byte[] Items { get; set; } = [];
    public int Money { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<Warehouse>
    {
        public void Configure(EntityTypeBuilder<Warehouse> builder)
        {

            builder.HasKey(entry => entry.Id);

            builder.Property(entry => entry.AccountId).IsRequired();
            builder.Property(entry => entry.Items).IsRequired();
            builder.Property(entry => entry.Money).IsRequired();

            builder.HasOne<Account>()
                .WithOne()
                .HasForeignKey<Warehouse>(entry => entry.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(entry => entry.AccountId).IsUnique();
        }
    }
}
