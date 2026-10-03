namespace LibreKO.Domain;

public enum StatusTone { Hint, Good, Bad }

public sealed class StatusLine
{
    private string _hint = "";
    private int _token;

    public string Text { get; private set; } = "";
    public StatusTone Tone { get; private set; } = StatusTone.Hint;

    public string Hint
    {
        set
        {
            _hint = value;
            if (Tone == StatusTone.Hint) Text = value;
        }
    }

    public int Show(string text, bool bad)
    {
        Text = text;
        Tone = bad ? StatusTone.Bad : StatusTone.Good;
        return ++_token;
    }

    public bool Expire(int token)
    {
        if (token != _token) return false;
        Reset();
        return true;
    }

    public void Reset()
    {
        _token++;
        Text = _hint;
        Tone = StatusTone.Hint;
    }
}
