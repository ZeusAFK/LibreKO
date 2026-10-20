namespace LibreKO.Game.World;

public static class NameRules
{
    public const char FirstCharacter = '!';
    public const char LastCharacter = '~';

    // A name is checked and taken while no other clan or character takes one.
    public static readonly SemaphoreSlim Changes = new(1, 1);

    public static bool IsPlain(string name) => name.All(c => c is >= FirstCharacter and <= LastCharacter);
}
