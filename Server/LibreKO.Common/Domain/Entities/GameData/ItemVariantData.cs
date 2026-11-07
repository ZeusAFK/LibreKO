using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemVariantData
{
    public byte VariantSet { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MadeFor { get; set; }
    public byte Rarity { get; set; }
    public short Damage { get; set; }
    public short Hitrate { get; set; }
    public short Evasionrate { get; set; }
    public short Duration { get; set; }
    public short PriceMultiplier { get; set; }
    public short Ac { get; set; }
    public short DaggerAc { get; set; }
    public short JamadarAc { get; set; }
    public short SwordAc { get; set; }
    public short MaceAc { get; set; }
    public short AxeAc { get; set; }
    public short SpearAc { get; set; }
    public short BowAc { get; set; }
    public short FireDamage { get; set; }
    public short IceDamage { get; set; }
    public short LightningDamage { get; set; }
    public short PoisonDamage { get; set; }
    public short HpDrain { get; set; }
    public short MpDamage { get; set; }
    public short MpDrain { get; set; }
    public short MirrorDamage { get; set; }
    public short GradeClass { get; set; }
    public short Str { get; set; }
    public short Sta { get; set; }
    public short Dex { get; set; }
    public short Intel { get; set; }
    public short Cha { get; set; }
    public short MaxHp { get; set; }
    public short MaxMp { get; set; }
    public short FireR { get; set; }
    public short ColdR { get; set; }
    public short LightningR { get; set; }
    public short MagicR { get; set; }
    public short PoisonR { get; set; }
    public short CurseR { get; set; }
    public int Plus { get; set; }
    public short ReqLevel { get; set; }
    public short ReqTitle { get; set; }
    public short ReqStr { get; set; }
    public short ReqSta { get; set; }
    public short ReqDex { get; set; }
    public short ReqIntel { get; set; }
    public short ReqCha { get; set; }
    public short UnbindStones { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<ItemVariantData>
    {
        public void Configure(EntityTypeBuilder<ItemVariantData> builder)
        {
            builder.HasKey(p => new { p.VariantSet, p.Id });
        }
    }
}
