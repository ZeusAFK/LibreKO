using System.Text;

namespace LibreKO.Game.World;

public class SmdFile
{
    public int MapSize { get; private set; }
    public float UnitDistance { get; private set; }
    public float[] HeightMap { get; private set; } = [];
    public short[] EventTiles { get; private set; } = [];
    public Dictionary<int, ObjectEvent> ObjectEvents { get; } = [];
    public Dictionary<int, RegeneEvent> RegeneEvents { get; } = [];
    public Dictionary<int, WarpInfo> Warps { get; } = [];

    // Collision data
    public float MapWidth { get; private set; }
    public float MapHeight { get; private set; }
    public int CollisionFaceCount { get; private set; }
    public float[] CollisionVertices { get; private set; } = []; // 3 floats per vertex, 3 vertices per face

    public int XRegionMax { get; private set; }
    public int ZRegionMax { get; private set; }

    private const float ViewDistance = 32.0f;

    public static SmdFile? Load(string filePath, bool loadWarpsAndRegene = true)
    {
        if (!File.Exists(filePath))
            return null;

        using var fs = File.OpenRead(filePath);
        using var reader = new BinaryReader(fs);

        var smd = new SmdFile();

        try
        {
            smd.LoadTerrain(reader);
            smd.LoadCollisionData(reader);

            float mapWidth = (smd.MapSize - 1) * smd.UnitDistance;
            if (Math.Abs(mapWidth - smd.MapWidth) > 0.01f)
                return null; // Mismatched map/collision dimensions

            smd.XRegionMax = (int)(mapWidth / ViewDistance);
            smd.ZRegionMax = (int)(mapWidth / ViewDistance);

            smd.LoadObjectEvents(reader);
            smd.LoadMapTiles(reader);

            if (loadWarpsAndRegene)
            {
                smd.LoadRegeneEvents(reader);
                smd.LoadWarpList(reader);
            }

            return smd;
        }
        catch (EndOfStreamException)
        {
            // Truncated file — return what we have if terrain loaded
            return smd.HeightMap.Length > 0 ? smd : null;
        }
    }

    private void LoadTerrain(BinaryReader reader)
    {
        MapSize = reader.ReadInt32();
        UnitDistance = reader.ReadSingle();

        int count = MapSize * MapSize;
        HeightMap = new float[count];
        for (int i = 0; i < count; i++)
            HeightMap[i] = reader.ReadSingle();
    }

    private void LoadCollisionData(BinaryReader reader)
    {
        MapWidth = reader.ReadSingle();
        MapHeight = reader.ReadSingle();

        CollisionFaceCount = reader.ReadInt32();

        if (CollisionFaceCount > 0)
        {
            // 3 vertices per face, 3 floats (x,y,z) per vertex
            int vertexCount = CollisionFaceCount * 3 * 3;
            CollisionVertices = new float[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                CollisionVertices[i] = reader.ReadSingle();
        }

        // Cell data - we skip it but need to read past it
        const int cellMainSize = 16; // 4 * 4
        const int cellMainDevide = 4;

        for (float fZ = 0.0f; fZ < MapHeight; fZ += cellMainSize)
        {
            for (float fX = 0.0f; fX < MapWidth; fX += cellMainSize)
            {
                uint bExist = reader.ReadUInt32();
                if (bExist == 0) continue;

                // CellMain: shapeCount + shapeIndices + 4x4 subcells
                int shapeCount = reader.ReadInt32();
                if (shapeCount > 0)
                    reader.ReadBytes(shapeCount * 2); // WORD indices

                for (int sz = 0; sz < cellMainDevide; sz++)
                {
                    for (int sx = 0; sx < cellMainDevide; sx++)
                    {
                        int polyCount = reader.ReadInt32();
                        if (polyCount > 0)
                            reader.ReadBytes(polyCount * 3 * 4); // uint32 indices
                    }
                }
            }
        }
    }

    private void LoadObjectEvents(BinaryReader reader)
    {
        int count = reader.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            var evt = new ObjectEvent
            {
                Belong = reader.ReadInt32(),
                Index = reader.ReadInt16(),
                Type = reader.ReadInt16(),
                ControlNpcId = reader.ReadInt16(),
                Status = reader.ReadInt16(),
                PosX = reader.ReadSingle(),
                PosY = reader.ReadSingle(),
                PosZ = reader.ReadSingle(),
                Life = 1
            };

            if (evt.Index > 0)
                ObjectEvents.TryAdd(evt.Index, evt);
        }
    }

    private void LoadMapTiles(BinaryReader reader)
    {
        int count = MapSize * MapSize;
        EventTiles = new short[count];
        for (int i = 0; i < count; i++)
            EventTiles[i] = reader.ReadInt16();
    }

