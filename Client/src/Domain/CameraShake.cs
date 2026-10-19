using System;

namespace LibreKO.Domain;

public sealed class CameraShake
{
    public const float GateSeconds = 5f;
    public const float GateAmplitude = 0.01f;
    public const float GateRate = 10f;
    public const float LookFactor = 2f;

    private float left;
    private float phase;
    private float amplitude;
    private float rate;

    public bool Active { get; private set; }

    public void Start(float seconds, float amplitude, float rate)
    {
        Active = true;
        left = seconds;
        this.amplitude = amplitude;
        this.rate = rate;
    }

    public float Step(float delta)
    {
        if (!Active) return 0f;
        float offset = -MathF.Sin(phase) * amplitude;
        left -= delta;
        if (phase < MathF.PI)
        {
            phase += rate * MathF.PI * delta;
        }
        else
        {
            phase = 0f;
            if (left < 0f)
            {
                Active = false;
                left = 0f;
            }
        }
        return offset;
    }
}
