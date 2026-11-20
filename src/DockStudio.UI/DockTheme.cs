using System;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using DockStudio.Core;

namespace DockStudio.UI
{
    /// <summary>Chi è l'app per l'interfaccia: identità (nome, sito) più colore e frase di presentazione.</summary>
    public sealed record DockBranding(AppIdentity App, Color Accent, string Tagline);

    /// <summary>Aspetto chiaro, scuro o come il sistema.</summary>
    public enum Appearance
    {
        System,
        Light,
        Dark,
    }

    /// <summary>Il tema comune delle app DockStudio (DockTheme.axaml) e il colore di accento di ogni app.</summary>
    public partial class DockTheme : Styles
    {
        public DockTheme() => AvaloniaXamlLoader.Load(this);

        /// <summary>Aggiunge il tema all'app con il suo colore di accento e l'aspetto scelto.</summary>
        public static void Apply(Application app, Color accent, Appearance appearance = Appearance.System)
        {
            app.Styles.Add(new DockTheme());
            SetAccent(app, accent);
            SetAppearance(app, appearance);
        }

        public static void SetAppearance(Application app, Appearance appearance) =>
            app.RequestedThemeVariant = appearance switch
            {
                Appearance.Light => ThemeVariant.Light,
                Appearance.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };

        /// <summary>Colore di accento e le sue varianti più chiare e più scure, come le vuole il tema Fluent.</summary>
        public static void SetAccent(Application app, Color accent)
        {
            app.Resources["SystemAccentColor"] = accent;
            for (int i = 1; i <= 3; i++)
            {
                app.Resources[$"SystemAccentColorLight{i}"] = Mix(accent, Colors.White, 0.2 * i);
                app.Resources[$"SystemAccentColorDark{i}"] = Mix(accent, Colors.Black, 0.2 * i);
            }
        }

        private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }
}
