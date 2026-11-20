using System;
using SkiaSharp;

namespace DockStudio.Imaging
{
    /// <summary>
    /// Effetti "da documentazione" applicati all'intera immagine. Ricevono e restituiscono immagini BGRA
    /// premoltiplicate (vedi <see cref="Pixels.Create"/>); dove serve, i bordi esterni diventano trasparenti.
    /// Ogni effetto restituisce una nuova immagine: chi chiama libera quella di partenza quando vuole.
    /// </summary>
    public static class ImageEffects
    {
        public static SKBitmap RoundCorners(SKBitmap src, double radius)
        {
            int w = src.Width, h = src.Height;
            float r = (float)Math.Min(radius, Math.Min(w, h) / 2.0);
            return Pixels.Render(w, h, canvas =>
            {
                canvas.ClipRoundRect(new SKRoundRect(new SKRect(0, 0, w, h), r, r), antialias: true);
                canvas.DrawBitmap(src, 0, 0);
            });
        }

        public static SKBitmap AddBorder(SKBitmap src, int thickness, SKColor color)
        {
            int w = src.Width + thickness * 2, h = src.Height + thickness * 2;
            return Pixels.Render(w, h, canvas =>
            {
                canvas.Clear(color);
                canvas.DrawBitmap(src, thickness, thickness);
            });
        }

        /// <summary>Ombra morbida in basso a destra; l'immagine viene allargata per contenerla.</summary>
        /// <param name="blur">Raggio della sfocatura in pixel.</param>
        /// <param name="depth">Distanza dell'ombra dall'immagine, in diagonale verso il basso a destra.</param>
        /// <param name="opacity">Opacità dell'ombra (0–1).</param>
        public static SKBitmap AddShadow(SKBitmap src, double blur = 18, double depth = 6, double opacity = 0.45)
        {
            int margin = (int)Math.Ceiling(blur + depth);
            int w = src.Width + margin * 2, h = src.Height + margin * 2;
            float offset = (float)(depth * Math.Sqrt(0.5)); // 45° verso il basso a destra
            float sigma = (float)(blur / 3);
            var shadowColor = SKColors.Black.WithAlpha((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255));
            return Pixels.Render(w, h, canvas =>
            {
                using var paint = new SKPaint
                {
                    ImageFilter = SKImageFilter.CreateDropShadow(offset, offset, sigma, sigma, shadowColor),
                };
                float at = (float)(margin - depth / 2);
                canvas.DrawBitmap(src, at, at, paint);
            });
        }

        /// <summary>Bordo inferiore "strappato" a zig-zag irregolare (sempre uguale per la stessa immagine).</summary>
        public static SKBitmap TornBottomEdge(SKBitmap src)
        {
            int w = src.Width, h = src.Height;
            double amplitude = Math.Clamp(h / 40.0, 5, 12);
            var rnd = new Random(w * 31 + h);

            using var path = new SKPath();
            path.MoveTo(0, 0);
            path.LineTo(w, 0);
            path.LineTo(w, (float)(h - amplitude * 2));
            double x = w;
            bool up = true;
            while (x > 0)
            {
                x -= 8 + rnd.NextDouble() * 14;
                double y = h - (up ? amplitude * (1.6 + rnd.NextDouble() * 0.4) : amplitude * rnd.NextDouble() * 0.5);
                path.LineTo((float)Math.Max(0, x), (float)y);
                up = !up;
            }
            path.LineTo(0, (float)(h - amplitude * 2));
            path.Close();

            return Pixels.Render(w, h, canvas =>
            {
                canvas.ClipPath(path, antialias: true);
                canvas.DrawBitmap(src, 0, 0);
            });
        }

        /// <summary>Testo semitrasparente in basso a destra, con contorno per leggerlo su ogni sfondo.</summary>
        public static SKBitmap AddWatermark(SKBitmap src, string text)
        {
            int w = src.Width, h = src.Height;
            if (string.IsNullOrWhiteSpace(text)) return src.Copy();
            // Dopo un'ombra o un bordo strappato l'immagine ha margini trasparenti:
            // il testo va dentro la parte opaca, non a cavallo del bordo.
            var content = Pixels.OpaqueBounds(src);
            float size = (float)Math.Clamp(Math.Min(content.Width, content.Height) / 16.0, 12, 64);

            using var typeface = Fonts.Ui(SKFontStyleWeight.SemiBold);
            using var fill = new SKPaint { Typeface = typeface, TextSize = size, IsAntialias = true, Color = SKColors.White };
            float textWidth = fill.MeasureText(text);
            var metrics = fill.FontMetrics;
            float lineHeight = fill.FontSpacing;
            float margin = size * 0.6f;
            float left = Math.Max(content.Left + margin, content.Right - textWidth - margin);
            float top = Math.Max(content.Top, content.Bottom - lineHeight - margin * 0.8f);
            using var glyphs = fill.GetTextPath(text, left, top - metrics.Ascent);

            return Pixels.Render(w, h, canvas =>
            {
                canvas.DrawBitmap(src, 0, 0);
                using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(0.55 * 255)) };
                canvas.SaveLayer(layer);
                using var outline = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = size / 8,
                    StrokeJoin = SKStrokeJoin.Round,
                    Color = new SKColor(0x20, 0x20, 0x20),
                };
                canvas.DrawPath(glyphs, outline);
                using var inside = new SKPaint { IsAntialias = true, Color = SKColors.White };
                canvas.DrawPath(glyphs, inside);
                canvas.Restore();
            });
        }
    }
}
