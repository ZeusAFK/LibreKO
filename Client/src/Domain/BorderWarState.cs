using System;

namespace LibreKO.Domain;

public sealed class BorderWarState
{
    public int KarusScore { get; private set; }
    public int ElmoradScore { get; private set; }
    public string Carrier { get; private set; } = "";
    public byte CarrierNation { get; private set; }
    public DateTime? AltarBackUtc { get; private set; }
    public int? Winner { get; private set; }
    public DateTime? HomeUtc { get; private set; }

    public bool Finished => Winner.HasValue;

    public void SetScores(int karus, int elmorad)
    {
        KarusScore = karus;
        ElmoradScore = elmorad;
    }

    public void FragmentTaken(string carrier, byte nation)
    {
        Carrier = carrier;
        CarrierNation = nation;
        AltarBackUtc = null;
    }

    public void AltarTimer(int seconds, DateTime now)
    {
        Carrier = "";
        CarrierNation = 0;
        AltarBackUtc = seconds > 0 ? now.AddSeconds(seconds) : null;
    }

    public void Finish(int winner, uint seconds, DateTime now)
    {
        Winner = winner;
        HomeUtc = now.AddSeconds(seconds);
        Carrier = "";
        CarrierNation = 0;
        AltarBackUtc = null;
    }

    public int AltarSecondsLeft(DateTime now) => SecondsUntil(AltarBackUtc, now);

    public int HomeSecondsLeft(DateTime now) => SecondsUntil(HomeUtc, now);

    public void Reset()
    {
        SetScores(0, 0);
        Carrier = "";
        CarrierNation = 0;
        AltarBackUtc = null;
        Winner = null;
        HomeUtc = null;
    }

    private static int SecondsUntil(DateTime? at, DateTime now) =>
        at is { } when ? Math.Max(0, (int)Math.Ceiling((when - now).TotalSeconds)) : 0;
}
