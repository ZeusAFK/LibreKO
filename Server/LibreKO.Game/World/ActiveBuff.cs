using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public class ActiveBuff
{
    public int MagicId { get; set; }
    public int CasterId { get; set; }
    public long ExpireTicks { get; set; }
    public short Duration { get; set; }
    public BuffType BuffType { get; set; }
    public int SpecialAmount { get; set; } // Type-specific value (ExpPct, SpecialAmount from MagicType4Data)

    // Stat modifiers from MagicType4Data
    public short BonusAc { get; set; }
    public short BonusAcPct { get; set; }
    public short BonusAttack { get; set; }
    public short BonusMagicAttack { get; set; }
    public int BonusMaxHp { get; set; }
    public byte BonusMaxHpPct { get; set; }
    public int BonusMaxMp { get; set; }
    public byte BonusMaxMpPct { get; set; }
    public short BonusHitRate { get; set; }
    public short BonusAvoidRate { get; set; }
    public short BonusStr { get; set; }
    public short BonusSta { get; set; }
    public short BonusDex { get; set; }
    public short BonusIntel { get; set; }
    public short BonusCha { get; set; }
    public short BonusFireR { get; set; }
    public short BonusColdR { get; set; }
    public short BonusLightningR { get; set; }
    public short BonusMagicR { get; set; }
    public short BonusPoisonR { get; set; }
    public short BonusDiseaseR { get; set; }
    public short BonusSpeed { get; set; }
    public short BonusAttackSpeed { get; set; }

    public bool IsExpired => DateTime.UtcNow.Ticks > ExpireTicks;

    public bool HasStatModifiers =>
        BonusAc != 0 || BonusAttack != 0 || BonusMaxHp != 0 || BonusMaxHpPct != 0 ||
        BonusMaxMp != 0 || BonusMaxMpPct != 0 || BonusStr != 0 || BonusSta != 0 ||
        BonusDex != 0 || BonusIntel != 0;
}
