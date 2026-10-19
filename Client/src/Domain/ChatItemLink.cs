using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LibreKO.Domain;

public readonly record struct ChatSegment(string Text, int ItemId);

public static class ChatItemLink
{
    public const string Open = "<LINK>[";
    public const string Close = "]</LINK>";
    public const int RefusedItem = 800112000;
    private const int BufferLength = 15;
    private const ushort KeySeed = 2070;
    private const int KeyMultiplier = 24705;
    private const int KeyIncrement = 5640;

    public static string Token(int itemId)
    {
        var buffer = new byte[BufferLength];
        byte[] digits = Encoding.ASCII.GetBytes(itemId.ToString(CultureInfo.InvariantCulture));
        Array.Copy(digits, buffer, Math.Min(digits.Length, BufferLength));
        ushort key = KeySeed;
        for (int i = 0; i < BufferLength; i++)
        {
            byte cipher = (byte)(buffer[i] ^ (key >> 8));
            buffer[i] = cipher;
            key = NextKey(key, cipher);
        }
        return Open + Convert.ToBase64String(buffer) + Close;
    }

    public static bool TryDecode(string payload, out int itemId)
    {
        itemId = 0;
        byte[] buffer;
        try { buffer = Convert.FromBase64String(payload); }
        catch (FormatException) { return false; }

        ushort key = KeySeed;
        var digits = new StringBuilder(BufferLength);
        for (int i = 0; i < buffer.Length; i++)
        {
            byte cipher = buffer[i];
            char plain = (char)(cipher ^ (key >> 8));
            key = NextKey(key, cipher);
            if (plain is < '0' or > '9') break;
            digits.Append(plain);
        }
        return int.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out itemId) && itemId > 0;
    }

    public static List<ChatSegment> Split(string message)
    {
        var segments = new List<ChatSegment>();
        var text = new StringBuilder();
        int at = 0;
        while (at < message.Length)
        {
            int open = message.IndexOf(Open, at, StringComparison.Ordinal);
            int close = open < 0 ? -1 : message.IndexOf(Close, open + Open.Length, StringComparison.Ordinal);
            if (open < 0 || close < 0)
            {
                text.Append(message, at, message.Length - at);
                break;
            }
            text.Append(message, at, open - at);
            string payload = message.Substring(open + Open.Length, close - open - Open.Length);
            if (TryDecode(payload, out int itemId))
            {
                if (text.Length > 0) segments.Add(new ChatSegment(text.ToString(), 0));
                text.Clear();
                segments.Add(new ChatSegment("", itemId));
            }
            at = close + Close.Length;
        }
        if (text.Length > 0) segments.Add(new ChatSegment(text.ToString(), 0));
        return segments;
    }

    private static ushort NextKey(ushort key, byte cipher) => (ushort)(KeyMultiplier * (key + cipher) + KeyIncrement);
}

public sealed class ChatLinkDraft
{
    private int itemId;
    private int start;
    private string visible = "";

    public bool Active => itemId != 0;

    public int WireExtra => Active ? ChatItemLink.Token(itemId).Length - visible.Length : 0;

    public (string Text, int Caret) Insert(string text, int caret, int itemId, string name)
    {
        if (Active || itemId <= 0 || itemId == ChatItemLink.RefusedItem) return (text, caret);
        caret = Math.Clamp(caret, 0, text.Length);
        this.itemId = itemId;
        start = caret;
        visible = $"[{name}]";
        string result = text[..caret] + visible + text[caret..];
        return (result, caret + visible.Length);
    }

    public void TextChanged(string oldText, string newText)
    {
        if (!Active) return;
        int prefix = 0;
        int shortest = Math.Min(oldText.Length, newText.Length);
        while (prefix < shortest && oldText[prefix] == newText[prefix]) prefix++;
        int suffix = 0;
        while (suffix < shortest - prefix
               && oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix]) suffix++;
        int editEnd = oldText.Length - suffix;
        int spanEnd = start + visible.Length;

        if (editEnd <= start) start += newText.Length - oldText.Length;
        else if (prefix < spanEnd) Clear();

        if (Active && (start < 0 || start + visible.Length > newText.Length
                       || string.CompareOrdinal(newText, start, visible, 0, visible.Length) != 0))
            Clear();
    }

    public string ToWire(string text)
    {
        if (!Active || start + visible.Length > text.Length
            || string.CompareOrdinal(text, start, visible, 0, visible.Length) != 0)
            return text;
        return text[..start] + ChatItemLink.Token(itemId) + text[(start + visible.Length)..];
    }

    public void Clear()
    {
        itemId = 0;
        start = 0;
        visible = "";
    }
}
