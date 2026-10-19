namespace LibreKO.Features.Core;

internal interface IMoveGates
{
    bool InputHeld { get; }

    bool WhisperHasFocus { get; }

    bool StallBlocksMove();
}
