using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockStudio.Imaging;
using SkiaSharp;

namespace ScreenLove.Services
{
    /// <summary>
    /// Passaggio delle immagini tra WPF (BGRA a 96 DPI, alfa non premoltiplicato) e il modulo immagini
    /// DockStudio.Imaging (SkiaSharp, alfa premoltiplicato). Serve finché l'interfaccia è in WPF.
    /// </summary>
    public static class SkiaBridge
    {
        public static SKBitmap ToSkia(BitmapSource source)
        {
            var (pixels, w, h) = Bytes(source);
            var result = Pixels.Create(w, h);
            WithPixmap(pixels, w, h, pixmap =>
            {
                if (!pixmap.ReadPixels(result.Info, result.GetPixels(), result.RowBytes))
                    throw new InvalidOperationException("Conversione dell'immagine non riuscita.");
            });
            return result;
        }

        public static BitmapSource ToWpf(SKBitmap bitmap)
        {
            int w = bitmap.Width, h = bitmap.Height, stride = w * 4;
            var pixels = new byte[stride * h];
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using var source = bitmap.PeekPixels();
                if (!source.ReadPixels(info, handle.AddrOfPinnedObject(), stride))
                    throw new InvalidOperationException("Conversione dell'immagine non riuscita.");
            }
            finally
            {
                handle.Free();
            }
            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        /// <summary>Applica un'operazione del modulo immagini a un'immagine WPF.</summary>
        public static BitmapSource Apply(BitmapSource source, Func<SKBitmap, SKBitmap> operation)
        {
            using var input = ToSkia(source);
            using var output = operation(input);
            return ToWpf(output);
        }

        /// <summary>
        /// Salva un'immagine WPF con il modulo immagini. Per il PNG i pixel passano direttamente al
        /// codificatore, senza una seconda copia (le catture a scorrimento possono superare i 300 MB).
        /// </summary>
        public static void Save(BitmapSource source, string path)
        {
            var (pixels, w, h) = Bytes(source);
            WithPixmap(pixels, w, h, pixmap => ImageFiles.Save(pixmap, path));
        }

        public static SKColor ToSkia(Color c) => new(c.R, c.G, c.B, c.A);

        private static (byte[] Pixels, int Width, int Height) Bytes(BitmapSource source)
        {
            var bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            bgra.CopyPixels(pixels, stride, 0);
            return (pixels, w, h);
        }

        private static void WithPixmap(byte[] pixels, int w, int h, Action<SKPixmap> use)
        {
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using var pixmap = new SKPixmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul), handle.AddrOfPinnedObject(), w * 4);
                use(pixmap);
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
