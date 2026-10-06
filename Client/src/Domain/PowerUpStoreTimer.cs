using System;

namespace LibreKO.Domain;

public static class PowerUpStoreTimer
{
    public static string Label(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "";
        if (remaining.TotalDays >= 1)
            return remaining.Hours > 0 ? $"{remaining.Days} d {remaining.Hours} h" : $"{remaining.Days} d";
        if (remaining.TotalHours >= 1)
            return remaining.Minutes > 0 ? $"{(int)remaining.TotalHours} h {remaining.Minutes} min" : $"{(int)remaining.TotalHours} h";
        return remaining.TotalMinutes >= 1 ? $"{(int)remaining.TotalMinutes} min" : "< 1 min";
    }
}
