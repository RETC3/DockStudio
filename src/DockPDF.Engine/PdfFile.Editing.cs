using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DockPDF.Engine
{
    /// <summary>Una firma digitale già presente nel PDF (dati grezzi per la verifica).</summary>
    public sealed record ExistingSignature(string SubFilter, string Time, byte[] Contents, int[] ByteRange);

    public sealed unsafe partial class PdfFile
    {
        /// <summary>Posizione, dimensioni e rotazione della pagina.</summary>
        public PageGeometry Geometry(int index)
        {
            lock (Pdfium.Sync)
            {
                var page = LoadPage(index);
                try { return GeometryOf(page); }
                finally { Pdfium.FPDF_ClosePage(page); }
            }
        }

        private static PageGeometry GeometryOf(IntPtr page)
        {
            float l, b, r, t;
            if (Pdfium.FPDFPage_GetCropBox(page, &l, &b, &r, &t) == 0 && Pdfium.FPDFPage_GetMediaBox(page, &l, &b, &r, &t) == 0)
            {
                l = 0; b = 0; r = 612; t = 792;
            }
            // La CropBox non può uscire dalla MediaBox (PDFium disegna l'intersezione).
            float ml, mb, mr, mt;
            if (Pdfium.FPDFPage_GetMediaBox(page, &ml, &mb, &mr, &mt) != 0)
            {
                l = Math.Max(l, ml); b = Math.Max(b, mb); r = Math.Min(r, mr); t = Math.Min(t, mt);
            }
            int rotation = (Pdfium.FPDFPage_GetRotation(page) % 4 + 4) % 4 * 90;
            return new PageGeometry(Math.Min(l, r), Math.Min(b, t), Math.Max(l, r), Math.Max(b, t), rotation);
        }

        /// <summary>
        /// Il testo della pagina diviso in blocchi (righe) che si possono modificare. Si escludono i testi
        /// inclinati o che nella vista non sono orizzontali.
        /// </summary>
        public IReadOnlyList<PdfTextBlock> TextBlocks(int index)
        {
            lock (Pdfium.Sync)
            {
                var page = LoadPage(index);
                var textPage = Pdfium.FPDFText_LoadPage(page);
                try
                {
                    var geometry = GeometryOf(page);
                    var pieces = new List<Piece>();
                    int count = Pdfium.FPDFPage_CountObjects(page);
                    for (int i = 0; i < count; i++)
                    {
                        var obj = Pdfium.FPDFPage_GetObject(page, i);
                        if (obj == IntPtr.Zero || Pdfium.FPDFPageObj_GetType(obj) != Pdfium.FPDF_PAGEOBJ_TEXT) continue;
                        if (ReadPiece(obj, i, textPage, geometry) is { } piece) pieces.Add(piece);
                    }
                    return Group(index, pieces);
                }
                finally
                {
                    if (textPage != IntPtr.Zero) Pdfium.FPDFText_ClosePage(textPage);
                    Pdfium.FPDF_ClosePage(page);
                }
            }
        }

        private sealed record Piece(int Index, PageRect Box, string Text, double Size, FontFamilyKind Family, bool Bold, bool Italic, Argb Color, double Baseline);

        private static Piece? ReadPiece(IntPtr obj, int index, IntPtr textPage, PageGeometry geometry)
        {
            Pdfium.FsMatrix m;
            if (Pdfium.FPDFPageObj_GetMatrix(obj, &m) == 0) return null;
            // Solo testo dritto (nella pagina non girata); la rotazione della pagina si gestisce a parte.
            if (Math.Abs(m.B) > 1e-3 * Math.Abs(m.A) || Math.Abs(m.C) > 1e-3 * Math.Abs(m.D) || m.A <= 0 || m.D <= 0) return null;
            if (geometry.Rotation != 0) return null; // nelle pagine girate il testo nella vista non è orizzontale

            float l, b, r, t;
            if (Pdfium.FPDFPageObj_GetBounds(obj, &l, &b, &r, &t) == 0) return null;

            // testo: la lunghezza è in byte, compreso il terminatore
            ulong bytes = Pdfium.FPDFTextObj_GetText(obj, textPage, null, new System.Runtime.InteropServices.CULong(0)).Value;
            if (bytes <= 2) return null;
            var buffer = new ushort[bytes / 2];
            fixed (ushort* p = buffer)
                Pdfium.FPDFTextObj_GetText(obj, textPage, p, new System.Runtime.InteropServices.CULong((uint)bytes));
            var text = new string(buffer.Select(c => (char)c).ToArray()).TrimEnd('\0');
            if (text.Trim().Length == 0) return null;

            float size;
            double fontSize = Pdfium.FPDFTextObj_GetFontSize(obj, &size) != 0 ? size : (t - b);
            double scale = Math.Sqrt(m.C * m.C + m.D * m.D);
            double effective = fontSize * (scale > 0 ? scale : 1);
            double height = t - b;
            // Alcune versioni danno già la dimensione con la scala: si sceglie quella coerente con l'altezza.
            if (effective > height * 3 && fontSize <= height * 3) effective = fontSize;
            if (effective <= 0 || effective > height * 3) effective = height / 1.1;

            var font = Pdfium.FPDFTextObj_GetFont(obj);
            string name = "";
            int weight = 400;
            if (font != IntPtr.Zero)
            {
                try
                {
                    nuint length = Pdfium.FPDFFont_GetBaseFontName(font, null, 0);
                    if (length > 1)
                    {
                        var nameBytes = new byte[(int)length];
                        fixed (byte* p = nameBytes) Pdfium.FPDFFont_GetBaseFontName(font, p, length);
                        name = Encoding.UTF8.GetString(nameBytes, 0, nameBytes.Length - 1);
                    }
                }
                catch (EntryPointNotFoundException) { /* PDFium più vecchio: niente nome */ }
                int w = Pdfium.FPDFFont_GetWeight(font);
                if (w > 0) weight = w;
            }
            var lower = name.ToLowerInvariant();
            var family = lower.Contains("courier") || lower.Contains("mono") || lower.Contains("consol") ? FontFamilyKind.Mono
                : lower.Contains("times") || lower.Contains("serif") && !lower.Contains("sans") || lower.Contains("georgia")
                  || lower.Contains("garamond") || lower.Contains("cambria") || lower.Contains("roman") ? FontFamilyKind.Serif
                : FontFamilyKind.Sans;
            bool bold = weight >= 600 || lower.Contains("bold") || lower.Contains("black") || lower.Contains("heavy") || lower.Contains("semibold");
            bool italic = lower.Contains("italic") || lower.Contains("oblique");

            uint cr, cg, cb, ca;
            var color = Pdfium.FPDFPageObj_GetFillColor(obj, &cr, &cg, &cb, &ca) != 0
                ? Argb.FromRgb((byte)cr, (byte)cg, (byte)cb)
                : Argb.Black;

            var box = geometry.ToView(l, b, r, t);
            // L'origine della matrice del testo è l'inizio della linea di base.
            double baseline = geometry.ToView(m.E, m.F).Y;
            if (baseline < box.Y || baseline > box.Bottom + effective * 0.1) baseline = box.Bottom - effective * 0.2;
            return new Piece(index, box, text, effective, family, bold, italic, color, baseline);
        }

        /// <summary>Mette insieme i pezzi di testo vicini sulla stessa riga, con la stessa dimensione.</summary>
        private static IReadOnlyList<PdfTextBlock> Group(int page, List<Piece> pieces)
        {
            var blocks = new List<PdfTextBlock>();
            var ordered = pieces.OrderBy(p => Math.Round(p.Baseline / Math.Max(1, p.Size * 0.5))).ThenBy(p => p.Box.X).ToList();
            var used = new bool[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                var line = new List<Piece> { ordered[i] };
                var last = ordered[i];
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    for (int j = 0; j < ordered.Count; j++)
                    {
                        if (used[j]) continue;
                        var p = ordered[j];
                        double ratio = p.Size / last.Size;
                        if (ratio < 0.77 || ratio > 1.3) continue;
                        if (Math.Abs(p.Baseline - last.Baseline) > 0.3 * last.Size) continue;
                        double gap = p.Box.X - last.Box.Right;
                        if (gap < -0.5 * last.Size || gap > 0.9 * last.Size) continue;
                        line.Add(p);
                        used[j] = true;
                        last = p;
                        grew = true;
                        break;
                    }
                }
                var text = new StringBuilder();
                Piece? previous = null;
                foreach (var p in line)
                {
                    if (previous != null)
                    {
                        double gap = p.Box.X - previous.Box.Right;
                        bool spaced = text.Length > 0 && char.IsWhiteSpace(text[^1]) || p.Text.Length > 0 && char.IsWhiteSpace(p.Text[0]);
                        if (gap > 0.18 * previous.Size && !spaced) text.Append(' ');
                    }
                    text.Append(p.Text);
                    previous = p;
                }
                double x1 = line.Min(p => p.Box.X), y1 = line.Min(p => p.Box.Y);
                double x2 = line.Max(p => p.Box.Right), y2 = line.Max(p => p.Box.Bottom);
                var first = line[0];
                blocks.Add(new PdfTextBlock(page, line.Select(p => p.Index).ToList(), new PageRect(x1, y1, x2 - x1, y2 - y1),
                    text.ToString().Trim(), first.Size, first.Family, first.Bold, first.Italic, first.Color, first.Baseline));
            }
            return blocks.OrderBy(b => b.Box.Y).ThenBy(b => b.Box.X).ToList();
        }

        /// <summary>Le firme digitali già nel PDF.</summary>
        public IReadOnlyList<ExistingSignature> Signatures()
        {
            var list = new List<ExistingSignature>();
            lock (Pdfium.Sync)
            {
                ObjectDisposedException.ThrowIf(_document == IntPtr.Zero, this);
                int count = Pdfium.FPDF_GetSignatureCount(_document);
                for (int i = 0; i < count; i++)
                {
                    var sig = Pdfium.FPDF_GetSignatureObject(_document, i);
                    if (sig == IntPtr.Zero) continue;
                    var contents = new byte[(int)Pdfium.FPDFSignatureObj_GetContents(sig, null, new(0)).Value];
                    fixed (byte* p = contents) Pdfium.FPDFSignatureObj_GetContents(sig, p, new((uint)contents.Length));
                    var range = new int[(int)Pdfium.FPDFSignatureObj_GetByteRange(sig, null, new(0)).Value];
                    fixed (int* p = range) Pdfium.FPDFSignatureObj_GetByteRange(sig, p, new((uint)range.Length));
                    list.Add(new ExistingSignature(AsciiOf(sig, &Pdfium.FPDFSignatureObj_GetSubFilter), AsciiOf(sig, &Pdfium.FPDFSignatureObj_GetTime), contents, range));
                }
            }
            return list;
        }

        private static string AsciiOf(IntPtr sig, delegate*<IntPtr, byte*, System.Runtime.InteropServices.CULong, System.Runtime.InteropServices.CULong> get)
        {
            int length = (int)get(sig, null, new(0)).Value;
            if (length <= 1) return "";
            var bytes = new byte[length];
            fixed (byte* p = bytes) get(sig, p, new((uint)length));
            return Encoding.ASCII.GetString(bytes, 0, length - 1);
        }

        /// <summary>
        /// Riscrive il PDF: toglie gli oggetti di testo indicati (indici di <see cref="TextBlocks"/> su questo
        /// documento) e incolla sopra ogni pagina la pagina corrispondente di <paramref name="overlay"/> (un PDF con
        /// una pagina per ogni pagina da modificare, grande quanto la sua CropBox e non girata).
        /// Restituisce il PDF risultante, in memoria.
        /// </summary>
        public byte[] Rewrite(IReadOnlyDictionary<int, IReadOnlyCollection<int>> removeObjects,
            byte[]? overlay, IReadOnlyList<(int Page, int OverlayPage)> overlayPages)
        {
            lock (Pdfium.Sync)
            {
                ObjectDisposedException.ThrowIf(_document == IntPtr.Zero, this);
                IntPtr overlayData = IntPtr.Zero, overlayDoc = IntPtr.Zero;
                try
                {
                    if (overlay != null && overlayPages.Count > 0)
                    {
                        overlayData = System.Runtime.InteropServices.Marshal.AllocHGlobal(overlay.Length);
                        System.Runtime.InteropServices.Marshal.Copy(overlay, 0, overlayData, overlay.Length);
                        overlayDoc = Pdfium.FPDF_LoadMemDocument64((void*)overlayData, (nuint)overlay.Length, null);
                        if (overlayDoc == IntPtr.Zero) throw new System.IO.IOException("Aggiunte non leggibili");
                    }
                    var pages = removeObjects.Keys.Concat(overlayPages.Select(o => o.Page)).Distinct();
                    foreach (var pageIndex in pages)
                    {
                        var page = LoadPage(pageIndex);
                        try
                        {
                            if (removeObjects.TryGetValue(pageIndex, out var objects))
                            {
                                var targets = objects.Distinct().OrderByDescending(i => i)
                                    .Select(i => Pdfium.FPDFPage_GetObject(page, i)).Where(o => o != IntPtr.Zero).ToList();
                                foreach (var obj in targets)
                                    if (Pdfium.FPDFPage_RemoveObject(page, obj) != 0) Pdfium.FPDFPageObj_Destroy(obj);
                            }
                            foreach (var (_, overlayPage) in overlayPages.Where(o => o.Page == pageIndex))
                            {
                                var geometry = GeometryOf(page);
                                var xobject = Pdfium.FPDF_NewXObjectFromPage(_document, overlayDoc, overlayPage);
                                if (xobject == IntPtr.Zero) throw new System.IO.IOException("Aggiunte non applicate");
                                try
                                {
                                    var form = Pdfium.FPDF_NewFormObjectFromXObject(xobject);
                                    if (form == IntPtr.Zero) throw new System.IO.IOException("Aggiunte non applicate");
                                    // La pagina delle aggiunte parte da (0,0): si sposta nell'angolo della CropBox.
                                    Pdfium.FPDFPageObj_Transform(form, 1, 0, 0, 1, geometry.Left, geometry.Bottom);
                                    Pdfium.FPDFPage_InsertObject(page, form);
                                }
                                finally
                                {
                                    Pdfium.FPDF_CloseXObject(xobject);
                                }
                            }
                            if (Pdfium.FPDFPage_GenerateContent(page) == 0) throw new System.IO.IOException($"Pagina {pageIndex + 1} non riscritta");
                        }
                        finally
                        {
                            Pdfium.FPDF_ClosePage(page);
                        }
                    }
                    using var output = new System.IO.MemoryStream();
                    if (!Pdfium.SaveAsCopy(_document, output, Pdfium.FPDF_NO_INCREMENTAL))
                        throw new System.IO.IOException($"Salvataggio non riuscito: {Name}");
                    return output.ToArray();
                }
                finally
                {
                    if (overlayDoc != IntPtr.Zero) Pdfium.FPDF_CloseDocument(overlayDoc);
                    if (overlayData != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(overlayData);
                }
            }
        }
    }
}
