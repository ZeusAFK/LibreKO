using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class JobChangeRules
{
    public const int MasterChangeScroll = 700_112_000;
    public const int NonMasterChangeScroll = 700_113_000;

    private const byte KarusBig = 1;
    private const byte KarusMiddle = 2;
    private const byte KarusSmall = 3;
    private const byte KarusWoman = 4;
    private const byte Kurian = 6;
    private const byte Babarian = 11;
    private const byte ElmoradMan = 12;
    private const byte ElmoradWoman = 13;
    private const byte Porutu = 14;

    private const short KaruWarrior = 101, KaruRogue = 102, KaruWizard = 103, KaruPriest = 104;
    private const short Berserker = 105, Guardian = 106, Hunter = 107, Penetrator = 108;
    private const short Sorserer = 109, Necromancer = 110, Shaman = 111, DarkPriest = 112;
    private const short ElmoWarrior = 201, ElmoRogue = 202, ElmoWizard = 203, ElmoPriest = 204;
    private const short Blade = 205, Protector = 206, Ranger = 207, Assassin = 208;
    private const short Mage = 209, Enchanter = 210, Cleric = 211, Druid = 212;
    private const short KurianStarter = 113, KurianNovice = 114, KurianMaster = 115;
    private const short PorutuStarter = 213, PorutuNovice = 214, PorutuMaster = 215;

    public static (short NewClass, byte NewRace)? Resolve(

        short currentClass, byte currentRace, byte nation, byte newJob, byte changeType)

    {

        var isKarus = nation == (byte)AccountNation.Karus;



        bool BeginnerOther(byte job) => !ClassIdHelper.IsSameJobGroup(currentClass, job)

                                        && ClassIdHelper.IsBeginner(currentClass);

        bool NoviceOther(byte job) => !ClassIdHelper.IsSameJobGroup(currentClass, job)

                                       && ClassIdHelper.IsNovice(currentClass);

        bool MasteredOther(byte job) => !ClassIdHelper.IsSameJobGroup(currentClass, job)

                                         && ClassIdHelper.IsMastered(currentClass);



        switch ((JobChangeTarget)newJob)

        {

            case JobChangeTarget.Warrior:

                if (BeginnerOther(1))

                    return isKarus

                        ? (KaruWarrior, KarusBig)

                        : (ElmoWarrior, currentRace == Porutu ? Babarian : currentRace);

                if (NoviceOther(1))

                    return isKarus

                        ? (Berserker, KarusBig)

                        : (Blade, currentRace == Porutu ? Babarian : currentRace);

                if (MasteredOther(1))

                    return isKarus

                        ? (changeType == 1 ? Berserker : Guardian, KarusBig)

                        : (changeType == 1 ? Blade : Protector, currentRace == Porutu ? Babarian : currentRace);

                return null;



            case JobChangeTarget.Rogue:

                if (BeginnerOther(2))

                    return isKarus

                        ? (KaruRogue, KarusMiddle)

                        : (ElmoRogue, currentRace is Babarian or Porutu ? ElmoradMan : currentRace);

                if (NoviceOther(2))

                    return isKarus

                        ? (Hunter, KarusMiddle)

                        : (Ranger, currentRace is Babarian or Porutu ? ElmoradMan : currentRace);

                if (MasteredOther(2))

                    return isKarus

                        ? (changeType == 1 ? Hunter : Penetrator, KarusMiddle)

                        : (changeType == 1 ? Ranger : Assassin, currentRace is Babarian or Porutu ? ElmoradMan : currentRace);

                return null;



            case JobChangeTarget.Mage:

                if (BeginnerOther(3))

                {

                    if (isKarus)

                    {

                        var race = currentRace is KarusBig or KarusMiddle or Kurian ? KarusSmall : currentRace;

                        return (KaruWizard, race);

                    }

                    else

                    {

                        var race = currentRace is Babarian or Porutu ? ElmoradMan : currentRace;

                        return (ElmoWizard, race);

                    }

                }

                if (NoviceOther(3))

                {

                    if (isKarus)

                    {

                        var race = currentRace is KarusBig or KarusMiddle or Kurian ? KarusSmall : currentRace;

                        return (Sorserer, race);

                    }

                    else

                    {

                        var race = currentRace is Babarian or Porutu ? ElmoradMan : currentRace;

                        return (Mage, race);

                    }

                }

                if (MasteredOther(3))

                {

                    if (isKarus)

                    {

                        var cls = changeType == 1 ? Sorserer : Necromancer;

                        var race = currentRace is KarusBig or KarusMiddle or Kurian ? KarusSmall : currentRace;

                        return (cls, race);

                    }

                    else

                    {

                        var cls = changeType == 1 ? Mage : Enchanter;

                        var race = currentRace is Babarian or Porutu ? ElmoradMan : currentRace;

                        return (cls, race);

                    }

                }

                return null;



            case JobChangeTarget.Priest:

                if (BeginnerOther(4))

                    return isKarus

                        ? (KaruPriest, currentRace == Kurian ? KarusWoman : currentRace)

                        : (ElmoPriest, currentRace is Babarian or Porutu ? ElmoradWoman : currentRace);

                if (NoviceOther(4))

                    return isKarus

                        ? (Shaman, currentRace == Kurian ? KarusWoman : currentRace)

                        : (Cleric, currentRace is Babarian or Porutu ? ElmoradWoman : currentRace);

                if (MasteredOther(4))

                    return isKarus

                        ? (changeType == 1 ? Shaman : DarkPriest, currentRace == Kurian ? KarusWoman : currentRace)

                        : (changeType == 1 ? Cleric : Druid, currentRace is Babarian or Porutu ? ElmoradWoman : currentRace);

                return null;



            case JobChangeTarget.Kurian:

                if (BeginnerOther(5))

                    return isKarus ? (KurianStarter, Kurian) : (PorutuStarter, Porutu);

                if (NoviceOther(5))

                    return isKarus ? (KurianNovice, Kurian) : (PorutuNovice, Porutu);

                if (MasteredOther(5))

                    return isKarus ? (KurianMaster, Kurian) : (PorutuMaster, Porutu);

                return null;



            default:

                return null;

        }

    }



    public static int FindChangeToken(UserSession session, byte changeType)
    {
        var preferred = changeType == 0 ? MasterChangeScroll : NonMasterChangeScroll;
        if (HasItemInBag(session, preferred))
            return preferred;

        return HasItemInBag(session, InventoryConstants.ItemJobChange)
            ? InventoryConstants.ItemJobChange
            : 0;
    }

    public static bool HasItemInBag(UserSession session, int itemId)
    {
        for (var i = InventoryConstants.SlotMax; i < InventoryConstants.SlotMax + InventoryConstants.HaveMax; i++)
        {
            var slot = session.Inventory[i];
            if (slot.ItemId == itemId && slot.Count > 0)
                return true;
        }
        return false;
    }

    public static bool HasEquippedItems(UserSession session)
    {
        for (var i = 0; i < InventoryConstants.SlotMax; i++)
        {
            if (!session.Inventory[i].IsEmpty)
                return true;
        }
        return false;
    }
}
