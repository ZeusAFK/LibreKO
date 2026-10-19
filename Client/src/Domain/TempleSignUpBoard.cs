using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public enum TempleEventType : byte
{
    None = 0,
    Chaos = 1,
    BorderDefenseWar = 2,
    JuraidMountain = 3,
    UnderTheCastle = 4,
    ForgottenTemple = 5,
}

public sealed class TempleSignUpBoard
{
    private readonly Dictionary<TempleEventType, DateTime> closesAt = new();
    private readonly List<TempleEventType> order = new();

    public TempleEventType Current { get; private set; }
    public TempleEventType Registered { get; private set; }
    public bool AnyOpen => order.Count > 0;
    public IReadOnlyList<TempleEventType> Open => order;

    public bool IsOpen(TempleEventType type) => closesAt.ContainsKey(type);

    public bool OpenSignUp(TempleEventType type, int seconds, DateTime utcNow)
    {
        if (type == TempleEventType.None || seconds <= 0) return false;
        bool fresh = !closesAt.ContainsKey(type);
        closesAt[type] = utcNow.AddSeconds(seconds);
        if (!fresh) return false;
        order.Add(type);
        if (Current == TempleEventType.None || Registered != Current) Current = type;
        return true;
    }

    public void Close(TempleEventType type)
    {
        if (!closesAt.Remove(type)) return;
        order.Remove(type);
        if (Registered == type) Registered = TempleEventType.None;
        if (Current == type) Current = order.Count > 0 ? order[^1] : TempleEventType.None;
    }

    public List<TempleEventType> Expire(DateTime utcNow)
    {
        var expired = closesAt.Where(e => e.Value <= utcNow).Select(e => e.Key).ToList();
        foreach (var type in expired) Close(type);
        return expired;
    }

    public void CloseAll()
    {
        foreach (var type in order.ToList()) Close(type);
    }

    public int SecondsLeft(TempleEventType type, DateTime utcNow) =>
        closesAt.TryGetValue(type, out var at) ? Math.Max(0, (int)Math.Ceiling((at - utcNow).TotalSeconds)) : 0;

    public bool Select(TempleEventType type)
    {
        if (!closesAt.ContainsKey(type)) return false;
        Current = type;
        return true;
    }

    public void SetRegistered(TempleEventType type)
    {
        if (closesAt.ContainsKey(type)) Registered = type;
    }

    public void ClearRegistered() => Registered = TempleEventType.None;
}
