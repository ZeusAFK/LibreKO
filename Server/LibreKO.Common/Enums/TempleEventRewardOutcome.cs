using System.Text.Json.Serialization;

namespace LibreKO.Common.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TempleEventRewardOutcome : byte
{
    Win = 0,
    Loss = 1,
    Timeout = 2,
    TimeoutWin = 3,
}
