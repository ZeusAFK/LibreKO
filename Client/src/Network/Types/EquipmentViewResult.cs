namespace LibreKO.Network;

public enum EquipmentViewResult : short
{
    Accepted = 0,
    NoSuchUser = -1,
    CannotChooseYourself = -2,
    NotInSameRegion = -3,
    NoViewEquipmentItem = -4,
}
