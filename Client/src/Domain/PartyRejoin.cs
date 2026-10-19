using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class PartyRejoin
{
    public const int TipOnText = 44730;
    public const int TipOffText = 44731;
    public const int TurnedOnText = 44732;
    public const int TurnedOffText = 44733;
    public const string TipOnFallback = "After a party member disconnects unexpectedly and then reconnects, if they are nearby, " +
        "a party invitation is automatically sent. The party invitation is sent only once. (Currently enabled)";
    public const string TipOffFallback = "After a party member disconnects unexpectedly and then reconnects, if they are nearby, " +
        "a party invitation is automatically sent. The party invitation is sent only once. (Currently disabled)";
    public const string TurnedOnFallback = "Party Member Re-Invite Mode After Unexpected Disconnection: on";
    public const string TurnedOffFallback = "Party Member Re-Invite Mode After Unexpected Disconnection: off";

    private readonly Dictionary<int, string> names = new();

    public bool On { get; private set; } = true;

    public void Toggle() => On = !On;

    public void Remember(int id, string name)
    {
        if (name.Length > 0) names[id] = name;
    }

    public void Forget(int id) => names.Remove(id);

    public void Clear() => names.Clear();

    public bool Recall(string name, bool amLeader, int partyCount, int maxMembers, bool alreadyMember)
    {
        if (!On || !amLeader || partyCount >= maxMembers || alreadyMember) return false;
        foreach (var (id, remembered) in names)
        {
            if (!string.Equals(remembered, name, StringComparison.OrdinalIgnoreCase)) continue;
            names.Remove(id);
            return true;
        }
        return false;
    }
}
