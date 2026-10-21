using System.Net;
using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using Xunit;

namespace LibreKO.Common.Tests;

public class AddressBucketTests
{
    [Fact]
    public void AnIpv6NetworkCountsAsOneAddress()
    {
        AddressBucket.Of(IPAddress.Parse("2001:db8:1:2:aaaa::1")).Should().Be(AddressBucket.Of(IPAddress.Parse("2001:db8:1:2:bbbb::9")));
        AddressBucket.Of(IPAddress.Parse("2001:db8:1:3::1")).Should().NotBe(AddressBucket.Of(IPAddress.Parse("2001:db8:1:2::1")));
    }

    [Fact]
    public void AnIpv4AddressIsItsOwnBucketHoweverItArrives()
    {
        AddressBucket.Of(IPAddress.Parse("::ffff:203.0.113.7")).Should().Be(IPAddress.Parse("203.0.113.7"));
        AddressBucket.Of(IPAddress.Parse("203.0.113.7")).Should().Be(IPAddress.Parse("203.0.113.7"));
    }
}
