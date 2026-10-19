namespace LibreKO.Features.Hud;

internal interface IGenieRunner
{
    GenieSetup Setup { get; }

    int Points { get; }

    void Stop();
}
