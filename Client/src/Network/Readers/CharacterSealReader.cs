namespace LibreKO.Network.Readers;

internal sealed class CharacterSealReader : IPacketReader
{
    private readonly CharacterSealSession characterSeal;
    private readonly PetSession pet;

    public CharacterSealReader(CharacterSealSession characterSeal, PetSession pet)
    {
        this.characterSeal = characterSeal;
        this.pet = pet;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_ITEM_UPGRADE, ItemUpgradeSubOpcode.CharacterSeal, HandleCharacterSeal);
    }

    private void HandleCharacterSeal(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        switch ((CharacterSealSubOpcode)p.ReadByte())
        {
            case CharacterSealSubOpcode.List:
                characterSeal.RaiseCharacterSealList(CharacterSealWire.ReadList(p));
                break;
            case CharacterSealSubOpcode.Seal:
                {
                    var outcome = CharacterSealWire.ReadSeal(p);
                    pet.Remember(outcome.Summary);
                    characterSeal.RaiseCharacterSeal(outcome);
                    break;
                }
            case CharacterSealSubOpcode.Unseal:
                characterSeal.RaiseCharacterUnseal(CharacterSealWire.ReadUnseal(p));
                break;
            case CharacterSealSubOpcode.Sheet:
                characterSeal.RaiseSealedCharacterSheet(CharacterSealWire.ReadSheet(p));
                break;
            case CharacterSealSubOpcode.Achievements:
                characterSeal.RaiseSealedAchievements(CharacterSealWire.ReadAchievements(p));
                break;
        }
    }
}
