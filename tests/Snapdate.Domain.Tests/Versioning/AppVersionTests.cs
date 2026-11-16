using Snapdate.Domain.Versioning;

namespace Snapdate.Domain.Tests.Versioning;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v0.2.0", 0, 2, 0, null)]
    [InlineData("V10.20.30", 10, 20, 30, null)]
    [InlineData("0.2.0-rc.1", 0, 2, 0, "rc.1")]
    [InlineData("1.0.0+abc123", 1, 0, 0, null)]
    [InlineData("1.0.0-beta+sha.5", 1, 0, 0, "beta")]
    [InlineData(" v1.0.0 ", 1, 0, 0, null)]
    public void Parses_valid_versions(string text, int major, int minor, int patch, string? pre)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal((major, minor, patch, pre), (v!.Major, v.Minor, v.Patch, v.PreRelease));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("a.b.c")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-a..b")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3-rc_1")]
    [InlineData("-1.2.3")]
    [InlineData("vv1.2.3")]
    [InlineData("99999999999.0.0")]
    [InlineData("latest")]
    public void Rejects_invalid_input_without_throwing(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out var v));
        Assert.Null(v);
    }

    [Fact]
    public void Parse_throws_FormatException_on_invalid_input() =>
        Assert.Throws<FormatException>(() => AppVersion.Parse("nope"));

    [Theory]
    [InlineData("0.2.0", "0.1.9")]
    [InlineData("0.10.0", "0.9.0")]
    [InlineData("1.0.0", "0.99.99")]
    [InlineData("0.2.1", "0.2.0")]
    [InlineData("0.2.0", "0.2.0-rc.1")]
    [InlineData("0.2.0-rc.2", "0.2.0-rc.1")]
    [InlineData("0.2.0-rc.10", "0.2.0-rc.9")]
    [InlineData("0.2.0-rc", "0.2.0-beta")]
    [InlineData("0.2.0-alpha.1", "0.2.0-alpha")]
    [InlineData("0.2.0-alpha.beta", "0.2.0-alpha.1")]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.11")]
    public void Orders_by_semver_precedence(string higher, string lower)
    {
        var h = AppVersion.Parse(higher);
        var l = AppVersion.Parse(lower);
        Assert.True(h > l);
        Assert.True(l < h);
        Assert.True(h >= l);
        Assert.True(l <= h);
        Assert.NotEqual(h, l);
    }

    [Fact]
    public void Prefix_and_build_metadata_do_not_affect_equality()
    {
        Assert.Equal(AppVersion.Parse("v1.2.3"), AppVersion.Parse("1.2.3+build.7"));
        Assert.Equal(AppVersion.Parse("v1.2.3").GetHashCode(), AppVersion.Parse("1.2.3+x").GetHashCode());
        Assert.True(AppVersion.Parse("1.2.3") == AppVersion.Parse("v1.2.3"));
    }

    [Fact]
    public void ToString_is_canonical()
    {
        Assert.Equal("0.2.0-rc.1", AppVersion.Parse("v0.2.0-rc.1+x").ToString());
        Assert.Equal("1.0.0", AppVersion.Parse("V1.0.0").ToString());
    }

    [Fact]
    public void IsPreRelease_reflects_tag()
    {
        Assert.True(AppVersion.Parse("1.0.0-rc.1").IsPreRelease);
        Assert.False(AppVersion.Parse("1.0.0").IsPreRelease);
    }
}
