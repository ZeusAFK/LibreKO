namespace LibreKO.Game.World;

public static class NameRules
{
    public const char FirstCharacter = '!';
    public const char LastCharacter = '~';

    public static readonly SemaphoreSlim Changes = new(1, 1);

    public static bool IsPlain(string name) => name.All(c => c is >= FirstCharacter and <= LastCharacter);
}
