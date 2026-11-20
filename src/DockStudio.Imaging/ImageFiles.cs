using System;
using System.IO;
using SkiaSharp;

namespace DockStudio.Imaging
{
    /// <summary>Apertura e salvataggio delle immagini (PNG, JPEG, BMP; in lettura anche GIF e WebP).</summary>
    public static class ImageFiles
    {
        public const int JpegQuality = 92;

        /// <summary>
        /// Apre un'immagine senza tenere bloccato il file e la porta nel formato del modulo
        /// (<see cref="Pixels"/>).
        /// </summary>
        public static SKBitmap Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using var decoded = SKBitmap.Decode(bytes)
                ?? throw new InvalidDataException($"Immagine non riconosciuta: {Path.GetFileName(path)}");
            return Pixels.Normalize(decoded);
        }

        /// <summary>Salva in base all'estensione (png, jpg/jpeg, bmp). Predefinito: PNG.</summary>
        public static void Save(SKBitmap image, string path)
        {
            using var pixmap = image.PeekPixels();
            Save(pixmap, path);
        }

        /// <summary>
        /// Salva pixel già in memoria, senza copiarli per il PNG (utile per le catture a scorrimento molto
        /// lunghe). Il file si scrive accanto e si sostituisce solo a salvataggio finito.
        /// </summary>
        public static void Save(SKPixmap pixels, string path)
        {
            string tmp = path + ".tmp";
            try
            {
                using (var output = File.Create(tmp))
                {
                    switch (Path.GetExtension(path).ToLowerInvariant())
                    {
                        case ".jpg":
                        case ".jpeg":
                            using (var white = OnWhite(pixels)) // JPEG non ha trasparenza: niente sfondo nero
                            using (var whitePixels = white.PeekPixels())
                                Encode(whitePixels, output, SKEncodedImageFormat.Jpeg, JpegQuality);
                            break;
                        case ".bmp":
                            using (var white = OnWhite(pixels))
                                WriteBmp(white, output);
                            break;
                        default:
                            Encode(pixels, output, SKEncodedImageFormat.Png, 100);
                            break;
                    }
                }
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { /* niente */ }
                throw;
            }
        }

        private static void Encode(SKPixmap pixels, Stream output, SKEncodedImageFormat format, int quality)
        {
            using var data = pixels.Encode(format, quality)
                ?? throw new IOException("Non è stato possibile codificare l'immagine.");
            data.SaveTo(output);
        }

        private static SKBitmap OnWhite(SKPixmap pixels)
        {
            using var image = SKImage.FromPixels(pixels);
            return Pixels.Render(pixels.Width, pixels.Height, canvas =>
            {
                canvas.Clear(SKColors.White);
                canvas.DrawImage(image, 0, 0);
            });
        }

        /// <summary>BMP a 24 bit dal basso verso l'alto (Skia sa leggere i BMP ma non scriverli).</summary>
        private static void WriteBmp(SKBitmap opaque, Stream output)
        {
            int w = opaque.Width, h = opaque.Height;
            int rowSize = (w * 3 + 3) & ~3;
            long imageSize = (long)rowSize * h;
            if (imageSize + 54 > uint.MaxValue) throw new IOException("Immagine troppo grande per il formato BMP.");

            using var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
            writer.Write((byte)'B'); writer.Write((byte)'M');
            writer.Write((uint)(54 + imageSize));
            writer.Write(0u);
            writer.Write(54u);
            writer.Write(40u);        // BITMAPINFOHEADER
            writer.Write(w);
            writer.Write(h);          // positivo: righe dal basso
            writer.Write((ushort)1);
            writer.Write((ushort)24);
            writer.Write(0u);         // BI_RGB
            writer.Write((uint)imageSize);
            writer.Write(3780);       // 96 DPI
            writer.Write(3780);
            writer.Write(0u);
            writer.Write(0u);

            using var pixmap = opaque.PeekPixels();
            var src = pixmap.GetPixelSpan();
            int stride = pixmap.RowBytes;
            var row = new byte[rowSize];
            for (int y = h - 1; y >= 0; y--)
            {
                int s = y * stride;
                for (int x = 0; x < w; x++)
                {
                    row[x * 3] = src[s + x * 4];         // B
                    row[x * 3 + 1] = src[s + x * 4 + 1]; // G
                    row[x * 3 + 2] = src[s + x * 4 + 2]; // R
                }
                writer.Write(row);
            }
        }
    }
}
