using Maple.Input;
using Xunit;

namespace Maple.Input.Tests;

public sealed class WindowsMapleHidDeviceLocatorTests
{
    [Fact]
    public void ReturnsOnlyProjectInterface()
    {
        var enumerator = new FakeEnumerator([@"\\?\root#maplevhfkeyboard#one"]);
        var locator = new WindowsMapleHidDeviceLocator(enumerator);

        Assert.True(locator.TryLocate(out string path, out string error), error);
        Assert.Equal(@"\\?\root#maplevhfkeyboard#one", path);
        Assert.Equal(MapleHidDeviceIdentity.InterfaceClassGuid, enumerator.RequestedGuid);
    }

    [Fact]
    public void MissingDeviceIsExplicit()
    {
        var locator = new WindowsMapleHidDeviceLocator(new FakeEnumerator([]));

        Assert.False(locator.TryLocate(out _, out string error));
        Assert.Equal("HID_DEVICE_NOT_INSTALLED", error);
    }

    [Fact]
    public void MultipleProjectInterfacesAreRejected()
    {
        var locator = new WindowsMapleHidDeviceLocator(new FakeEnumerator(["one", "two"]));

        Assert.False(locator.TryLocate(out _, out string error));
        Assert.Equal("HID_DEVICE_AMBIGUOUS:2", error);
    }

    private sealed class FakeEnumerator(IReadOnlyList<string> paths) : IDeviceInterfaceEnumerator
    {
        public Guid RequestedGuid { get; private set; }

        public bool TryEnumerate(Guid interfaceClassGuid, out IReadOnlyList<string> devicePaths, out string error)
        {
            RequestedGuid = interfaceClassGuid;
            devicePaths = paths;
            error = string.Empty;
            return true;
        }
    }
}
