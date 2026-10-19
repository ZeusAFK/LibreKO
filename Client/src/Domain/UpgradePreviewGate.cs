using System;

namespace LibreKO.Domain;

public sealed class UpgradePreviewGate
{
    private int[]? items;
    private int[]? positions;

    public bool Pending => items != null;

    public bool Begin(int[] items, int[] positions)
    {
        if (Pending) return false;
        this.items = (int[])items.Clone();
        this.positions = (int[])positions.Clone();
        return true;
    }

    public bool Complete(int[] items, int[] positions)
    {
        bool current = this.items != null && this.positions != null
            && this.items.AsSpan().SequenceEqual(items) && this.positions.AsSpan().SequenceEqual(positions);
        this.items = null;
        this.positions = null;
        return current;
    }
}
