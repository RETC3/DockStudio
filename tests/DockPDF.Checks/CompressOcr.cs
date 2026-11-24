using System;
using System.IO;
using System.Linq;
using DockPDF.Engine;
using DockStudio.Ocr.Windows;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;

namespace DockPDF.Checks
{
    internal static partial class Program
    {
        static partial void RunCompressOcrChecks(string work)
        {
            Section("Comprimi", () => CheckCompress(work));
            Section("Riconosci il testo (OCR)", () => CheckOcr(work));
        }

        /// <summary>Una "foto": sfumature e un po' di rumore, come una fotografia vera (comprime poco in PNG).</summary>
        private static string MakePhoto(string folder, string name, int w, int h, SKEncodedImageFormat format, int quality = 95)
        {
            var random = new Random(7);
            using var bmp = new SKBitmap(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    bmp.SetPixel(x, y, new SKColor((byte)(x * 255 / w), (byte)(y * 255 / h), (byte)(128 + random.Next(-40, 40))));
            var path = Path.Combine(folder, name);
            using var data = bmp.Encode(format, quality);
            File.WriteAllBytes(path, data.ToArray());
            return path;
        }

        /// <summary>PDF di due pagine con foto grandi (una JPEG, una non compressa).</summary>
        internal static string MakeHeavy(string work)
        {
            var jpg = MakePhoto(work, "grande.jpg", 2400, 1800, SKEncodedImageFormat.Jpeg);
            var png = MakePhoto(work, "grande.png", 1200, 900, SKEncodedImageFormat.Png);
            var heavy = Path.Combine(work, "pesante.pdf");
            PdfTools.ImagesToPdf(new[] { jpg, png }, heavy, PageLayout.A4);
            return heavy;
        }

        /// <summary>Una "scansione": una lettera disegnata come immagine, senza testo vero.</summary>
        internal static string MakeScan(string work)
        {
            var source = Path.Combine(work, "lettera.pdf");
            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(595);
                page.Height = XUnit.FromPoint(842);
                using var gfx = XGraphics.FromPdfPage(page);
                var font = new XFont("Arial", 22);
                gfx.DrawString("Fattura numero 2026 del 23 novembre", font, XBrushes.Black, new XPoint(60, 120));
                gfx.DrawString("Perugia, via Roma 15", font, XBrushes.Black, new XPoint(60, 170));
                gfx.DrawString("Totale da pagare: 1250 euro", font, XBrushes.Black, new XPoint(60, 220));
                doc.Save(source);
            }
            var scanImage = Path.Combine(work, "scansione.png");
            using (var f = PdfFile.Open(source))
            using (var bmp = f.RenderAtDpi(0, 150))
                DockStudio.Imaging.ImageFiles.Save(bmp, scanImage);
            var scan = Path.Combine(work, "scansione.pdf");
            PdfTools.ImagesToPdf(new[] { scanImage }, scan);
            return scan;
        }

        private static void CheckCompress(string work)
        {
            var heavy = MakeHeavy(work);

            foreach (var level in new[] { CompressLevel.High, CompressLevel.Medium, CompressLevel.Low })
            {
                var output = Path.Combine(work, $"compresso-{level}.pdf");
                var result = PdfCompress.Compress(heavy, null, output, level);
                Check(result.Saved && result.Images == 2 && result.ImagesChanged >= 1,
                    $"{level}: da {result.Before / 1024} KB a {result.After / 1024} KB ({result.Saving:P0}), {result.ImagesChanged} immagini su {result.Images}");
                using var file = PdfFile.Open(output);
                Check(file.PageCount == 2, $"{level}: 2 pagine");
                using var page = file.Render(0, 300);
                var c = page.GetPixel(150, 106); // centro della foto
                Check(Math.Abs(c.Red - 127) < 40 && Math.Abs(c.Green - 127) < 40, $"{level}: la foto c'è ancora ({c})");
            }
            long high = new FileInfo(Path.Combine(work, "compresso-High.pdf")).Length;
            long low = new FileInfo(Path.Combine(work, "compresso-Low.pdf")).Length;
            Check(low < high, "il livello più basso dà il file più piccolo");

            // Un PDF già leggero (solo testo) non diventa più grande: si salva solo se conviene.
            var text = MakeSample(work, "solo-testo.pdf", 2);
            var again = PdfCompress.Compress(text, null, Path.Combine(work, "solo-testo-compresso.pdf"), CompressLevel.Medium);
            Check(!again.Saved || again.After < again.Before, $"PDF di solo testo: {(again.Saved ? "più piccolo" : "nessun file creato")}");
            if (again.Saved)
                using (var f = PdfFile.Open(Path.Combine(work, "solo-testo-compresso.pdf")))
                    Check(f.Text(1).Contains("Pagina 2"), "il testo resta");
        }

