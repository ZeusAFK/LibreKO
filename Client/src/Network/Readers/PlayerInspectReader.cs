namespace LibreKO.Network.Readers;

internal sealed class PlayerInspectReader : IPacketReader
{
    private readonly PlayerInspectSession playerInspect;

    private const int WornSlots =
            InventoryConstants.SlotMax + InventoryConstants.CospreWireMax;

    public PlayerInspectReader(PlayerInspectSession playerInspect)
    {
        this.playerInspect = playerInspect;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_USER_INFO, PlayerInspectSubOpcode.Detail, HandleUserInformation);
        dispatcher.On(GameOpcodes.GS_USER_INFO, PlayerInspectSubOpcode.Equipment, HandleEquipmentView);
    }

    private void HandleUserInformation(Packet p)
    {
        var (result, info) = UserInfoReply.Read(p);
        if (info != null) playerInspect.RaiseUserInformation(info);
        else if ((PlayerInspectResult)result is PlayerInspectResult.NoUser or PlayerInspectResult.NoUserAgain) playerInspect.RaiseUserInformationMissing();
    }

    private void HandleEquipmentView(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        var result = (EquipmentViewResult)p.ReadShort();
        if (result != EquipmentViewResult.Accepted)
        {
            playerInspect.RaiseEquipmentView(result, default);
            return;
        }

        string name = p.ReadSByteString();
        int cls = p.ReadShort();
        int race = p.ReadByte();
        int face = p.ReadByte();
        int hair = p.ReadInt();
        int level = p.ReadByte();
        int rebirth = p.ReadByte();
        int nation = p.ReadByte();

        int maxHp = p.ReadShort();
        int maxMp = p.ReadShort();
        p.ReadShort();

        int str = p.ReadByte(), strBonus = p.ReadByte();
        int sta = p.ReadByte(), staBonus = p.ReadByte();
        int dex = p.ReadByte(), dexBonus = p.ReadByte();
        int intel = p.ReadByte(), intelBonus = p.ReadByte();
        int magic = p.ReadByte(), magicBonus = p.ReadByte();

        int attack = p.ReadShort(), defence = p.ReadShort();
        int fireR = p.ReadShort(), iceR = p.ReadShort(), lightR = p.ReadShort();
        int magicR = p.ReadShort(), curseR = p.ReadShort(), poisonR = p.ReadShort();

        var worn = new List<(int, int, short, byte)>(WornSlots);
        for (int i = 0; i < WornSlots && p.RemainingBytes >= 11; i++)
        {
            int itemId = p.ReadInt();
            short dura = p.ReadShort();
            byte flag = p.ReadByte();
            p.ReadInt();
            if (itemId != 0)
                worn.Add((InspectWornSlot(i), itemId, dura, flag));
        }

        playerInspect.RaiseEquipmentView(EquipmentViewResult.Accepted, new EquipmentView(
            name, cls, race, face, hair, level, rebirth, nation, maxHp, maxMp,
            str, strBonus, sta, staBonus, dex, dexBonus, intel, intelBonus, magic, magicBonus,
            attack, defence, fireR, iceR, lightR, magicR, curseR, poisonR, worn));
    }

    private static int InspectWornSlot(int wireIndex) =>
            wireIndex < InventoryConstants.SlotMax
                ? wireIndex
                : InventoryConstants.CospreStart
                  + InventoryConstants.CospreWirePositions[wireIndex - InventoryConstants.SlotMax];
}
