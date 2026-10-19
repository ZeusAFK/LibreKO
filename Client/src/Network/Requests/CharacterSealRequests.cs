namespace LibreKO.Network.Requests;

public interface ICharacterSealRequests
{
    void List();
    void Send(byte bagPos, int scrollId, string name, string code);
    void CharacterUnseal(byte bagPos, int ringId, byte slot);
    void SealedCharacterSheet(int uniqueId);
    void SealedAchievements(string name);
}

public sealed class CharacterSealRequests : ICharacterSealRequests
{
    private readonly IPacketSink sink;

    public CharacterSealRequests(IPacketSink sink) => this.sink = sink;

    public void List()
    {
        var p = CharacterSealPacket(CharacterSealSubOpcode.List);
        p.WriteUShort(SealServerIndex);
        sink.Send(p);
    }

    public void Send(byte bagPos, int scrollId, string name, string code)
    {
        var p = CharacterSealPacket(CharacterSealSubOpcode.Seal);
        p.WriteInt(SealRequestFromBag);
        p.WriteByte(bagPos);
        p.WriteInt(scrollId);
        p.WriteString(name);
        p.WriteString(code);
        sink.Send(p);
    }

    public void CharacterUnseal(byte bagPos, int ringId, byte slot)
    {
        var p = CharacterSealPacket(CharacterSealSubOpcode.Unseal);
        p.WriteInt(SealRequestFromBag);
        p.WriteByte(bagPos);
        p.WriteInt(ringId);
        p.WriteByte(slot);
        sink.Send(p);
    }

    public void SealedCharacterSheet(int uniqueId)
    {
        var p = CharacterSealPacket(CharacterSealSubOpcode.Sheet);
        p.WriteInt(uniqueId);
        sink.Send(p);
    }

    public void SealedAchievements(string name)
    {
        var p = CharacterSealPacket(CharacterSealSubOpcode.Achievements);
        p.WriteString(name);
        sink.Send(p);
    }

    internal const ushort SealServerIndex = 1;
    internal const int SealRequestFromBag = -1;

    private static Packet CharacterSealPacket(CharacterSealSubOpcode kind)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)ItemUpgradeSubOpcode.CharacterSeal);
        p.WriteByte((byte)kind);
        return p;
    }
}
