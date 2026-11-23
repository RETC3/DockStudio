using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;

namespace DockPDF.Views
{
    /// <summary>
    /// Il cerchio degli strumenti: una corona divisa in spicchi, uno per strumento, con al centro DockPDF.
    /// Si passa con il mouse per illuminare uno spicchio e si fa clic per aprirlo. Esc, un clic fuori o
    /// un clic sull'icona dell'area di notifica lo chiudono.
    /// </summary>
    public sealed class RadialMenu : Control
    {
        public const double Outer = 168;
        public const double Inner = 62;
        private const double Gap = 2.2; // gradi di vuoto tra due spicchi

        private readonly IReadOnlyList<PdfTool> _tools = Tools.All;
        private int _hover = -2; // -2 = niente, -1 = centro, 0.. = spicchio

        public event Action<PdfTool>? ToolChosen;
        public event Action? CenterChosen;

        public RadialMenu()
        {
            Width = Height = 2 * Outer + 24; // spazio per l'ombra
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        /// <summary>Lo spicchio sotto il mouse (o -1 per il centro), per i controlli automatici.</summary>
        public int Hover
        {
            get => _hover;
            set
            {
                if (_hover == value) return;
                _hover = value;
                InvalidateVisual();
            }
        }

        private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);

        private double SliceAngle => 360.0 / _tools.Count;

        /// <summary>Angolo di partenza dello spicchio: il primo è in alto, poi in senso orario.</summary>
        private double StartAngle(int index) => -90 - SliceAngle / 2 + index * SliceAngle;

        private int HitTest(Point p)
        {
            var v = p - Center;
            double r = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            if (r <= Inner - 4) return -1;
            if (r > Outer) return -2;
            double angle = Math.Atan2(v.Y, v.X) * 180 / Math.PI; // -180..180, 0 = destra
            double rel = ((angle - StartAngle(0)) % 360 + 360) % 360;
            return Math.Min(_tools.Count - 1, (int)(rel / SliceAngle));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            Hover = HitTest(e.GetPosition(this));
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            Hover = -2;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            int hit = HitTest(e.GetPosition(this));
            if (hit == -1) CenterChosen?.Invoke();
            else if (hit >= 0 && Tools.IsReady(_tools[hit])) ToolChosen?.Invoke(_tools[hit]);
        }

        /// <summary>Spicchio di corona tra due angoli (gradi).</summary>
        private Geometry Slice(double from, double to, double inner, double outer)
        {
            var c = Center;
            Point P(double r, double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                bool large = to - from > 180;
                ctx.BeginFigure(P(outer, from), true);
                ctx.ArcTo(P(outer, to), new Size(outer, outer), 0, large, SweepDirection.Clockwise);
                ctx.LineTo(P(inner, to));
                ctx.ArcTo(P(inner, from), new Size(inner, inner), 0, large, SweepDirection.CounterClockwise);
                ctx.EndFigure(true);
            }
            return g;
        }

