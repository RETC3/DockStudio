using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DockStudio.Core.Licensing;
using DockStudio.Core.Suite;
using DockStudio.Core.Updates;
using DockStudio.UI;
using DockStudio.UI.Windows;
using static DockPDF.Text;

namespace DockPDF.Views
{
    /// <summary>
    /// Finestra principale: la home con le schede degli strumenti e, sopra, l'area di lavoro dello strumento
    /// scelto. Chiudendola DockPDF resta nell'area di notifica (se l'impostazione è attiva).
    /// </summary>
    public sealed class MainWindow : Window
    {
        private readonly Button _back = new() { IsVisible = false };
        private readonly TextBlock _toolTitle = new() { Classes = { "heading" }, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        private readonly Button _menu = new() { Content = "⋯", Padding = new Thickness(12, 7) };
        private readonly Button _update = new() { IsVisible = false, Classes = { "accent" } };
        private readonly TextBlock _status = new() { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ContentControl _host = new();
        private readonly Control _home;
        private readonly StartOptions _start;
        private UpdateInfo? _available;
        private bool _exiting;

        public MainWindow(StartOptions start)
        {
            _start = start;
            Classes.Add("dock");
            Title = "DockPDF";
            Icon = AppInfo.Icon;
            Width = App.Settings.Width ?? 1100;
            Height = App.Settings.Height ?? 720;
            MinWidth = 720;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _back.Content = T("nav.back");
            _back.Click += (_, _) => ShowHome();
            _menu.Flyout = BuildMenu();
            _update.Click += (_, _) => { if (_available != null) ShowUpdate(_available); };

            var toolbar = new DockPanel { Margin = new Thickness(16, 12, 16, 8), LastChildFill = false };
            var left = Ui.Row(10, new LogoMark(),
                new TextBlock { Text = "DockPDF", Classes = { "brand" }, FontSize = 20, Margin = new Thickness(0, 0, 10, 0) },
                _back, _toolTitle);
            DockPanel.SetDock(left, Dock.Left);
            DockPanel.SetDock(_menu, Dock.Right);
            DockPanel.SetDock(_update, Dock.Right);
            _update.Margin = new Thickness(0, 0, 8, 0);
            _update.VerticalAlignment = VerticalAlignment.Center;
            _menu.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(left);
            toolbar.Children.Add(_menu);
            toolbar.Children.Add(_update);

            var statusBar = new DockPanel { Margin = new Thickness(16, 6, 16, 8) };
            statusBar.Children.Add(_status);

            _home = BuildHome();
            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            DockPanel.SetDock(statusBar, Dock.Bottom);
            root.Children.Add(toolbar);
            root.Children.Add(statusBar);
            root.Children.Add(_host);
            Content = root;
            ShowHome();
            SetStatus(T("status.ready"));

            // File trascinati sulla home: PDF → Modifica (uno) o Unisci (più d'uno); immagini → Da immagini a PDF.
            Pick.AcceptDrops(_home, p => Pick.IsPdf(p) || Pick.IsImage(p), OpenFiles);

            Opened += (_, _) =>
            {
                if (_start.Tool != null) OpenTool(_start.Tool.Value, _start.Files);
                else if (_start.Files.Count > 0) OpenFiles(_start.Files);
                if (_start.Automatic) StartAutomatic();
            };
            Closing += OnClosing;
        }

        /// <summary>Lo strumento aperto, o null sulla home.</summary>
        internal Workspace? Current => _host.Content as Workspace;

        // ---- Home ---------------------------------------------------------------------------------------

        private Control BuildHome()
        {
            var grid = new UniformGrid { Columns = 4 };
            foreach (var tool in Tools.All) grid.Children.Add(ToolCard(tool));
            var panel = Ui.Column(6,
                Ui.Label(T("home.title"), "title"),
                Ui.Label(T("home.body"), "muted"));
            grid.Margin = new Thickness(-6, 12, -6, 0);
            panel.Children.Add(grid);
            panel.Margin = new Thickness(22, 8, 22, 16);
            panel.MaxWidth = 1100;
            return new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        private Control ToolCard(PdfTool tool)
        {
            bool ready = Tools.IsReady(tool);
            var badge = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(28, AppInfo.Accent.R, AppInfo.Accent.G, AppInfo.Accent.B)),
                Child = new ToolIcon(tool, 24) { Stroke = new SolidColorBrush(AppInfo.Accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var title = Ui.Row(8, Ui.Label(Tools.Name(tool), "heading"));
            if (!ready)
                title.Children.Add(new Border
                {
                    Background = Ui.Brush(this, "DockLine", Brushes.LightGray),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 1),
                    Child = new TextBlock { Text = T("home.soon"), FontSize = 11, Classes = { "muted" } },
                    VerticalAlignment = VerticalAlignment.Center,
                });
            var content = Ui.Column(8, badge, title, Ui.Label(Tools.Description(tool), "muted"));
            var button = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(18),
                Margin = new Thickness(6),
                CornerRadius = new CornerRadius(12),
                MinHeight = 140,
                // Gli strumenti non ancora pronti restano visibili ma attenuati (non con lo sfondo grigio dei pulsanti disattivati).
                Opacity = ready ? 1 : 0.5,
                Cursor = ready ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
                Classes = { "toolCard" },
            };
            button.Bind(BackgroundProperty, this.GetResourceObservable("DockSurface"));
            button.Bind(BorderBrushProperty, this.GetResourceObservable("DockLine"));
            button.BorderThickness = new Thickness(1);
            button.Click += (_, _) => { if (ready) OpenTool(tool); };
            ToolTip.SetTip(button, ready ? null : T("home.soonTip"));
            return button;
        }

        public void ShowHome()
        {
            (Current as IDisposable)?.Dispose();
            _host.Content = _home;
            _back.IsVisible = false;
            _toolTitle.IsVisible = false;
            Title = "DockPDF";
        }

        /// <summary>Apre lo strumento, con i file indicati già caricati.</summary>
        public void OpenTool(PdfTool tool, IReadOnlyList<string>? files = null)
        {
            if (!Tools.IsReady(tool)) return;
            if (Current?.Tool != tool)
            {
                (Current as IDisposable)?.Dispose();
                Workspace workspace = tool switch
                {
                    PdfTool.Edit or PdfTool.Sign => new EditorWorkspace(this, tool),
                    PdfTool.Organize or PdfTool.Merge => new PagesWorkspace(this, tool),
                    PdfTool.Split => new SplitWorkspace(this),
                    PdfTool.ImagesToPdf => new ImagesWorkspace(this),
                    PdfTool.PdfToImages => new ExportWorkspace(this),
                    PdfTool.Protect => new ProtectWorkspace(this, protect: true),
                    PdfTool.Compress => new CompressWorkspace(this),
                    PdfTool.Ocr => new OcrWorkspace(this),
                    _ => new ProtectWorkspace(this, protect: false),
                };
                _host.Content = workspace;
                _back.IsVisible = true;
                _toolTitle.Text = Tools.Name(tool);
                _toolTitle.IsVisible = true;
                Title = $"{Tools.Name(tool)} — DockPDF";
                SetStatus(Tools.Description(tool));
            }
            if (files is { Count: > 0 }) _ = Current!.AddFilesAsync(files);
        }

        /// <summary>File aperti senza strumento (trascinati, dalla riga di comando): si sceglie lo strumento adatto.</summary>
        public void OpenFiles(IReadOnlyList<string> files)
        {
            var pdfs = files.Where(Pick.IsPdf).ToList();
            if (pdfs.Count > 0) OpenTool(pdfs.Count == 1 ? PdfTool.Edit : PdfTool.Merge, pdfs);
            else if (files.Any(Pick.IsImage)) OpenTool(PdfTool.ImagesToPdf, files.Where(Pick.IsImage).ToList());
        }

        public void SetStatus(string text) => _status.Text = text;

        /// <summary>Un errore non previsto: messaggio nella barra di stato (la segnalazione parte a parte, se scelta).</summary>
        internal void ShowError(Exception ex) => SetStatus(F("status.unexpected", ex.Message));

        /// <summary>
        /// Chiede la password di un PDF protetto e la prova; null se si annulla.
        /// </summary>
        public async Task<string?> AskPassword(string path)
        {
            bool wrong = false;
            while (true)
            {
                var password = await new PasswordWindow(System.IO.Path.GetFileName(path), wrong).ShowDialog<string?>(this);
                if (password == null) return null;
                try
                {
                    using var _ = Engine.PdfFile.Open(path, password);
                    return password;
                }
                catch (Engine.PdfPasswordException)
                {
                    wrong = true;
                }
            }
        }

        // ---- Menu ---------------------------------------------------------------------------------------

        private MenuFlyout BuildMenu()
        {
            var flyout = new MenuFlyout();
            var radial = new MenuItem { Header = T("menu.radial") };
            radial.Click += (_, _) => App.Current?.ShowRadial();
            flyout.Items.Add(radial);
            var tray = new MenuItem
            {
                Header = T("menu.keepInTray"),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = App.Settings.KeepInTray,
            };
            tray.Click += (_, _) =>
            {
                App.Settings.KeepInTray = !App.Settings.KeepInTray;
                tray.IsChecked = App.Settings.KeepInTray;
                App.SettingsStore.Save(App.Settings);
                SetStatus(T(App.Settings.KeepInTray ? "status.trayOn" : "status.trayOff"));
            };
            flyout.Items.Add(tray);
            flyout.Items.Add(new Separator());

            var appearance = new MenuItem { Header = T("menu.appearance") };
            foreach (var a in Enum.GetValues<Appearance>())
            {
                var item = new MenuItem { Header = T("appearance." + a.ToString().ToLowerInvariant()) };
                item.Click += (_, _) =>
                {
                    App.Settings.Appearance = a.ToString();
                    DockTheme.SetAppearance(Application.Current!, a);
                    App.SettingsStore.Save(App.Settings);
                };
                appearance.Items.Add(item);
            }
            flyout.Items.Add(appearance);
            var privacy = new MenuItem { Header = T("menu.privacy") };
            privacy.Click += async (_, _) => await AskPrivacy();
            flyout.Items.Add(privacy);
            var check = new MenuItem { Header = T("menu.checkUpdates") };
            check.Click += (_, _) => CheckForUpdates(manual: true);
            flyout.Items.Add(check);
            var about = new MenuItem { Header = F("menu.about", "DockPDF") };
            about.Click += (_, _) => ShowAbout();
            flyout.Items.Add(about);
            flyout.Items.Add(new Separator());
            var exit = new MenuItem { Header = T("menu.exit") };
            exit.Click += (_, _) => ExitApp();
            flyout.Items.Add(exit);
            return flyout;
        }

        private void ExitApp()
        {
            _exiting = true;
            if (App.Current is { } app) app.Exit();
            else Close();
        }

        /// <summary>
        /// Chiudere la finestra con l'area di notifica attiva la nasconde soltanto. La prima volta si spiega
        /// dove trovare DockPDF (e si può scegliere di chiuderlo del tutto).
        /// </summary>
        private async void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                App.Settings.Width = Width;
                App.Settings.Height = Height;
            }
            if (_exiting || !App.Settings.KeepInTray || App.Current == null) return;
            e.Cancel = true;
            if (!App.Settings.TrayHintShown)
            {
                App.Settings.TrayHintShown = true;
                App.SettingsStore.Save(App.Settings);
                bool stay = await new ConfirmWindow(T("trayHint.title"), T("trayHint.body"), T("trayHint.stay"), T("trayHint.exit"))
                    .ShowDialog<bool>(this);
                if (!stay)
                {
                    ExitApp();
                    return;
                }
            }
            ShowHome();
            Hide();
        }

