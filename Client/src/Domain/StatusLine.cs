namespace LibreKO.Domain;

public enum StatusTone { Hint, Good, Bad }

public sealed class StatusLine
{
    private string hint = "";
    private int token;

    public string Text { get; private set; } = "";
    public StatusTone Tone { get; private set; } = StatusTone.Hint;

    public string Hint
    {
        set
        {
            hint = value;
            if (Tone == StatusTone.Hint) Text = value;
        }
    }

    public int Show(string text, bool bad)
    {
        Text = text;
        Tone = bad ? StatusTone.Bad : StatusTone.Good;
        return ++token;
    }

    public bool Expire(int token)
    {
        if (token != this.token) return false;
        Reset();
        return true;
    }

    public void Reset()
    {
        token++;
        Text = hint;
        Tone = StatusTone.Hint;
    }
}
