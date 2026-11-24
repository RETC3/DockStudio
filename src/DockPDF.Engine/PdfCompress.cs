using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace DockPDF.Engine
{
    /// <summary>Quanto comprimere.</summary>
    public enum CompressLevel
    {
        /// <summary>Qualità alta: immagini fino a 200 dpi, JPEG 85.</summary>
        High,

        /// <summary>Equilibrio: immagini fino a 150 dpi, JPEG 72. Va bene per leggere e stampare.</summary>
        Medium,

        /// <summary>File più piccolo possibile: immagini fino a 96 dpi, JPEG 55. Per lo schermo e l'email.</summary>
        Low,
    }

    /// <summary>Esito della compressione.</summary>
    public sealed record CompressResult(long Before, long After, int ImagesChanged, int Images, bool Saved)
    {
        public double Saving => Before <= 0 ? 0 : 1 - (double)After / Before;
    }

    /// <summary>
    /// Riduce il peso di un PDF: le immagini più grandi del necessario si rimpiccioliscono e si ricomprimono in JPEG,
    /// i contenuti si comprimono al massimo e gli oggetti non usati si tolgono. Testo e grafica vettoriale non cambiano.
    /// </summary>
    public static class PdfCompress
    {
        public static (int Dpi, int Quality) Settings(CompressLevel level) => level switch
        {
            CompressLevel.High => (200, 85),
            CompressLevel.Low => (96, 55),
            _ => (150, 72),
        };

        /// <summary>
        /// Comprime <paramref name="path"/> in <paramref name="output"/>. Se il risultato non è più piccolo
        /// dell'originale, non scrive niente (<see cref="CompressResult.Saved"/> false).
        /// </summary>
        public static CompressResult Compress(string path, string? password, string output, CompressLevel level, IProgress<double>? progress = null)
        {
            long before = new FileInfo(path).Length;
            var (dpi, quality) = Settings(level);

            byte[] bytes;
            double maxPageSide;
            using (var file = PdfFile.Open(path, password))
            {
                if (file.IsEncrypted)
                {
                    using var clear = new MemoryStream();
                    file.SaveWithoutSecurity(clear);
                    bytes = clear.ToArray();
                }
                else
                {
                    bytes = File.ReadAllBytes(path);
                }
                maxPageSide = Enumerable.Range(0, file.PageCount).Select(i => { var (w, h) = file.PageSize(i); return Math.Max(w, h); }).DefaultIfEmpty(842).Max();
            }

            using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
            // Un'immagine non serve più grande della pagina più grande alla risoluzione scelta.
            int maxSide = (int)Math.Ceiling(maxPageSide / 72.0 * dpi);
            var images = document.Internals.GetAllObjects().OfType<PdfDictionary>()
                .Where(d => d.Stream != null && d.Elements.GetName("/Subtype") == "/Image").ToList();
            int changed = 0;
            for (int i = 0; i < images.Count; i++)
            {
                try
                {
                    if (Recompress(images[i], maxSide, quality)) changed++;
                }
                catch (Exception)
                {
                    // immagine in un formato particolare: resta com'è
                }
                progress?.Report((i + 1) / (double)Math.Max(1, images.Count));
            }

            document.Options.CompressContentStreams = true;
            document.Options.NoCompression = false;
            document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            using var result = new MemoryStream();
            document.Save(result, false);

            long after = result.Length;
            if (after >= before)
                return new CompressResult(before, before, changed, images.Count, false);
            var tmp = output + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, result.ToArray());
                File.Move(tmp, output, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            return new CompressResult(before, after, changed, images.Count, true);
        }

        /// <summary>
        /// Ricomprime un'immagine RGB o in scala di grigi a 8 bit (JPEG o non compressa). Le altre (maschere, colori
        /// indicizzati, CMYK, bianco e nero delle scansioni JBIG2/CCITT) restano com'erano.
        /// </summary>
        private static bool Recompress(PdfDictionary image, int maxSide, int quality)
        {
            var e = image.Elements;
            if (e.GetBoolean("/ImageMask") || e.ContainsKey("/Decode") || e.ContainsKey("/Mask")) return false;
            int width = e.GetInteger("/Width"), height = e.GetInteger("/Height");
            if (width <= 0 || height <= 0 || e.GetInteger("/BitsPerComponent") != 8) return false;

            int components = Components(e["/ColorSpace"]);
            if (components is not (1 or 3)) return false;

            var filter = e["/Filter"] is PdfArray arr ? (arr.Elements.Count == 1 ? arr.Elements.GetName(0) : "") : e.GetName("/Filter");
            var original = image.Stream!.Value;
            SKBitmap? decoded;
            if (filter == "/DCTDecode")
            {
                decoded = SKBitmap.Decode(original);
            }
            else if (filter is "/FlateDecode" or "")
            {
                var raw = image.Stream.UnfilteredValue;
                if (raw.Length < width * height * components) return false;
                decoded = FromRaw(raw, width, height, components);
            }
            else
            {
                return false;
            }
            if (decoded == null) return false;

            using (decoded)
            {
                double scale = Math.Min(1, (double)maxSide / Math.Max(width, height));
                int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
                using var resized = scale < 0.999
                    ? decoded.Resize(new SKImageInfo(w, h, decoded.ColorType, decoded.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell))
                    : null;
                var source = resized ?? decoded;
                using var data = source.Encode(SKEncodedImageFormat.Jpeg, quality);
                if (data == null) return false;
                var jpeg = data.ToArray();
                // Solo se conviene davvero (almeno il 10% in meno).
                if (jpeg.Length > original.Length * 0.9) return false;

                image.Stream.Value = jpeg;
                e.SetName("/Filter", "/DCTDecode");
                e.Remove("/DecodeParms");
                e.SetInteger("/Width", source.Width);
                e.SetInteger("/Height", source.Height);
                e.SetInteger("/BitsPerComponent", 8);
                if (source.ColorType == SKColorType.Gray8) e.SetName("/ColorSpace", "/DeviceGray");
                else if (components != 3 || e["/ColorSpace"] is not PdfName) e.SetName("/ColorSpace", "/DeviceRGB");
                e.SetInteger("/Length", jpeg.Length);
                return true;
            }
        }

        private static int Components(PdfItem? colorSpace)
        {
            var cs = colorSpace is PdfReference r ? r.Value : colorSpace;
            switch (cs)
            {
                case PdfName n:
                    return n.Value switch { "/DeviceRGB" or "/CalRGB" => 3, "/DeviceGray" or "/CalGray" => 1, _ => 0 };
                case PdfArray a when a.Elements.Count >= 2 && a.Elements.GetName(0) == "/ICCBased":
                    var profile = a.Elements[1] is PdfReference pr ? pr.Value as PdfDictionary : a.Elements[1] as PdfDictionary;
                    return profile?.Elements.GetInteger("/N") ?? 0;
                case PdfArray a when a.Elements.Count >= 1 && a.Elements.GetName(0) is "/CalRGB":
                    return 3;
                case PdfArray a when a.Elements.Count >= 1 && a.Elements.GetName(0) is "/CalGray":
                    return 1;
                default:
                    return 0;
            }
        }

        private static SKBitmap FromRaw(byte[] raw, int width, int height, int components)
        {
            if (components == 1)
            {
                var gray = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
                var span = gray.GetPixelSpan();
                for (int y = 0; y < height; y++)
                    raw.AsSpan(y * width, width).CopyTo(span.Slice(y * gray.RowBytes, width));
                gray.NotifyPixelsChanged();
                return gray;
            }
            var rgb = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            var pixels = rgb.GetPixelSpan();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int s = (y * width + x) * 3, d = y * rgb.RowBytes + x * 4;
                    pixels[d] = raw[s];
                    pixels[d + 1] = raw[s + 1];
                    pixels[d + 2] = raw[s + 2];
                    pixels[d + 3] = 255;
                }
            rgb.NotifyPixelsChanged();
            return rgb;
        }
    }
}
