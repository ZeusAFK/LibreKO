namespace LibreKO.Network.Readers;

internal sealed class ChangeHairReader : IPacketReader
{
    private readonly ChangeHairSession changeHair;
    private readonly SelfSession self;

    public ChangeHairReader(ChangeHairSession changeHair, SelfSession self)
    {
        this.changeHair = changeHair;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_CHANGE_HAIR, HandleChangeHair);
    }

    private void HandleChangeHair(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var result = (ChangeHairResult)p.ReadByte();
        if (result == ChangeHairResult.OpenShop) { changeHair.RaiseBeautyShop(); return; }
        if (result is not ChangeHairResult.Succeeded and not ChangeHairResult.Failed || !changeHair.Request.TryFinish()) return;
        if (result == ChangeHairResult.Succeeded) self.ChangeLook(changeHair.Request.Face, changeHair.Request.Hair);
        changeHair.RaiseChangeHairResult(result == ChangeHairResult.Succeeded, changeHair.Request.Face, changeHair.Request.Hair);
    }
}
