using System.Text.Json.Serialization;

namespace LibreKO.Common.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TempleEvent : byte
{
    None = 0,
    Chaos = 1,
    BorderDefenseWar = 2,
    JuraidMountain = 3,
}
