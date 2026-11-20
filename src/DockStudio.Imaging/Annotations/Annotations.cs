using System;
using System.Collections.Generic;
using SkiaSharp;

namespace DockStudio.Imaging.Annotations
{
    /// <summary>
    /// Elemento vettoriale disegnato sopra un'immagine (frecce, forme, testo...). Coordinate in pixel
    /// dell'immagine. Il modello non dipende dall'interfaccia: l'editor (WPF oggi, Avalonia domani) gestisce
    /// mouse e tastiera e chiama Drag, Move, DragHandle e HitTest; il disegno lo fa Skia.
    /// </summary>
    public abstract class Annotation
    {
        public SKColor Color { get; set; } = SKColors.Red;
        public float Thickness { get; set; } = 4;

        /// <summary>Ordine di disegno: sfocature sotto, evidenziatori e spotlight in mezzo, il resto sopra.</summary>
        public virtual int Layer => 2;

        public abstract SKRect Bounds { get; }
        public abstract void Render(SKCanvas canvas, AnnotatedImage image);
        public abstract void Move(SKPoint delta);

        /// <summary>Aggiorna la forma mentre viene trascinata (start = punto iniziale).</summary>
        public virtual void Drag(SKPoint start, SKPoint current, bool constrain) { }

        public virtual bool IsValid => Bounds.Width >= 3 || Bounds.Height >= 3;

        public virtual bool HitTest(SKPoint p)
        {
            var b = Bounds;
            b.Inflate(Thickness / 2 + 4, Thickness / 2 + 4);
            return b.Contains(p);
        }

        public Annotation Clone() => (Annotation)MemberwiseClone();

        /// <summary>Maniglie per ridimensionare l'elemento selezionato (coordinate immagine). Vuoto = nessuna.</summary>
        public virtual SKPoint[] Handles => Array.Empty<SKPoint>();

        /// <summary>
        /// Trascina la maniglia <paramref name="index"/> fino a <paramref name="p"/>, partendo dalla forma
        /// che l'elemento aveva a inizio trascinamento (<paramref name="original"/>).
        /// </summary>
        public virtual void DragHandle(int index, SKPoint p, bool constrain, Annotation original) { }

        protected static SKPaint Stroke(SKColor color, float thickness) => new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = thickness,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
        };

