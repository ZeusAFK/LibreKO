namespace LibreKO.Session;

public sealed class PreGameSession
{
    public event Action<bool, int>? LoginResultEvent;

    internal void RaiseLoginResult(bool arg1, int arg2) => LoginResultEvent?.Invoke(arg1, arg2);

    public event Action? LoginThrottledEvent;

    internal void RaiseLoginThrottled() => LoginThrottledEvent?.Invoke();

    public event Action<int>? NationResultEvent;

    internal void RaiseNationResult(int value) => NationResultEvent?.Invoke(value);

    public event Action<CreateCharacterResult>? CreateCharResultEvent;

    internal void RaiseCreateCharResult(CreateCharacterResult result) => CreateCharResultEvent?.Invoke(result);

    public event Action<SelectingCharacterNameChangeResult, int, string>? RequiredNameChangeEvent;

    internal void RaiseRequiredNameChange(SelectingCharacterNameChangeResult result, int slot, string name) =>
        RequiredNameChangeEvent?.Invoke(result, slot, name);

    public event Action<List<CharacterSummary>>? CharListEvent;

    internal void RaiseCharList(List<CharacterSummary> list) => CharListEvent?.Invoke(list);

    public event Action<MyInfo>? EnterWorldEvent;

    internal void RaiseEnterWorld(MyInfo myInfo) => EnterWorldEvent?.Invoke(myInfo);
}
