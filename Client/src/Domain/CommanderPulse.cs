namespace LibreKO.Domain;

public sealed class CommanderPulse
{
    public const int StartAlpha = 150;
    public const int MaxAlpha = 250;
    public const int AlphaStep = 4;
    public const double StepSeconds = 0.01;
    public const double MinSeconds = 5.0;
    public const int FramesPerImage = 8;
    public const int RingImages = 10;
    public const int SweepImages = 9;

    private int direction = 1;
    private double sinceStep;
    private int frames;

    public int Alpha { get; private set; }
    public double Elapsed { get; private set; }
    public bool Running { get; private set; }
    public int RingImage { get; private set; }
    public int SweepImage { get; private set; }
    public bool Sweeping { get; private set; }

    public void Start()
    {
        Alpha = StartAlpha;
        direction = 1;
        sinceStep = StepSeconds;
        frames = 0;
        Elapsed = 0;
        RingImage = 0;
        SweepImage = 0;
        Sweeping = true;
        Running = true;
    }

    public void Stop() => Running = false;

    public void Frame(double delta)
    {
        if (!Running) return;
        Elapsed += delta;
        sinceStep += delta;
        if (sinceStep >= StepSeconds)
        {
            sinceStep = 0;
            Alpha += AlphaStep * direction;
            if (Alpha >= MaxAlpha)
            {
                Alpha = MaxAlpha;
                direction = -1;
            }
            else if (Alpha <= 0)
            {
                Alpha = 0;
                direction = 1;
                if (Elapsed > MinSeconds) Running = false;
            }
        }
        if (++frames < FramesPerImage) return;
        frames = 0;
        RingImage = (RingImage + 1) % RingImages;
        if (!Sweeping) return;
        if (++SweepImage >= SweepImages)
        {
            SweepImage = SweepImages - 1;
            Sweeping = false;
        }
    }
}
