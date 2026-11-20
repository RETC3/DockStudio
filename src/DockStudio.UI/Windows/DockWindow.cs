using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DockStudio.UI.Windows
{
    /// <summary>
    /// Base delle finestre comuni: stile "dock", dimensioni comode anche su schermi bassi, piccoli aiuti per
    /// costruire il contenuto in codice (righe di testo, pulsanti, schede).
    /// </summary>
    public abstract class DockWindow : Window
    {
        protected DockWindow(DockBranding branding)
        {
            Branding = branding;
            Classes.Add("dock");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            Width = 480;
            MaxHeight = 720;
        }

        public DockBranding Branding { get; }

        protected static string T(string key) => UiText.Strings[key];

        protected static string F(string key, params object?[] args) => UiText.Strings.Format(key, args);

        protected static TextBlock Text(string text, params string[] classes)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            foreach (var c in classes) block.Classes.Add(c);
            return block;
        }

        protected static Button ButtonFor(string text, Action onClick, bool accent = false)
        {
            var button = new Button { Content = text };
            if (accent) button.Classes.Add("accent");
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>Fila di pulsanti allineata a destra.</summary>
        protected static StackPanel Buttons(params Control[] buttons)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var b in buttons) panel.Children.Add(b);
            return panel;
        }

        /// <summary>Tabella a due colonne (etichetta, valore).</summary>
        protected static Grid Facts(params (string Label, string Value)[] rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 6, ColumnSpacing = 16 };
            for (int i = 0; i < rows.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = Text(rows[i].Label, "muted");
                var value = Text(rows[i].Value);
                Grid.SetRow(label, i);
                Grid.SetRow(value, i);
                Grid.SetColumn(value, 1);
                grid.Children.Add(label);
                grid.Children.Add(value);
            }
            return grid;
        }

        protected static Border Card(Control content) => new Border { Child = content, Classes = { "card" } };

        /// <summary>Contenuto della finestra: colonna con margini.</summary>
        protected void SetBody(params Control[] children)
        {
            var panel = new StackPanel { Spacing = 14, Margin = new Thickness(22) };
            foreach (var c in children) panel.Children.Add(c);
            Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        }

        protected void OpenUrl(string url)
        {
            try { _ = Launcher.LaunchUriAsync(new Uri(url)); }
            catch { /* nessun browser: pazienza */ }
        }
    }
}
