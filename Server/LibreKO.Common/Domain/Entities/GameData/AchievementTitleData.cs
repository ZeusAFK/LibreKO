using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class AchievementTitleData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AchievementId { get; set; }

    public short Strength { get; set; }
    public short Hp { get; set; }
    public short Dexterity { get; set; }
    public short Intelligence { get; set; }
    public short Magic { get; set; }
    public short Attack { get; set; }
    public short Defence { get; set; }
    public short LoyaltyBonus { get; set; }
    public short ExpBonus { get; set; }
    public short ShortSwordAc { get; set; }
    public short JamadarAc { get; set; }
    public short SwordAc { get; set; }
    public short BlowAc { get; set; }
    public short AxeAc { get; set; }
    public short SpearAc { get; set; }
    public short ArrowAc { get; set; }
    public short FireBonus { get; set; }
    public short IceBonus { get; set; }
    public short LightBonus { get; set; }
    public short FireResist { get; set; }
    public short IceResist { get; set; }
    public short LightResist { get; set; }
    public short MagicResist { get; set; }
    public short CurseResist { get; set; }
    public short PoisonResist { get; set; }

    public bool HasBonus =>
        Strength != 0 || Hp != 0 || Dexterity != 0 || Intelligence != 0 || Magic != 0
        || Attack != 0 || Defence != 0 || LoyaltyBonus != 0 || ExpBonus != 0
        || ShortSwordAc != 0 || JamadarAc != 0 || SwordAc != 0 || BlowAc != 0
        || AxeAc != 0 || SpearAc != 0 || ArrowAc != 0
        || FireBonus != 0 || IceBonus != 0 || LightBonus != 0
        || FireResist != 0 || IceResist != 0 || LightResist != 0
        || MagicResist != 0 || CurseResist != 0 || PoisonResist != 0;

    public void Add(AchievementTitleData other)
    {
        Strength += other.Strength;
        Hp += other.Hp;
        Dexterity += other.Dexterity;
        Intelligence += other.Intelligence;
        Magic += other.Magic;
        Attack += other.Attack;
        Defence += other.Defence;
        LoyaltyBonus += other.LoyaltyBonus;
        ExpBonus += other.ExpBonus;
        ShortSwordAc += other.ShortSwordAc;
        JamadarAc += other.JamadarAc;
        SwordAc += other.SwordAc;
        BlowAc += other.BlowAc;
        AxeAc += other.AxeAc;
        SpearAc += other.SpearAc;
        ArrowAc += other.ArrowAc;
        FireBonus += other.FireBonus;
        IceBonus += other.IceBonus;
        LightBonus += other.LightBonus;
        FireResist += other.FireResist;
        IceResist += other.IceResist;
        LightResist += other.LightResist;
        MagicResist += other.MagicResist;
        CurseResist += other.CurseResist;
        PoisonResist += other.PoisonResist;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<AchievementTitleData>
    {
        public void Configure(EntityTypeBuilder<AchievementTitleData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.Property(p => p.Name).HasMaxLength(64);
            builder.Ignore(p => p.HasBonus);
        }
    }
}
