using System.Text;
using Maple.Cloud;
using Xunit;

namespace Maple.Runtime.Tests.Cloud;

public sealed class WindowsDpapiCredentialStoreTests
{
    [Fact]
    public async Task CurrentUserDpapiRoundTripReplaceAndClearUsesCiphertextOnDisk()
    {
        if (!OperatingSystem.IsWindows()) return;

        string directory = Path.Combine(Path.GetTempPath(), "MapleDpapiTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "bailian.dat");
        const string first = "maple-dpapi-test-key-0001";
        const string second = "maple-dpapi-test-key-0002";
        var store = new WindowsBailianCredentialStore(path);

        try
        {
            await store.SetAsync(first.AsMemory(), CancellationToken.None);
            Assert.True(await store.IsConfiguredAsync(CancellationToken.None));
            Assert.DoesNotContain(first, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)), StringComparison.Ordinal);

            using (BailianCredentialLease? lease = await store.LeaseAsync(CancellationToken.None))
            {
                Assert.NotNull(lease);
                Assert.Equal(first, lease.Reveal());
            }

            await store.SetAsync(second.AsMemory(), CancellationToken.None);
            using (BailianCredentialLease? lease = await store.LeaseAsync(CancellationToken.None))
            {
                Assert.NotNull(lease);
                Assert.Equal(second, lease.Reveal());
            }

            await store.ClearAsync(CancellationToken.None);
            Assert.False(await store.IsConfiguredAsync(CancellationToken.None));
            Assert.Null(await store.LeaseAsync(CancellationToken.None));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
