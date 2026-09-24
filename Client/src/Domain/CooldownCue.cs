using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public static class CooldownCue
{
    public const float MinRecastSeconds = 2f;

    public static void Collect(
        IReadOnlyDictionary<int, double> readyAt, double since, double now,
        int[] hotbar, Func<int, float> recastSeconds, List<int> into)
    {
        into.Clear();
        if (since <= 0 || now <= since) return;
        foreach (var (skillId, until) in readyAt)
        {
            if (until <= since || until > now) continue;
            if (Array.IndexOf(hotbar, skillId) < 0) continue;
            if (recastSeconds(skillId) < MinRecastSeconds) continue;
            into.Add(skillId);
        }
    }
}
