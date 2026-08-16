using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Maple.Capture;
using Maple.Contracts;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Maple.Host;

public sealed class WindowsWgcFrameSource : IWindowFrameSource, IDisposable
{
    private readonly object sync = new();
    private readonly object readbackSync = new();
    private readonly ID3D11Device nativeDevice;
    private readonly ID3D11DeviceContext nativeContext;
    private readonly IDirect3DDevice direct3DDevice;
    private ID3D11Texture2D? stagingTexture;
    private int stagingWidth;
    private int stagingHeight;
    private int stagingFormat;
    private Direct3D11CaptureFramePool? framePool;
    private GraphicsCaptureSession? captureSession;
    private GraphicsCaptureItem? captureItem;
    private CapturedFrame? latestFrame;
    private TaskCompletionSource<bool>? frameAvailable;
    private nint activeHwnd;
    private long requestedFrameId;
    private long requestedAtMonoMs;
    private int requestedDpi = 96;
    private int requestedClientWidth;
    private int requestedClientHeight;
    private int cropX;
    private int cropY;
    private int poolWidth;
    private int poolHeight;
    private bool disposed;

    public string Status { get; private set; } = "WGC_CREATED";

    public WindowsWgcFrameSource()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) || !GraphicsCaptureSession.IsSupported())
            throw new PlatformNotSupportedException("Windows Graphics Capture is not supported");
        (nativeDevice, nativeContext) = CreateNativeDevice();
        direct3DDevice = CreateDirect3DDevice(nativeDevice);
    }

    public async ValueTask<CapturedFrame?> TryCaptureAsync(
        CaptureTarget target,
        long frameId,
        long nowMonoMs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        Task waitForFrame;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            requestedFrameId = frameId;
            requestedAtMonoMs = nowMonoMs;
            requestedDpi = target.Dpi;
            EnsureSession(ParseHwnd(target.Hwnd));
            UpdateClientCrop(target);
            latestFrame?.Dispose();
            latestFrame = null;
            frameAvailable ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            waitForFrame = frameAvailable.Task;
        }

        try { await waitForFrame.WaitAsync(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }

        lock (sync) return disposed ? null : TakeCompatibleLatest(target);
    }

    private void EnsureSession(nint hwnd)
    {
        if (captureSession is not null && hwnd == activeHwnd) return;
        StopSession();
        captureItem = WgcInterop.CreateItemForWindow(hwnd);
        if (captureItem.Size.Width <= 0 || captureItem.Size.Height <= 0)
            throw new InvalidOperationException("WGC target has invalid bounds");
        framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            direct3DDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            captureItem.Size);
        framePool.FrameArrived += OnFrameArrived;
        captureSession = framePool.CreateCaptureSession(captureItem);
        captureSession.IsCursorCaptureEnabled = false;
        activeHwnd = hwnd;
        poolWidth = captureItem.Size.Width;
        poolHeight = captureItem.Size.Height;
        captureSession.StartCapture();
        Status = $"WGC_SESSION_STARTED:{poolWidth}x{poolHeight}";
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            CapturedFrame captured;
            int contentWidth;
            int contentHeight;
            using (Direct3D11CaptureFrame frame = sender.TryGetNextFrame())
            {
                contentWidth = frame.ContentSize.Width;
                contentHeight = frame.ContentSize.Height;
                if (contentWidth <= 0 || contentHeight <= 0) return;
                lock (readbackSync) captured = ReadFrame(frame.Surface, contentWidth, contentHeight);
            }
            if (contentWidth != poolWidth || contentHeight != poolHeight)
            {
                sender.Recreate(
                    direct3DDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    new Windows.Graphics.SizeInt32 { Width = contentWidth, Height = contentHeight });
                poolWidth = contentWidth;
                poolHeight = contentHeight;
            }
            if (!Monitor.TryEnter(sync))
            {
                captured.Dispose();
                return;
            }
            try
            {
                if (disposed) { captured.Dispose(); return; }
                CapturedFrame? replaced = latestFrame;
                latestFrame = captured;
                Status = $"WGC_FRAME_READY:{captured.Width}x{captured.Height}";
                replaced?.Dispose();
                TaskCompletionSource<bool>? available = frameAvailable;
                frameAvailable = null;
                available?.TrySetResult(true);
            }
            finally { Monitor.Exit(sync); }
        }
        catch (Exception exception)
        {
            if (!Monitor.TryEnter(sync)) return;
            try
            {
                Status = "WGC_FRAME_ERROR:" + exception.GetType().Name;
                TaskCompletionSource<bool>? available = frameAvailable;
                frameAvailable = null;
                available?.TrySetResult(false);
            }
            finally { Monitor.Exit(sync); }
        }
    }

    private unsafe CapturedFrame ReadFrame(IDirect3DSurface surface, int width, int height)
    {
        long started = Stopwatch.GetTimestamp();
        int outputWidth = Volatile.Read(ref requestedClientWidth);
        int outputHeight = Volatile.Read(ref requestedClientHeight);
        int sourceX = Volatile.Read(ref cropX);
        int sourceY = Volatile.Read(ref cropY);
        if (outputWidth <= 0 || outputHeight <= 0 || sourceX < 0 || sourceY < 0
            || sourceX + outputWidth > width || sourceY + outputHeight > height)
            throw new InvalidOperationException("WGC client crop is outside the captured surface");
        using ID3D11Texture2D source = WgcInterop.OpenTexture(surface);
        Texture2DDescription sourceDescription = source.Description;
        ID3D11Texture2D staging = GetOrCreateStagingTexture(width, height, sourceDescription.Format);
        nativeContext.CopyResource(staging, source);
        MappedSubresource mapped = nativeContext.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        int stride = checked(outputWidth * 4);
        int length = checked(stride * outputHeight);
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(length);
        try
        {
            using MemoryHandle destination = owner.Memory.Pin();
            for (int row = 0; row < outputHeight; row++)
            {
                byte* sourceRow = (byte*)mapped.DataPointer + ((row + sourceY) * mapped.RowPitch) + (sourceX * 4);
                byte* destinationRow = (byte*)destination.Pointer + (row * stride);
                Buffer.MemoryCopy(sourceRow, destinationRow, stride, stride);
            }
        }
        catch
        {
            owner.Dispose();
            throw;
        }
        finally { nativeContext.Unmap(staging, 0); }

        var metadata = new CaptureFrameMetadata
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            FrameId = Volatile.Read(ref requestedFrameId),
            CapturedAtMonoMs = Volatile.Read(ref requestedAtMonoMs),
            ClientWidth = outputWidth,
            ClientHeight = outputHeight,
            Dpi = Volatile.Read(ref requestedDpi),
            CaptureBackend = CaptureBackend.Wgc,
            CaptureDurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            DroppedReason = DroppedFrameReason.None,
        };
        return new CapturedFrame(metadata, outputWidth, outputHeight, stride, CapturedPixelFormat.Bgra32, owner, length);
    }

    private ID3D11Texture2D GetOrCreateStagingTexture(int width, int height, Format format)
    {
        int formatValue = (int)format;
        if (!WgcReadbackResourcePolicy.ShouldRecreate(
            stagingTexture is not null,
            stagingWidth,
            stagingHeight,
            stagingFormat,
            width,
            height,
            formatValue))
        {
            return stagingTexture!;
        }

        stagingTexture?.Dispose();
        stagingTexture = nativeDevice.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        stagingWidth = width;
        stagingHeight = height;
        stagingFormat = formatValue;
        return stagingTexture;
    }

    private void UpdateClientCrop(CaptureTarget target)
    {
        WgcInterop.NativeRect bounds = WgcInterop.GetCaptureBounds(activeHwnd, poolWidth, poolHeight);
        requestedClientWidth = target.ClientWidth;
        requestedClientHeight = target.ClientHeight;
        cropX = target.ClientLeft - bounds.Left;
        cropY = target.ClientTop - bounds.Top;
    }

    private CapturedFrame? TakeLatest()
    {
        CapturedFrame? frame = latestFrame;
        latestFrame = null;
        return frame;
    }

    private CapturedFrame? TakeCompatibleLatest(CaptureTarget target)
    {
        CapturedFrame? frame = TakeLatest();
        if (frame is null) return null;
        if (frame.Width == target.ClientWidth && frame.Height == target.ClientHeight) return frame;
        Status = $"WGC_SIZE_MISMATCH:{frame.Width}x{frame.Height}!={target.ClientWidth}x{target.ClientHeight}";
        frame.Dispose();
        return null;
    }

    private void StopSession()
    {
        if (framePool is not null) framePool.FrameArrived -= OnFrameArrived;
        captureSession?.Dispose();
        framePool?.Dispose();
        captureSession = null;
        framePool = null;
        captureItem = null;
        activeHwnd = 0;
        poolWidth = 0;
        poolHeight = 0;
        requestedClientWidth = 0;
        requestedClientHeight = 0;
        cropX = 0;
        cropY = 0;
        latestFrame?.Dispose();
        latestFrame = null;
        frameAvailable?.TrySetResult(false);
        frameAvailable = null;
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            StopSession();
            lock (readbackSync)
            {
                stagingTexture?.Dispose();
                stagingTexture = null;
                direct3DDevice.Dispose();
                nativeContext.Dispose();
                nativeDevice.Dispose();
            }
        }
    }

    private static nint ParseHwnd(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Target HWND is empty", nameof(value));
        string numeric = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return nint.Parse(numeric, System.Globalization.NumberStyles.HexNumber);
    }

    private static (ID3D11Device Device, ID3D11DeviceContext Context) CreateNativeDevice()
    {
        ID3D11Device device;
        try
        {
            device = D3D11.D3D11CreateDevice(
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0]);
        }
        catch
        {
            device = D3D11.D3D11CreateDevice(
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0]);
        }
        return (device, device.ImmediateContext);
    }

    private static IDirect3DDevice CreateDirect3DDevice(ID3D11Device device)
    {
        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        int result = WgcInterop.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out nint inspectable);
        Marshal.ThrowExceptionForHR(result);
        try { return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
        finally { Marshal.Release(inspectable); }
    }
}

internal static class WgcInterop
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Texture2DGuid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface(in Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    internal static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out NativeRect value, int valueSize);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect value);

    internal static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
    {
        IGraphicsCaptureItemInterop interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        nint itemPointer = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
        try { return GraphicsCaptureItem.FromAbi(itemPointer); }
        finally { Marshal.Release(itemPointer); }
    }

    internal static ID3D11Texture2D OpenTexture(IDirect3DSurface surface)
    {
        IDirect3DDxgiInterfaceAccess access = surface.As<IDirect3DDxgiInterfaceAccess>();
        nint texturePointer = access.GetInterface(Texture2DGuid);
        return new ID3D11Texture2D(texturePointer);
    }

    internal static NativeRect GetCaptureBounds(nint hwnd, int expectedWidth, int expectedHeight)
    {
        if (DwmGetWindowAttribute(hwnd, 9, out NativeRect extended, Marshal.SizeOf<NativeRect>()) >= 0
            && extended.Width == expectedWidth && extended.Height == expectedHeight)
            return extended;
        if (GetWindowRect(hwnd, out NativeRect window)
            && window.Width == expectedWidth && window.Height == expectedHeight)
            return window;
        throw new InvalidOperationException("Unable to read WGC target bounds");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }
}
