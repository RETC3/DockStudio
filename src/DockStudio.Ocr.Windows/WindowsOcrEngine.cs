using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using WinOcr = Windows.Media.Ocr.OcrEngine;

namespace DockStudio.Ocr.Windows
{
    /// <summary>
    /// L'OCR integrato in Windows: nessun servizio esterno, funziona offline. Le lingue sono quelle installate in
    /// Windows con l'opzione "Riconoscimento ottico dei caratteri".
    /// </summary>
    public sealed class WindowsOcrEngine : IOcrEngine
    {
        public IReadOnlyList<OcrLanguage> Languages =>
            WinOcr.AvailableRecognizerLanguages.Select(l => new OcrLanguage(l.LanguageTag, l.NativeName)).ToList();

        public string? DefaultLanguage => WinOcr.TryCreateFromUserProfileLanguages()?.RecognizerLanguage.LanguageTag;

        public int MaxImageDimension => (int)WinOcr.MaxImageDimension;

        public async Task<OcrPage> RecognizeAsync(SKBitmap image, string? language = null, CancellationToken cancel = default)
        {
            var engine = language != null ? WinOcr.TryCreateFromLanguage(new Language(language)) : WinOcr.TryCreateFromUserProfileLanguages();
            engine ??= WinOcr.AvailableRecognizerLanguages.Select(WinOcr.TryCreateFromLanguage).FirstOrDefault(e => e != null);
            if (engine == null)
                throw new OcrUnavailableException(
                    "Nessuna lingua per il riconoscimento del testo è installata in Windows. Aggiungila da Impostazioni › " +
                    "Ora e lingua › Lingua (opzione \"Riconoscimento ottico dei caratteri\").");

            // Troppo grande per il motore: si riduce e alla fine si riportano le coordinate.
            double scale = Math.Min(1, (double)MaxImageDimension / Math.Max(image.Width, image.Height));
            SKBitmap source = image;
            SKBitmap? scaled = null;
            if (scale < 1)
            {
                scaled = image.Resize(new SKImageInfo((int)(image.Width * scale), (int)(image.Height * scale), SKColorType.Bgra8888, SKAlphaType.Premul),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                source = scaled;
            }
            try
            {
                var pixels = source.Bytes;
                using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8,
                    source.Width, source.Height, BitmapAlphaMode.Premultiplied);
                cancel.ThrowIfCancellationRequested();
                var result = await engine.RecognizeAsync(bitmap).AsTask(cancel);
                double back = 1 / scale;
                var lines = result.Lines
                    .Select(l => new OcrLine(l.Words.Select(w => new OcrWord(w.Text,
                        w.BoundingRect.X * back, w.BoundingRect.Y * back, w.BoundingRect.Width * back, w.BoundingRect.Height * back)).ToList()))
                    .ToList();
                return new OcrPage(lines, engine.RecognizerLanguage.LanguageTag);
            }
            finally
            {
                scaled?.Dispose();
            }
        }
    }
}
