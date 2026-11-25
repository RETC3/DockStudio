using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DockStudio.Launcher.Views
{
    /// <summary>
    /// Il simbolo di DockStudio in vettoriale: la "D" a moduli del sito (site/public/logo.svg), bianca su un
    /// quadrato blu, come l'icona dell'exe.
    /// </summary>
    public sealed class LogoMark : Control
    {
        // site/public/logo.svg (64×64)
        private static readonly Geometry D = Geometry.Parse(
            "M9,8 L15,8 A3,3 0 0 1 18,11 L18,53 A3,3 0 0 1 15,56 L9,56 A3,3 0 0 1 6,53 L6,11 A3,3 0 0 1 9,8 Z " +
            "M22,8 L34,8 A22,22 0 0 1 55.2,24 L22,24 Z " +
            "M24,28 L55,28 A2,2 0 0 1 57,30 L57,34 A2,2 0 0 1 55,36 L24,36 A2,2 0 0 1 22,34 L22,30 A2,2 0 0 1 24,28 Z " +
            "M22,40 L55.2,40 A22,22 0 0 1 34,56 L22,56 Z");

        public LogoMark()
        {
            Width = 28;
            Height = 28;
        }

        public override void Render(DrawingContext context)
        {
            double s = Math.Min(Bounds.Width, Bounds.Height);
            // come l'icona: margine di 1/32 e angoli di 7/32
            context.DrawRectangle(new SolidColorBrush(AppInfo.Accent), null, new Rect(s / 32, s / 32, s * 30 / 32, s * 30 / 32), s * 7 / 32, s * 7 / 32);
            // la D occupa il 62% del quadrato, al centro
            double scale = s * 0.62 / 64, offset = s * 0.19;
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset, offset)))
                context.DrawGeometry(Brushes.White, null, D);
        }
    }

    /// <summary>
    /// L'icona di un'app: quella vera per le app già uscite (inclusa nel launcher), altrimenti il quadrato
    /// colorato con la lettera, come sul sito.
    /// </summary>
    public sealed class AppTile : Control
    {
        private readonly CatalogApp _app;
        private readonly Bitmap? _icon;
        private readonly FormattedText _letter;

        public AppTile(CatalogApp app, double size = 44)
        {
            _app = app;
            Width = size;
            Height = size;
            _icon = LoadIcon(app.Slug);
            _letter = new FormattedText(app.Letter, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), size * (app.Letter.Length > 1 ? 0.36 : 0.44), Brushes.White);
        }

        /// <summary>True se l'app ha la sua icona vera (non la lettera).</summary>
        public bool HasIcon => _icon != null;

        private static Bitmap? LoadIcon(string slug)
        {
            try
            {
                var uri = new Uri($"avares://DockStudio/Assets/Apps/{slug}.ico");
                return AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : null;
            }
            catch
            {
                return null;
            }
        }

        public override void Render(DrawingContext context)
        {
            double s = Math.Min(Bounds.Width, Bounds.Height);
            if (_icon != null)
            {
                context.DrawImage(_icon, new Rect(0, 0, s, s));
                return;
            }
            var color = Color.TryParse(_app.Color, out var c) ? c : AppInfo.Accent;
            // stesse proporzioni delle icone vere: margine di 1/32 e angoli di 7/32
            double m = s / 32;
            context.DrawRectangle(new SolidColorBrush(color), null, new Rect(m, m, s - 2 * m, s - 2 * m), s * 7 / 32, s * 7 / 32);
            context.DrawText(_letter, new Point((s - _letter.Width) / 2, (s - _letter.Height) / 2));
        }
    }
}
