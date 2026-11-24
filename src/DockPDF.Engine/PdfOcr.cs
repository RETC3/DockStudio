using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DockStudio.Ocr;

namespace DockPDF.Engine
{
    /// <summary>Esito del riconoscimento del testo.</summary>
    public sealed record OcrSummary(int PagesRecognized, int PagesSkipped, int Words, string Text, string Language);

    /// <summary>
    /// Rende cercabile un PDF scansionato: ogni pagina viene disegnata ad alta risoluzione, il motore OCR del sistema
    /// ne riconosce le parole e sopra ciascuna si scrive il testo invisibile (la scansione resta com'è).
    /// </summary>
    public static class PdfOcr
    {
        /// <summary>Risoluzione a cui si legge la pagina: 300 dpi, il valore consigliato per l'OCR.</summary>
        public const int Dpi = 300;

        /// <param name="skipPagesWithText">Le pagine che hanno già testo vero non si toccano (di solito non sono scansioni).</param>
        public static async Task<OcrSummary> MakeSearchableAsync(string path, string? password, string output, IOcrEngine engine,
            string? language = null, bool skipPagesWithText = true, IProgress<(int Page, int Count)>? progress = null,
            CancellationToken cancel = default)
        {
            var edits = new EditSet();
            var text = new List<string>();
            int recognized = 0, skipped = 0, words = 0;
            string used = language ?? engine.DefaultLanguage ?? "";
            using (var file = PdfFile.Open(path, password))
            {
                for (int page = 0; page < file.PageCount; page++)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress?.Report((page, file.PageCount));
                    if (skipPagesWithText && file.HasText(page))
                    {
                        skipped++;
                        text.Add(file.Text(page));
                        continue;
                    }
                    var (w, h) = file.PageSize(page);
                    // 300 dpi, ma senza superare quello che il motore accetta
                    double dpi = Math.Min(Dpi, engine.MaxImageDimension * 72.0 / Math.Max(w, h));
                    using var image = file.Render(page, Math.Max(1, (int)Math.Round(w * dpi / 72)), forPrinting: true);
                    var result = await engine.RecognizeAsync(image, language, cancel).ConfigureAwait(false);
                    used = result.Language;
                    double toPoints = w / image.Width;
                    foreach (var line in result.Lines)
                        foreach (var word in line.Words)
                        {
                            edits.Items.Add(new HiddenTextItem(page,
                                new PageRect(word.X * toPoints, word.Y * toPoints, word.Width * toPoints, word.Height * toPoints), word.Text));
                            words++;
                        }
                    text.Add(result.Text);
                    recognized++;
                }
                progress?.Report((file.PageCount, file.PageCount));
            }
            // Le parole sono testo a sé: tra una parola e l'altra di una riga non c'è uno spazio vero, ma i
            // lettori lo deducono dalla distanza (come per tutti i PDF con OCR).
            await Task.Run(() => PdfEditor.Apply(path, password, edits, output), cancel).ConfigureAwait(false);
            return new OcrSummary(recognized, skipped, words, string.Join("\n\n", text.Where(t => t.Length > 0)), used);
        }
    }
}
