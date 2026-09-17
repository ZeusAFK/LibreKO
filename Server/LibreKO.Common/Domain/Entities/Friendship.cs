using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class Friendship
{
    public const int MaxFriends = 24;

    public int CharacterId { get; set; }
    public int FriendCharacterId { get; set; }
    public DateTime AddedAt { get; set; }

    public Character? Character { get; set; }
    public Character? Friend { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<Friendship>
    {
        public void Configure(EntityTypeBuilder<Friendship> builder)
        {
            builder.HasKey(f => new { f.CharacterId, f.FriendCharacterId });

            builder.Property(f => f.AddedAt).IsRequired();

            builder.HasIndex(f => f.CharacterId);

            builder.HasOne(f => f.Character)
                .WithMany()
                .HasForeignKey(f => f.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting a character also removes everyone else's entry pointing at them, but MySQL
            // rejects two cascading paths into the same table, so this side is cleaned up in code.
            builder.HasOne(f => f.Friend)
                .WithMany()
                .HasForeignKey(f => f.FriendCharacterId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
