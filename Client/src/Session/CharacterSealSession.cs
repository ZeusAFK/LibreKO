namespace LibreKO.Session;

public sealed class CharacterSealSession
{
    public event Action<CharacterSealList>? CharacterSealListEvent;

    internal void RaiseCharacterSealList(CharacterSealList characterSealList) => CharacterSealListEvent?.Invoke(characterSealList);

    public event Action<CharacterSealOutcome>? CharacterSealEvent;

    internal void RaiseCharacterSeal(CharacterSealOutcome characterSealOutcome) => CharacterSealEvent?.Invoke(characterSealOutcome);

    public event Action<CharacterUnsealOutcome>? CharacterUnsealEvent;

    internal void RaiseCharacterUnseal(CharacterUnsealOutcome characterUnsealOutcome) => CharacterUnsealEvent?.Invoke(characterUnsealOutcome);

    public event Action<SealedCharacterSheet?>? SealedCharacterSheetEvent;

    internal void RaiseSealedCharacterSheet(SealedCharacterSheet? sealedCharacterSheet) => SealedCharacterSheetEvent?.Invoke(sealedCharacterSheet);

    public event Action<int[]>? SealedAchievementsEvent;

    internal void RaiseSealedAchievements(int[] value) => SealedAchievementsEvent?.Invoke(value);
}
