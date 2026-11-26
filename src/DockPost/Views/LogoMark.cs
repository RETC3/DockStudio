using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DockPost.Views
{
    /// <summary>
    /// Il simbolo di DockPost in vettoriale: quadrato arancio con una busta bianca. Stesso disegno di
    /// site/public/apps/dockpost/logo.svg e dell'icona dell'exe (scripts/make-dockpost-icon.ps1).
    /// </summary>
    public sealed class LogoMark : Control
    {
        public LogoMark()
        {
            Width = 28;
            Height = 28;
        }

        public override void Render(DrawingContext context)
        {
            double s = System.Math.Min(Bounds.Width, Bounds.Height) / 32.0;
            var accent = new SolidColorBrush(AppInfo.Accent);
            context.DrawRectangle(accent, null, new Rect(1 * s, 1 * s, 30 * s, 30 * s), 7 * s, 7 * s);
            context.DrawRectangle(Brushes.White, null, new Rect(6 * s, 9 * s, 20 * s, 14 * s), 2 * s, 2 * s);
            var flap = new StreamGeometry();
            using (var g = flap.Open())
            {
                g.BeginFigure(new Point(7.5 * s, 10.5 * s), false);
                g.LineTo(new Point(16 * s, 17 * s));
                g.LineTo(new Point(24.5 * s, 10.5 * s));
                g.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(accent, 1.8 * s, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), flap);
        }
    }
}
