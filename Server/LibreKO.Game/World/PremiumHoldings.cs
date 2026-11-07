using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public readonly record struct HeldPremium(byte Type, short Hours);

public enum PremiumSwitchResult
{
    Switched,
    NotHeld,
    TooSoon,
}

public static class PremiumHoldings
{
    public const byte DcPremium = 10;
    public const byte ExpPremium = 11;
    public const byte WarPremium = 12;
    public const byte SwitchingPremium = 13;
    public const int ListedMax = 6;
    public static readonly TimeSpan SwitchCooldown = TimeSpan.FromSeconds(30);

    private static readonly byte[] SwitchedTypes = [DcPremium, ExpPremium, WarPremium];

    public static IReadOnlyList<byte> TypesGrantedBy(byte type) => type == SwitchingPremium ? SwitchedTypes : [type];

    public static void Grant(UserSession session, byte type, int days, DateTime now)
    {
        foreach (var granted in TypesGrantedBy(type))
        {
            var standing = session.HeldPremiums.TryGetValue(granted, out var expiry) && expiry > now ? expiry : now;
            session.HeldPremiums[granted] = standing.AddDays(days);
            if (session.PremiumType == 0)
                session.PremiumService = granted;
        }
    }

    public static IReadOnlyList<HeldPremium> Listed(UserSession session, DateTime now) =>
        session.HeldPremiums
            .Select(held => new HeldPremium(held.Key, HoursLeft(held.Value, now)))
            .Where(held => held.Hours > 0)
            .OrderBy(held => held.Type)
            .Take(ListedMax)
            .ToList();

    public static PremiumSwitchResult Switch(UserSession session, byte type, DateTime now)
    {
        if (!session.HeldPremiums.TryGetValue(type, out var expiry) || HoursLeft(expiry, now) == 0)
            return PremiumSwitchResult.NotHeld;
        if (SwitchWait(session, now) > TimeSpan.Zero)
            return PremiumSwitchResult.TooSoon;

        session.PremiumService = type;
        session.LastPremiumSwitch = now;
        return PremiumSwitchResult.Switched;
    }

    public static Packet ListPacket(UserSession session, IGameDataService gameData) =>
        MiscPacketWriter.Premium(Listed(session, DateTime.UtcNow), session.PremiumType, PremiumBonusList.For(session, gameData));

    public static TimeSpan SwitchWait(UserSession session, DateTime now)
    {
        var wait = session.LastPremiumSwitch + SwitchCooldown - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    public static void ChooseOnLogin(UserSession session, DateTime now)
    {
        if (session.PremiumType != 0)
            return;

        var longest = session.HeldPremiums.Where(held => HoursLeft(held.Value, now) > 0)
            .OrderByDescending(held => held.Value).Select(held => held.Key).FirstOrDefault();
        if (longest != 0)
            session.PremiumService = longest;
    }

    private static short HoursLeft(DateTime expiry, DateTime now) =>
        (short)Math.Clamp(Math.Floor((expiry - now).TotalHours), 0, short.MaxValue);
}
