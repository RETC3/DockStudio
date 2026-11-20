using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using DockStudio.Core.Updates;

namespace DockStudio.UI.Windows
{
    /// <summary>
    /// Nuova versione disponibile: novità, download con avanzamento e avvio dell'installer. Come si avvia
    /// l'installer dipende dal sistema e lo decide l'app (<paramref name="launchInstaller"/>).
    /// </summary>
    public sealed class UpdateWindow : DockWindow
    {
        private readonly UpdateClient _client;
        private readonly UpdateInfo _update;
        private readonly Action<string>? _launchInstaller;
        private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, IsVisible = false, Height = 6 };
        private readonly TextBlock _status = Text("", "muted");
        private readonly Button _install;
        private CancellationTokenSource? _download;

        /// <param name="launchInstaller">
        /// Avvia l'installer scaricato e chiude l'app; null se l'app non può aggiornarsi da sola
        /// (versione portatile): allora resta solo il pulsante per aprire la pagina.
        /// </param>
        public UpdateWindow(DockBranding branding, UpdateClient client, UpdateInfo update, Action<string>? launchInstaller)
            : base(branding)
        {
            _client = client;
            _update = update;
            _launchInstaller = launchInstaller;
            Title = F("update.title", branding.App.Name);

            bool canInstall = launchInstaller != null && update.SetupUrl != null;
            _install = ButtonFor(T("update.install"), Install, accent: true);
            _install.IsVisible = canInstall;
            if (!canInstall)
                _status.Text = launchInstaller == null ? T("update.portable") : T("update.noInstaller");
            _status.IsVisible = !canInstall;

            var notes = new TextBlock { Text = update.Notes.Length > 0 ? update.Notes : "—", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            SetBody(
                Text(F("update.available", branding.App.Name, update.Version.ToString(3)), "title"),
                Text(F("update.current", client.CurrentVersion.ToString(3)), "muted"),
                Card(new StackPanel
                {
                    Spacing = 6,
                    Children = { Text(T("update.notes"), "heading"), new ScrollViewer { MaxHeight = 260, Content = notes } },
                }),
                _progress,
                _status,
                Buttons(
                    ButtonFor(T("update.openPage"), () => OpenUrl(update.PageUrl)),
                    ButtonFor(T("common.later"), Close),
                    _install));

            Closed += (_, _) => _download?.Cancel();
        }

        private async void Install()
        {
            if (_launchInstaller == null) return;
            _install.IsEnabled = false;
            _progress.IsVisible = true;
            _status.IsVisible = true;
            _download = new CancellationTokenSource();
            var progress = new Progress<double>(p =>
            {
                _progress.Value = p;
                _status.Text = F("update.downloading", p);
            });
            try
            {
                var setup = await _client.DownloadSetupAsync(_update, progress, _download.Token);
                _status.Text = T("update.launching");
                _launchInstaller(setup);
            }
            catch (OperationCanceledException)
            {
                // finestra chiusa durante il download
            }
            catch (Exception ex)
            {
                _status.Text = F("update.failed", ex.Message);
                _status.Classes.Add("error");
                _install.IsEnabled = true;
                _progress.IsVisible = false;
            }
        }
    }
}
