namespace LibreKO.Network.Requests;

public interface IPetRequests
{
    void Mode(PetMode mode);
    bool Hatch(int npcId, int eggItemId, int bagSlot, string name);
    bool Transform(int npcId, int petItemId, int petSlot, int petUniqueId, int materialItemId, int materialSlot);
    bool Rename(int npcId, int petItemId, int petSlot, int petUniqueId, int certificateItemId, int certificateSlot, string name);
    bool Feed(int bagSlot, int foodItemId);
    void Dismiss();
    bool Skill(int stage, int skillId, int casterId, int targetId, int x, int y, int z);
}

public sealed class PetRequests : IPetRequests
{
    private const byte PetFoodTrailFlag = 1;
    private const byte PetFoodTrailPad = 0;
    private const int PetTransformMaterialSlots = 3;
    private const int PetSkillTrailInts = 3;

    private readonly IPacketSink sink;
    private readonly PetSession pet;
    private readonly SelfSession self;
    private readonly ICombatRequests combat;

    public PetRequests(IPacketSink sink, PetSession pet, SelfSession self, ICombatRequests combat)
    {
        this.sink = sink;
        this.pet = pet;
        this.self = self;
        this.combat = combat;
    }

    public void Mode(PetMode mode)
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte((byte)PetSubOpcode.ModeFunction);
        p.WriteByte((byte)PetFunction.Mode);
        p.WriteByte((byte)mode);
        sink.Send(p);
    }

    public bool Hatch(int npcId, int eggItemId, int bagSlot, string name)
    {
        if (!sink.Connected || pet.PendingIncubation != null || !IsBagSlot(bagSlot)) return false;
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)ItemUpgradeSubOpcode.PetHatching);
        p.WriteInt(npcId);
        p.WriteInt(eggItemId);
        p.WriteByte((byte)bagSlot);
        p.WriteString(name);
        pet.PendingIncubation = PetIncubationRequest.Hatch(eggItemId, bagSlot);
        sink.Send(p);
        return true;
    }

    public bool Transform(int npcId, int petItemId, int petSlot, int petUniqueId, int materialItemId, int materialSlot)
    {
        if (!sink.Connected || pet.PendingIncubation != null || !IsBagSlot(petSlot) || !IsBagSlot(materialSlot)
            || petSlot == materialSlot) return false;
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)ItemUpgradeSubOpcode.PetTransform);
        p.WriteInt(npcId);
        p.WriteInt(petItemId);
        p.WriteByte((byte)petSlot);
        p.WriteInt(materialItemId);
        p.WriteByte((byte)materialSlot);
        for (int i = 1; i < PetTransformMaterialSlots; i++)
        {
            p.WriteInt(0);
            p.WriteByte(0);
        }
        pet.PendingIncubation = PetIncubationRequest.Transformation(petItemId, petSlot, petUniqueId, materialItemId, materialSlot);
        sink.Send(p);
        return true;
    }

    public bool Rename(int npcId, int petItemId, int petSlot, int petUniqueId, int certificateItemId, int certificateSlot, string name)
    {
        if (!sink.Connected || pet.PendingRename != null || !IsBagSlot(petSlot) || !IsBagSlot(certificateSlot)
            || petSlot == certificateSlot) return false;
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte((byte)PetSubOpcode.ModeFunction);
        p.WriteByte((byte)PetFunction.NameChange);
        p.WriteInt(npcId);
        p.WriteInt(petItemId);
        p.WriteByte((byte)petSlot);
        p.WriteInt(certificateItemId);
        p.WriteByte((byte)certificateSlot);
        p.WriteString(name);
        pet.PendingRename = new PetRenameRequest(petItemId, petSlot, petUniqueId, certificateItemId, certificateSlot, name);
        sink.Send(p);
        return true;
    }

    public bool Feed(int bagSlot, int foodItemId)
    {
        if (!sink.Connected || pet.PendingFeed != null || !IsBagSlot(bagSlot)) return false;
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte((byte)PetSubOpcode.ModeFunction);
        p.WriteByte((byte)PetFunction.Food);
        p.WriteByte((byte)bagSlot);
        p.WriteInt(foodItemId);
        p.WriteByte(PetFoodTrailFlag);
        p.WriteByte(PetFoodTrailPad);
        pet.PendingFeed = new PetFeedRequest(bagSlot, foodItemId);
        sink.Send(p);
        return true;
    }

    public void Dismiss() => combat.Magic(MagicSub.Cancel, PetWire.FamiliarSummonSkill, self.MyCharId);

    public bool Skill(int stage, int skillId, int casterId, int targetId, int x, int y, int z)
    {
        if (!sink.Connected) return false;
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte((byte)PetSubOpcode.UseSkill);
        p.WriteByte((byte)stage);
        p.WriteInt(skillId);
        p.WriteInt(casterId);
        p.WriteInt(targetId);
        p.WriteInt(x);
        p.WriteInt(y);
        p.WriteInt(z);
        for (int i = 0; i < PetSkillTrailInts; i++) p.WriteInt(0);
        sink.Send(p);
        return true;
    }

    private static bool IsBagSlot(int bagSlot) => bagSlot >= 0 && bagSlot < InventoryConstants.HaveMax;
}
