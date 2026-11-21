using System;
using System.Collections.Generic;

namespace DockSpace.Engine
{
    /// <summary>Rettangolo della mappa (coordinate in pixel).</summary>
    public readonly record struct TreemapRect(double X, double Y, double Width, double Height)
    {
        public double Area => Width * Height;
        public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
    }

    /// <summary>Un riquadro della mappa: l'elemento, la sua posizione e la profondità (0 = contenuto diretto).</summary>
    public readonly record struct TreemapTile(FsNode Node, TreemapRect Rect, int Depth);

    /// <summary>
    /// Mappa a riquadri: ogni file e cartella è un rettangolo con area proporzionale alla dimensione; le
    /// cartelle contengono i loro riquadri. Disposizione "squarified" (Bruls, Huizing, van Wijk), che tiene
    /// i rettangoli il più possibile vicini a quadrati.
    /// </summary>
    public static class Treemap
    {
        /// <summary>
        /// Riquadri di <paramref name="folder"/> dentro <paramref name="bounds"/>. Le cartelle abbastanza grandi
        /// si dividono a loro volta, con un bordo e (se c'è posto) una fascia per il nome alta
        /// <paramref name="headerHeight"/>. Si ferma sotto <paramref name="minSide"/> pixel o a
        /// <paramref name="maxTiles"/> riquadri. L'ordine è dal contenitore al contenuto, per disegnare in sequenza.
        /// </summary>
        public static List<TreemapTile> Layout(FsNode folder, TreemapRect bounds, int maxDepth = 6,
            double minSide = 3, double headerHeight = 16, double padding = 2, int maxTiles = 60000)
        {
            var tiles = new List<TreemapTile>();
            var queue = new Queue<(FsNode Folder, TreemapRect Rect, int Depth)>();
            queue.Enqueue((folder, bounds, 0));
            while (queue.Count > 0 && tiles.Count < maxTiles)
            {
                var (dir, rect, depth) = queue.Dequeue();
                foreach (var (node, r) in Squarify(dir.Children, rect))
                {
                    if (r.Width < minSide || r.Height < minSide) continue;
                    tiles.Add(new TreemapTile(node, r, depth));
                    if (!node.IsDirectory || depth + 1 >= maxDepth || node.Children.Count == 0) continue;
                    var inner = Inner(r, headerHeight, padding);
                    if (inner.Width >= minSide * 2 && inner.Height >= minSide * 2)
                        queue.Enqueue((node, inner, depth + 1));
                }
            }
            return tiles;
        }

        /// <summary>Spazio interno di una cartella: tolti il bordo e, se c'è posto, la fascia del nome.</summary>
        public static TreemapRect Inner(TreemapRect r, double headerHeight, double padding)
        {
            double header = r.Height > headerHeight * 3 && r.Width > 60 ? headerHeight : 0;
            return new TreemapRect(r.X + padding, r.Y + padding + header,
                Math.Max(0, r.Width - padding * 2), Math.Max(0, r.Height - padding * 2 - header));
        }

        /// <summary>Dispone gli elementi (già ordinati dal più grande) nel rettangolo, saltando quelli vuoti.</summary>
        public static IEnumerable<(FsNode Node, TreemapRect Rect)> Squarify(IReadOnlyList<FsNode> items, TreemapRect bounds)
        {
            long total = 0;
            int n = 0;
            foreach (var i in items)
            {
                if (i.Size <= 0) break; // ordinati: da qui in poi sono tutti vuoti
                total += i.Size;
                n++;
            }
            if (total <= 0 || bounds.Width <= 0 || bounds.Height <= 0) yield break;

            double scale = bounds.Area / total;
            var rect = bounds;
            int start = 0;
            while (start < n)
            {
                double side = Math.Min(rect.Width, rect.Height);
                // Si aggiungono elementi alla riga finché il rapporto peggiore tra i lati migliora.
                int end = start;
                double rowArea = 0, worst = double.MaxValue;
                while (end < n)
                {
                    double area = items[end].Size * scale;
                    double nextArea = rowArea + area;
                    double nextWorst = Worst(items, start, end, nextArea, side, scale);
                    if (end > start && nextWorst > worst) break;
                    rowArea = nextArea;
                    worst = nextWorst;
                    end++;
                }

                // Riga lungo il lato corto.
                bool horizontal = rect.Width >= rect.Height; // riga in verticale a sinistra se il rettangolo è largo
                double thickness = side > 0 ? rowArea / side : 0;
                double offset = 0;
                for (int i = start; i < end; i++)
                {
                    double area = items[i].Size * scale;
                    double length = thickness > 0 ? area / thickness : 0;
                    var r = horizontal
                        ? new TreemapRect(rect.X, rect.Y + offset, thickness, length)
                        : new TreemapRect(rect.X + offset, rect.Y, length, thickness);
                    offset += length;
                    yield return (items[i], r);
                }
                rect = horizontal
                    ? new TreemapRect(rect.X + thickness, rect.Y, Math.Max(0, rect.Width - thickness), rect.Height)
                    : new TreemapRect(rect.X, rect.Y + thickness, rect.Width, Math.Max(0, rect.Height - thickness));
                start = end;
            }
        }

        private static double Worst(IReadOnlyList<FsNode> items, int start, int end, double rowArea, double side, double scale)
        {
            // Elementi ordinati: il più grande è il primo, il più piccolo l'ultimo aggiunto.
            double max = items[start].Size * scale;
            double min = items[end].Size * scale;
            double s2 = side * side, r2 = rowArea * rowArea;
            return Math.Max(s2 * max / r2, r2 / (s2 * min));
        }

        /// <summary>Il riquadro più interno sotto il punto (x, y), o null.</summary>
        public static TreemapTile? HitTest(IReadOnlyList<TreemapTile> tiles, double x, double y)
        {
            // I riquadri interni vengono dopo i contenitori: si cerca dall'ultimo.
            for (int i = tiles.Count - 1; i >= 0; i--)
                if (tiles[i].Rect.Contains(x, y)) return tiles[i];
            return null;
        }
    }
}
