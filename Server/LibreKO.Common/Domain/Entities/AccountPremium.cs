using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class AccountPremium
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public byte Type { get; set; }
    public DateTime Expiry { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<AccountPremium>
    {
        public void Configure(EntityTypeBuilder<AccountPremium> builder)
        {
            builder.ToTable("AccountPremiums");
            builder.HasKey(p => p.Id);
            builder.HasIndex(p => new { p.AccountId, p.Type }).IsUnique();
        }
    }
}
