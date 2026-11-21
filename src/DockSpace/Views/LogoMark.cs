using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DockSpace.Views
{
    /// <summary>
    /// Il simbolo di DockSpace disegnato in vettoriale (nitido a ogni dimensione): quadrato verde acqua con una
    /// piccola mappa a riquadri. Stesso disegno di site/public/apps/dockspace/logo.svg e dell'icona dell'exe.
    /// </summary>
    public sealed class LogoMark : Control
    {
        private static readonly IBrush Soft = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));

        public LogoMark()
        {
            Width = 28;
            Height = 28;
        }

        public override void Render(DrawingContext context)
        {
            double s = System.Math.Min(Bounds.Width, Bounds.Height) / 32.0;
            Rect R(double x, double y, double w, double h) => new(x * s, y * s, w * s, h * s);
            context.DrawRectangle(new SolidColorBrush(AppInfo.Accent), null, R(1, 1, 30, 30), 7 * s, 7 * s);
            context.DrawRectangle(Brushes.White, null, R(7, 7, 10, 18), 1.6 * s, 1.6 * s);
            context.DrawRectangle(Brushes.White, null, R(19, 7, 6, 10), 1.6 * s, 1.6 * s);
            context.DrawRectangle(Soft, null, R(19, 19, 6, 6), 1.6 * s, 1.6 * s);
        }
    }
}
