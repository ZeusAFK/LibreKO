using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

internal static class ItemMovePacketMapper
{
    private const int AttackAmountScale = 100;

    public static Packet BuildResponse(UserSession session, byte subcommand)
    {
        var writer = ItemMovePacketWriter.Move(subcommand);

        if (subcommand != (byte)ItemMoveSubOpcode.Failed)
            writer.Stats = BuildStatBlock(session);

        return writer.Build();
    }

    public static Packet BuildArrangedResponse(UserSession session)
    {
        var writer = ItemMovePacketWriter.Arranged();

        var start = InventoryConstants.InventoryStart;
        for (var slot = start; slot < start + InventoryConstants.HaveMax; slot++)
        {
            var item = session.Inventory[slot];
            writer.AddInventorySlot(item.ItemId, item.Durability, item.Count, item.Flag);
        }

        return writer.Build();
    }

    public static Packet BuildArrangeRefusedResponse() =>
        ItemMovePacketWriter.ArrangeRefused().Build();

    private static ItemMovePacketWriter.StatBlock BuildStatBlock(UserSession session)
    {
        var stats = session.Stats;
        var attackAmount = session.AttackAmount == 0 ? AttackAmountScale : session.AttackAmount;

        return new ItemMovePacketWriter.StatBlock
        {
            TotalHit = (short)(stats.TotalHit * attackAmount / AttackAmountScale),
            TotalAc = stats.TotalAc,
            MaxWeight = stats.MaxWeight,
            MaxHp = stats.MaxHp,
            MaxMp = stats.MaxMp,
            StrengthBonus = stats.StrBonus,
            StaminaBonus = stats.StaBonus,
            DexterityBonus = stats.DexBonus,
            IntelligenceBonus = stats.IntBonus,
            CharismaBonus = stats.ChaBonus,
            FireResistance = stats.FireR,
            ColdResistance = stats.ColdR,
            LightningResistance = stats.LightningR,
            MagicResistance = stats.MagicR,
            DiseaseResistance = stats.DiseaseR,
            PoisonResistance = stats.PoisonR,
        };
    }
}
