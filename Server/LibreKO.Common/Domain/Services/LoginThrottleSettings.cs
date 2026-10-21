namespace LibreKO.Common.Domain.Services;

public enum BusyAddressMode
{
    Slow,
    Refuse,
}

public class LoginThrottleSettings
{
    public BusyAddressMode BusyAddress { get; set; } = BusyAddressMode.Slow;
}
