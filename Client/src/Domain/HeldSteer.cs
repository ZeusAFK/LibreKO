namespace LibreKO.Domain;

public struct HeldSteer
{
    private bool _armed;
    private float _pressX, _pressY;

    public void ArmAt(float x, float y)
    {
        _armed = true;
        _pressX = x;
        _pressY = y;
    }

    public void Disarm() => _armed = false;

    public bool TryStart(bool buttonHeld, float x, float y, float slop)
    {
        if (!_armed) return false;
        if (!buttonHeld)
        {
            _armed = false;
            return false;
        }
        float dx = x - _pressX, dy = y - _pressY;
        if (dx * dx + dy * dy <= slop * slop) return false;
        _armed = false;
        return true;
    }
}
