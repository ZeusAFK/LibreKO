namespace LibreKO.Session;

public sealed class PlayerInspectSession
{
    public event Action<UserInfoReply>? UserInformationEvent;

    internal void RaiseUserInformation(UserInfoReply userInfoReply) => UserInformationEvent?.Invoke(userInfoReply);

    public event Action? UserInformationMissingEvent;

    internal void RaiseUserInformationMissing() => UserInformationMissingEvent?.Invoke();

    public event Action<EquipmentViewResult, EquipmentView>? EquipmentViewEvent;

    internal void RaiseEquipmentView(EquipmentViewResult equipmentViewResult, EquipmentView equipmentView) => EquipmentViewEvent?.Invoke(equipmentViewResult, equipmentView);
}
