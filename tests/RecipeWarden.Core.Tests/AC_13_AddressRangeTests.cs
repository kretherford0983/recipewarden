using System.Net;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Core.Tests;

/// <summary>SEC-41: every special-purpose range, at its edges, plus the addresses just outside.</summary>
[Trait("Req", "SEC-41")]
[Trait("Req", "AC-13")]
public sealed class AC_13_AddressRangeTests
{
    [Theory]
    [InlineData("0.1.2.3", true)]
    [InlineData("1.0.0.1", false)]
    [InlineData("9.255.255.255", false)]
    [InlineData("10.255.255.255", true)]
    [InlineData("11.0.0.0", false)]
    [InlineData("100.63.255.255", false)]
    [InlineData("100.64.0.0", true)]
    [InlineData("100.127.255.255", true)]
    [InlineData("126.255.255.255", false)]
    [InlineData("127.255.255.254", true)]
    [InlineData("169.253.0.1", false)]
    [InlineData("169.254.0.1", true)]
    [InlineData("172.15.255.255", false)]
    [InlineData("172.16.0.0", true)]
    [InlineData("192.0.0.8", true)]
    [InlineData("192.0.1.1", false)]
    [InlineData("192.0.2.1", true)]
    [InlineData("192.88.99.1", true)]
    [InlineData("192.88.98.1", false)]
    [InlineData("192.167.0.1", false)]
    [InlineData("198.17.0.1", false)]
    [InlineData("198.18.0.1", true)]
    [InlineData("198.19.255.255", true)]
    [InlineData("198.20.0.1", false)]
    [InlineData("198.51.100.7", true)]
    [InlineData("198.51.101.7", false)]
    [InlineData("203.0.113.9", true)]
    [InlineData("203.0.114.9", false)]
    [InlineData("223.255.255.255", false)]
    [InlineData("239.1.1.1", true)]
    [InlineData("240.0.0.1", true)]
    public void Ipv4_ranges(string address, bool blocked)
    {
        UrlRules.IsBlocked(IPAddress.Parse(address)).ShouldBe(blocked, address);
    }

    [Theory]
    [InlineData("fdff:ffff::1", true)]
    [InlineData("fbff::1", false)]
    [InlineData("febf::1", true)]
    [InlineData("fec0::1", true)]
    [InlineData("fe7f::1", false)]
    [InlineData("ff05::2", true)]
    [InlineData("2001:db8::1", true)]
    [InlineData("2001:db9::1", false)]
    [InlineData("100::1", true)]
    [InlineData("100:0:0:1::1", false)]
    [InlineData("64:ff9b::808:808", false)]
    [InlineData("64:ff9b::c0a8:10a", true)]
    [InlineData("64:ff9b:1::a00:5", false)]
    [InlineData("2002:808:808::1", false)]
    [InlineData("2002:7f00:1::1", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    [InlineData("::ffff:100.64.1.1", true)]
    [InlineData("::0.0.0.1", true)]
    [InlineData("2a00:1450:4001::1", false)]
    public void Ipv6_ranges(string address, bool blocked)
    {
        UrlRules.IsBlocked(IPAddress.Parse(address)).ShouldBe(blocked, address);
    }

    [Fact]
    public void Absolute_uris_are_checked_the_same_way()
    {
        UrlRules.Check(new Uri("https://example.com/")).IsAllowed.ShouldBeTrue();
        UrlRules.Check(new Uri("http://[fd00::1]/")).Error.ShouldBe(UrlRules.PrivateAddress);
        UrlRules.Check(new Uri("/relative", UriKind.Relative)).Error.ShouldBe(UrlRules.NotAnAddress);
        UrlRules.Check(new Uri("mailto:a@example.com")).Error.ShouldBe(UrlRules.WrongScheme);
        UrlRules.Check("http:///nohost").Error.ShouldNotBeNull();
        Should.Throw<ArgumentNullException>(() => UrlRules.Check((Uri)null!));
    }
}
