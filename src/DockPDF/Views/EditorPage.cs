using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DockPDF.Engine;

namespace DockPDF.Views
{
    /// <summary>Gli strumenti dell'editor.</summary>
    public enum EditTool
    {
        Select,
        EditText,
        Text,
        Highlight,
        Whiteout,
        Rectangle,
        Ellipse,
        Line,
        Arrow,
        Pen,
        Image,
        Signature,
        DigitalSignature,
    }

    /// <summary>Disegno delle modifiche con Avalonia, uguale (a meno dei dettagli dei caratteri) a quello nel PDF.</summary>
    internal static class EditDrawing
    {
        public static FontFamily Family(FontFamilyKind kind) => kind switch
        {
            FontFamilyKind.Serif => new FontFamily("Times New Roman, Liberation Serif, serif"),
            FontFamilyKind.Mono => new FontFamily("Courier New, Liberation Mono, monospace"),
            FontFamilyKind.Script => new FontFamily("Segoe Script, Ink Free, Brush Script MT, cursive"),
            _ => new FontFamily("Arial, Liberation Sans, sans-serif"),
        };

        public static Typeface Typeface(FontFamilyKind kind, bool bold, bool italic) =>
            new(Family(kind), italic ? FontStyle.Italic : FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal);

        public static Color ToColor(Argb c) => Color.FromArgb(c.A, c.R, c.G, c.B);

        public static IBrush Brush(Argb c) => new SolidColorBrush(ToColor(c));

        public static FormattedText Text(string text, FontFamilyKind family, double size, Argb color, bool bold, bool italic, double z, double? width)
        {
            var ft = new FormattedText(text.Length == 0 ? " " : text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Typeface(family, bold, italic), Math.Max(1, size * z), Brush(color))
            {
                LineHeight = size * 1.2 * z,
            };
            if (width is { } w && w > 0) ft.MaxTextWidth = w * z;
            return ft;
        }

        private static readonly ConditionalWeakTable<byte[], Bitmap> Images = new();

