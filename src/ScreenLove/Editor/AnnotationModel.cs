using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using DockStudio.Imaging.Annotations;
using ScreenLove.Services;
using SkiaSharp;
using M = DockStudio.Imaging.Annotations;

namespace ScreenLove.Editor
{
    /// <summary>
    /// Passaggio dalle annotazioni dell'editor WPF al modello del modulo immagini (DockStudio.Imaging), che
    /// disegna l'immagine finale con Skia. L'editor WPF continua a disegnare a schermo con WPF finché non
    /// passerà ad Avalonia; i controlli confrontano i due disegni.
    /// </summary>
    public static class AnnotationModel
    {
        public static M.Annotation ToModel(Annotation a)
        {
            M.Annotation m = a switch
            {
                RectangleAnnotation r => new M.RectangleAnnotation { Rect = ToSkia(r.Rect) },
                EllipseAnnotation e => new M.EllipseAnnotation { Rect = ToSkia(e.Rect) },
                HighlightAnnotation h => new M.HighlightAnnotation { Rect = ToSkia(h.Rect) },
                SpotlightAnnotation s => new M.SpotlightAnnotation { Rect = ToSkia(s.Rect) },
                BlurAnnotation b => new M.BlurAnnotation { Rect = ToSkia(b.Rect) },
                ArrowAnnotation ar => new M.ArrowAnnotation { Start = ToSkia(ar.Start), End = ToSkia(ar.End) },
                TextAnnotation t => new M.TextAnnotation { Position = ToSkia(t.Position), Text = t.Text, FontSize = (float)t.FontSize },
                StepAnnotation st => new M.StepAnnotation { Center = ToSkia(st.Center), Number = st.Number },
                _ => throw new NotSupportedException($"Annotazione non gestita: {a.GetType().Name}"),
            };
            m.Color = SkiaBridge.ToSkia(a.Color);
            m.Thickness = (float)a.Thickness;
            return m;
        }

        /// <summary>Immagine finale disegnata dal modulo immagini: base + annotazioni.</summary>
        public static System.Windows.Media.Imaging.BitmapSource Flatten(EditorDocument doc)
        {
            using var image = new AnnotatedImage(SkiaBridge.ToSkia(doc.Base));
            image.Items = doc.Items.Select(ToModel).ToList();
            using var flat = image.Flatten();
            return SkiaBridge.ToWpf(flat);
        }

        public static SKPoint ToSkia(Point p) => new((float)p.X, (float)p.Y);
        public static SKRect ToSkia(Rect r) => r.IsEmpty ? SKRect.Empty : new SKRect((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
    }
}
