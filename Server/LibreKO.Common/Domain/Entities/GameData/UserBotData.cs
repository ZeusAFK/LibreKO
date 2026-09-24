using System;
using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class UserBotData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AccountNation Nation { get; set; } = AccountNation.None;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    internal class EntityConfiguration : IEntityTypeConfiguration<UserBotData>
    {
        public void Configure(EntityTypeBuilder<UserBotData> builder)
        {
            builder.ToTable("UserBots");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedOnAdd();
            builder.Property(p => p.Name).HasMaxLength(21).IsRequired();
            builder.HasIndex(p => p.Name).IsUnique();
        }
    }
}
