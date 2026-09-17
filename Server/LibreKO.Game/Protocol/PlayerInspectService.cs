using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IPlayerInspectService
{
    Task HandleUserInformationAsync(UserSession session, Packet packet);
    Task HandleEquipmentViewAsync(UserSession session, Packet packet);
}

public class PlayerInspectService(SessionManager sessionManager) : IPlayerInspectService
{
    public async Task HandleUserInformationAsync(UserSession session, Packet packet)
    {
        var target = ResolveTarget(session, packet, out _);
        if (target == null)
        {
            await session.Client.SendPacket(PlayerInspectPacketWriter.DetailRefused());
            return;
        }

        var visible = PlayerInspectPacketWriter.VisibleEquipmentSlots
            .Select(slot => target.Inventory[slot].ItemId)
            .ToArray();

        await session.Client.SendPacket(PlayerInspectPacketWriter.UserDetail(
            new PlayerInspectPacketWriter.Detail(
                target.Name,
                target.Level,
                target.Class,
                target.Loyalty,
                target.MonthlyLoyalty,
                ClanOf(target),
                (byte)target.RebirthLevel,
                visible)));
    }

    public async Task HandleEquipmentViewAsync(UserSession session, Packet packet)
    {
        var target = ResolveTarget(session, packet, out var refusal);
        if (target == null)
        {
            await session.Client.SendPacket(PlayerInspectPacketWriter.EquipmentRefused(refusal));
            return;
        }

        var stats = target.Stats;

        await session.Client.SendPacket(PlayerInspectPacketWriter.EquipmentView(
            new PlayerInspectPacketWriter.Equipment(
                target.Name,
                target.Class,
                target.Race,
                target.Face,
                target.Hair,
                target.Level,
                (byte)target.RebirthLevel,
                (byte)target.Nation,
                target.MaxHp,
                target.MaxMp,
                target.Strength, 0,
                target.Stamina, 0,
                target.Dexterity, 0,
                target.Intelligence, 0,
                target.Magic, 0,
                (short)stats.TotalHit,
                stats.TotalAc,
                stats.FireR, stats.ColdR, stats.LightningR,
                stats.MagicR, stats.DiseaseR, stats.PoisonR,
                WornOf(target))));
    }

    private static PlayerInspectPacketWriter.WornItem[] WornOf(UserSession target)
    {
        var worn = new PlayerInspectPacketWriter.WornItem[PlayerInspectPacketWriter.EquipmentSlotCount];

        for (var slot = 0; slot < InventoryConstants.SlotMax; slot++)
        {
            if (slot == InventoryConstants.Head && target.IsHidingHelmet)
                continue;

            worn[slot] = WornOf(target.Inventory[slot]);
        }

        for (var i = 0; i < InventoryConstants.CospreWireMax; i++)
        {
            var slot = InventoryConstants.CospreStart + InventoryConstants.CospreWirePositions[i];
            worn[InventoryConstants.SlotMax + i] = WornOf(target.Inventory[slot]);
        }

        return worn;
    }

    private static PlayerInspectPacketWriter.WornItem WornOf(ItemSlot item) =>
        item.IsEmpty
            ? default
            : new PlayerInspectPacketWriter.WornItem(item.ItemId, item.Durability, item.Flag, 0);

    private PlayerInspectPacketWriter.Clan ClanOf(UserSession target)
    {
        if (target.KnightsId <= 0)
            return default;

        var clan = sessionManager.Knights.GetClan(target.KnightsId);
        return clan == null
            ? default
            : new PlayerInspectPacketWriter.Clan(
                clan.Id, clan.MarkVersion, clan.Flag, clan.Grade, clan.Name, clan.Chief, clan.Grade);
    }

    private UserSession? ResolveTarget(
        UserSession session, Packet packet, out EquipmentViewResult refusal)
    {
        refusal = EquipmentViewResult.NoSuchUser;

        if (packet.RemainingBytes < 1)
            return null;

        var name = packet.ReadSByteString();
        if (string.IsNullOrEmpty(name) || name.Length > UserInfoPacketConstants.NameMax)
            return null;

        var target = sessionManager.GetByName(name);
        if (target == null)
            return null;

        if (target.CharacterId == session.CharacterId)
        {
            refusal = EquipmentViewResult.CannotChooseYourself;
            return null;
        }

        if (target.IsGM && !session.IsGM)
            return null;

        if (session.IsGM)
            return target;

        refusal = EquipmentViewResult.NotInSameRegion;

        if (target.ZoneId != session.ZoneId || target.IsInvisible)
            return null;

        var dx = target.X - session.X;
        var dz = target.Z - session.Z;
        return dx * dx + dz * dz
            > UserInfoPacketConstants.MaxInspectDistance * UserInfoPacketConstants.MaxInspectDistance
            ? null
            : target;
    }
}
