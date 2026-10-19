namespace LibreKO.Domain;

public enum UpgradeAnswerView { Reveal, Quiet }

public sealed class UpgradeSession
{
    private bool watching;

    public bool Awaiting { get; private set; }
    public bool CanSend => !Awaiting;

    public void Sent()
    {
        Awaiting = true;
        watching = true;
    }

    public void Closed() => watching = false;

    public UpgradeAnswerView Answered()
    {
        var view = Awaiting && watching ? UpgradeAnswerView.Reveal : UpgradeAnswerView.Quiet;
        Awaiting = false;
        watching = false;
        return view;
    }
}
