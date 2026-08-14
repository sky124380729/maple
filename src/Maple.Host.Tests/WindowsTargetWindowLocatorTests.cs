using Maple.Host;
using Xunit;
using Xunit.Abstractions;

namespace Maple.Host.Tests;

public sealed class WindowsTargetWindowLocatorTests
{
    private readonly ITestOutputHelper output;

    public WindowsTargetWindowLocatorTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void NoEligibleWindowReturnsNotFound()
    {
        var locator = new WindowsTargetWindowLocator(new FakeWindowSystem([]));

        TargetWindowDiscoveryResult result = locator.Locate();

        Assert.Equal(TargetWindowDiscoveryStatus.NotFound, result.Status);
        Assert.Equal("TARGET_NOT_FOUND", result.DiagnosticCode);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void OneEligibleWindowIsBoundWithStableIdentity()
    {
        var candidate = ValidCandidate() with { IsForeground = false };
        var locator = new WindowsTargetWindowLocator(new FakeWindowSystem([candidate]));

        TargetWindowDiscoveryResult result = locator.Locate();

        Assert.Equal(TargetWindowDiscoveryStatus.Found, result.Status);
        WindowIdentity target = Assert.Single(result.Candidates);
        Assert.Equal("0x0000000000012345", target.Hwnd);
        Assert.Equal(4768, target.Pid);
        Assert.Equal(2048, target.ClientWidth);
        Assert.Equal(1200, target.ClientHeight);
        Assert.Equal(144, target.Dpi);
        Assert.False(target.IsForeground);
        Assert.Equal(64, target.ProcessPathSha256.Length);
        Assert.DoesNotContain("Maplestory_Classic.exe", target.ProcessPathSha256);
        Assert.Equal("1.2.3.4", target.ProcessVersion);
    }

    [Fact]
    public void MultipleEligibleWindowsRequireSelection()
    {
        var locator = new WindowsTargetWindowLocator(new FakeWindowSystem([
            ValidCandidate(),
            ValidCandidate() with { Hwnd = (nint)0x4567, Pid = 9210 },
        ]));

        TargetWindowDiscoveryResult result = locator.Locate();

        Assert.Equal(TargetWindowDiscoveryStatus.SelectionRequired, result.Status);
        Assert.Equal("TARGET_SELECTION_REQUIRED", result.DiagnosticCode);
        Assert.Equal(2, result.Candidates.Count);
    }

    public static TheoryData<WindowCandidate> InvalidCandidates => new()
    {
        ValidCandidate() with { IsVisible = false },
        ValidCandidate() with { Title = "其他窗口" },
        ValidCandidate() with { ClassName = "OtherClass" },
        ValidCandidate() with { ClientWidth = 320 },
        ValidCandidate() with { ClientHeight = 200 },
        ValidCandidate() with { ProcessPath = string.Empty },
    };

    [Theory]
    [MemberData(nameof(InvalidCandidates))]
    public void InvalidWindowsAreRejected(WindowCandidate candidate)
    {
        var locator = new WindowsTargetWindowLocator(new FakeWindowSystem([candidate]));

        TargetWindowDiscoveryResult result = locator.Locate();

        Assert.Equal(TargetWindowDiscoveryStatus.NotFound, result.Status);
    }

    [Fact]
    public void MinimizedWindowRemainsDiscoverableForSafeDiagnostics()
    {
        var locator = new WindowsTargetWindowLocator(new FakeWindowSystem([
            ValidCandidate() with { IsMinimized = true, ClientWidth = 0, ClientHeight = 0 },
        ]));

        TargetWindowDiscoveryResult result = locator.Locate();

        Assert.Equal(TargetWindowDiscoveryStatus.Found, result.Status);
        Assert.True(Assert.Single(result.Candidates).IsMinimized);
    }

    [Fact]
    public void LiveWindowsTargetCanBeRequiredForMachineEvidence()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MAPLE_REQUIRE_LIVE_TARGET"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var windowSystem = new Win32WindowSystem();
        IReadOnlyList<WindowCandidate> rawWindows = windowSystem.EnumerateTopLevelWindows();
        foreach (WindowCandidate raw in rawWindows.Where(candidate =>
                     candidate.Title.Contains("冒险岛", StringComparison.Ordinal)
                     || candidate.ClassName.Contains("Unity", StringComparison.OrdinalIgnoreCase)))
        {
            output.WriteLine($"RAW hwnd=0x{raw.Hwnd.ToInt64():X};pid={raw.Pid};title={raw.Title};class={raw.ClassName};visible={raw.IsVisible};minimized={raw.IsMinimized};client={raw.ClientWidth}x{raw.ClientHeight};dpi={raw.Dpi};path={raw.ProcessPath};started={raw.ProcessStartedAtUtc:O}");
        }

        TargetWindowDiscoveryResult result = new WindowsTargetWindowLocator(windowSystem).Locate();
        foreach (WindowIdentity candidate in result.Candidates)
        {
            output.WriteLine($"{candidate.Hwnd};pid={candidate.Pid};client={candidate.ClientWidth}x{candidate.ClientHeight};dpi={candidate.Dpi};foreground={candidate.IsForeground};version={candidate.ProcessVersion};pathHash={candidate.ProcessPathSha256}");
        }

        Assert.Equal(TargetWindowDiscoveryStatus.Found, result.Status);
        WindowIdentity target = Assert.Single(result.Candidates);
        if (!target.IsMinimized)
        {
            Assert.True(target.ClientWidth >= 640);
            Assert.True(target.ClientHeight >= 360);
        }
        Assert.Equal(64, target.ProcessPathSha256.Length);
    }

    private static WindowCandidate ValidCandidate() => new(
        Hwnd: (nint)0x12345,
        Pid: 4768,
        Title: "冒险岛怀旧服",
        ClassName: "UnityWndClass",
        IsVisible: true,
        IsMinimized: false,
        IsForeground: true,
        ClientLeft: 10,
        ClientTop: 20,
        ClientWidth: 2048,
        ClientHeight: 1200,
        Dpi: 144,
        ProcessStartedAtUtc: new DateTimeOffset(2026, 8, 14, 11, 32, 43, TimeSpan.Zero),
        ProcessPath: @"D:\Games\Maplestory_Classic.exe",
        ProcessVersion: "1.2.3.4");

    private sealed class FakeWindowSystem(IReadOnlyList<WindowCandidate> candidates) : IWindowSystem
    {
        public IReadOnlyList<WindowCandidate> EnumerateTopLevelWindows() => candidates;
    }
}
