using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static DockStudio.Launcher.Text;

namespace DockStudio.Launcher.Views
{
    /// <summary>
    /// La scheda di un'app: icona, nome, versione o stato, descrizione e i pulsanti che servono (Apri, Aggiorna,
    /// Installa, Scopri), con l'avanzamento mentre si scarica e si installa.
    /// </summary>
    public sealed class AppCard : Border
    {
        private readonly MainWindow _window;
        private readonly Library _library;
        private readonly CatalogApp _app;
        private readonly TextBlock _subtitle = new() { Classes = { "muted" }, FontSize = 12.5 };
        private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 6, IsVisible = false };
        private readonly TextBlock _phase = new() { Classes = { "muted" }, FontSize = 12.5, IsVisible = false };
        private readonly TextBlock _error = new() { Classes = { "error" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };

        /// <summary>Per gli stili è un Border: così vale lo stile comune Border.card del tema.</summary>
        protected override Type StyleKeyOverride => typeof(Border);

        public AppCard(MainWindow window, Library library, CatalogApp app)
        {
            _window = window;
            _library = library;
            _app = app;
            Classes.Add("card");
            Width = 316;
            MinHeight = 196;
            Margin = new Thickness(6);
            Padding = new Thickness(16);

            var name = new TextBlock { Text = app.Name, Classes = { "heading" } };
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new AppTile(app),
                    new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { name, _subtitle } },
                },
            };
            var description = new TextBlock
            {
                Text = app.Description,
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 3,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 13,
            };
            var bottom = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom, Children = { _progress, _phase, _error, _buttons } };
            var layout = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            header.Margin = new Thickness(0, 0, 0, 10);
            bottom.Margin = new Thickness(0, 12, 0, 0);
            layout.Children.Add(header);
            layout.Children.Add(bottom);
            layout.Children.Add(description);
            Child = layout;
            Update();
        }

        /// <summary>Aggiorna testi, avanzamento e pulsanti dallo stato del launcher.</summary>
        public void Update()
        {
            _library.Installed.TryGetValue(_app.Slug, out var installed);
            _library.Updates.TryGetValue(_app.Slug, out var update);
            _library.Jobs.TryGetValue(_app.Slug, out var job);

            Opacity = installed == null && !_app.IsAvailable ? 0.62 : 1;
            _subtitle.Text = installed != null ? F("card.version", installed.Version.ToString(3))
                : _app.IsAvailable ? T("card.notInstalled")
                : T(_app.Status == "sviluppo" ? "card.development" : "card.soon");
            if (installed != null && update != null && job == null)
                _subtitle.Text = F("card.updateAvailable", installed.Version.ToString(3), update.Version.ToString(3));

            bool running = job is { Running: true };
            _progress.IsVisible = running;
            _phase.IsVisible = running;
            if (running)
            {
                _progress.IsIndeterminate = job!.Phase != InstallPhase.Downloading;
                _progress.Value = job.Value;
                _phase.Text = job.Phase switch
                {
                    InstallPhase.Downloading => F("job.downloading", job.Value),
                    InstallPhase.Installing => T("job.installing"),
                    _ => T("job.checking"),
                };
            }
            _error.IsVisible = job is { Running: false };
            _error.Text = job?.Error ?? "";

            _buttons.Children.Clear();
            if (running) return;
            if (job is { Running: false })
            {
                _buttons.Children.Add(Action(T("card.retry"), () => _ = _library.InstallAsync(_app.Slug)));
                _buttons.Children.Add(Action(T("card.dismiss"), () => _library.Dismiss(_app.Slug)));
                return;
            }
            if (installed != null)
            {
                _buttons.Children.Add(Action(T("card.open"), () =>
                {
                    Installs.Launch(installed);
                    _window.SetStatus(F("status.opening", _app.Name));
                }, accent: update == null));
                if (update != null)
                {
                    // con l'aggiornamento i pulsanti sono già due: la pagina dell'app resta fuori (non ci starebbe)
                    _buttons.Children.Add(Action(F("card.update", update.Version.ToString(3)), () => _ = _library.InstallAsync(_app.Slug), accent: true));
                    return;
                }
            }
            else if (_app.IsAvailable)
            {
                _buttons.Children.Add(Action(T("card.install"), () => _ = _library.InstallAsync(_app.Slug), accent: true));
            }
            _buttons.Children.Add(Action(T("card.page"), () => Shell.OpenUrl(_app.Page)));
        }

        private static Button Action(string text, Action click, bool accent = false)
        {
            var button = new Button { Content = text };
            if (accent) button.Classes.Add("accent");
            button.Click += (_, _) => click();
            return button;
        }
    }

    /// <summary>Domanda con due risposte (true = la prima).</summary>
    public sealed class ConfirmWindow : Window
    {
        public ConfirmWindow(string title, string body, string yes, string no)
        {
            Classes.Add("dock");
            Title = title;
            Icon = AppInfo.Icon;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var ok = new Button { Content = yes, Classes = { "accent" } };
            ok.Click += (_, _) => Close(true);
            var cancel = new Button { Content = no };
            cancel.Click += (_, _) => Close(false);
            Content = new StackPanel
            {
                Margin = new Thickness(22),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = title, Classes = { "title" }, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
                },
            };
        }
    }
}
