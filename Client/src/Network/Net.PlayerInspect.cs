using System;
using System.Collections.Generic;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public const byte UserInformationSub = 2;
    public const byte EquipmentViewSub = 6;

    public const byte DetailAccepted = 1;

    public const int InspectWornSlots =
        InventoryConstants.SlotMax + InventoryConstants.CospreWireMax;

    public const int InspectVisibleEquipment = 7;

    public enum EquipmentViewResult : short
    {
        Accepted = 0,
        NoSuchUser = -1,
        CannotChooseYourself = -2,
        NotInSameRegion = -3,
        NoViewEquipmentItem = -4,
    }

    public readonly struct UserInformation
    {
        public readonly string Name;
        public readonly int Level, Class;
        public readonly int Loyalty, MonthlyLoyalty;
        public readonly int ClanId, ClanMarkVersion, ClanFlag, ClanGrade, ClanRanking;
        public readonly string ClanName, ClanChief;
        public readonly int RebirthLevel;
        public readonly int[] VisibleEquipment;

        public UserInformation(string name, int level, int cls, int loyalty, int monthlyLoyalty,
            int clanId, int clanMarkVersion, int clanFlag, int clanGrade, string clanName,
            string clanChief, int clanRanking, int rebirthLevel, int[] visibleEquipment)
        {
            Name = name; Level = level; Class = cls;
            Loyalty = loyalty; MonthlyLoyalty = monthlyLoyalty;
            ClanId = clanId; ClanMarkVersion = clanMarkVersion; ClanFlag = clanFlag;
            ClanGrade = clanGrade; ClanName = clanName; ClanChief = clanChief;
            ClanRanking = clanRanking; RebirthLevel = rebirthLevel;
            VisibleEquipment = visibleEquipment;
        }
    }

    public readonly struct EquipmentView
    {
        public readonly string Name;
        public readonly int Class, Race, Face, Hair, Level, RebirthLevel, Nation;
        public readonly int MaxHp, MaxMp;
        public readonly int Str, StrBonus, Sta, StaBonus, Dex, DexBonus;
        public readonly int Intel, IntelBonus, Magic, MagicBonus;
        public readonly int Attack, Defence;
        public readonly int FireR, IceR, LightningR, MagicR, CurseR, PoisonR;
        public readonly List<(int Slot, int ItemId, short Durability, byte Flag)> Worn;

        public EquipmentView(string name, int cls, int race, int face, int hair,
            int level, int rebirthLevel, int nation, int maxHp, int maxMp,
            int str, int strBonus, int sta, int staBonus, int dex, int dexBonus,
            int intel, int intelBonus, int magic, int magicBonus,
            int attack, int defence,
            int fireR, int iceR, int lightningR, int magicR, int curseR, int poisonR,
            List<(int Slot, int ItemId, short Durability, byte Flag)> worn)
        {
            Name = name; Class = cls; Race = race; Face = face; Hair = hair;
            Level = level; RebirthLevel = rebirthLevel; Nation = nation;
            MaxHp = maxHp; MaxMp = maxMp;
            Str = str; StrBonus = strBonus; Sta = sta; StaBonus = staBonus;
            Dex = dex; DexBonus = dexBonus; Intel = intel; IntelBonus = intelBonus;
            Magic = magic; MagicBonus = magicBonus;
            Attack = attack; Defence = defence;
            FireR = fireR; IceR = iceR; LightningR = lightningR;
            MagicR = magicR; CurseR = curseR; PoisonR = poisonR;
            Worn = worn;
        }
    }

    public event Action<bool, UserInformation>? UserInformationEvent;
    public event Action<EquipmentViewResult, EquipmentView>? EquipmentViewEvent;

    private void HandleUserInfoOpcode(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();

        switch (sub)
        {
            case UserInformationSub: HandleUserInformation(p); break;
            case EquipmentViewSub:   HandleEquipmentView(p); break;
            default:                 HandleNearbyPlayers(p, sub); break;
        }
    }

    private void HandleUserInformation(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        if (p.ReadByte() != DetailAccepted)
        {
            UserInformationEvent?.Invoke(false, default);
            return;
        }

        string name = p.ReadSByteString();
        int level = p.ReadByte();
        int cls = p.ReadShort();
        int loyalty = p.ReadInt();
        int monthly = p.ReadInt();
        p.ReadByte();

        int clanId = p.ReadShort();
        int clanMark = p.ReadShort();
        int clanFlag = p.ReadByte();
        int clanGrade = p.ReadByte();
        string clanName = p.ReadSByteString();
        string clanChief = p.ReadSByteString();
        int clanRanking = p.ReadByte();

        int rebirth = p.ReadByte();
        p.ReadShort();

        var visible = new int[InspectVisibleEquipment];
        for (int i = 0; i < InspectVisibleEquipment; i++)
            visible[i] = p.ReadInt();

        UserInformationEvent?.Invoke(true, new UserInformation(
            name, level, cls, loyalty, monthly,
            clanId, clanMark, clanFlag, clanGrade, clanName, clanChief, clanRanking,
            rebirth, visible));
    }

    private void HandleEquipmentView(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        var result = (EquipmentViewResult)p.ReadShort();
        if (result != EquipmentViewResult.Accepted)
        {
            EquipmentViewEvent?.Invoke(result, default);
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

        var worn = new List<(int, int, short, byte)>(InspectWornSlots);
        for (int i = 0; i < InspectWornSlots && p.RemainingBytes >= 11; i++)
        {
            int itemId = p.ReadInt();
            short dura = p.ReadShort();
            byte flag = p.ReadByte();
            p.ReadInt();
            if (itemId != 0)
                worn.Add((InspectWornSlot(i), itemId, dura, flag));
        }

        EquipmentViewEvent?.Invoke(EquipmentViewResult.Accepted, new EquipmentView(
            name, cls, race, face, hair, level, rebirth, nation, maxHp, maxMp,
            str, strBonus, sta, staBonus, dex, dexBonus, intel, intelBonus, magic, magicBonus,
            attack, defence, fireR, iceR, lightR, magicR, curseR, poisonR, worn));
    }

    private static int InspectWornSlot(int wireIndex) =>
        wireIndex < InventoryConstants.SlotMax
            ? wireIndex
            : InventoryConstants.CospreStart
              + InventoryConstants.CospreWirePositions[wireIndex - InventoryConstants.SlotMax];

    public void SendUserInformationRequest(string name) => SendInspectRequest(UserInformationSub, name);

    public void SendEquipmentViewRequest(string name) => SendInspectRequest(EquipmentViewSub, name);

    private void SendInspectRequest(byte sub, string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var p = new Packet(GameOpcodes.GS_USER_INFO);
        p.WriteByte(sub);
        p.WriteSByteString(name);
        _conn.Send(p);
    }
}
