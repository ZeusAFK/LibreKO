namespace LibreKO.Features.Core;

internal interface IDebugOverlay
{
    bool IsOpen { get; }

    void Invalidate();
}
