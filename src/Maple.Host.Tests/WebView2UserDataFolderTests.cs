using Maple.Host;
using Xunit;

namespace Maple.Host.Tests;

public sealed class WebView2UserDataFolderTests
{
    [Fact]
    public void ResolveUsesPreferredFolderWhenWritable()
    {
        var created = new List<string>();

        string result = WebView2UserDataFolder.Resolve("preferred", "fallback", created.Add);

        Assert.Equal(Path.GetFullPath("preferred"), result);
        Assert.Equal([Path.GetFullPath("preferred")], created);
    }

    [Fact]
    public void ResolveFallsBackWhenPreferredFolderIsUnauthorized()
    {
        var created = new List<string>();

        string result = WebView2UserDataFolder.Resolve("preferred", "fallback", path =>
        {
            created.Add(path);
            if (path.EndsWith("preferred", StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        });

        Assert.Equal(Path.GetFullPath("fallback"), result);
        Assert.Equal([Path.GetFullPath("preferred"), Path.GetFullPath("fallback")], created);
    }
}
