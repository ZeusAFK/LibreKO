using Godot;

namespace LibreKO.Presentation;

internal sealed class ViewFrustum
{
    private const int MaxPlanes = 6;

    private readonly Plane[] planes = new Plane[MaxPlanes];
    private int count;

    internal void Refresh(Camera3D? camera)
    {
        count = 0;
        if (camera == null) return;
        var frustum = camera.GetFrustum();
        count = Mathf.Min(frustum.Count, planes.Length);
        for (int i = 0; i < count; i++) planes[i] = frustum[i];
    }

    internal bool Contains(Vector3 position, float margin)
    {
        for (int i = 0; i < count; i++)
            if (planes[i].DistanceTo(position) > margin) return false;
        return true;
    }
}
