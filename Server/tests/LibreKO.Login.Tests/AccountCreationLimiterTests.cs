using System.Net;
using FluentAssertions;
using LibreKO.Login.Configuration;
using Microsoft.Extensions.Options;

namespace LibreKO.Login.Tests;

public class AccountCreationLimiterTests
{
    private static readonly DateTime Start = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static AccountCreationLimiter Limiter(int perHour) =>
        new(Options.Create(new LoginServerSettings { Account = new AccountSettings { AutoCreate = true, AutoCreatePerNetworkPerHour = perHour } }));

    [Fact]
    public void ANetworkMayCreateItsHourlyShareAndNoMore()
    {
        var limiter = Limiter(3);

        limiter.TryTake(IPAddress.Parse("203.0.113.10"), Start).Should().BeTrue();
        limiter.TryTake(IPAddress.Parse("203.0.113.11"), Start).Should().BeTrue();
        limiter.TryTake(IPAddress.Parse("203.0.113.12"), Start).Should().BeTrue();
        limiter.TryTake(IPAddress.Parse("203.0.113.13"), Start).Should().BeFalse("the same /24 has used its three");
        limiter.TryTake(IPAddress.Parse("198.51.100.7"), Start).Should().BeTrue("another network has its own count");
    }

    [Fact]
    public void TheCountFreesUpAfterAnHour()
    {
        var limiter = Limiter(1);
        var address = IPAddress.Parse("203.0.113.10");

        limiter.TryTake(address, Start).Should().BeTrue();
        limiter.TryTake(address, Start.AddMinutes(59)).Should().BeFalse();
        limiter.TryTake(address, Start.AddHours(1)).Should().BeTrue();
    }

    [Fact]
    public void LoopbackAndAZeroLimitAreNeverRefused()
    {
        var limiter = Limiter(1);
        limiter.TryTake(IPAddress.Loopback, Start).Should().BeTrue();
        limiter.TryTake(IPAddress.Loopback, Start).Should().BeTrue();

        var open = Limiter(0);
        open.TryTake(IPAddress.Parse("203.0.113.10"), Start).Should().BeTrue();
        open.TryTake(IPAddress.Parse("203.0.113.10"), Start).Should().BeTrue();
    }
}
