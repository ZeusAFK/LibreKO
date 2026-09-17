using System;
using Godot;
using FileAccess = Godot.FileAccess;

namespace LibreKO;

public sealed class KoCollision
{
    public Vector3[] Verts = Array.Empty<Vector3>();

    public static KoCollision? Load(string stem)
    {
        using var f = FileAccess.Open($"res://assets/terrain/{stem}/collision.bin", FileAccess.ModeFlags.Read);
        if (f == null)
            return null;
        var buf = f.GetBuffer((long)f.GetLength());
        if (buf.Length < 8 || buf[0] != (byte)'K' || buf[1] != (byte)'O' || buf[2] != (byte)'C' || buf[3] != (byte)'L')
            return null;
        uint tris = BitConverter.ToUInt32(buf, 4);
        long need = 8L + (long)tris * 9 * 4;
        if (tris == 0 || buf.Length < need)
            return null;

        var verts = new Vector3[tris * 3];
        int o = 8;
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i] = new Vector3(
                BitConverter.ToSingle(buf, o),
                BitConverter.ToSingle(buf, o + 4),
                BitConverter.ToSingle(buf, o + 8));
            o += 12;
        }
        return new KoCollision { Verts = verts };
    }
}
