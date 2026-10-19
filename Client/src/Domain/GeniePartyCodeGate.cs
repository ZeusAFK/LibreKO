using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

// A private message authorizes only its sender, once, for two minutes.
public sealed class GeniePartyCodeGate
{
    public const double AuthorizationSeconds = 120;
    public const int MaxPendingSenders = 128;
    private readonly Dictionary<int, (string Name, double Until)> senders = new();
    private string code = "";
    private bool enabled;

    public void Configure(bool enabled, string code)
    {
        code = code.Trim();
        enabled &= code.Length > 0;
        if (enabled != this.enabled || !string.Equals(code, this.code, StringComparison.Ordinal)) senders.Clear();
        this.enabled = enabled;
        this.code = code;
    }

    public bool Receive(ChatType channel, int senderId, string senderName, string message, int selfId, double now)
    {
        if (!enabled || channel != ChatType.Private || senderId <= 0 || senderId == selfId
            || string.IsNullOrWhiteSpace(senderName)
            || !string.Equals(message.Trim(), code, StringComparison.Ordinal)) return false;
        foreach (int id in new List<int>(senders.Keys))
            if (senders[id].Until <= now) senders.Remove(id);
        if (senders.Count >= MaxPendingSenders && !senders.ContainsKey(senderId)) return false;
        senders[senderId] = (senderName, now + AuthorizationSeconds);
        return true;
    }

    public bool TryAccept(int inviterId, string inviterName, double now)
    {
        if (!enabled || !senders.TryGetValue(inviterId, out var sender)) return false;
        if (sender.Until <= now) { senders.Remove(inviterId); return false; }
        if (!string.Equals(sender.Name, inviterName, StringComparison.OrdinalIgnoreCase)) return false;
        senders.Remove(inviterId);
        return true;
    }

    public void Clear() => senders.Clear();
}
