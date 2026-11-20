using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace DockStudio.Imaging.Annotations
{
    /// <summary>
    /// Un'immagine di base (nel formato del modulo, vedi <see cref="Pixels"/>) con le annotazioni sopra:
    /// il documento dell'editor. Ritaglio, pixelatura e immagine finale non dipendono dall'interfaccia.
    /// </summary>
    public sealed class AnnotatedImage : IDisposable
    {
        /// <param name="baseImage">Immagine di base: diventa di proprietà del documento.</param>
        public AnnotatedImage(SKBitmap baseImage) => SetBase(baseImage);

        public SKBitmap Base { get; private set; } = null!;
        public int Width => Base.Width;
        public int Height => Base.Height;

        /// <summary>Cambia a ogni modifica dell'immagine di base: chi tiene copie in cache le rifà.</summary>
        public int Version { get; private set; }

        public List<Annotation> Items { get; set; } = new();

        /// <summary>Sostituisce l'immagine di base (per esempio dopo un ritaglio o un effetto).</summary>
        public void SetBase(SKBitmap baseImage)
        {
            if (baseImage.ColorType != SKColorType.Bgra8888 || baseImage.AlphaType != SKAlphaType.Premul)
            {
                var normalized = Pixels.Normalize(baseImage);
                baseImage.Dispose();
                baseImage = normalized;
            }
            var old = Base;
            Base = baseImage;
            if (old != null && !ReferenceEquals(old, baseImage)) old.Dispose();
            Version++;
        }

        public SKRectI ClipToImage(SKRectI r)
        {
            int x1 = Math.Max(0, r.Left), y1 = Math.Max(0, r.Top);
            int x2 = Math.Min(Width, r.Right), y2 = Math.Min(Height, r.Bottom);
            return x2 > x1 && y2 > y1 ? new SKRectI(x1, y1, x2, y2) : SKRectI.Empty;
        }

        /// <summary>Copia di una zona dell'immagine di base (vuota se la zona è fuori dall'immagine).</summary>
        public SKBitmap Crop(SKRectI area)
        {
            var r = ClipToImage(area);
            if (r.IsEmpty) throw new ArgumentException("La zona da ritagliare è fuori dall'immagine.", nameof(area));
            return Pixels.Render(r.Width, r.Height, canvas => canvas.DrawBitmap(Base, -r.Left, -r.Top));
        }

        /// <summary>
        /// Versione a blocchi (pixelata) di una zona dell'immagine di base; <paramref name="placed"/> è la zona
        /// effettiva dentro l'immagine. Null se la zona è tutta fuori.
        /// </summary>
        public SKBitmap? Pixelate(SKRectI area, int block, out SKRectI placed)
        {
            placed = ClipToImage(area);
            var r = placed;
            if (r.IsEmpty) return null;
            block = Math.Max(1, block);

            var result = Pixels.Create(r.Width, r.Height);
            using var basePixels = Base.PeekPixels();
            var src = basePixels.GetPixelSpan();
            int srcStride = basePixels.RowBytes;
            int dstStride = result.RowBytes;
            var dst = new byte[dstStride * r.Height];

            for (int by = 0; by < r.Height; by += block)
            {
                for (int bx = 0; bx < r.Width; bx += block)
                {
                    int bw = Math.Min(block, r.Width - bx), bh = Math.Min(block, r.Height - by);
                    long sb = 0, sg = 0, sr = 0;
                    for (int y = 0; y < bh; y++)
                    {
                        int i = (r.Top + by + y) * srcStride + (r.Left + bx) * 4;
                        for (int x = 0; x < bw; x++, i += 4)
                        {
                            sb += src[i];
                            sg += src[i + 1];
                            sr += src[i + 2];
                        }
                    }
                    int n = bw * bh;
                    byte b = (byte)(sb / n), g = (byte)(sg / n), rr = (byte)(sr / n);
                    for (int y = 0; y < bh; y++)
                    {
                        int o = (by + y) * dstStride + bx * 4;
                        for (int x = 0; x < bw; x++, o += 4)
                        {
                            dst[o] = b;
                            dst[o + 1] = g;
                            dst[o + 2] = rr;
                            dst[o + 3] = 255;
                        }
                    }
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(dst, 0, result.GetPixels(), dst.Length);
            result.NotifyPixelsChanged();
            return result;
        }

        /// <summary>
        /// Disegna le annotazioni (più <paramref name="extra"/>, quella che si sta disegnando). Gli spotlight
        /// formano un'unica ombra con un "buco" per ciascuno: con più spotlight l'immagine non diventa più scura.
        /// </summary>
        public void RenderAnnotations(SKCanvas canvas, Annotation? extra = null)
        {
            var all = extra != null ? Items.Append(extra).ToList() : Items;
            var spotlights = all.OfType<SpotlightAnnotation>().ToList();
            bool spotlightsDone = false;
            foreach (var a in all.OrderBy(a => a.Layer))
            {
                if (a is SpotlightAnnotation)
                {
                    if (!spotlightsDone)
                        SpotlightAnnotation.RenderGroup(canvas, this, spotlights);
                    spotlightsDone = true;
                    continue;
                }
                a.Render(canvas, this);
            }
        }

        /// <summary>Immagine finale: base + annotazioni, alla risoluzione originale.</summary>
        public SKBitmap Flatten() =>
            Pixels.Render(Width, Height, canvas =>
            {
                canvas.DrawBitmap(Base, 0, 0);
                RenderAnnotations(canvas);
            });

        public void Dispose() => Base?.Dispose();
    }
}
