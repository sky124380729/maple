using Xunit;

namespace Maple.Host.Tests;

public sealed class LocalNavigationPolicyTests
{
    [Theory]
    [InlineData("https://maple.local/index.html", true)]
    [InlineData("https://maple.local/assets/index.js", true)]
    [InlineData("http://maple.local/index.html", false)]
    [InlineData("https://maple.local.example/index.html", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("file:///tmp/index.html", false)]
    public void AllowsOnlyTheHttpsVirtualHost(string uri, bool expected)
    {
        Assert.Equal(expected, LocalNavigationPolicy.IsAllowed(uri));
    }
}