        // ---- Avvio, privacy e statistiche ----------------------------------------------------------------

        /// <summary>Quello che DockPDF fa da solo all'apertura: scelta sulla privacy, aggiornamenti, minuti d'uso.</summary>
        private async void StartAutomatic()
        {
            if (!App.Settings.PrivacyChoiceMade)
            {
                if (Shell.TakeInstallerPrivacyChoice() is { } choice) App.ApplyPrivacy(choice.Usage, choice.Errors);
                else await AskPrivacy();
            }
            CheckForUpdates(manual: false);
            StartUsageTimer();
        }

        /// <summary>"Aiutaci a migliorare": al primo avvio (se l'installer non l'ha già chiesto) e dal menu.</summary>
        private async Task AskPrivacy()
        {
            var s = App.Settings;
            var window = new PrivacyWindow(AppInfo.Branding, s.PrivacyChoiceMade && s.SendUsageStats, s.PrivacyChoiceMade && s.SendErrorReports)
            {
                Icon = AppInfo.Icon,
            };
            // Chiudere la finestra senza confermare vale come "nessuna delle due".
            window.Chosen += App.ApplyPrivacy;
            await window.ShowDialog(this);
        }

        private int _unsavedMinutes;

        /// <summary>
        /// Tempo di utilizzo: ogni minuto con la finestra di DockPDF in primo piano conta un minuto (solo se le
        /// statistiche sono state scelte). Si salva ogni 5 minuti e alla chiusura.
        /// </summary>
        private void StartUsageTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            timer.Tick += (_, _) =>
            {
                if (!IsActive || !UsageStats.AddUsageMinute(App.Settings)) return;
                if (++_unsavedMinutes < 5) return;
                _unsavedMinutes = 0;
                App.SettingsStore.Save(App.Settings);
            };
            timer.Start();
            Closed += (_, _) => timer.Stop();
        }

