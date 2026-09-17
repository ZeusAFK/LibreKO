using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class KingElectionList
{
    public int Id { get; set; }
    public byte Type { get; set; }
    public byte Nation { get; set; }
    public short Knights { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Money { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<KingElectionList>
    {
        public void Configure(EntityTypeBuilder<KingElectionList> builder)
        {
            builder.HasKey(p => p.Id);
        }
    }
}
