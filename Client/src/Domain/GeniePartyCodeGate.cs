using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

// A private message authorizes only its sender, once, for two minutes.
public sealed class GeniePartyCodeGate
{
    public const double AuthorizationSeconds = 120;
    private readonly Dictionary<int, (string Name, double Until)> _senders = new();
    private string _code = "";
    private bool _enabled;

    public void Configure(bool enabled, string code)
    {
        code = code.Trim();
        enabled &= code.Length > 0;
        if (enabled != _enabled || !string.Equals(code, _code, StringComparison.Ordinal)) _senders.Clear();
        _enabled = enabled;
        _code = code;
    }

    public bool Receive(byte channel, int senderId, string senderName, string message, int selfId, double now)
    {
        if (!_enabled || channel != 2 || senderId <= 0 || senderId == selfId
            || string.IsNullOrWhiteSpace(senderName)
            || !string.Equals(message.Trim(), _code, StringComparison.Ordinal)) return false;
        foreach (int id in new List<int>(_senders.Keys))
            if (_senders[id].Until <= now) _senders.Remove(id);
        if (_senders.Count >= 128 && !_senders.ContainsKey(senderId)) return false;
        _senders[senderId] = (senderName, now + AuthorizationSeconds);
        return true;
    }

    public bool TryAccept(int inviterId, string inviterName, double now)
    {
        if (!_enabled || !_senders.TryGetValue(inviterId, out var sender)) return false;
        if (sender.Until <= now) { _senders.Remove(inviterId); return false; }
        if (!string.Equals(sender.Name, inviterName, StringComparison.OrdinalIgnoreCase)) return false;
        _senders.Remove(inviterId);
        return true;
    }

    public void Clear() => _senders.Clear();
}
