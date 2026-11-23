using System.Net;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Core.Tests;

/// <summary>SEC-40, SEC-41, AC-13: which addresses URL import may reach.</summary>
[Trait("Req", "AC-13")]
[Trait("Req", "SEC-40")]
[Trait("Req", "SEC-41")]
public sealed class AC_13_UrlRulesTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("fd00:ec2::254")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::")]
    [InlineData("::ffff:192.168.1.10")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("64:ff9b::a00:5")]
    [InlineData("2002:c0a8:010a::1")]
    [InlineData("::10.0.0.5")]
    public void Private_and_special_addresses_are_blocked(string address)
    {
        UrlRules.IsBlocked(IPAddress.Parse(address)).ShouldBeTrue(address);
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700::6810:84e5")]
    [InlineData("::ffff:93.184.216.34")]
    public void Public_addresses_are_allowed(string address)
    {
        UrlRules.IsBlocked(IPAddress.Parse(address)).ShouldBeFalse(address);
    }

    [Theory]
    [InlineData("https://www.example.com/soup")]
    [InlineData("http://example.com:80/soup?x=1")]
    [InlineData("https://example.com:443/")]
    public void Ordinary_web_addresses_pass(string url)
    {
        UrlRules.Check(url).IsAllowed.ShouldBeTrue(url);
    }

    [Theory]
    [InlineData("ftp://example.com/soup", UrlRules.WrongScheme)]
    [InlineData("file:///etc/passwd", UrlRules.WrongScheme)]
    [InlineData("javascript:alert(1)", UrlRules.WrongScheme)]
    [InlineData("https://example.com:8443/", UrlRules.WrongPort)]
    [InlineData("http://example.com:22/", UrlRules.WrongPort)]
    [InlineData("https://user:pass@example.com/", UrlRules.HasCredentials)]
    [InlineData("http://127.0.0.1/", UrlRules.PrivateAddress)]
    [InlineData("http://10.0.0.5/admin", UrlRules.PrivateAddress)]
    [InlineData("http://192.168.1.10/", UrlRules.PrivateAddress)]
    [InlineData("http://169.254.169.254/latest/meta-data/", UrlRules.PrivateAddress)]
    [InlineData("http://[::1]/", UrlRules.PrivateAddress)]
    [InlineData("http://localhost/", UrlRules.PrivateAddress)]
    [InlineData("http://app.localhost/", UrlRules.PrivateAddress)]
    [InlineData("http://2130706433/", UrlRules.PrivateAddress)]
    [InlineData("http://0x7f.1/", UrlRules.PrivateAddress)]
    [InlineData("soup", UrlRules.NotAnAddress)]
    [InlineData("", UrlRules.NotAnAddress)]
    public void Refused_addresses_say_why(string url, string error)
    {
        var check = UrlRules.Check(url);
        check.IsAllowed.ShouldBeFalse(url);
        check.Error.ShouldBe(error, url);
    }

    [Fact]
    public void Very_long_addresses_are_refused()
    {
        UrlRules.Check("https://example.com/" + new string('a', 2100)).Error.ShouldBe(UrlRules.TooLong);
    }
}
