using System;
using Godot;

namespace LibreKO;

public partial class MoneyEdit : LineEdit
{
    public event Action<long>? ValueChanged;

    private readonly long _max;
    private bool _formatting;

    public MoneyEdit(long max, float width, int fontSize = 13)
    {
        _max = max;
        CustomMinimumSize = new Vector2(width, 0);
        Alignment = HorizontalAlignment.Right;
        AddThemeFontSizeOverride("font_size", fontSize);
        TextChanged += OnTextChanged;
    }

    public long Value
    {
        get => DigitsOf(Text);
        set
        {
            long clamped = Math.Clamp(value, 0, _max);
            _formatting = true;
            Text = clamped.ToString("n0");
            CaretColumn = Text.Length;
            _formatting = false;
        }
    }

    private void OnTextChanged(string text)
    {
        if (_formatting) return;

        int digitsBeforeCaret = 0;
        for (int i = 0; i < CaretColumn && i < text.Length; i++)
            if (char.IsDigit(text[i])) digitsBeforeCaret++;

        long value = Math.Min(DigitsOf(text), _max);
        string formatted = value == 0 && !HasDigit(text) ? "" : value.ToString("n0");

        _formatting = true;
        Text = formatted;
        CaretColumn = CaretAfterDigits(formatted, digitsBeforeCaret);
        _formatting = false;

        ValueChanged?.Invoke(value);
    }

    private static bool HasDigit(string text)
    {
        foreach (char c in text) if (char.IsDigit(c)) return true;
        return false;
    }

    private static long DigitsOf(string text)
    {
        long value = 0;
        foreach (char c in text)
        {
            if (!char.IsDigit(c)) continue;
            if (value > (long.MaxValue - 9) / 10) return long.MaxValue;
            value = value * 10 + (c - '0');
        }
        return value;
    }

    private static int CaretAfterDigits(string formatted, int digits)
    {
        if (digits <= 0) return 0;
        int seen = 0;
        for (int i = 0; i < formatted.Length; i++)
        {
            if (!char.IsDigit(formatted[i])) continue;
            if (++seen == digits) return i + 1;
        }
        return formatted.Length;
    }
}
