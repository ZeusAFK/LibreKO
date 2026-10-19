namespace LibreKO.Presentation;

public sealed class WingPick
{
    private const double SwitchDelay = 0.2;

    public string? Shown;
    public string? Pending;
    public double Since;

    public string Settle(string wanted, double now)
    {
        if (wanted is WingState.Die or WingState.Sit || Shown == null)
            return Accept(wanted);
        if (wanted == Shown)
        {
            Pending = null;
            return Shown;
        }
        if (wanted != Pending)
        {
            Pending = wanted;
            Since = now;
            return Shown;
        }
        return now - Since >= SwitchDelay ? Accept(wanted) : Shown;
    }

    private string Accept(string wanted)
    {
        Shown = wanted;
        Pending = null;
        return wanted;
    }
}