        private static void CheckOcr(string work)
        {
            var engine = new WindowsOcrEngine();
            var languages = engine.Languages;
            if (languages.Count == 0)
            {
                // Succede su alcune macchine senza lingue OCR (per esempio i server delle build): non è un errore di DockPDF.
                Console.WriteLine("  (OCR non provato: in questo Windows non c'è nessuna lingua per il riconoscimento del testo)");
                return;
            }
            Check(true, "lingue OCR di Windows: " + string.Join(", ", languages.Select(l => l.Tag)));

            var scan = MakeScan(work);
            using (var f = PdfFile.Open(scan))
                Check(!f.HasText(0), "la scansione non ha testo");

            var output = Path.Combine(work, "scansione-cercabile.pdf");
            var summary = PdfOcr.MakeSearchableAsync(scan, null, output, engine, languages.Any(l => l.Tag.StartsWith("it")) ? languages.First(l => l.Tag.StartsWith("it")).Tag : null)
                .GetAwaiter().GetResult();
            Check(summary.PagesRecognized == 1 && summary.Words >= 15, $"1 pagina riconosciuta, {summary.Words} parole ({summary.Language})");
            Check(summary.Text.Contains("Fattura") && summary.Text.Contains("Perugia") && summary.Text.Contains("1250"), "testo riconosciuto: " + summary.Text.Replace("\n", " | "));
            using (var f = PdfFile.Open(output))
            {
                var text = f.Text(0);
                Check(f.HasText(0) && text.Contains("Fattura") && text.Contains("Totale"), "il PDF ora ha il testo: " + text.Replace("\r\n", " | "));
                // il testo è invisibile: la pagina si vede come prima
                using var before = PdfFile.Open(scan);
                using var a = before.Render(0, 400);
                using var b = f.Render(0, 400);
                int different = 0;
                for (int y = 0; y < a.Height; y += 2)
                    for (int x = 0; x < a.Width; x += 2)
                    {
                        var p = a.GetPixel(x, y);
                        var q = b.GetPixel(x, y);
                        if (Math.Abs(p.Red - q.Red) + Math.Abs(p.Green - q.Green) + Math.Abs(p.Blue - q.Blue) > 30) different++;
                    }
                Check(different == 0, $"il testo aggiunto non si vede ({different} punti diversi)");
                // la parola "Fattura" sta sopra la parola nella scansione (x 60, base 120 nella lettera)
                var words = f.TextBlocks(0);
                var first = words.FirstOrDefault(w => w.Text.StartsWith("Fattura"));
                Check(first != null && Math.Abs(first.Box.X - 60) < 4 && Math.Abs(first.Baseline - 120) < 5,
                    "posizione delle parole: " + (first == null ? string.Join(" | ", words.Select(w => w.Text)) : $"x {first.Box.X:0.0}, base {first.Baseline:0.0}, \"{first.Text}\""));
                var last = words.FirstOrDefault(w => w.Text.Contains("1250"));
                Check(last != null && Math.Abs(last.Baseline - 220) < 5, "terza riga alla sua altezza: " + (last == null ? "?" : $"base {last.Baseline:0.0}"));
                Check(text.IndexOf("Fattura", StringComparison.Ordinal) < text.IndexOf("Perugia", StringComparison.Ordinal), "ordine delle righe");
            }

            // Un PDF che ha già testo: con "salta" non si tocca nessuna pagina.
            var withText = MakeSample(work, "con-testo.pdf", 2);
            var skipped = PdfOcr.MakeSearchableAsync(withText, null, Path.Combine(work, "con-testo-ocr.pdf"), engine).GetAwaiter().GetResult();
            Check(skipped.PagesRecognized == 0 && skipped.PagesSkipped == 2, "le pagine con testo si saltano");
        }
    }
}
