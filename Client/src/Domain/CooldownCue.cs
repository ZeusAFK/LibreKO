using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public static class CooldownCue
{
    public const float MinSoundRecastSeconds = 2f;

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
            if (recastSeconds(skillId) <= 0f) continue;
            into.Add(skillId);
        }
    }

    public static bool Sounds(List<int> cued, Func<int, float> recastSeconds)
    {
        foreach (int skillId in cued)
            if (recastSeconds(skillId) >= MinSoundRecastSeconds) return true;
        return false;
    }
}
