using System;
using SkiaSharp;

namespace DockStudio.Imaging
{
    /// <summary>
    /// Formato comune delle immagini nel modulo: BGRA a 8 bit per canale, alfa premoltiplicato (il formato
    /// in cui Skia disegna). Le app convertono da e verso il loro formato una volta sola, ai bordi.
    /// </summary>
    public static class Pixels
    {
        public static SKImageInfo Info(int width, int height) =>
            new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        public static SKBitmap Create(int width, int height) => new(Info(width, height));

        /// <summary>Nuova immagine trasparente su cui disegnare.</summary>
        public static SKBitmap Render(int width, int height, Action<SKCanvas> draw)
        {
            var bitmap = Create(width, height);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                draw(canvas);
                canvas.Flush();
            }
            return bitmap;
        }

        /// <summary>Converte un'immagine qualsiasi (per esempio appena decodificata) nel formato del modulo.</summary>
        public static SKBitmap Normalize(SKBitmap source)
        {
            if (source.ColorType == SKColorType.Bgra8888 && source.AlphaType == SKAlphaType.Premul)
                return source.Copy();
            var result = Create(source.Width, source.Height);
            using var pixmap = source.PeekPixels();
            if (!pixmap.ReadPixels(result.Info, result.GetPixels(), result.RowBytes))
            {
                result.Dispose();
                throw new InvalidOperationException("Formato dell'immagine non supportato.");
            }
            return result;
        }

        /// <summary>L'immagine appoggiata su sfondo bianco, senza trasparenza (per JPEG e BMP).</summary>
        public static SKBitmap OnWhite(SKBitmap image) =>
            Render(image.Width, image.Height, canvas =>
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(image, 0, 0);
            });

        /// <summary>Rettangolo che contiene i pixel completamente opachi (tutta l'immagine se non ce ne sono).</summary>
        public static SKRectI OpaqueBounds(SKBitmap image)
        {
            int w = image.Width, h = image.Height;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            using var pixmap = image.PeekPixels();
            var bytes = pixmap.GetPixelSpan();
            int stride = pixmap.RowBytes;
            int alpha = image.ColorType == SKColorType.Rgba8888 || image.ColorType == SKColorType.Bgra8888 ? 3 : -1;
            if (alpha < 0) return new SKRectI(0, 0, w, h);
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                    if (bytes[row + x * 4 + alpha] == 255)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            }
            return maxX < 0 ? new SKRectI(0, 0, w, h) : new SKRectI(minX, minY, maxX + 1, maxY + 1);
        }
    }

    /// <summary>Caratteri dell'interfaccia: Segoe UI su Windows, il carattere di sistema altrove.</summary>
    public static class Fonts
    {
        private static readonly string[] UiFamilies = { "Segoe UI", ".AppleSystemUIFont", "Helvetica Neue", "Arial" };

        public static SKTypeface Ui(SKFontStyleWeight weight = SKFontStyleWeight.Normal)
        {
            foreach (var family in UiFamilies)
            {
                var typeface = SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                if (typeface != null && string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                    return typeface;
                typeface?.Dispose();
            }
            return SKTypeface.FromFamilyName(null, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        }

        /// <summary>Carattere per scrivere testo antialias con posizionamento subpixel.</summary>
        public static SKFont Create(SKTypeface typeface, float size) =>
            new(typeface, size) { Subpixel = true, Edging = SKFontEdging.Antialias };
    }
}