        public static Bitmap? Image(byte[] data)
        {
            if (Images.TryGetValue(data, out var bitmap)) return bitmap;
            try
            {
                bitmap = new Bitmap(new MemoryStream(data));
                Images.AddOrUpdate(data, bitmap);
                return bitmap;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static Rect Scale(PageRect r, double z) => new(r.X * z, r.Y * z, r.Width * z, r.Height * z);

        /// <summary>Il riquadro che occupa una modifica (per selezionarla e spostarla), in punti.</summary>
        public static PageRect Bounds(EditItem item, double measuredTextHeight = 0)
        {
            switch (item)
            {
                case TextItem t: return t.Box with { Height = Math.Max(t.Size * 1.2, measuredTextHeight > 0 ? measuredTextHeight : t.Box.Height) };
                case TextReplacement r: return r.Block.Box;
                case ShapeItem s: return s.Box;
                case ImageItem i: return i.Box;
                case SignatureItem s: return s.Box;
                case LineItem l:
                {
                    var r = PageRect.FromCorners(l.From.X, l.From.Y, l.To.X, l.To.Y);
                    double pad = Math.Max(4, l.Width * 2);
                    return new PageRect(r.X - pad, r.Y - pad, r.Width + 2 * pad, r.Height + 2 * pad);
                }
                case InkItem ink:
                {
                    var points = ink.Strokes.SelectMany(s => s).ToList();
                    if (points.Count == 0) return default;
                    double pad = Math.Max(3, ink.Width);
                    double x1 = points.Min(p => p.X), y1 = points.Min(p => p.Y), x2 = points.Max(p => p.X), y2 = points.Max(p => p.Y);
                    return new PageRect(x1 - pad, y1 - pad, x2 - x1 + 2 * pad, y2 - y1 + 2 * pad);
                }
                default: return default;
            }
        }

        /// <summary>La stessa modifica spostata di (dx, dy) punti.</summary>
        public static EditItem Move(EditItem item, double dx, double dy)
        {
            PageRect M(PageRect r) => r with { X = r.X + dx, Y = r.Y + dy };
            PagePoint P(PagePoint p) => new(p.X + dx, p.Y + dy);
            return item switch
            {
                TextItem t => t with { Box = M(t.Box) },
                ShapeItem s => s with { Box = M(s.Box) },
                ImageItem i => i with { Box = M(i.Box) },
                SignatureItem s => s with { Box = M(s.Box) },
                LineItem l => l with { From = P(l.From), To = P(l.To) },
                InkItem ink => ink with { Strokes = ink.Strokes.Select(s => (IReadOnlyList<PagePoint>)s.Select(P).ToList()).ToList() },
                _ => item, // il testo esistente resta al suo posto
            };
        }

        /// <summary>La stessa modifica con un riquadro nuovo (solo quelle con un riquadro).</summary>
        public static EditItem Resize(EditItem item, PageRect box) => item switch
        {
            TextItem t => t with { Box = box },
            ShapeItem s => s with { Box = box },
            ImageItem i => i with { Box = box },
            SignatureItem s => s with { Box = box },
            _ => item,
        };

        public static bool CanResize(EditItem item) => item is TextItem or ShapeItem or ImageItem or SignatureItem;

        public static void Draw(DrawingContext dc, EditItem item, double z)
        {
            switch (item)
            {
                case TextItem t:
                    dc.DrawText(Text(t.Text, t.Family, t.Size, t.Color, t.Bold, t.Italic, z, t.Box.Width), new Point(t.Box.X * z, t.Box.Y * z));
                    break;
                case TextReplacement r:
                {
                    var b = r.Block;
                    // si copre il testo vecchio (nel PDF verrà tolto davvero) e si scrive quello nuovo sulla stessa base
                    dc.DrawRectangle(Brushes.White, null, Scale(new PageRect(b.Box.X - 1, b.Box.Y - 1, b.Box.Width + 2, b.Box.Height + 2), z));
                    var ft = Text(r.NewText, b.Family, b.Size, b.Color, b.Bold, b.Italic, z, null);
                    dc.DrawText(ft, new Point(b.Box.X * z, b.Baseline * z - ft.Baseline));
                    break;
                }
                case ShapeItem s:
                {
                    IBrush? fill = s.Fill.A > 0 ? Brush(s.Fill) : null;
                    IPen? pen = s.Stroke.A > 0 && s.StrokeWidth > 0 ? new Pen(Brush(s.Stroke), s.StrokeWidth * z, lineJoin: PenLineJoin.Round) : null;
                    var rect = Scale(s.Box, z);
                    if (s.Ellipse) dc.DrawEllipse(fill, pen, rect.Center, rect.Width / 2, rect.Height / 2);
                    else dc.DrawRectangle(fill, pen, rect);
                    break;
                }
                case LineItem l:
                {
                    var pen = new Pen(Brush(l.Color), l.Width * z, lineCap: PenLineCap.Round);
                    var a = new Point(l.From.X * z, l.From.Y * z);
                    var b = new Point(l.To.X * z, l.To.Y * z);
                    dc.DrawLine(pen, a, b);
                    if (l.Arrow)
                    {
                        var d = b - a;
                        double length = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                        if (length > 0.01)
                        {
                            double ux = d.X / length, uy = d.Y / length, head = Math.Max(6, l.Width * 4) * z;
                            var g = new StreamGeometry();
                            using (var ctx = g.Open())
                            {
                                ctx.BeginFigure(b, true);
                                ctx.LineTo(new Point(b.X - head * ux + head * 0.5 * uy, b.Y - head * uy - head * 0.5 * ux));
                                ctx.LineTo(new Point(b.X - head * ux - head * 0.5 * uy, b.Y - head * uy + head * 0.5 * ux));
                                ctx.EndFigure(true);
                            }
                            dc.DrawGeometry(Brush(l.Color), null, g);
                        }
                    }
                    break;
                }
                case InkItem ink:
                    DrawStrokes(dc, ink.Strokes.Select(s => s.Select(p => new Point(p.X * z, p.Y * z)).ToList()), Brush(ink.Color), ink.Width * z);
                    break;
                case ImageItem img:
                    if (Image(img.Data) is { } bitmap) DrawFitted(dc, bitmap, Scale(img.Box, z));
                    break;
                case SignatureItem sig:
                    DrawSignature(dc, sig.Signature, Scale(sig.Box, z));
                    break;
            }
        }

        public static void DrawStrokes(DrawingContext dc, IEnumerable<IReadOnlyList<Point>> strokes, IBrush brush, double width)
        {
            var pen = new Pen(brush, Math.Max(0.5, width), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            foreach (var stroke in strokes)
            {
                if (stroke.Count == 0) continue;
                if (stroke.Count == 1)
                {
                    dc.DrawEllipse(brush, null, stroke[0], width / 2, width / 2);
                    continue;
                }
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(stroke[0], false);
                    for (int i = 1; i < stroke.Count; i++) ctx.LineTo(stroke[i]);
                    ctx.EndFigure(false);
                }
                dc.DrawGeometry(null, pen, g);
            }
        }

        private static void DrawFitted(DrawingContext dc, Bitmap bitmap, Rect box)
        {
            double ratio = bitmap.Size.Width / Math.Max(1, bitmap.Size.Height);
            double w = box.Width, h = box.Width / ratio;
            if (h > box.Height) { h = box.Height; w = h * ratio; }
            dc.DrawImage(bitmap, new Rect(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h));
        }

        /// <summary>Firma dentro il riquadro, centrata, come nel PDF.</summary>
        public static void DrawSignature(DrawingContext dc, HandSignature signature, Rect box)
        {
            var brush = Brush(signature.Color);
            if (signature.Strokes is { Count: > 0 } strokes)
            {
                double ratio = Math.Max(0.1, signature.AspectRatio);
                double w = box.Width, h = box.Width / ratio;
                if (h > box.Height) { h = box.Height; w = h * ratio; }
                double x0 = box.X + (box.Width - w) / 2, y0 = box.Y + (box.Height - h) / 2;
                DrawStrokes(dc, strokes.Select(s => s.Select(p => new Point(x0 + p.X * w, y0 + p.Y * h)).ToList()), brush, Math.Max(0.6, signature.StrokeRatio * h));
            }
            else if (!string.IsNullOrWhiteSpace(signature.TypedName))
            {
                double size = box.Height * 0.7;
                var ft = new FormattedText(signature.TypedName, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    Typeface(FontFamilyKind.Script, false, false), Math.Max(1, size), brush);
                if (ft.Width > box.Width)
                {
                    size *= box.Width / ft.Width;
                    ft = new FormattedText(signature.TypedName, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        Typeface(FontFamilyKind.Script, false, false), Math.Max(1, size), brush);
                }
                dc.DrawText(ft, new Point(box.X + (box.Width - ft.Width) / 2, box.Y + (box.Height - ft.Height) / 2));
            }
            else if (signature.Image is { Length: > 0 } data && Image(data) is { } bitmap)
            {
                DrawFitted(dc, bitmap, box);
            }
        }
    }

    /// <summary>
    /// Una pagina nell'editor: l'immagine della pagina (disegnata quando entra in vista), il livello che disegna le
    /// modifiche e riceve mouse e penna, e i campi del modulo.
    /// </summary>
    public sealed class EditorPage : Panel
    {
        private readonly EditorWorkspace _editor;
        private readonly Image _image = new() { Stretch = Stretch.Fill };
        private readonly EditLayer _layer;
        private readonly Canvas _fields = new();
        private readonly Canvas _overlay = new();
        private double _renderedZoom;
        private CancellationTokenSource? _render;

        public EditorPage(EditorWorkspace editor, int index, PageGeometry geometry)
        {
            _editor = editor;
            Index = index;
            Geometry = geometry;
            Background = Brushes.White;
            Margin = new Thickness(0, 0, 0, 16);
            HorizontalAlignment = HorizontalAlignment.Center;
            _layer = new EditLayer(editor, this);
            Children.Add(_image);
            Children.Add(_layer);
            // Campi e testo in linea stanno su una pagina bianca: sempre con l'aspetto chiaro.
            Children.Add(new ThemeVariantScope { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light, Child = new Panel { Children = { _fields, _overlay } } });
            EffectiveViewportChanged += (_, e) =>
            {
                bool visible = e.EffectiveViewport.Width > 0 && e.EffectiveViewport.Height > 0
                               && e.EffectiveViewport.Bottom > -800 && e.EffectiveViewport.Top < Bounds.Height + 800;
                if (visible) EnsureRendered();
                else if (_image.Source != null && (e.EffectiveViewport.Top > Bounds.Height + 4000 || e.EffectiveViewport.Bottom < -4000)) Release();
            };
        }

        public int Index { get; }
        public PageGeometry Geometry { get; }
        public double Zoom => _editor.Zoom;
        internal EditLayer Layer => _layer;

        /// <summary>I controlli dei campi del modulo.</summary>
        internal Canvas FieldsCanvas => _fields;

        /// <summary>Canvas sopra tutto, per l'editor del testo in linea.</summary>
        internal Canvas Overlay => _overlay;

        public void ApplyZoom()
        {
            Width = Geometry.ViewWidth * Zoom;
            Height = Geometry.ViewHeight * Zoom;
            _layer.InvalidateVisual();
            _editor.LayoutFields(this, _fields);
            if (_image.Source != null && Math.Abs(_renderedZoom - Zoom) > 0.01) { _renderedZoom = 0; EnsureRendered(); }
        }

        private void EnsureRendered()
        {
            if (_renderedZoom > 0 && Math.Abs(_renderedZoom - Zoom) < 0.01) return;
            _render?.Cancel();
            var cancel = _render = new CancellationTokenSource();
            double zoom = Zoom;
            // Un po' più di definizione sugli schermi ad alta densità, senza esagerare con la memoria.
            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int width = (int)Math.Clamp(Geometry.ViewWidth * zoom * scaling, 50, 2600);
            _renderedZoom = zoom;
            _ = Task.Run(() =>
            {
                if (cancel.IsCancellationRequested) return null;
                using var skia = _editor.Document.Render(Index, width);
                return Thumbs.ToAvalonia(skia);
            }).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result != null && !cancel.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() => _image.Source = t.Result);
            });
        }

