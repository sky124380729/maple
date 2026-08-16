using System.Diagnostics;
using Maple.InputBroker;
using Xunit;

namespace Maple.InputBroker.Tests;

public sealed class BrokerProcessIdentityReaderTests
{
    [Fact]
    public void ReadsCurrentProcessWithLimitedInformationRights()
    {
        using Process process = Process.GetCurrentProcess();
        var reader = new WindowsBrokerProcessIdentityReader();

        BrokerProcessIdentity identity = reader.Read(process.Id);

        Assert.Equal(process.StartTime.ToUniversalTime().Ticks, identity.StartedAtUtcTicks);
        Assert.Equal(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(identity.ExecutablePath), StringComparer.OrdinalIgnoreCase);
    }
}