        protected static SKPaint Fill(SKColor color) => new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };

        /// <summary>Rettangolo tra due punti in qualsiasi ordine; con <paramref name="square"/> lati uguali.</summary>
        public static SKRect RectFrom(SKPoint a, SKPoint b, bool square = false)
        {
            if (square)
            {
                float size = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
                b = new SKPoint(a.X + Math.Sign(b.X - a.X) * size, a.Y + Math.Sign(b.Y - a.Y) * size);
            }
            return new SKRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        /// <summary>Luminosità percepita (0–1) di un colore.</summary>
        protected static double Luminance(SKColor c) => (0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue) / 255;
    }

    public abstract class BoxAnnotation : Annotation
    {
        public SKRect Rect { get; set; }
        public override SKRect Bounds => Rect;

        public override void Move(SKPoint delta)
        {
            var r = Rect;
            r.Offset(delta);
            Rect = r;
        }

        public override void Drag(SKPoint start, SKPoint current, bool constrain) => Rect = RectFrom(start, current, constrain);

        /// <summary>Angoli e metà dei lati, in senso orario dall'angolo in alto a sinistra.</summary>
        public override SKPoint[] Handles
        {
            get
            {
                var r = Rect;
                float cx = r.MidX, cy = r.MidY;
                return new[]
                {
                    new SKPoint(r.Left, r.Top), new SKPoint(cx, r.Top), new SKPoint(r.Right, r.Top), new SKPoint(r.Right, cy),
                    new SKPoint(r.Right, r.Bottom), new SKPoint(cx, r.Bottom), new SKPoint(r.Left, r.Bottom), new SKPoint(r.Left, cy),
                };
            }
        }

        public override void DragHandle(int index, SKPoint p, bool constrain, Annotation original)
        {
            var o = ((BoxAnnotation)original).Rect;
            float l = o.Left, t = o.Top, r = o.Right, b = o.Bottom;
            if (index is 0 or 6 or 7) l = p.X;
            if (index is 2 or 3 or 4) r = p.X;
            if (index is 0 or 1 or 2) t = p.Y;
            if (index is 4 or 5 or 6) b = p.Y;

            // Maiusc su un angolo: mantiene le proporzioni di partenza.
            if (constrain && index is 0 or 2 or 4 or 6 && o.Width > 0 && o.Height > 0)
            {
                float ax = index is 0 or 6 ? o.Right : o.Left, ay = index is 0 or 2 ? o.Bottom : o.Top;
                float scale = Math.Max(Math.Abs(p.X - ax) / o.Width, Math.Abs(p.Y - ay) / o.Height);
                float nx = ax + Math.Sign(p.X - ax == 0 ? 1 : p.X - ax) * o.Width * scale;
                float ny = ay + Math.Sign(p.Y - ay == 0 ? 1 : p.Y - ay) * o.Height * scale;
                Rect = RectFrom(new SKPoint(ax, ay), new SKPoint(nx, ny));
                return;
            }
            Rect = RectFrom(new SKPoint(l, t), new SKPoint(r, b)); // si può anche "ribaltare" oltre il lato opposto
        }
    }

    public sealed class RectangleAnnotation : BoxAnnotation
    {
        public override void Render(SKCanvas canvas, AnnotatedImage image)
        {
            using var pen = Stroke(Color, Thickness);
            canvas.DrawRoundRect(Rect, 2, 2, pen);
        }

        public override bool HitTest(SKPoint p)
        {
            if (!base.HitTest(p)) return false;
            var inner = Rect;
            float d = Thickness / 2 + 6;
            if (inner.Width <= 2 * d || inner.Height <= 2 * d) return true;
            inner.Inflate(-d, -d);
            return !inner.Contains(p); // si seleziona cliccando sul bordo
        }
    }

    public sealed class EllipseAnnotation : BoxAnnotation
    {
        public override void Render(SKCanvas canvas, AnnotatedImage image)
        {
            using var pen = Stroke(Color, Thickness);
            canvas.DrawOval(Rect, pen);
        }
    }

    public sealed class HighlightAnnotation : BoxAnnotation
    {
        public override int Layer => 1;

        public override void Render(SKCanvas canvas, AnnotatedImage image)
        {
            using var paint = new SKPaint { Style = SKPaintStyle.Fill, Color = Color.WithAlpha(0x66) };
            canvas.DrawRect(Rect, paint);
        }
    }

    /// <summary>Oscura tutto tranne il rettangolo, per mettere in risalto una zona.</summary>
    public sealed class SpotlightAnnotation : BoxAnnotation
    {
        public override int Layer => 1;

        public override void Render(SKCanvas canvas, AnnotatedImage image) => RenderGroup(canvas, image, new[] { this });

        /// <summary>Un'unica ombra su tutta l'immagine, con un buco per ogni spotlight.</summary>
        public static void RenderGroup(SKCanvas canvas, AnnotatedImage image, IReadOnlyCollection<SpotlightAnnotation> spotlights)
        {
            if (spotlights.Count == 0) return;
            canvas.Save();
            foreach (var s in spotlights)
                canvas.ClipRoundRect(new SKRoundRect(s.Rect, 6, 6), SKClipOperation.Difference, antialias: true);
            using var shade = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0, 0, 0, 0x99) };
            canvas.DrawRect(new SKRect(0, 0, image.Width, image.Height), shade);
            canvas.Restore();
        }

        public override bool HitTest(SKPoint p) => Rect.Contains(p);
    }

    /// <summary>Pixela una zona per nascondere dati sensibili.</summary>
    public sealed class BlurAnnotation : BoxAnnotation
    {
        public override int Layer => 0;

        /// <summary>Lato dei blocchi della pixelatura, in pixel.</summary>
        public int BlockSize => Math.Max(6, (int)(Thickness * 2.5));

        public override void Render(SKCanvas canvas, AnnotatedImage image)
        {
            var area = SKRectI.Round(Rect);
            using var pixelated = image.Pixelate(area, BlockSize, out var placed);
            if (pixelated != null)
                canvas.DrawBitmap(pixelated, placed.Left, placed.Top);
        }

        public override bool HitTest(SKPoint p) => Rect.Contains(p);
    }

    public sealed class ArrowAnnotation : Annotation
    {
        public SKPoint Start { get; set; }
        public SKPoint End { get; set; }

        public override SKRect Bounds => RectFrom(Start, End);
        public override bool IsValid => (End - Start).Length >= 6;

        public override void Move(SKPoint delta)
        {
            Start += delta;
            End += delta;
        }

        public override void Drag(SKPoint start, SKPoint current, bool constrain)
        {
            Start = start;
            End = constrain ? Snap45(start, current) : current;
        }

        /// <summary>Blocca la direzione da <paramref name="from"/> a multipli di 45°.</summary>
        public static SKPoint Snap45(SKPoint from, SKPoint to)
        {
            var v = to - from;
            double angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
            float len = v.Length;
            return new SKPoint(from.X + (float)Math.Cos(angle) * len, from.Y + (float)Math.Sin(angle) * len);
        }

        /// <summary>Coda e punta della freccia.</summary>
        public override SKPoint[] Handles => new[] { Start, End };

        public override void DragHandle(int index, SKPoint p, bool constrain, Annotation original)
        {
            var o = (ArrowAnnotation)original;
            if (index == 0)
                Start = constrain ? Snap45(o.End, p) : p;
            else
                End = constrain ? Snap45(o.Start, p) : p;
        }

        public override void Render(SKCanvas canvas, AnnotatedImage image)
        {
            var v = End - Start;
            float len = v.Length;
            if (len < 1) return;
            var dir = new SKPoint(v.X / len, v.Y / len);
            var normal = new SKPoint(-dir.Y, dir.X);

            float head = Math.Min(Math.Max(12, Thickness * 4.2f), len * 0.7f);
            float halfWidth = head * 0.55f;
            var basePoint = End - Scale(dir, head);

            using (var pen = Stroke(Color, Thickness))
                canvas.DrawLine(Start, basePoint + Scale(dir, head * 0.3f), pen);

            using var path = new SKPath();
            path.MoveTo(End);
            path.LineTo(basePoint + Scale(normal, halfWidth));
            path.LineTo(basePoint + Scale(dir, head * 0.15f));
            path.LineTo(basePoint - Scale(normal, halfWidth));
            path.Close();
            using (var fill = Fill(Color))
                canvas.DrawPath(path, fill);
            using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, StrokeJoin = SKStrokeJoin.Round, Color = Color })
                canvas.DrawPath(path, edge);
        }

        public override bool HitTest(SKPoint p)
        {
            var v = End - Start;
            float lenSq = v.LengthSquared;
            float t = lenSq == 0 ? 0 : Math.Clamp(((p.X - Start.X) * v.X + (p.Y - Start.Y) * v.Y) / lenSq, 0, 1);
            var closest = Start + Scale(v, t);
            return (p - closest).Length <= Thickness / 2 + 6;
        }

        private static SKPoint Scale(SKPoint v, float k) => new(v.X * k, v.Y * k);
    }

    public sealed class TextAnnotation : Annotation
    {
        public SKPoint Position { get; set; }
        public string Text { get; set; } = "";
        public float FontSize { get; set; } = 24;

        private string[] Lines => Text.Replace("\r\n", "\n").Split('\n');

        public override SKRect Bounds
        {
            get
            {
                using var typeface = Fonts.Ui(SKFontStyleWeight.SemiBold);
                using var font = Fonts.Create(typeface, FontSize);
                float width = 0;
                foreach (var line in Lines) width = Math.Max(width, font.MeasureText(line));
                return SKRect.Create(Position.X, Position.Y, Math.Max(width, 4), font.Spacing * Lines.Length);
            }
        }

        public override bool IsValid => !string.IsNullOrWhiteSpace(Text);

        public override void Move(SKPoint delta) => Position += delta;

        public override void Render(SKCanvas canvas, AnnotatedImage image)
        {
            using var typeface = Fonts.Ui(SKFontStyleWeight.SemiBold);
            using var font = Fonts.Create(typeface, FontSize);
            using var glyphs = new SKPath();
            float baseline = Position.Y - font.Metrics.Ascent;
            foreach (var line in Lines)
            {
                using var linePath = font.GetTextPath(line, new SKPoint(Position.X, baseline));
                glyphs.AddPath(linePath);
                baseline += font.Spacing;
            }

            // contorno di contrasto per leggere il testo su qualunque sfondo
            var outline = Luminance(Color) < 0.6 ? SKColors.White : new SKColor(0x22, 0x22, 0x22);
            using (var pen = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(2, FontSize / 7), StrokeJoin = SKStrokeJoin.Round, Color = outline })
                canvas.DrawPath(glyphs, pen);
            using (var fill = Fill(Color))
                canvas.DrawPath(glyphs, fill);
        }
    }

    /// <summary>Cerchio numerato per indicare una sequenza di passaggi.</summary>
    public sealed class StepAnnotation : Annotation
    {
        public SKPoint Center { get; set; }
        public int Number { get; set; } = 1;
        public float Radius => 11 + Thickness * 1.8f;

        public override SKRect Bounds => SKRect.Create(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
        public override bool IsValid => true;

        public override void Move(SKPoint delta) => Center += delta;

        public override void Drag(SKPoint start, SKPoint current, bool constrain) => Center = current;

        public override void Render(SKCanvas canvas, AnnotatedImage image)
        {
            using (var fill = Fill(Color))
                canvas.DrawCircle(Center, Radius, fill);
            using (var ring = Stroke(SKColors.White, Math.Max(2, Radius / 7)))
                canvas.DrawCircle(Center, Radius, ring);

            using var typeface = Fonts.Ui(SKFontStyleWeight.Bold);
            using var font = Fonts.Create(typeface, Radius * 1.15f);
            using var paint = new SKPaint { IsAntialias = true, Color = Luminance(Color) > 0.7 ? SKColors.Black : SKColors.White };
            string text = Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            float width = font.MeasureText(text);
            float top = Center.Y - font.Spacing / 2;
            canvas.DrawText(text, Center.X - width / 2, top - font.Metrics.Ascent, SKTextAlign.Left, font, paint);
        }

        public override bool HitTest(SKPoint p) => (p - Center).Length <= Radius + 3;
    }
}