        public override void Render(DrawingContext context)
        {
            var c = Center;
            var surface = Ui.Brush(this, "DockSurface", Brushes.White);
            var text = Ui.Brush(this, "DockText", Brushes.Black);
            var muted = Ui.Brush(this, "DockMuted", Brushes.Gray);
            var line = Ui.Brush(this, "DockLine", Brushes.LightGray);
            var accent = new SolidColorBrush(AppInfo.Accent);
            bool dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

            // Ombra morbida: cerchi sempre più trasparenti attorno alla corona.
            for (int i = 10; i >= 1; i--)
                context.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(dark ? 10 : 6), 0, 0, 0)), null, new Point(c.X, c.Y + 3), Outer + i, Outer + i);

            context.DrawEllipse(surface, new Pen(line, 1), c, Outer, Outer);

            var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
            for (int i = 0; i < _tools.Count; i++)
            {
                var tool = _tools[i];
                bool ready = Tools.IsReady(tool);
                bool hot = i == _hover && ready;
                double from = StartAngle(i) + Gap / 2, to = StartAngle(i + 1) - Gap / 2;
                var slice = Slice(from, to, Inner + 4, Outer - 6);
                context.DrawGeometry(hot ? accent : surface, hot ? null : new Pen(line, 1), slice);

                double mid = (from + to) / 2 * Math.PI / 180;
                double r = (Inner + Outer) / 2 + 2;
                var at = new Point(c.X + r * Math.Cos(mid), c.Y + r * Math.Sin(mid));
                IBrush ink = hot ? Brushes.White : ready ? text : muted;

                // simbolo sopra, nome sotto
                const double icon = 22;
                var pen = new Pen(hot ? Brushes.White : ready ? accent : muted, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                using (context.PushOpacity(ready ? 1 : 0.55))
                {
                    using (context.PushTransform(Matrix.CreateScale(icon / 24, icon / 24) * Matrix.CreateTranslation(at.X - icon / 2, at.Y - icon - 2)))
                        context.DrawGeometry(null, pen, Tools.Icon(tool));
                    var label = new FormattedText(Tools.ShortName(tool), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 11, ink)
                    {
                        TextAlignment = TextAlignment.Center,
                        MaxTextWidth = 74,
                    };
                    context.DrawText(label, new Point(at.X - 37, at.Y + 2));
                }
            }

            // Centro: il simbolo di DockPDF; con il mouse sopra diventa "apri DockPDF".
            bool centerHot = _hover == -1;
            context.DrawEllipse(centerHot ? accent : surface, new Pen(centerHot ? accent : line, 1), c, Inner - 4, Inner - 4);
            const double logo = 34;
            var origin = new Point(c.X - logo / 2, c.Y - logo / 2 - (centerHot ? 8 : 0));
            using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
                DrawLogo(context, logo, centerHot);
            if (centerHot)
            {
                var open = new FormattedText(Text.T("radial.open"), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 11, Brushes.White)
                {
                    TextAlignment = TextAlignment.Center,
                    MaxTextWidth = 90,
                };
                context.DrawText(open, new Point(c.X - 45, c.Y + 12));
            }
        }

        private static void DrawLogo(DrawingContext context, double size, bool inverted)
        {
            double s = size / 32.0;
            var accent = new SolidColorBrush(AppInfo.Accent);
            IBrush back = inverted ? Brushes.White : accent;
            IBrush page = inverted ? accent : Brushes.White;
            context.DrawRectangle(back, null, new Rect(s, s, 30 * s, 30 * s), 7 * s, 7 * s);
            using (context.PushTransform(Matrix.CreateScale(s, s)))
                context.DrawGeometry(page, null, Geometry.Parse("M9,6 L18.5,6 L23,10.5 L23,26 L9,26 Z"));
            foreach (var (x, y, w) in new[] { (12.0, 14.5, 8.0), (12.0, 18.0, 8.0), (12.0, 21.5, 5.0) })
                context.DrawRectangle(back, null, new Rect(x * s, y * s, w * s, 1.6 * s));
        }
    }

    /// <summary>Finestra trasparente, senza bordi e sempre in primo piano che contiene il cerchio degli strumenti.</summary>
    public sealed class RadialWindow : Window
    {
        public readonly RadialMenu Menu = new();
        private bool _closing;

        public event Action<PdfTool>? ToolChosen;
        public event Action? HomeChosen;

        public RadialWindow()
        {
            Title = "DockPDF";
            Icon = AppInfo.Icon;
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Content = Menu;

            Menu.ToolChosen += tool => { CloseOnce(); ToolChosen?.Invoke(tool); };
            Menu.CenterChosen += () => { CloseOnce(); HomeChosen?.Invoke(); };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseOnce(); };
            // Un clic altrove (la finestra perde il primo piano) lo chiude.
            Deactivated += (_, _) => CloseOnce();
        }

        private void CloseOnce()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        /// <summary>
        /// Mostra il cerchio centrato sul mouse (pixel fisici), tenendolo tutto dentro l'area di lavoro dello
        /// schermo: vicino all'area di notifica sale e si sposta a sinistra quanto serve.
        /// </summary>
        public void ShowNear((int X, int Y)? cursor)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            var screens = Screens;
            var point = cursor is { } c ? new PixelPoint(c.X, c.Y) : (PixelPoint?)null;
            var screen = point is { } p ? screens.ScreenFromPoint(p) : screens.Primary;
            screen ??= screens.Primary;
            if (screen != null)
            {
                double scale = screen.Scaling;
                int size = (int)Math.Ceiling(Menu.Width * scale);
                var area = screen.WorkingArea;
                var at = point ?? area.Center;
                int x = Math.Clamp(at.X - size / 2, area.X, area.Right - size);
                int y = Math.Clamp(at.Y - size / 2, area.Y, area.Bottom - size);
                Position = new PixelPoint(x, y);
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            Show();
            Activate();
        }
    }
}