        /// <summary>Ridisegna la pagina (dopo il salvataggio, per esempio).</summary>
        public void Refresh()
        {
            _renderedZoom = 0;
            EnsureRendered();
        }

        private void Release()
        {
            _render?.Cancel();
            _image.Source = null;
            _renderedZoom = 0;
        }

        public void InvalidateItems() => _layer.InvalidateVisual();

        internal bool HasImage => _image.Source != null;
    }

    /// <summary>
    /// Il livello delle modifiche di una pagina: le disegna, mostra la selezione e traduce mouse e penna nelle
    /// azioni dello strumento scelto.
    /// </summary>
    public sealed class EditLayer : Control
    {
        private const double Handle = 9;
        private readonly EditorWorkspace _editor;
        private readonly EditorPage _page;

        // trascinamento in corso
        private Point? _start;
        private Point _current;
        private int _dragItem = -1;
        private bool _resizing;
        private EditItem? _original;
        private List<Point>? _ink;

        public EditLayer(EditorWorkspace editor, EditorPage page)
        {
            _editor = editor;
            _page = page;
            Focusable = false;
            ClipToBounds = true;
        }

        private double Z => _page.Zoom;

        private PagePoint ToPage(Point p) => new(p.X / Z, p.Y / Z);

        public override void Render(DrawingContext dc)
        {
            // trasparente ma "pieno": così riceve i clic su tutta la pagina
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            var items = _editor.Items;
            // Prima il testo corretto (sta "dentro" la pagina), poi le aggiunte sopra, come nel PDF salvato.
            foreach (bool replacements in new[] { true, false })
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Page == _page.Index && i != _editor.EditingIndex && (items[i] is TextReplacement) == replacements)
                        EditDrawing.Draw(dc, items[i], Z);

            // blocchi di testo modificabili
            if (_editor.ActiveTool == EditTool.EditText)
            {
                var dash = new Pen(new SolidColorBrush(Color.FromArgb(200, AppInfo.Accent.R, AppInfo.Accent.G, AppInfo.Accent.B)), 1, new DashStyle(new double[] { 3, 2 }, 0));
                var tint = new SolidColorBrush(Color.FromArgb(24, AppInfo.Accent.R, AppInfo.Accent.G, AppInfo.Accent.B));
                foreach (var block in _editor.BlocksOf(_page.Index))
                    dc.DrawRectangle(tint, dash, EditDrawing.Scale(block.Box, Z).Inflate(2));
            }

            // firma digitale da applicare al salvataggio
            if (_editor.DigitalBox is { } digital && _editor.DigitalPage == _page.Index)
            {
                var r = EditDrawing.Scale(digital, Z);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 27, 42, 107)), new Pen(new SolidColorBrush(Color.FromRgb(27, 42, 107)), 1.2, new DashStyle(new double[] { 4, 3 }, 0)), r);
                var ft = new FormattedText(Text.T("editor.digitalBox"), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), Math.Clamp(r.Height / 5, 8, 13), new SolidColorBrush(Color.FromRgb(27, 42, 107)))
                { MaxTextWidth = Math.Max(10, r.Width - 8) };
                dc.DrawText(ft, new Point(r.X + 4, r.Y + 4));
            }

            // selezione
            if (_editor.SelectedIndex is int sel && sel >= 0 && sel < items.Count && items[sel].Page == _page.Index && sel != _editor.EditingIndex)
            {
                var r = EditDrawing.Scale(EditDrawing.Bounds(items[sel], MeasureTextHeight(items[sel])), Z).Inflate(3);
                var accent = new SolidColorBrush(AppInfo.Accent);
                dc.DrawRectangle(null, new Pen(accent, 1.2, new DashStyle(new double[] { 4, 2 }, 0)), r);
                if (EditDrawing.CanResize(items[sel]))
                    dc.DrawRectangle(Brushes.White, new Pen(accent, 1.5), new Rect(r.Right - Handle / 2, r.Bottom - Handle / 2, Handle, Handle));
            }

            // disegno in corso
            if (_start is { } s)
            {
                var accent = new SolidColorBrush(AppInfo.Accent);
                var style = _editor.Style;
                switch (_editor.ActiveTool)
                {
                    case EditTool.Highlight:
                        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(128, 255, 225, 77)), null, new Rect(s, _current).Normalize());
                        break;
                    case EditTool.Whiteout:
                        dc.DrawRectangle(Brushes.White, new Pen(Brushes.LightGray, 1), new Rect(s, _current).Normalize());
                        break;
                    case EditTool.Rectangle:
                    case EditTool.Image:
                    case EditTool.Signature:
                    case EditTool.DigitalSignature:
                        dc.DrawRectangle(null, new Pen(accent, 1.2, new DashStyle(new double[] { 4, 2 }, 0)), new Rect(s, _current).Normalize());
                        break;
                    case EditTool.Ellipse:
                    {
                        var r = new Rect(s, _current).Normalize();
                        dc.DrawEllipse(null, new Pen(EditDrawing.Brush(style.Color), style.StrokeWidth * Z), r.Center, r.Width / 2, r.Height / 2);
                        break;
                    }
                    case EditTool.Line:
                    case EditTool.Arrow:
                        dc.DrawLine(new Pen(EditDrawing.Brush(style.Color), style.StrokeWidth * Z, lineCap: PenLineCap.Round), s, _current);
                        break;
                    case EditTool.Pen when _ink != null:
                        EditDrawing.DrawStrokes(dc, new[] { (IReadOnlyList<Point>)_ink }, EditDrawing.Brush(style.Color), style.StrokeWidth * Z);
                        break;
                }
            }
        }

        /// <summary>Altezza del testo a capo (in punti) per la selezione.</summary>
        private double MeasureTextHeight(EditItem item) => item is TextItem t
            ? EditDrawing.Text(t.Text, t.Family, t.Size, t.Color, t.Bold, t.Italic, 1, t.Box.Width).Height
            : 0;

        private int HitItem(Point p)
        {
            var items = _editor.Items;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].Page != _page.Index) continue;
                var r = EditDrawing.Scale(EditDrawing.Bounds(items[i], MeasureTextHeight(items[i])), Z).Inflate(3);
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        private bool OnHandle(Point p)
        {
            if (_editor.SelectedIndex is not int sel || sel < 0 || sel >= _editor.Items.Count) return false;
            var item = _editor.Items[sel];
            if (item.Page != _page.Index || !EditDrawing.CanResize(item)) return false;
            var r = EditDrawing.Scale(EditDrawing.Bounds(item, MeasureTextHeight(item)), Z).Inflate(3);
            return new Rect(r.Right - Handle, r.Bottom - Handle, Handle * 2, Handle * 2).Contains(p);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;
            var p = point.Position;
            _editor.CommitInlineEditor();
            _editor.Focus();
            e.Pointer.Capture(this);
            _start = p;
            _current = p;

            switch (_editor.ActiveTool)
            {
                case EditTool.Select:
                {
                    if (OnHandle(p))
                    {
                        _dragItem = _editor.SelectedIndex!.Value;
                        _resizing = true;
                        _original = _editor.Items[_dragItem];
                        return;
                    }
                    int hit = HitItem(p);
                    _editor.Select(hit >= 0 ? hit : null);
                    if (hit >= 0)
                    {
                        if (e.ClickCount >= 2 && _editor.Items[hit] is TextItem or TextReplacement)
                        {
                            _start = null;
                            _editor.EditText(hit, _page);
                            return;
                        }
                        _dragItem = hit;
                        _original = _editor.Items[hit];
                    }
                    else
                    {
                        _start = null;
                    }
                    break;
                }
                case EditTool.EditText:
                {
                    _start = null;
                    // una sostituzione già fatta si riapre; altrimenti si sceglie il blocco sotto il mouse
                    int existing = HitItem(p);
                    if (existing >= 0 && _editor.Items[existing] is TextReplacement)
                    {
                        _editor.EditText(existing, _page);
                        return;
                    }
                    var pp = ToPage(p);
                    var block = _editor.BlocksOf(_page.Index).FirstOrDefault(b =>
                        new PageRect(b.Box.X - 2, b.Box.Y - 2, b.Box.Width + 4, b.Box.Height + 4).Contains(pp.X, pp.Y));
                    if (block != null) _editor.StartReplacement(block, _page);
                    break;
                }
                case EditTool.Text:
                {
                    _start = null;
                    var pp = ToPage(p);
                    var style = _editor.Style;
                    var item = new TextItem(_page.Index, new PageRect(pp.X, pp.Y - style.FontSize * 0.2, 240, style.FontSize * 1.2), "",
                        style.Family, style.FontSize, style.Color, style.Bold, style.Italic);
                    int index = _editor.Add(item);
                    _editor.EditText(index, _page);
                    break;
                }
                case EditTool.Pen:
                    _ink = new List<Point> { p };
                    break;
            }
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var p = e.GetPosition(this);
            if (_start == null)
            {
                Cursor = _editor.ActiveTool == EditTool.Select
                    ? OnHandle(p) ? new Cursor(StandardCursorType.BottomRightCorner)
                    : HitItem(p) >= 0 ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default
                    : _editor.ActiveTool == EditTool.Text || _editor.ActiveTool == EditTool.EditText ? new Cursor(StandardCursorType.Ibeam)
                    : new Cursor(StandardCursorType.Cross);
                return;
            }
            _current = new Point(Math.Clamp(p.X, 0, Bounds.Width), Math.Clamp(p.Y, 0, Bounds.Height));
            if (_dragItem >= 0 && _original != null)
            {
                double dx = (_current.X - _start.Value.X) / Z, dy = (_current.Y - _start.Value.Y) / Z;
                EditItem changed;
                if (_resizing)
                {
                    var b = EditDrawing.Bounds(_original);
                    changed = EditDrawing.Resize(_original, b with { Width = Math.Max(8, b.Width + dx), Height = Math.Max(6, b.Height + dy) });
                }
                else
                {
                    changed = EditDrawing.Move(_original, dx, dy);
                }
                _editor.ReplaceLive(_dragItem, changed);
            }
            else if (_ink != null)
            {
                _ink.Add(_current);
            }
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_start is not { } start)
            {
                e.Pointer.Capture(null);
                return;
            }
            var end = _current;
            _start = null;
            e.Pointer.Capture(null);
            var a = ToPage(start);
            var b = ToPage(end);
            var box = PageRect.FromCorners(a.X, a.Y, b.X, b.Y);
            bool tiny = box.Width < 3 && box.Height < 3;
            var style = _editor.Style;
            int page = _page.Index;

            if (_dragItem >= 0 && _original != null)
            {
                if (!ReferenceEquals(_editor.Items[_dragItem], _original)) _editor.CommitLive(_dragItem, _original);
                _dragItem = -1;
                _resizing = false;
                _original = null;
                InvalidateVisual();
                return;
            }

            switch (_editor.ActiveTool)
            {
                case EditTool.Highlight when !tiny:
                    _editor.Add(ShapeItem.Highlight(page, box, style.Highlight));
                    break;
                case EditTool.Whiteout when !tiny:
                    _editor.Add(ShapeItem.Whiteout(page, box));
                    break;
                case EditTool.Rectangle when !tiny:
                    _editor.Add(new ShapeItem(page, box, style.Color, Argb.Transparent, style.StrokeWidth));
                    break;
                case EditTool.Ellipse when !tiny:
                    _editor.Add(new ShapeItem(page, box, style.Color, Argb.Transparent, style.StrokeWidth, Ellipse: true));
                    break;
                case EditTool.Line when !tiny:
                case EditTool.Arrow when !tiny:
                    _editor.Add(new LineItem(page, a, b, style.Color, style.StrokeWidth, _editor.ActiveTool == EditTool.Arrow));
                    break;
                case EditTool.Pen when _ink is { Count: > 0 }:
                    _editor.Add(new InkItem(page, new[] { (IReadOnlyList<PagePoint>)Simplify(_ink).Select(ToPage).ToList() }, style.Color, style.StrokeWidth), select: false);
                    break;
                case EditTool.Image:
                case EditTool.Signature:
                case EditTool.DigitalSignature:
                    _editor.PlaceAt(page, tiny ? null : box, a);
                    break;
            }
            _ink = null;
            InvalidateVisual();
        }

        /// <summary>Toglie i punti troppo vicini (il mouse ne manda moltissimi).</summary>
        private static List<Point> Simplify(List<Point> points)
        {
            var result = new List<Point> { points[0] };
            foreach (var p in points.Skip(1))
            {
                var last = result[^1];
                if (Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) >= 1.5) result.Add(p);
            }
            if (result.Count == 1 && points.Count > 1) result.Add(points[^1]);
            return result;
        }
    }
}
