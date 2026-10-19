using Godot;

namespace LibreKO.Features;

internal enum CombatLogKind { Damage, Outgoing, Incoming, Recovery, Resource, Status, Spent, Experience, National }

internal interface ICombatLog
{
    void Notice(string message);

    void Notice(string message, Color? tint);

    void Add(string message, CombatLogKind kind, Color? tint = null, string? markup = null, LogTab tab = LogTab.System);

    void Refusal(int textId, string fallback);
}
