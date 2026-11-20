using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockStudio.Imaging;

namespace ScreenLove.Services
{
    /// <summary>
    /// Apertura e salvataggio delle immagini di ScreenLove: il lavoro lo fa il modulo condiviso
    /// DockStudio.Imaging; qui si converte da e verso le immagini WPF (BGRA32 a 96 DPI).
    /// </summary>
    public static class ImageIO
    {
        /// <summary>Salva in base all'estensione (png, jpg/jpeg, bmp). Predefinito: PNG.</summary>
        public static void Save(BitmapSource image, string path) => SkiaBridge.Save(image, path);

        /// <summary>
        /// Carica un'immagine senza bloccare il file e la normalizza in BGRA32 a 96 DPI,
        /// così 1 pixel = 1 unità WPF in tutto l'editor.
        /// </summary>
        public static BitmapSource LoadNormalized(string path)
        {
            using var image = ImageFiles.Load(path);
            return SkiaBridge.ToWpf(image);
        }

        public static BitmapSource Normalize(BitmapSource source)
        {
            var converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            converted.CopyPixels(pixels, stride, 0);
            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
    }
}
