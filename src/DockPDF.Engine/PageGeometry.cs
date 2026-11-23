using System;

namespace DockPDF.Engine
{
    /// <summary>
    /// Come si passa dalle coordinate del PDF (punti, origine in basso a sinistra, pagina non girata) a quelle
    /// "come si vede la pagina" (origine in alto a sinistra, pagina girata della sua rotazione) e viceversa.
    /// </summary>
    public readonly record struct PageGeometry(double Left, double Bottom, double Right, double Top, int Rotation)
    {
        /// <summary>Larghezza e altezza della pagina non girata (riquadro visibile, CropBox).</summary>
        public double Width => Right - Left;
        public double Height => Top - Bottom;

        /// <summary>Larghezza e altezza come la si vede.</summary>
        public double ViewWidth => Rotation % 180 == 0 ? Width : Height;
        public double ViewHeight => Rotation % 180 == 0 ? Height : Width;

        /// <summary>Dal PDF alla vista.</summary>
        public PagePoint ToView(double x, double y)
        {
            // prima in alto a sinistra, non girata
            double ux = x - Left, uy = Top - y;
            return Rotation switch
            {
                90 => new PagePoint(Height - uy, ux),
                180 => new PagePoint(Width - ux, Height - uy),
                270 => new PagePoint(uy, Width - ux),
                _ => new PagePoint(ux, uy),
            };
        }

        /// <summary>Dalla vista al PDF.</summary>
        public PagePoint FromView(double vx, double vy)
        {
            var (ux, uy) = Rotation switch
            {
                90 => (vy, Height - vx),
                180 => (Width - vx, Height - vy),
                270 => (Width - vy, vx),
                _ => (vx, vy),
            };
            return new PagePoint(Left + ux, Top - uy);
        }

        /// <summary>Un rettangolo del PDF nella vista.</summary>
        public PageRect ToView(double left, double bottom, double right, double top)
        {
            var a = ToView(left, bottom);
            var b = ToView(right, top);
            return PageRect.FromCorners(a.X, a.Y, b.X, b.Y);
        }

        /// <summary>
        /// La matrice (a, b, c, d, e, f) che porta dalla vista allo spazio non girato con origine in alto a
        /// sinistra (x' = a·x + c·y + e, y' = b·x + d·y + f), per disegnare "dritti" sulle pagine girate.
        /// </summary>
        public (double A, double B, double C, double D, double E, double F) ViewToUnrotatedTopDown() => Rotation switch
        {
            90 => (0, -1, 1, 0, 0, Height),
            180 => (-1, 0, 0, -1, Width, Height),
            270 => (0, 1, -1, 0, Width, 0),
            _ => (1, 0, 0, 1, 0, 0),
        };
    }
}