        /// <summary>Statistiche anonime insieme al controllo automatico (al massimo una volta al giorno), se scelte.</summary>
        private static async Task SendUsageStatsAsync()
        {
            var hadId = App.Settings.InstallId;
            bool sent = await AppInfo.Usage.SendAsync(App.Settings, Shell.IsInstalled);
            if (sent || App.Settings.InstallId != hadId) App.SettingsStore.Save(App.Settings); // minuti azzerati o codice nuovo
        }

        // ---- Aggiornamenti ------------------------------------------------------------------------------

        /// <summary>
        /// Cerca una nuova versione. Automatico: al massimo una volta al giorno, in silenzio; se c'è, compare il
        /// pulsante "Aggiorna". Dal menu: sempre, con l'esito nella barra di stato.
        /// </summary>
        private async void CheckForUpdates(bool manual)
        {
            if (!manual && App.Settings.LastUpdateCheck is DateTime last && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
            if (manual) SetStatus(T("update.checking"));
            try
            {
                var update = await AppInfo.Updates.CheckAsync();
                App.Settings.LastUpdateCheck = DateTime.UtcNow;
                App.SettingsStore.Save(App.Settings);
                if (!manual) _ = SendUsageStatsAsync();
                _available = update;
                _update.IsVisible = update != null;
                if (update != null)
                {
                    _update.Content = F("update.button", update.Version.ToString(3));
                    if (manual) ShowUpdate(update);
                }
                else if (manual)
                {
                    SetStatus(F("update.none", AppInfo.Updates.CurrentVersion.ToString(3)));
                }
            }
            catch (Exception ex)
            {
                // Senza internet o con il sito irraggiungibile: in automatico non si disturba.
                if (manual) SetStatus(F("update.failed", ex.Message));
            }
        }

        private void ShowUpdate(UpdateInfo update)
        {
            Action<string>? launch = Shell.IsInstalled
                ? setup =>
                {
                    Shell.LaunchUpdate(setup);
                    // L'installer aspetta che DockPDF sia chiuso, poi lo riapre.
                    ExitApp();
                }
                : null; // versione portatile: solo il link alla pagina
            var window = new UpdateWindow(AppInfo.Branding, AppInfo.Updates, update, launch) { Icon = AppInfo.Icon };
            window.ShowDialog(this);
        }

        private void ShowAbout()
        {
            var licenses = new LicenseManager(AppInfo.Identity.Name);
            var check = licenses.Check();
            var window = new AboutWindow(AppInfo.Branding, AppInfo.Version, portable: false, check,
                new SuiteRegistry().Installed(),
                openLicense: () =>
                {
                    var lw = new LicenseWindow(AppInfo.Branding, licenses) { Icon = AppInfo.Icon };
                    lw.Show(licenses.Check());
                }) { Icon = AppInfo.Icon };
            window.ShowDialog(this);
        }
    }
}
