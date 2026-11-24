using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace DockPDF.Engine
{
    /// <summary>
    /// Applica le modifiche e salva: toglie il testo da sostituire, disegna le aggiunte, compila il modulo e, se
    /// richiesto, firma digitalmente (sempre per ultimo, perché la firma copre tutto il file).
    /// </summary>
    public static class PdfEditor
    {
        public static void Apply(string path, string? password, EditSet edits, string output, DigitalSignRequest? sign = null)
        {
            PdfFonts.Install();
            byte[] bytes = Load(path, password);
            bytes = Rewrite(bytes, edits);

            using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
            document.Info.Creator = string.IsNullOrEmpty(document.Info.Creator) ? "DockPDF" : document.Info.Creator;
            if (edits.Fields.Count > 0) PdfForms.Fill(document, edits.Fields);
            if (sign != null)
            {
                DigitalSigning.Prepare(document, sign, bytes);
                // La firma si calcola durante il salvataggio, sul file intero.
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
                return;
            }
            PdfSources.SaveSafely(document, output);
        }

        /// <summary>I byte del PDF senza protezione (serve la password se è protetto).</summary>
        private static byte[] Load(string path, string? password)
        {
            using var file = PdfFile.Open(path, password);
            if (!file.IsEncrypted) return File.ReadAllBytes(path);
            using var clear = new MemoryStream();
            file.SaveWithoutSecurity(clear);
            return clear.ToArray();
        }

        /// <summary>Testo tolto e aggiunte incollate, con PDFium; restituisce il PDF nuovo.</summary>
        private static byte[] Rewrite(byte[] bytes, EditSet edits)
        {
            var hidden = edits.Items.OfType<HiddenTextItem>().ToList();
            var drawn = edits.Items.Where(i => i is not HiddenTextItem && (i is not TextReplacement || ((TextReplacement)i).NewText.Length > 0)).ToList();
            var remove = edits.Items.OfType<TextReplacement>()
                .GroupBy(r => r.Page)
                .ToDictionary(g => g.Key, g => (IReadOnlyCollection<int>)g.SelectMany(r => r.Block.Objects).ToList());
            if (drawn.Count == 0 && remove.Count == 0 && hidden.Count == 0) return bytes;

            using var file = PdfFile.Open(bytes, "documento");
            var pages = drawn.Select(i => i.Page).Distinct().OrderBy(p => p).ToList();
            byte[]? overlay = null;
            var map = new List<(int, int)>();
            if (pages.Count > 0)
            {
                using var layer = new PdfDocument();
                foreach (var pageIndex in pages)
                {
                    var geometry = file.Geometry(pageIndex);
                    var page = layer.AddPage();
                    page.Width = XUnit.FromPoint(geometry.Width);
                    page.Height = XUnit.FromPoint(geometry.Height);
                    using (var gfx = XGraphics.FromPdfPage(page))
                    {
                        // Si disegna "come si vede la pagina": la matrice porta nello spazio della pagina non girata.
                        var (a, b, c, d, e, f) = geometry.ViewToUnrotatedTopDown();
                        gfx.MultiplyTransform(new XMatrix(a, b, c, d, e, f));
                        foreach (var item in drawn.Where(i => i.Page == pageIndex)) Draw(gfx, item);
                    }
                    map.Add((pageIndex, layer.PageCount - 1));
                }
                using var ms = new MemoryStream();
                layer.Save(ms, false);
                overlay = ms.ToArray();
            }
            return file.Rewrite(remove, overlay, map, hidden);
        }

        // ---- Disegno delle aggiunte -----------------------------------------------------------------------

        internal static XColor Color(Argb c) => XColor.FromArgb(c.A, c.R, c.G, c.B);

        internal static XFont Font(FontFamilyKind family, double size, bool bold = false, bool italic = false)
        {
            var style = (bold, italic) switch
            {
                (true, true) => XFontStyleEx.BoldItalic,
                (true, false) => XFontStyleEx.Bold,
                (false, true) => XFontStyleEx.Italic,
                _ => XFontStyleEx.Regular,
            };
            return new XFont(PdfFonts.FamilyName(family), size, style, new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        private static void Draw(XGraphics gfx, EditItem item)
        {
            switch (item)
            {
                case TextItem t:
                    DrawText(gfx, t.Text, Font(t.Family, t.Size, t.Bold, t.Italic), Color(t.Color), t.Box.X, t.Box.Y, t.Box.Width, t.Size);
                    break;
                case TextReplacement r:
                {
                    var block = r.Block;
                    var font = Font(block.Family, block.Size, block.Bold, block.Italic);
                    var brush = new XSolidBrush(Color(block.Color));
                    double y = block.Baseline;
                    foreach (var line in r.NewText.Replace("\r", "").Split('\n'))
                    {
                        gfx.DrawString(line, font, brush, block.Box.X, y, XStringFormats.BaseLineLeft);
                        y += block.Size * 1.2;
                    }
                    break;
                }
                case ShapeItem s:
                {
                    var rect = new XRect(s.Box.X, s.Box.Y, s.Box.Width, s.Box.Height);
                    XPen? pen = s.Stroke.A > 0 && s.StrokeWidth > 0 ? new XPen(Color(s.Stroke), s.StrokeWidth) { LineJoin = XLineJoin.Round } : null;
                    XBrush? brush = s.Fill.A > 0 ? new XSolidBrush(Color(s.Fill)) : null;
                    if (s.Ellipse)
                    {
                        if (pen != null && brush != null) gfx.DrawEllipse(pen, brush, rect);
                        else if (brush != null) gfx.DrawEllipse(brush, rect);
                        else if (pen != null) gfx.DrawEllipse(pen, rect);
                    }
                    else
                    {
                        if (pen != null && brush != null) gfx.DrawRectangle(pen, brush, rect);
                        else if (brush != null) gfx.DrawRectangle(brush, rect);
                        else if (pen != null) gfx.DrawRectangle(pen, rect);
                    }
                    break;
                }
                case LineItem l:
                    DrawLine(gfx, l);
                    break;
                case InkItem ink:
                    DrawStrokes(gfx, ink.Strokes, Color(ink.Color), ink.Width);
                    break;
                case ImageItem img:
                    DrawImage(gfx, img.Data, img.Box);
                    break;
                case SignatureItem sig:
                    DrawSignature(gfx, sig.Signature, sig.Box);
                    break;
            }
        }

        /// <summary>Testo che va a capo da solo nella larghezza indicata (e alle righe nuove).</summary>
        private static void DrawText(XGraphics gfx, string text, XFont font, XColor color, double x, double y, double width, double size)
        {
            var brush = new XSolidBrush(color);
            double lineHeight = size * 1.2;
            foreach (var line in WrapLines(gfx, text, font, width))
            {
                gfx.DrawString(line, font, brush, x, y, XStringFormats.TopLeft);
                y += lineHeight;
            }
        }

        internal static IEnumerable<string> WrapLines(XGraphics gfx, string text, XFont font, double width)
        {
            foreach (var paragraph in text.Replace("\r", "").Split('\n'))
            {
                if (width <= 0 || gfx.MeasureString(paragraph, font).Width <= width)
                {
                    yield return paragraph;
                    continue;
                }
                var line = "";
                foreach (var word in paragraph.Split(' '))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && gfx.MeasureString(candidate, font).Width > width)
                    {
                        yield return line;
                        line = word;
                    }
                    else
                    {
                        line = candidate;
                    }
                }
                yield return line;
            }
        }

        private static void DrawLine(XGraphics gfx, LineItem l)
        {
            var pen = new XPen(Color(l.Color), l.Width) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };
            gfx.DrawLine(pen, l.From.X, l.From.Y, l.To.X, l.To.Y);
            if (!l.Arrow) return;
            double dx = l.To.X - l.From.X, dy = l.To.Y - l.From.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-3) return;
            double ux = dx / length, uy = dy / length;
            double head = Math.Max(6, l.Width * 4);
            var p1 = new XPoint(l.To.X - head * ux + head * 0.5 * uy, l.To.Y - head * uy - head * 0.5 * ux);
            var p2 = new XPoint(l.To.X - head * ux - head * 0.5 * uy, l.To.Y - head * uy + head * 0.5 * ux);
            gfx.DrawPolygon(new XSolidBrush(Color(l.Color)), new[] { new XPoint(l.To.X, l.To.Y), p1, p2 }, XFillMode.Winding);
        }

        private static void DrawStrokes(XGraphics gfx, IEnumerable<IReadOnlyList<PagePoint>> strokes, XColor color, double width)
        {
            var pen = new XPen(color, width) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };
            foreach (var stroke in strokes)
            {
                if (stroke.Count == 0) continue;
                if (stroke.Count == 1)
                {
                    var p = stroke[0];
                    gfx.DrawEllipse(new XSolidBrush(color), p.X - width / 2, p.Y - width / 2, width, width);
                    continue;
                }
                gfx.DrawLines(pen, stroke.Select(p => new XPoint(p.X, p.Y)).ToArray());
            }
        }

        private static void DrawImage(XGraphics gfx, byte[] data, PageRect box)
        {
            var copy = new MemoryStream(data.Length);
            copy.Write(data);
            copy.Position = 0;
            using var image = XImage.FromStream(copy);
            // dentro il riquadro, senza deformare
            double ratio = image.PixelWidth / (double)Math.Max(1, image.PixelHeight);
            double w = box.Width, h = box.Width / ratio;
            if (h > box.Height) { h = box.Height; w = h * ratio; }
            gfx.DrawImage(image, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }

        /// <summary>Firma nel riquadro, centrata e senza deformarla.</summary>
        internal static void DrawSignature(XGraphics gfx, HandSignature signature, PageRect box)
        {
            var color = Color(signature.Color);
            if (signature.Strokes is { Count: > 0 } strokes)
            {
                double ratio = Math.Max(0.1, signature.AspectRatio);
                double w = box.Width, h = box.Width / ratio;
                if (h > box.Height) { h = box.Height; w = h * ratio; }
                double x0 = box.X + (box.Width - w) / 2, y0 = box.Y + (box.Height - h) / 2;
                var scaled = strokes.Select(s => (IReadOnlyList<PagePoint>)s.Select(p => new PagePoint(x0 + p.X * w, y0 + p.Y * h)).ToList());
                DrawStrokes(gfx, scaled, color, Math.Max(0.6, signature.StrokeRatio * h));
            }
            else if (!string.IsNullOrWhiteSpace(signature.TypedName))
            {
                // la dimensione più grande che entra nel riquadro
                double size = box.Height * 0.7;
                var font = Font(FontFamilyKind.Script, size);
                var measured = gfx.MeasureString(signature.TypedName, font);
                if (measured.Width > box.Width)
                {
                    size *= box.Width / measured.Width;
                    font = Font(FontFamilyKind.Script, size);
                }
                gfx.DrawString(signature.TypedName, font, new XSolidBrush(color),
                    new XRect(box.X, box.Y, box.Width, box.Height), XStringFormats.Center);
            }
            else if (signature.Image is { Length: > 0 } image)
            {
                DrawImage(gfx, image, box);
            }
        }
    }
}
