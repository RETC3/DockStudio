using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace DockStudio.Ocr
{
    /// <summary>Una parola riconosciuta, con il suo riquadro in pixel dell'immagine.</summary>
    public sealed record OcrWord(string Text, double X, double Y, double Width, double Height);

    /// <summary>Una riga di parole.</summary>
    public sealed record OcrLine(IReadOnlyList<OcrWord> Words)
    {
        public string Text => string.Join(" ", Words.Select(w => w.Text));
    }

    /// <summary>Il testo riconosciuto in un'immagine.</summary>
    public sealed record OcrPage(IReadOnlyList<OcrLine> Lines, string Language)
    {
        /// <summary>Le righe ricomposte, con una riga vuota tra i paragrafi.</summary>
        public string Text
        {
            get
            {
                var sb = new StringBuilder();
                double prevBottom = double.NaN, prevHeight = 0;
                foreach (var line in Lines.Where(l => l.Words.Count > 0))
                {
                    double top = line.Words.Min(w => w.Y);
                    double bottom = line.Words.Max(w => w.Y + w.Height);
                    double height = bottom - top;
                    if (!double.IsNaN(prevBottom))
                    {
                        sb.AppendLine();
                        if (top - prevBottom > Math.Max(height, prevHeight) * 0.9) sb.AppendLine();
                    }
                    sb.Append(line.Text);
                    prevBottom = bottom;
                    prevHeight = height;
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>Una lingua che il motore sa leggere.</summary>
    public sealed record OcrLanguage(string Tag, string Name);

    /// <summary>Non c'è nessuna lingua installata per l'OCR.</summary>
    public sealed class OcrUnavailableException : Exception
    {
        public OcrUnavailableException(string message) : base(message) { }
    }

    /// <summary>Un motore OCR del sistema (offline).</summary>
    public interface IOcrEngine
    {
        IReadOnlyList<OcrLanguage> Languages { get; }

        /// <summary>La lingua dell'utente, se il motore la sa leggere.</summary>
        string? DefaultLanguage { get; }

        /// <summary>Il lato più lungo dell'immagine che il motore accetta.</summary>
        int MaxImageDimension { get; }

        /// <summary>Riconosce il testo; l'immagine è in formato BGRA premoltiplicato (DockStudio.Imaging.Pixels).</summary>
        Task<OcrPage> RecognizeAsync(SKBitmap image, string? language = null, CancellationToken cancel = default);
    }
}
