namespace LibreKO.Domain;

public enum UpgradeAnswerView { Reveal, Quiet }

public sealed class UpgradeSession
{
    private bool _watching;

    public bool Awaiting { get; private set; }
    public bool CanSend => !Awaiting;

    public void Sent()
    {
        Awaiting = true;
        _watching = true;
    }

    public void Closed() => _watching = false;

    public UpgradeAnswerView Answered()
    {
        var view = Awaiting && _watching ? UpgradeAnswerView.Reveal : UpgradeAnswerView.Quiet;
        Awaiting = false;
        _watching = false;
        return view;
    }
}
