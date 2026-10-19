namespace LibreKO.Session;

public sealed class ClientSettingsSession
{
    public event Action<bool, GameLanguage>? LanguageEvent;

    internal void RaiseLanguage(bool arg1, GameLanguage gameLanguage) => LanguageEvent?.Invoke(arg1, gameLanguage);
}
