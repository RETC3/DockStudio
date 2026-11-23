using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DockStudio.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace DockPDF.Engine
{
    /// <summary>Come dividere un PDF.</summary>
    public enum SplitMode
    {
        /// <summary>Un file per pagina.</summary>
        EachPage,

        /// <summary>Un file ogni N pagine.</summary>
        EveryN,

        /// <summary>Un file per ogni intervallo scritto ("1-3, 4-9, 10-").</summary>
        Ranges,

        /// <summary>Un solo file con le pagine degli intervalli (estrai).</summary>
        Extract,
    }

    /// <summary>Formato delle pagine create da immagini.</summary>
    public enum PageLayout
    {
        /// <summary>Pagina grande quanto l'immagine (a 150 punti per pollice, per non avere pagine enormi).</summary>
        FitImage,
        A4,
        Letter,
    }

    /// <summary>Gli strumenti di DockPDF che lavorano su file interi.</summary>
    public static class PdfTools
    {
        // ---- Dividi ed estrai -----------------------------------------------------------------------------

        /// <summary>
        /// Divide <paramref name="path"/> e salva i pezzi in <paramref name="folder"/> come "nome_p1-3.pdf".
        /// Restituisce i file creati, nell'ordine.
        /// </summary>
        public static IReadOnlyList<string> Split(string path, string? password, SplitMode mode, int everyN,
            IReadOnlyList<PageRange>? ranges, string folder)
        {
            using var sources = new PdfSources(_ => password);
            int count = sources.Get(path).PageCount;
            var groups = mode switch
            {
                SplitMode.EachPage => Enumerable.Range(0, count).Select(i => new[] { new PageRange(i, i) }).ToList(),
                SplitMode.EveryN => Enumerable.Range(0, (count + Math.Max(1, everyN) - 1) / Math.Max(1, everyN))
                    .Select(g => new[] { new PageRange(g * everyN, Math.Min(count, (g + 1) * everyN) - 1) }).ToList(),
                SplitMode.Ranges => (ranges ?? throw new ArgumentNullException(nameof(ranges))).Select(r => new[] { r }).ToList(),
                _ => new List<PageRange[]> { (ranges ?? throw new ArgumentNullException(nameof(ranges))).ToArray() },
            };

            Directory.CreateDirectory(folder);
            var name = Path.GetFileNameWithoutExtension(path);
            var created = new List<string>();
            foreach (var group in groups)
            {
                var label = string.Join(",", group.Select(r => r.ToString()));
                var output = UniquePath(Path.Combine(folder, $"{name}_p{label}.pdf"));
                var pages = PageRanges.Pages(group).Select(i => new PageRef(path, i)).ToList();
                sources.Compose(pages, output);
                created.Add(output);
            }
            return created;
        }

        // ---- Password -------------------------------------------------------------------------------------

        /// <summary>
        /// Salva una copia protetta: serve <paramref name="userPassword"/> per aprirla. Con i limiti attivi si
        /// può impedire la stampa e la copia del testo (la password del proprietario li toglie).
        /// </summary>
        public static void Protect(string path, string? currentPassword, string output, string userPassword,
            string? ownerPassword = null, bool allowPrint = true, bool allowCopy = true)
        {
            if (string.IsNullOrEmpty(userPassword)) throw new ArgumentException("Password vuota", nameof(userPassword));
            using var document = OpenForChange(path, currentPassword);
            var security = document.SecuritySettings;
            security.UserPassword = userPassword;
            // Senza una password del proprietario se ne crea una casuale: i limiti non si tolgono con quella di apertura.
            security.OwnerPassword = string.IsNullOrEmpty(ownerPassword) ? RandomPassword() : ownerPassword;
            security.PermitPrint = allowPrint;
            security.PermitFullQualityPrint = allowPrint;
            security.PermitExtractContent = allowCopy;
            security.PermitModifyDocument = false;
            security.PermitAnnotations = allowCopy;
            security.PermitFormsFill = true;
            security.PermitAssembleDocument = false;
            document.SecurityHandler.SetEncryptionToV5();
            PdfSources.SaveSafely(document, output);
        }

        /// <summary>Salva una copia senza password né limiti. Serve la password (di apertura o del proprietario).</summary>
        public static void Unlock(string path, string? password, string output)
        {
            using var file = PdfFile.Open(path, password);
            var tmp = output + ".tmp";
            try
            {
                using (var stream = File.Create(tmp))
                    file.SaveWithoutSecurity(stream);
                File.Move(tmp, output, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        /// <summary>Apre un PDF da modificare; se è protetto, prima lo sblocca in memoria con PDFium.</summary>
        internal static PdfDocument OpenForChange(string path, string? password)
        {
            using var file = PdfFile.Open(path, password);
            if (!file.IsEncrypted)
                return PdfReader.Open(new MemoryStream(File.ReadAllBytes(path)), PdfDocumentOpenMode.Modify);
            var clear = new MemoryStream();
            file.SaveWithoutSecurity(clear);
            clear.Position = 0;
            return PdfReader.Open(clear, PdfDocumentOpenMode.Modify);
        }

        private static string RandomPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        // ---- Immagini → PDF -------------------------------------------------------------------------------

        public const double ImageDpi = 150;

        /// <summary>
        /// Un PDF con un'immagine per pagina, nell'ordine dato. Le foto JPEG entrano così come sono (niente perdita
        /// di qualità, file piccolo); gli altri formati come PNG. Si tiene conto della rotazione scritta dalle
        /// fotocamere (EXIF).
        /// </summary>
        public static void ImagesToPdf(IReadOnlyList<string> images, string output, PageLayout layout = PageLayout.FitImage,
            double marginMm = 0)
        {
            if (images.Count == 0) throw new ArgumentException("Nessuna immagine", nameof(images));
            using var document = new PdfDocument();
            document.Info.Creator = "DockPDF";
            foreach (var path in images)
            {
                using var data = ImageForPdf(path, out int pixelW, out int pixelH);
                using var image = XImage.FromStream(data);
                double imgW = pixelW * 72.0 / ImageDpi, imgH = pixelH * 72.0 / ImageDpi;
                var page = document.AddPage();
                double margin = marginMm * 72.0 / 25.4;
                if (layout == PageLayout.FitImage)
                {
                    page.Width = XUnit.FromPoint(imgW + 2 * margin);
                    page.Height = XUnit.FromPoint(imgH + 2 * margin);
                }
                else
                {
                    var (w, h) = layout == PageLayout.A4 ? (595.28, 841.89) : (612.0, 792.0);
                    bool landscape = pixelW > pixelH;
                    page.Width = XUnit.FromPoint(landscape ? h : w);
                    page.Height = XUnit.FromPoint(landscape ? w : h);
                }
                double areaW = page.Width.Point - 2 * margin, areaH = page.Height.Point - 2 * margin;
                double scale = Math.Min(areaW / imgW, areaH / imgH);
                double drawW = imgW * scale, drawH = imgH * scale;
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(image, margin + (areaW - drawW) / 2, margin + (areaH - drawH) / 2, drawW, drawH);
            }
            PdfSources.SaveSafely(document, output);
        }

        /// <summary>I byte da mettere nel PDF: il JPEG originale se possibile, altrimenti un PNG.</summary>
        private static MemoryStream ImageForPdf(string path, out int width, out int height)
        {
            var bytes = File.ReadAllBytes(path);
            using (var codec = SKCodec.Create(new MemoryStream(bytes)))
            {
                if (codec == null) throw new InvalidDataException($"Immagine non riconosciuta: {Path.GetFileName(path)}");
                bool upright = codec.EncodedOrigin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default;
                if (codec.EncodedFormat == SKEncodedImageFormat.Jpeg && upright)
                {
                    width = codec.Info.Width;
                    height = codec.Info.Height;
                    var copy = new MemoryStream(bytes.Length);
                    copy.Write(bytes);
                    copy.Position = 0;
                    return copy; // PDFsharp vuole un MemoryStream con il buffer accessibile
                }
            }
            using var image = LoadUpright(bytes, path);
            width = image.Width;
            height = image.Height;
            var png = new MemoryStream();
            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
                encoded.SaveTo(png);
            png.Position = 0;
            return png;
        }

        /// <summary>Decodifica l'immagine girandola come indica l'EXIF.</summary>
        private static SKBitmap LoadUpright(byte[] bytes, string path)
        {
            using var codec = SKCodec.Create(new MemoryStream(bytes))
                ?? throw new InvalidDataException($"Immagine non riconosciuta: {Path.GetFileName(path)}");
            using var decoded = SKBitmap.Decode(codec)
                ?? throw new InvalidDataException($"Immagine non riconosciuta: {Path.GetFileName(path)}");
            var origin = codec.EncodedOrigin;
            bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            int w = swap ? decoded.Height : decoded.Width, h = swap ? decoded.Width : decoded.Height;
            return Pixels.Render(w, h, canvas =>
            {
                switch (origin)
                {
                    case SKEncodedOrigin.TopRight: canvas.Translate(w, 0); canvas.Scale(-1, 1); break;
                    case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;
                    case SKEncodedOrigin.BottomLeft: canvas.Translate(0, h); canvas.Scale(1, -1); break;
                    case SKEncodedOrigin.LeftTop: canvas.Scale(-1, 1); canvas.RotateDegrees(90); break;
                    case SKEncodedOrigin.RightTop: canvas.Translate(w, 0); canvas.RotateDegrees(90); break;
                    case SKEncodedOrigin.RightBottom: canvas.Translate(w, h); canvas.Scale(1, -1); canvas.RotateDegrees(90); break;
                    case SKEncodedOrigin.LeftBottom: canvas.Translate(0, h); canvas.RotateDegrees(-90); break;
                }
                canvas.DrawBitmap(decoded, 0, 0);
            });
        }

        // ---- PDF → immagini -------------------------------------------------------------------------------

        /// <summary>
        /// Salva le pagine come immagini ("nome_p1.png", ...) alla risoluzione indicata. <paramref name="format"/>:
        /// "png" o "jpg". Senza intervalli si salvano tutte le pagine.
        /// </summary>
        public static IReadOnlyList<string> PdfToImages(string path, string? password, IReadOnlyList<PageRange>? ranges,
            int dpi, string format, string folder, IProgress<double>? progress = null)
        {
            using var file = PdfFile.Open(path, password);
            var pages = ranges == null || ranges.Count == 0
                ? Enumerable.Range(0, file.PageCount).ToList()
                : PageRanges.Pages(ranges).ToList();
            Directory.CreateDirectory(folder);
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = format.Equals("jpg", StringComparison.OrdinalIgnoreCase) || format.Equals("jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
            var created = new List<string>();
            for (int i = 0; i < pages.Count; i++)
            {
                using var bitmap = file.RenderAtDpi(pages[i], dpi);
                var output = UniquePath(Path.Combine(folder, $"{name}_p{pages[i] + 1}{ext}"));
                ImageFiles.Save(bitmap, output);
                created.Add(output);
                progress?.Report((i + 1) / (double)pages.Count);
            }
            return created;
        }

        // ---- Varie ----------------------------------------------------------------------------------------

        /// <summary>Se il file esiste già aggiunge " (2)", " (3)"... prima dell'estensione.</summary>
        public static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path) ?? "";
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (int n = 2; ; n++)
            {
                var candidate = Path.Combine(dir, $"{name} ({n}){ext}");
                if (!File.Exists(candidate)) return candidate;
            }
        }
    }
}
