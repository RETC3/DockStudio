using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ScreenLove.Native;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Security.Authorization.AppCapabilityAccess;
using WinRT;

namespace ScreenLove.Recording
{
    /// <summary>
    /// Cattura una zona dello schermo con Windows.Graphics.Capture. I fotogrammi restano nella scheda grafica:
    /// si ritaglia la zona con una copia sulla GPU e la superficie va direttamente al codificatore,
    /// senza passare dal processore. Funziona solo se la zona sta tutta in un monitor.
    /// </summary>
    internal sealed class GpuScreenCapture : IDisposable
    {
        /// <summary>Superfici in uso al massimo: oltre, il codificatore è in ritardo e il fotogramma si scarta.</summary>
        private const int MaxSlots = 8;

        private sealed record Slot(ID3D11Texture2D Texture, IDirect3DSurface Surface);

        private readonly object _gpuLock = new();
        private readonly ConcurrentBag<Slot> _free = new();
        private readonly ManualResetEventSlim _firstFrame = new(false);
        private readonly int _width, _height, _offsetX, _offsetY;

        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDirect3DDevice? _winrtDevice;
        private GraphicsCaptureItem? _item;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private ID3D11Texture2D? _latest;
        private Windows.Graphics.SizeInt32 _poolSize;
        private int _slots;
        private bool _disposed;

        private GpuScreenCapture(int width, int height, int offsetX, int offsetY)
        {
            _width = width;
            _height = height;
            _offsetX = offsetX;
            _offsetY = offsetY;
        }

        /// <summary>
        /// Prepara la cattura della zona (coordinate fisiche) con dimensioni <paramref name="width"/> × <paramref name="height"/>.
        /// Restituisce null, con il motivo, se non è possibile: si userà il metodo classico.
        /// </summary>
        public static GpuScreenCapture? TryStart(Int32Rect region, int width, int height, bool showCursor, out string? reason)
        {
            reason = null;
            if (Environment.GetEnvironmentVariable("SCREENLOVE_VIDEO_GDI") == "1")
            {
                reason = "metodo classico richiesto (SCREENLOVE_VIDEO_GDI)";
                return null;
            }
            try
            {
                if (!GraphicsCaptureSession.IsSupported())
                {
                    reason = "Windows.Graphics.Capture non supportato";
                    return null;
                }

                var rect = new NativeMethods.RECT { Left = region.X, Top = region.Y, Right = region.X + width, Bottom = region.Y + height };
                var monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONULL);
                var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
                {
                    reason = "monitor non trovato";
                    return null;
                }
                var m = info.rcMonitor;
                if (rect.Left < m.Left || rect.Top < m.Top || rect.Right > m.Right || rect.Bottom > m.Bottom)
                {
                    reason = "la zona occupa più monitor";
                    return null;
                }

                var capture = new GpuScreenCapture(width, height, rect.Left - m.Left, rect.Top - m.Top);
                try
                {
                    capture.Start(monitor, showCursor);
                    // Windows manda subito un primo fotogramma: se non arriva, meglio il metodo classico.
                    if (!capture._firstFrame.Wait(TimeSpan.FromSeconds(2)))
                    {
                        reason = "nessun fotogramma dalla scheda grafica";
                        capture.Dispose();
                        return null;
                    }
                    return capture;
                }
                catch
                {
                    capture.Dispose();
                    throw;
                }
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return null;
            }
        }

