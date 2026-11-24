using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DockPDF.Engine;
using DockStudio.Documents;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace DockPDF.Checks
{
    internal static partial class Program
    {
        private static int _failures;
        private static string _images = "";

        private static int Main(string[] args)
        {
            _images = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "dockpdf-checks"));
            Directory.CreateDirectory(_images);
            var work = Path.Combine(Path.GetTempPath(), "dockpdf-work-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(work);
            try
            {
                PdfFonts.Install();
                // PDF di prova da usare a mano: DOCKPDF_SAMPLES=<cartella> li crea lì e basta.
                if (Environment.GetEnvironmentVariable("DOCKPDF_SAMPLES") is { Length: > 0 } samples)
                {
                    Directory.CreateDirectory(samples);
                    File.Copy(MakeForm(work), Path.Combine(samples, "Modulo di iscrizione.pdf"), true);
                    File.Copy(MakeSample(work, "contratto.pdf", 5, "Contratto, pagina"), Path.Combine(samples, "Contratto.pdf"), true);
                    File.Copy(MakeRotated(work), Path.Combine(samples, "Pagine girate.pdf"), true);
                    File.Copy(MakeScan(work), Path.Combine(samples, "Scansione.pdf"), true);
                    File.Copy(MakeHeavy(work), Path.Combine(samples, "Foto pesanti.pdf"), true);
                    Console.WriteLine($"PDF di prova in {samples}");
                    return 0;
                }
                Section("Intervalli di pagine", CheckRanges);
                Section("Lettura e disegno (PDFium)", () => CheckRead(work));
                Section("Unisci, ordina, ruota, elimina", () => CheckCompose(work));
                Section("Dividi ed estrai", () => CheckSplit(work));
                Section("Password", () => CheckPasswords(work));
                Section("Immagini → PDF", () => CheckImagesToPdf(work));
                Section("PDF → immagini", () => CheckPdfToImages(work));
                RunEditChecks(work);
                RunCompressOcrChecks(work);
                RunUiChecks(work);
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { /* resti nella cartella temporanea */ }
            }

            Console.WriteLine();
            Console.WriteLine($"Immagini: {_images}");
            Console.WriteLine(_failures == 0 ? "Tutti i controlli superati." : $"{_failures} controlli NON superati.");
            return _failures == 0 ? 0 : 1;
        }

        /// <summary>Controlli della modifica (Edit.cs).</summary>
        static partial void RunEditChecks(string work);

        /// <summary>Controlli di Comprimi e OCR (CompressOcr.cs).</summary>
        static partial void RunCompressOcrChecks(string work);

        /// <summary>Controlli delle finestre (nel file Ui.cs, quando c'è l'app).</summary>
        static partial void RunUiChecks(string work);

        private static void Section(string name, Action body)
        {
            Console.WriteLine();
            Console.WriteLine($"== {name}");
            try { body(); }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERRORE  eccezione: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                _failures++;
            }
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "OK    " : "ERRORE")}  {what}");
            if (!ok) _failures++;
        }

        // ---- PDF di prova --------------------------------------------------------------------------------

        /// <summary>Un PDF con pagine "Prova N" (A4 verticali; la terza orizzontale).</summary>
        internal static string MakeSample(string folder, string name, int pages, string label = "Pagina")
        {
            var path = Path.Combine(folder, name);
            using var doc = new PdfDocument();
            var font = new XFont("Segoe UI", 40, XFontStyleEx.Bold);
            for (int i = 1; i <= pages; i++)
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(i == 3 ? 842 : 595);
                page.Height = XUnit.FromPoint(i == 3 ? 595 : 842);
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawRectangle(XBrushes.IndianRed, 40, 40, 120, 60);
                gfx.DrawString($"{label} {i}", font, XBrushes.Black, new XPoint(60, 200));
            }
            doc.Save(path);
            return path;
        }

        private static string PageLabel(PdfFile file, int index) => file.Text(index).Trim();

        // ---- Controlli del motore ------------------------------------------------------------------------

        private static void CheckRanges()
        {
            Check(PageRanges.TryParse("1-3, 5, 8-", 10, out var r) && r.Count == 3 && r[2] == new PageRange(7, 9), "\"1-3, 5, 8-\" su 10 pagine");
            Check(PageRanges.Pages(r).SequenceEqual(new[] { 0, 1, 2, 4, 7, 8, 9 }), "pagine degli intervalli");
            Check(!PageRanges.TryParse("0-2", 10, out _), "la pagina 0 non esiste");
            Check(!PageRanges.TryParse("4-2", 10, out _), "intervallo al contrario");
            Check(!PageRanges.TryParse("12", 10, out _), "pagina oltre la fine");
            Check(!PageRanges.TryParse("a", 10, out _), "testo non valido");
            Check(PageRanges.TryParse("-2; 9", 10, out var r2) && r2[0] == new PageRange(0, 1) && r2[1] == new PageRange(8, 8), "\"-2; 9\"");
        }

        private static void CheckRead(string work)
        {
            var path = MakeSample(work, "lettura.pdf", 4);
            using var file = PdfFile.Open(path);
            Check(file.PageCount == 4, "4 pagine");
            Check(!file.IsEncrypted, "non protetto");
            var (w, h) = file.PageSize(0);
            Check(Math.Abs(w - 595) < 1 && Math.Abs(h - 842) < 1, $"A4 verticale: {w}×{h}");
            var (w3, h3) = file.PageSize(2);
            Check(w3 > h3, "la terza è orizzontale");
            Check(PageLabel(file, 1) == "Pagina 2", "testo della pagina 2: " + PageLabel(file, 1));
            using var bmp = file.Render(0, 300);
            Check(bmp.Width == 300 && Math.Abs(bmp.Height - 425) <= 1, $"anteprima 300 px: {bmp.Width}×{bmp.Height}");
            var red = bmp.GetPixel((int)(100 * 300 / 595.0), (int)(70 * 300 / 595.0));
            Check(red.Red > 150 && red.Green < 140, "il riquadro rosso si vede: " + red);
            Check(bmp.GetPixel(290, 420) == SKColors.White, "sfondo bianco");
            using var turned = file.Render(0, 300, rotation: 90);
            Check(turned.Height < turned.Width, "anteprima girata di 90°");
            using var hi = file.RenderAtDpi(0, 144);
            Check(Math.Abs(hi.Width - 1190) <= 1, "144 dpi: " + hi.Width);
            Save(bmp, "pagina-1");

            File.WriteAllText(Path.Combine(work, "finto.pdf"), "non sono un pdf");
            bool format = false;
            try { PdfFile.Open(Path.Combine(work, "finto.pdf")).Dispose(); }
            catch (PdfFormatException) { format = true; }
            Check(format, "un file non PDF dà PdfFormatException");
        }

        private static void CheckCompose(string work)
        {
            var a = MakeSample(work, "a.pdf", 3, "A");
            var b = MakeSample(work, "b.pdf", 2, "B");
            var output = Path.Combine(work, "unito.pdf");
            using (var sources = new PdfSources())
                sources.Compose(new List<PageRef>
                {
                    new(b, 1), new(a, 0, 90), new(a, 2), new(b, 0, 270),
                }, output);
            using var file = PdfFile.Open(output);
            Check(file.PageCount == 4, "4 pagine (una di A eliminata)");
            var labels = Enumerable.Range(0, file.PageCount).Select(i => PageLabel(file, i)).ToArray();
            Check(labels.SequenceEqual(new[] { "B 2", "A 1", "A 3", "B 1" }), "ordine: " + string.Join(", ", labels));
            using var doc = PdfReader.Open(output, PdfDocumentOpenMode.Import);
            Check(doc.Pages[1].Rotate == 90 && doc.Pages[3].Rotate == 270 && doc.Pages[0].Rotate == 0, "rotazioni 0/90/0/270");
            var (w, h) = file.PageSize(1);
            Check(w > h, "la pagina girata di 90° risulta orizzontale");

            // L'uscita può essere uno dei file d'origine (Organizza sovrascrive lo stesso file).
            using (var sources = new PdfSources())
                sources.Compose(new List<PageRef> { new(a, 2), new(a, 1) }, a);
            using var again = PdfFile.Open(a);
            Check(again.PageCount == 2 && PageLabel(again, 0) == "A 3", "sovrascrivere il file d'origine");
        }

        private static void CheckSplit(string work)
        {
            var path = MakeSample(work, "lungo.pdf", 7);
            var each = Path.Combine(work, "ogni");
            var files = PdfTools.Split(path, null, SplitMode.EachPage, 0, null, each);
            Check(files.Count == 7 && Path.GetFileName(files[0]) == "lungo_p1.pdf", "una pagina per file: " + files.Count);

            var byThree = PdfTools.Split(path, null, SplitMode.EveryN, 3, null, Path.Combine(work, "tre"));
            Check(byThree.Count == 3 && Path.GetFileName(byThree[2]) == "lungo_p7.pdf", "ogni 3 pagine: " + string.Join(", ", byThree.Select(Path.GetFileName)));
            using (var last = PdfFile.Open(byThree[1]))
                Check(last.PageCount == 3 && PageLabel(last, 0) == "Pagina 4", "il secondo pezzo parte dalla pagina 4");

            PageRanges.TryParse("1-2, 5-", 7, out var ranges);
            var parts = PdfTools.Split(path, null, SplitMode.Ranges, 0, ranges, Path.Combine(work, "intervalli"));
            Check(parts.Count == 2 && Path.GetFileName(parts[1]) == "lungo_p5-7.pdf", "per intervalli");

            var extracted = PdfTools.Split(path, null, SplitMode.Extract, 0, ranges, Path.Combine(work, "estratte"));
            using (var one = PdfFile.Open(extracted.Single()))
                Check(one.PageCount == 5 && PageLabel(one, 2) == "Pagina 5", "estrai in un solo file: " + Path.GetFileName(extracted[0]));

            var again = PdfTools.Split(path, null, SplitMode.Extract, 0, ranges, Path.Combine(work, "estratte"));
            Check(Path.GetFileName(again[0]) == "lungo_p1-2,5-7 (2).pdf", "non sovrascrive: " + Path.GetFileName(again[0]));
        }

        private static void CheckPasswords(string work)
        {
            var path = MakeSample(work, "segreto.pdf", 2);
            var locked = Path.Combine(work, "segreto-protetto.pdf");
            PdfTools.Protect(path, null, locked, "apriti", allowPrint: false);
            Check(PdfFile.NeedsPassword(locked), "il PDF protetto chiede la password");
            bool wrong = false;
            try { PdfFile.Open(locked, "sbagliata").Dispose(); }
            catch (PdfPasswordException ex) { wrong = ex.WrongPassword; }
            Check(wrong, "password sbagliata riconosciuta");
            using (var open = PdfFile.Open(locked, "apriti"))
                Check(open.IsEncrypted && PageLabel(open, 1) == "Pagina 2", "si apre con la password giusta");

            // Le pagine di un PDF protetto si possono unire se si conosce la password di apertura.
            var merged = Path.Combine(work, "unito-da-protetto.pdf");
            using (var sources = new PdfSources(p => p.EndsWith("protetto.pdf") ? "apriti" : null))
                sources.Compose(new List<PageRef> { new(locked, 1), new(path, 0) }, merged);
            using (var m = PdfFile.Open(merged))
                Check(!m.IsEncrypted && m.PageCount == 2 && PageLabel(m, 0) == "Pagina 2", "unione con un PDF protetto");

            var clear = Path.Combine(work, "segreto-libero.pdf");
            PdfTools.Unlock(locked, "apriti", clear);
            Check(!PdfFile.NeedsPassword(clear), "password tolta");
            using (var c = PdfFile.Open(clear))
                Check(!c.IsEncrypted && c.PageCount == 2, "senza protezione e con tutte le pagine");

            // Protezione di un file già protetto (cambio password).
            var changed = Path.Combine(work, "segreto-nuova.pdf");
            PdfTools.Protect(locked, "apriti", changed, "nuova");
            bool old = false;
            try { PdfFile.Open(changed, "apriti").Dispose(); } catch (PdfPasswordException) { old = true; }
            using (var n = PdfFile.Open(changed, "nuova"))
                Check(old && n.PageCount == 2, "cambio di password");
        }

        private static void CheckImagesToPdf(string work)
        {
            string MakeImage(string name, int w, int h, SKColor color, SKEncodedImageFormat format)
            {
                using var bmp = new SKBitmap(w, h);
                using (var canvas = new SKCanvas(bmp)) canvas.Clear(color);
                var path = Path.Combine(work, name);
                using var data = bmp.Encode(format, 90);
                File.WriteAllBytes(path, data.ToArray());
                return path;
            }
            var jpg = MakeImage("foto.jpg", 1500, 1000, SKColors.SteelBlue, SKEncodedImageFormat.Jpeg);
            var png = MakeImage("schema.png", 600, 900, SKColors.Orange, SKEncodedImageFormat.Png);
            var output = Path.Combine(work, "immagini.pdf");
            PdfTools.ImagesToPdf(new[] { jpg, png }, output);
            using (var file = PdfFile.Open(output))
            {
                Check(file.PageCount == 2, "una pagina per immagine");
                var (w, h) = file.PageSize(0);
                Check(Math.Abs(w - 720) < 1 && Math.Abs(h - 480) < 1, $"pagina grande quanto la foto a 150 dpi: {w:0}×{h:0}");
                using var bmp = file.Render(1, 200);
                var c = bmp.GetPixel(100, 150);
                Check(c.Red > 200 && c.Blue < 80, "la seconda pagina è arancione: " + c);
            }
            Check(new FileInfo(output).Length < new FileInfo(jpg).Length + 20_000, "il JPEG entra senza ricompressione");

            var a4 = Path.Combine(work, "immagini-a4.pdf");
            PdfTools.ImagesToPdf(new[] { jpg, png }, a4, PageLayout.A4, marginMm: 10);
            using (var file = PdfFile.Open(a4))
            {
                var (w0, h0) = file.PageSize(0);
                var (w1, h1) = file.PageSize(1);
                Check(Math.Abs(w0 - 842) < 1 && Math.Abs(h1 - 842) < 1 && w1 < h1, "A4 orizzontale per la foto larga, verticale per l'altra");
                using var bmp = file.Render(0, 842);
                Check(bmp.GetPixel(5, 200) == SKColors.White, "margine bianco");
            }
        }

        private static void CheckPdfToImages(string work)
        {
            var path = MakeSample(work, "immagini-da.pdf", 3);
            var folder = Path.Combine(work, "png");
            var files = PdfTools.PdfToImages(path, null, null, 72, "png", folder);
            Check(files.Count == 3 && Path.GetFileName(files[2]) == "immagini-da_p3.png", "tre PNG");
            using (var img = SKBitmap.Decode(files[0]))
                Check(Math.Abs(img.Width - 595) <= 1, "72 dpi = 595 px di larghezza: " + img.Width);
            PageRanges.TryParse("2", 3, out var only);
            var jpgs = PdfTools.PdfToImages(path, null, only, 150, "jpg", Path.Combine(work, "jpg"));
            Check(jpgs.Count == 1 && jpgs[0].EndsWith("_p2.jpg"), "solo la pagina 2 in JPEG");
        }

        internal static void Save(SKBitmap bitmap, string name)
        {
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(_images, name + ".png"), data.ToArray());
        }
    }
}