    private void LoadRegeneEvents(BinaryReader reader)
    {
        int count = reader.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            var evt = new RegeneEvent
            {
                PosX = reader.ReadSingle(),
                PosY = reader.ReadSingle(),
                PosZ = reader.ReadSingle(),
                AreaZ = reader.ReadSingle(),
                AreaX = reader.ReadSingle(),
                RegenePoint = i
            };

            RegeneEvents.TryAdd(i, evt);
        }
    }

    private void LoadWarpList(BinaryReader reader)
    {
        int count = reader.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            try
            {
                var warp = new WarpInfo
                {
                    WarpId = reader.ReadInt16(),
                    Name = Encoding.ASCII.GetString(reader.ReadBytes(32)).TrimEnd('\0'),
                    Announce = Encoding.ASCII.GetString(reader.ReadBytes(256)).TrimEnd('\0'),
                };
                reader.ReadUInt16(); // padding
                warp.Fee = reader.ReadUInt32();
                warp.Zone = reader.ReadInt16();
                reader.ReadUInt16(); // padding
                warp.X = reader.ReadSingle();
                warp.Y = reader.ReadSingle();
                warp.Z = reader.ReadSingle();
                warp.Radius = reader.ReadSingle();
                warp.Nation = reader.ReadInt16();
                reader.ReadUInt16(); // padding

                if (warp.WarpId > 0)
                    Warps.TryAdd(warp.WarpId, warp);
            }
            catch (EndOfStreamException)
            {
                // Some SMDs have truncated warp data
                break;
            }
        }
    }

    public float GetHeight(float x, float z)
    {
        int iX = (int)(x / UnitDistance);
        int iZ = (int)(z / UnitDistance);

        float dX = (x - iX * UnitDistance) / UnitDistance;
        float dZ = (z - iZ * UnitDistance) / UnitDistance;

        if (dX < 0.0f || dZ < 0.0f || dX >= 1.0f || dZ >= 1.0f)
            return float.MinValue;

        if (iX < 0 || iX >= MapSize - 1 || iZ < 0 || iZ >= MapSize - 1)
            return float.MinValue;

        float h1, h2, h3;

        if ((iX + iZ) % 2 == 1)
        {
            if (dX + dZ < 1.0f)
            {
                h1 = HeightMap[iX * MapSize + iZ + 1];
                h2 = HeightMap[(iX + 1) * MapSize + iZ];
                h3 = HeightMap[iX * MapSize + iZ];

                float h12 = h1 + (h2 - h1) * dX;
                float h32 = h3 + (h2 - h3) * dX;
                return h32 + (h12 - h32) * (dZ / (1.0f - dX));
            }
            else
            {
                h1 = HeightMap[iX * MapSize + iZ + 1];
                h2 = HeightMap[(iX + 1) * MapSize + iZ];
                h3 = HeightMap[(iX + 1) * MapSize + iZ + 1];

                if (dX == 0.0f) return h1;

                float h12 = h1 + (h2 - h1) * dX;
                float h13 = h1 + (h3 - h1) * dX;
                return h13 + (h12 - h13) * ((1.0f - dZ) / dX);
            }
        }
        else
        {
            if (dZ > dX)
            {
                h1 = HeightMap[iX * MapSize + iZ + 1];
                h2 = HeightMap[(iX + 1) * MapSize + iZ + 1];
                h3 = HeightMap[iX * MapSize + iZ];

                float h12 = h1 + (h2 - h1) * dX;
                float h32 = h3 + (h2 - h3) * dX;
                return h12 + (h32 - h12) * ((1.0f - dZ) / (1.0f - dX));
            }
            else
            {
                h1 = HeightMap[iX * MapSize + iZ];
                h2 = HeightMap[(iX + 1) * MapSize + iZ];
                h3 = HeightMap[(iX + 1) * MapSize + iZ + 1];

                if (dX == 0.0f) return h1;

                float h12 = h1 + (h2 - h1) * dX;
                float h13 = h1 + (h3 - h1) * dX;
                return h12 + (h13 - h12) * (dZ / dX);
            }
        }
    }

    public int GetEventId(int x, int z)
    {
        if (x < 0 || x >= MapSize || z < 0 || z >= MapSize)
            return -1;

        return EventTiles[x * MapSize + z];
    }

    public int GetEventIdAtPosition(float x, float z)
    {
        int iX = (int)(x / UnitDistance);
        int iZ = (int)(z / UnitDistance);
        return GetEventId(iX, iZ);
    }

    public bool IsValidPosition(float x, float z)
    {
        return x >= 0 && x < (MapSize - 1) * UnitDistance
            && z >= 0 && z < (MapSize - 1) * UnitDistance;
    }
}

public class ObjectEvent
{
    public int Belong { get; set; }
    public short Index { get; set; }
    public short Type { get; set; }
    public short ControlNpcId { get; set; }
    public short Status { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public byte Life { get; set; }
}

public class RegeneEvent
{
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float AreaZ { get; set; }
    public float AreaX { get; set; }
    public int RegenePoint { get; set; }
}

public class WarpInfo
{
    public short WarpId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Announce { get; set; } = string.Empty;
    public uint Fee { get; set; }
    public short Zone { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; }
    public short Nation { get; set; }
}
