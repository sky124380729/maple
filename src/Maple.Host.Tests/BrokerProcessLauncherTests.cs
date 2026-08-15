using System.Diagnostics;
using Maple.Host;
using Xunit;

namespace Maple.Host.Tests;

public sealed class BrokerProcessLauncherTests
{
    [Fact]
    public void LauncherUsesRunAsAndOnlyNonSecretArguments()
    {
        var launcher = new BrokerProcessLauncher();

        ProcessStartInfo info = launcher.CreateStartInfo(
            "C:\\Maple\\Maple.InputBroker.exe",
            "maple.0123456789abcdef",
            1234);

        Assert.Equal("runas", info.Verb);
        Assert.True(info.UseShellExecute);
        Assert.Contains("--pipe maple.0123456789abcdef", info.Arguments);
        Assert.Contains("--parent-pid 1234", info.Arguments);
        Assert.DoesNotContain("token", info.Arguments, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scan", info.Arguments, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SessionPipeNamesAreUnpredictableAndDistinct()
    {
        string first = BrokerProcessLauncher.CreatePipeName();
        string second = BrokerProcessLauncher.CreatePipeName();

        Assert.NotEqual(first, second);
        Assert.StartsWith("maple.", first);
        Assert.True(first.Length >= 22);
    }
}
