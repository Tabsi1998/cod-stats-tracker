using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace CodStats.Desktop.Capture;

/// <summary>
/// Single frames of one window through Windows Graphics Capture, the API OBS and the Xbox Game Bar use. No hooks
/// into the game: Windows composes the window and hands over a copy. The session stays open while the game runs;
/// a frame is only copied to memory when <see cref="GrabAsync"/> asks for one.
/// </summary>
public sealed partial class WindowCapture : IDisposable
{
    private readonly IDirect3DDevice device;
    private readonly GraphicsCaptureItem item;
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;

    /// <summary>
    /// One buffer for every frame of this capture. A new 33 MB array per 4K frame piled up to gigabytes on a real
    /// PC: .NET waits long before collecting large arrays when the PC has plenty of RAM (issue #26).
    /// </summary>
    private byte[] buffer = [];

    /// <summary>
    /// The size the frame pool was made for. Not the item's size: that already reports the new size when the window
    /// changes, while the pool keeps cutting frames to the old one until it is recreated.
    /// </summary>
    private Windows.Graphics.SizeInt32 poolSize;

    private WindowCapture(IntPtr window)
    {
        Window = window;
        device = CreateDevice();
        item = CreateItem(window);
        poolSize = item.Size;
        pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, poolSize);
        session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        // Windows 11: no yellow border around the game. Windows 10 does not know the property and shows the border.
        if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
        {
            session.IsBorderRequired = false;
        }
        session.StartCapture();
    }

    /// <summary>The window this capture follows; the game may replace it (splash window, then the real one).</summary>
    public IntPtr Window { get; }

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    public static WindowCapture? Start(IntPtr window) =>
        window == IntPtr.Zero || !IsSupported ? null : new WindowCapture(window);

    /// <summary>The newest frame of the window as pixels in memory, or null when none arrived within the timeout.</summary>
    public async Task<Frame?> GrabAsync(TimeSpan timeout, CancellationToken cancel)
    {
        var deadline = DateTime.UtcNow + timeout;
        Direct3D11CaptureFrame? captured = null;
        while (DateTime.UtcNow < deadline)
        {
            // Drain to the newest frame; older ones are discarded unread.
            while (pool.TryGetNextFrame() is { } next)
            {
                captured?.Dispose();
                captured = next;
            }
            if (captured is not null && (captured.ContentSize.Width != poolSize.Width || captured.ContentSize.Height != poolSize.Height))
            {
                // The game changed its window size (it often starts small and then goes to full resolution): the
                // frame is cut to the old size. Recreate the pool and wait for a frame of the new size.
                poolSize = captured.ContentSize;
                captured.Dispose();
                captured = null;
                pool.Recreate(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, poolSize);
                Diagnostics.Log($"capture resized to {poolSize.Width}x{poolSize.Height}");
            }
            if (captured is not null)
            {
                break;
            }
            await Task.Delay(30, cancel);
        }
        if (captured is null)
        {
            return null;
        }

        using (captured)
        {
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(captured.Surface, BitmapAlphaMode.Premultiplied).AsTask(cancel);
            var size = bitmap.PixelWidth * bitmap.PixelHeight * 4;
            if (buffer.Length != size)
            {
                buffer = new byte[size];
            }
            var frame = new Frame(bitmap.PixelWidth, bitmap.PixelHeight, buffer);
            bitmap.CopyToBuffer(buffer.AsBuffer());
            return frame;
        }
    }

    public void Dispose()
    {
        Array.Clear(buffer);
        session.Dispose();
        pool.Dispose();
        device.Dispose();
    }

    private static unsafe GraphicsCaptureItem CreateItem(IntPtr window)
    {
        // IGraphicsCaptureItemInterop is a classic COM interface on the activation factory; call it through its vtable.
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interopIid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr, in interopIid, out var interop));
        try
        {
            var createForWindow = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)interop)[3];
            var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IGraphicsCaptureItem
            IntPtr pointer;
            Marshal.ThrowExceptionForHR(createForWindow(interop, window, &itemIid, &pointer));
            try
            {
                return GraphicsCaptureItem.FromAbi(pointer);
            }
            finally
            {
                Marshal.Release(pointer);
            }
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    private static IDirect3DDevice CreateDevice()
    {
        const uint BgraSupport = 0x20;
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1 /* hardware */, IntPtr.Zero, BgraSupport, IntPtr.Zero, 0, 7 /* SDK */,
            out var d3dDevice, IntPtr.Zero, IntPtr.Zero));
        try
        {
            var dxgiGuid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in dxgiGuid, out var dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));
                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            Marshal.Release(d3dDevice);
        }
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels,
        uint featureLevelCount, uint sdkVersion, out IntPtr device, IntPtr featureLevel, IntPtr immediateContext);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
