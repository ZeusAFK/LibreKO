using Godot;

namespace LibreKO.Features;

internal interface ITerrainQueries
{
    Vector3 GroundPos(float mapX, float mapZ, float serverY);

    Vector3 GroundPos(float mapX, float mapZ, float serverY, float lift);

    Vector3 RestingSpot(Vector3 feet);
}
