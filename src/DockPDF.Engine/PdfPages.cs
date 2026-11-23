using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace DockPDF.Engine
{
    /// <summary>Una pagina da mettere nel PDF finale: file d'origine, posizione (da 0) e rotazione in più.</summary>
    public sealed record PageRef(string Source, int Index, int Rotation = 0);

    /// <summary>
    /// Il cuore di Unisci, Organizza, Dividi ed Estrai: un PDF nuovo fatto di pagine prese da uno o più PDF,
    /// nell'ordine voluto e con le rotazioni volute. Le pagine si copiano così come sono (testo, immagini,
    /// collegamenti), senza ridisegnarle.
    /// </summary>
    public sealed class PdfSources : IDisposable
    {
        private readonly Dictionary<string, PdfDocument> _open = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<string, string?> _password;

        /// <param name="password">Password di un file protetto (null se non serve o non si conosce).</param>
        public PdfSources(Func<string, string?>? password = null) => _password = password ?? (_ => null);

        /// <summary>Il documento aperto per copiarne le pagine (una volta sola per file).</summary>
        public PdfDocument Get(string path)
        {
            var full = Path.GetFullPath(path);
            if (_open.TryGetValue(full, out var doc)) return doc;
            doc = OpenForImport(full, _password(full));
            _open[full] = doc;
            return doc;
        }

        /// <summary>
        /// Apre un PDF per copiarne le pagine. PDFsharp vuole la password del proprietario per copiare da un file
        /// protetto: in tutti gli altri casi (password di apertura, solo limiti) il file si sblocca prima con
        /// PDFium, in memoria.
        /// </summary>
        public static PdfDocument OpenForImport(string path, string? password)
        {
            try
            {
                return PdfReader.Open(path, password ?? "", PdfDocumentOpenMode.Import);
            }
            catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or NotSupportedException)
            {
                // Protetto (o con una cifratura che PDFsharp non conosce): copia sbloccata con PDFium.
                using var file = PdfFile.Open(path, password);
                var clear = new MemoryStream();
                file.SaveWithoutSecurity(clear);
                clear.Position = 0;
                return PdfReader.Open(clear, PdfDocumentOpenMode.Import);
            }
        }

        /// <summary>Crea il PDF con le pagine indicate e lo salva in <paramref name="output"/>.</summary>
        public void Compose(IReadOnlyList<PageRef> pages, string output, string? title = null)
        {
            if (pages.Count == 0) throw new ArgumentException("Nessuna pagina", nameof(pages));
            using var result = new PdfDocument();
            result.Info.Creator = "DockPDF";
            if (title != null) result.Info.Title = title;
            foreach (var page in pages)
            {
                var source = Get(page.Source);
                var original = source.Pages[page.Index];
                var added = result.AddPage(original);
                int rotate = ((original.Rotate + page.Rotation) % 360 + 360) % 360;
                added.Rotate = rotate;
            }
            SaveSafely(result, output);
        }

        /// <summary>Salva accanto e sostituisce solo a salvataggio riuscito (anche se l'uscita è uno dei file d'origine).</summary>
        internal static void SaveSafely(PdfDocument document, string output)
        {
            var tmp = output + ".tmp";
            try
            {
                document.Save(tmp);
                File.Move(tmp, output, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        /// <summary>Tutte le pagine di un file, nell'ordine.</summary>
        public static IEnumerable<PageRef> AllPages(string path, int pageCount) =>
            Enumerable.Range(0, pageCount).Select(i => new PageRef(path, i));

        public void Dispose()
        {
            foreach (var doc in _open.Values) doc.Dispose();
            _open.Clear();
        }
    }

    /// <summary>Un intervallo di pagine, da 0 e con la fine compresa.</summary>
    public readonly record struct PageRange(int From, int To)
    {
        public int Count => To - From + 1;

        /// <summary>Come lo scrive una persona: "3" oppure "1-4" (da 1).</summary>
        public override string ToString() => From == To ? (From + 1).ToString() : $"{From + 1}-{To + 1}";
    }

    /// <summary>Lettura degli intervalli scritti a mano: "1-3, 5, 8-" (8- vuol dire dalla 8 alla fine).</summary>
    public static class PageRanges
    {
        public static bool TryParse(string? text, int pageCount, out List<PageRange> ranges)
        {
            ranges = new List<PageRange>();
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var raw in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var part = raw.Trim().Replace('–', '-');
                if (part.Length == 0) continue;
                int dash = part.IndexOf('-');
                int from, to;
                if (dash < 0)
                {
                    if (!int.TryParse(part, out from)) return false;
                    to = from;
                }
                else
                {
                    var a = part[..dash].Trim();
                    var b = part[(dash + 1)..].Trim();
                    if (a.Length == 0) from = 1;
                    else if (!int.TryParse(a, out from)) return false;
                    if (b.Length == 0) to = pageCount;
                    else if (!int.TryParse(b, out to)) return false;
                }
                if (from < 1 || to > pageCount || from > to) return false;
                ranges.Add(new PageRange(from - 1, to - 1));
            }
            return ranges.Count > 0;
        }

        /// <summary>Le pagine (da 0) degli intervalli, nell'ordine scritto.</summary>
        public static IEnumerable<int> Pages(IEnumerable<PageRange> ranges) =>
            ranges.SelectMany(r => Enumerable.Range(r.From, r.Count));
    }
}
