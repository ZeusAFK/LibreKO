using System;

namespace LibreKO.Domain;

public sealed class UpgradePreviewGate
{
    private int[]? _items;
    private int[]? _positions;

    public bool Pending => _items != null;

    public bool Begin(int[] items, int[] positions)
    {
        if (Pending) return false;
        _items = (int[])items.Clone();
        _positions = (int[])positions.Clone();
        return true;
    }

    public bool Complete(int[] items, int[] positions)
    {
        bool current = _items != null && _positions != null
            && _items.AsSpan().SequenceEqual(items) && _positions.AsSpan().SequenceEqual(positions);
        _items = null;
        _positions = null;
        return current;
    }
}
