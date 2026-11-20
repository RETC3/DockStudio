using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenLove.Services;
using Fx = DockStudio.Imaging.ImageEffects;

namespace ScreenLove.Editor
{
    /// <summary>
    /// Effetti "da documentazione" sull'intera immagine. Il disegno lo fa il modulo condiviso
    /// DockStudio.Imaging (SkiaSharp); qui si passa solo da e verso le immagini WPF dell'editor.
    /// </summary>
    public static class ImageEffects
    {
        public static BitmapSource RoundCorners(BitmapSource src, double radius) =>
            SkiaBridge.Apply(src, img => Fx.RoundCorners(img, radius));

        public static BitmapSource AddBorder(BitmapSource src, int thickness, Color color) =>
            SkiaBridge.Apply(src, img => Fx.AddBorder(img, thickness, SkiaBridge.ToSkia(color)));

        /// <summary>Ombra morbida in basso a destra; l'immagine viene allargata per contenerla.</summary>
        public static BitmapSource AddShadow(BitmapSource src, double blur = 18, double depth = 6, double opacity = 0.45) =>
            SkiaBridge.Apply(src, img => Fx.AddShadow(img, blur, depth, opacity));

        /// <summary>Bordo inferiore "strappato" a zig-zag irregolare (sempre uguale per la stessa immagine).</summary>
        public static BitmapSource TornBottomEdge(BitmapSource src) =>
            SkiaBridge.Apply(src, Fx.TornBottomEdge);

        /// <summary>Testo semitrasparente in basso a destra, con contorno per leggerlo su ogni sfondo.</summary>
        public static BitmapSource AddWatermark(BitmapSource src, string text) =>
            string.IsNullOrWhiteSpace(text) ? src : SkiaBridge.Apply(src, img => Fx.AddWatermark(img, text));
    }
}
