namespace LibreKO.Domain;

public struct HeldSteer
{
    private bool armed;
    private float pressX, pressY;

    public void ArmAt(float x, float y)
    {
        armed = true;
        pressX = x;
        pressY = y;
    }

    public void Disarm() => armed = false;

    public bool TryStart(bool buttonHeld, float x, float y, float slop)
    {
        if (!armed) return false;
        if (!buttonHeld)
        {
            armed = false;
            return false;
        }
        float dx = x - pressX, dy = y - pressY;
        if (dx * dx + dy * dy <= slop * slop) return false;
        armed = false;
        return true;
    }
}