        private void Start(IntPtr monitor, bool showCursor)
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out _device).CheckError();
            _context = _device!.ImmediateContext;
            // Il codificatore di Windows usa le nostre superfici da un altro thread.
            using (var mt = _device.QueryInterfaceOrNull<ID3D11Multithread>())
                mt?.SetMultithreadProtected(true);

            using (var dxgi = _device.QueryInterface<IDXGIDevice>())
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var ptr));
                _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(ptr);
                Marshal.Release(ptr);
            }

            _item = CreateItemForMonitor(monitor);
            _latest = _device.CreateTexture2D(TextureDesc(_width, _height));

            _poolSize = _item.Size;
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
            _framePool.FrameArrived += OnFrameArrived;
            _session = _framePool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = showCursor;
            TryHideYellowBorder(_session);
            _session.StartCapture();
        }

        /// <summary>Da Windows 11 si può togliere il bordo giallo che Windows disegna attorno allo schermo catturato.</summary>
        private static void TryHideYellowBorder(GraphicsCaptureSession session)
        {
            try
            {
                if (!ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
                    return;
                var access = GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless).AsTask();
                if (access.Wait(TimeSpan.FromSeconds(2)) && access.Result == AppCapabilityAccessStatus.Allowed)
                    session.IsBorderRequired = false;
            }
            catch
            {
                // il bordo resta: non è un problema per la registrazione
            }
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null) return;
            System.Console.WriteLine($"DEBUG arrivo attende lock t{Environment.CurrentManagedThreadId}");
            lock (_gpuLock)
            {
                System.Console.WriteLine("DEBUG arrivo dentro");
                if (_disposed) return;
                using var source = TextureOf(frame.Surface);
                var desc = source.Description;
                int right = Math.Min(_offsetX + _width, (int)desc.Width);
                int bottom = Math.Min(_offsetY + _height, (int)desc.Height);
                if (right > _offsetX && bottom > _offsetY)
                {
                    var box = new Vortice.Mathematics.Box(_offsetX, _offsetY, 0, right, bottom, 1);
                    _context!.CopySubresourceRegion(_latest!, 0, 0, 0, 0, source, 0, box);
                    _firstFrame.Set();
                    System.Console.WriteLine("DEBUG arrivo copiato");
                }

                // Risoluzione del monitor cambiata durante la registrazione.
                if (frame.ContentSize.Width != _poolSize.Width || frame.ContentSize.Height != _poolSize.Height)
                {
                    _poolSize = frame.ContentSize;
                    sender.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
                }
            }
        }

        /// <summary>
        /// Copia l'ultimo fotogramma in una superficie libera e la restituisce, con l'azione da chiamare quando
        /// il codificatore l'ha usata. Null se il codificatore è in ritardo e non ci sono superfici libere.
        /// </summary>
        public IDirect3DSurface? TakeFrame(out Action release)
        {
            release = () => { };
            lock (_gpuLock)
            {
                if (_disposed) return null;
                if (!_free.TryTake(out var slot))
                {
                    if (_slots >= MaxSlots) return null;
                    slot = CreateSlot();
                    _slots++;
                }
                System.Console.WriteLine("DEBUG take copia");
                _context!.CopyResource(slot.Texture, _latest!);
                _context.Flush();
                System.Console.WriteLine("DEBUG take fatto");
                var taken = slot;
                release = () => _free.Add(taken);
                return slot.Surface;
            }
        }

        private Slot CreateSlot()
        {
            var texture = _device!.CreateTexture2D(TextureDesc(_width, _height));
            using var dxgiSurface = texture.QueryInterface<IDXGISurface>();
            Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out var ptr));
            var surface = MarshalInterface<IDirect3DSurface>.FromAbi(ptr);
            Marshal.Release(ptr);
            return new Slot(texture, surface);
        }

        private static Texture2DDescription TextureDesc(int width, int height) => new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };

        public void Dispose()
        {
            lock (_gpuLock)
            {
                if (_disposed) return;
                _disposed = true;
            }
            try { _session?.Dispose(); } catch { }
            try
            {
                if (_framePool != null)
                {
                    _framePool.FrameArrived -= OnFrameArrived;
                    _framePool.Dispose();
                }
            }
            catch { }
            while (_free.TryTake(out var slot))
            {
                (slot.Surface as IDisposable)?.Dispose();
                slot.Texture.Dispose();
            }
            _latest?.Dispose();
            (_winrtDevice as IDisposable)?.Dispose();
            _context?.Dispose();
            _device?.Dispose();
            _firstFrame.Dispose();
        }

        // ---------- interoperabilità WinRT / Direct3D ----------

        private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        private static readonly Guid Texture2DIid = typeof(ID3D11Texture2D).GUID;

        private static GraphicsCaptureItem CreateItemForMonitor(IntPtr monitor)
        {
            var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
            var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
            var iid = GraphicsCaptureItemIid;
            var ptr = interop.CreateForMonitor(monitor, ref iid);
            try
            {
                return MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr);
            }
            finally
            {
                Marshal.Release(ptr);
            }
        }

        private static ID3D11Texture2D TextureOf(IDirect3DSurface surface)
        {
            var access = surface.As<IDirect3DDxgiInterfaceAccess>();
            var iid = Texture2DIid;
            return new ID3D11Texture2D(access.GetInterface(ref iid));
        }

        [ComImport, System.Runtime.InteropServices.Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
            IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
        }

        [ComImport, System.Runtime.InteropServices.Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3DDxgiInterfaceAccess
        {
            IntPtr GetInterface([In] ref Guid iid);
        }

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);
    }
}
