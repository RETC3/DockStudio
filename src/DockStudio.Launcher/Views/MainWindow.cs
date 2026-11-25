using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DockStudio.Core.Licensing;
using DockStudio.Core.Updates;
using DockStudio.UI;
using DockStudio.UI.Windows;
using static DockStudio.Launcher.Text;

namespace DockStudio.Launcher.Views
{
    /// <summary>
    /// La finestra del launcher: le app installate (Apri, Aggiorna), quelle da installare (Installa) e quelle in
    /// arrivo. Chiudendola DockStudio resta nell'area di notifica (se l'impostazione è attiva).
    /// </summary>
    public sealed class MainWindow : Window
    {
        private readonly Library _library;
        private readonly StartOptions _start;
        private readonly Button _menu = new() { Content = "⋯", Padding = new Thickness(12, 7) };
        private readonly Button _updateAll = new() { IsVisible = false, Classes = { "accent" } };
        private readonly TextBlock _status = new() { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _license = new() { Classes = { "muted" }, Margin = new Thickness(0, 0, 16, 0) };
        private readonly StackPanel _sections = new() { Spacing = 22 };
        private readonly Dictionary<string, AppCard> _cards = new(StringComparer.OrdinalIgnoreCase);
        private bool _exiting;

        public MainWindow(Library library, StartOptions start)
        {
            _library = library;
            _start = start;
            Classes.Add("dock");
            Title = "DockStudio";
            Icon = AppInfo.Icon;
            Width = LauncherApp.Settings.Width ?? 1000;
            Height = LauncherApp.Settings.Height ?? 700;
            MinWidth = 420;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _menu.Flyout = BuildMenu();
            _updateAll.Click += async (_, _) => await _library.UpdateAllAsync();

            var toolbar = new DockPanel { Margin = new Thickness(16, 12, 16, 8), LastChildFill = false };
            var left = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { new LogoMark(), new TextBlock { Text = "DockStudio", Classes = { "brand" }, FontSize = 20, VerticalAlignment = VerticalAlignment.Center } },
            };
            DockPanel.SetDock(left, Dock.Left);
            DockPanel.SetDock(_menu, Dock.Right);
            DockPanel.SetDock(_updateAll, Dock.Right);
            _updateAll.Margin = new Thickness(0, 0, 8, 0);
            _updateAll.VerticalAlignment = VerticalAlignment.Center;
            _menu.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(left);
            toolbar.Children.Add(_menu);
            toolbar.Children.Add(_updateAll);

            var statusBar = new DockPanel { Margin = new Thickness(16, 6, 16, 8) };
            DockPanel.SetDock(_license, Dock.Left);
            statusBar.Children.Add(_license);
            statusBar.Children.Add(_status);

            var body = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(22, 4, 22, 20),
                MaxWidth = 1100,
                Children =
                {
                    new TextBlock { Text = T("home.title"), Classes = { "title" } },
                    new TextBlock { Text = T("home.body"), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) },
                    _sections,
                },
            };
            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            DockPanel.SetDock(statusBar, Dock.Bottom);
            root.Children.Add(toolbar);
            root.Children.Add(statusBar);
            root.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            Content = root;

            _library.Changed += Render;
            _library.Progress += slug => { if (_cards.TryGetValue(slug, out var card)) card.Update(); };
            Closed += (_, _) => _library.Changed -= Render;
            Render();
            ShowLicense();
            SetStatus(T("status.ready"));

            // un'app installata o tolta mentre la finestra era nascosta (non nei controlli automatici: lì lo stato è finto)
            if (_start.Automatic) Activated += (_, _) => _library.RefreshInstalled();
            Opened += (_, _) => { if (_start.Automatic) StartAutomatic(); };
            Closing += OnClosing;
        }

        public void SetStatus(string text) => _status.Text = text;

        // ---- Schede -----------------------------------------------------------------------------------

        /// <summary>Ridisegna le tre sezioni: installate, da installare, in arrivo.</summary>
        private void Render()
        {
            var installed = _library.Apps.Where(a => _library.Installed.ContainsKey(a.Slug)).ToList();
            var available = _library.Apps.Where(a => a.IsAvailable && !_library.Installed.ContainsKey(a.Slug)).ToList();
            var soon = _library.Apps.Where(a => !a.IsAvailable && !_library.Installed.ContainsKey(a.Slug)).ToList();

            _cards.Clear();
            _sections.Children.Clear();
            if (installed.Count == 0)
                _sections.Children.Add(Section(T("section.installed"), new List<CatalogApp>(), T("section.installedEmpty")));
            else
                _sections.Children.Add(Section(T("section.installed"), installed));
            if (available.Count > 0) _sections.Children.Add(Section(T("section.available"), available));
            if (soon.Count > 0) _sections.Children.Add(Section(T("section.soon"), soon));

            int updates = _library.PendingUpdates;
            _updateAll.IsVisible = updates > 0;
            _updateAll.Content = updates == 1 ? T("update.one") : F("update.all", updates);
        }

        private Control Section(string title, List<CatalogApp> apps, string? empty = null)
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = title, Classes = { "heading" } });
            if (empty != null) panel.Children.Add(new TextBlock { Text = empty, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 0) };
            foreach (var app in apps)
            {
                var card = new AppCard(this, _library, app);
                _cards[app.Slug] = card;
                wrap.Children.Add(card);
            }
            panel.Children.Add(wrap);
            return panel;
        }

        // ---- Menu -------------------------------------------------------------------------------------

        private MenuFlyout BuildMenu()
        {
            var flyout = new MenuFlyout();
            var check = new MenuItem { Header = T("menu.checkUpdates") };
            check.Click += (_, _) => CheckForUpdates(manual: true);
            flyout.Items.Add(check);
            flyout.Items.Add(new Separator());

            var startup = new MenuItem { Header = T("menu.startWithWindows"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = Shell.StartsWithWindows };
            startup.Click += (_, _) =>
            {
                Shell.SetStartWithWindows(!Shell.StartsWithWindows);
                startup.IsChecked = Shell.StartsWithWindows;
            };
            flyout.Items.Add(startup);
            var tray = new MenuItem { Header = T("menu.keepInTray"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = LauncherApp.Settings.KeepInTray };
            tray.Click += (_, _) =>
            {
                LauncherApp.Settings.KeepInTray = !LauncherApp.Settings.KeepInTray;
                tray.IsChecked = LauncherApp.Settings.KeepInTray;
                LauncherApp.SettingsStore.Save(LauncherApp.Settings);
                SetStatus(T(LauncherApp.Settings.KeepInTray ? "status.trayOn" : "status.trayOff"));
            };
            flyout.Items.Add(tray);
            var appearance = new MenuItem { Header = T("menu.appearance") };
            foreach (var a in Enum.GetValues<Appearance>())
            {
                var item = new MenuItem { Header = T("appearance." + a.ToString().ToLowerInvariant()) };
                item.Click += (_, _) =>
                {
                    LauncherApp.Settings.Appearance = a.ToString();
                    DockTheme.SetAppearance(Application.Current!, a);
                    LauncherApp.SettingsStore.Save(LauncherApp.Settings);
                };
                appearance.Items.Add(item);
            }
            flyout.Items.Add(appearance);
            var privacy = new MenuItem { Header = T("menu.privacy") };
            privacy.Click += async (_, _) => await AskPrivacy();
            flyout.Items.Add(privacy);
            var license = new MenuItem { Header = T("menu.license") };
            license.Click += (_, _) => OpenLicense();
            flyout.Items.Add(license);
            var site = new MenuItem { Header = T("menu.site") };
            site.Click += (_, _) => Shell.OpenUrl(AppInfo.SuiteSite + "/");
            flyout.Items.Add(site);
            var about = new MenuItem { Header = F("menu.about", "DockStudio") };
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
            if (LauncherApp.Current is { } app) app.Exit();
            else Close();
        }

        private async void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                LauncherApp.Settings.Width = Width;
                LauncherApp.Settings.Height = Height;
            }
            if (_exiting || !LauncherApp.Settings.KeepInTray || LauncherApp.Current == null) return;
            e.Cancel = true;
            if (!LauncherApp.Settings.TrayHintShown)
            {
                LauncherApp.Settings.TrayHintShown = true;
                LauncherApp.SettingsStore.Save(LauncherApp.Settings);
                bool stay = await new ConfirmWindow(T("trayHint.title"), T("trayHint.body"), T("trayHint.stay"), T("trayHint.exit")).ShowDialog<bool>(this);
                if (!stay)
                {
                    ExitApp();
                    return;
                }
            }
            Hide();
        }

        // ---- Licenza, informazioni, privacy -------------------------------------------------------------

        private void ShowLicense()
        {
            var check = new LicenseManager(AppInfo.Identity.Name).Check();
            _license.Text = check.Description;
        }

        private void OpenLicense()
        {
            var licenses = new LicenseManager(AppInfo.Identity.Name);
            var window = new LicenseWindow(AppInfo.Branding, licenses) { Icon = AppInfo.Icon };
            window.Closed += (_, _) => ShowLicense();
            window.Show(licenses.Check());
        }

        private void ShowAbout()
        {
            var licenses = new LicenseManager(AppInfo.Identity.Name);
            var window = new AboutWindow(AppInfo.Branding, AppInfo.Version, portable: !Shell.IsInstalled, licenses.Check(),
                new DockStudio.Core.Suite.SuiteRegistry().Installed(), OpenLicense) { Icon = AppInfo.Icon };
            window.ShowDialog(this);
        }

        private async Task AskPrivacy()
        {
            var s = LauncherApp.Settings;
            var window = new PrivacyWindow(AppInfo.Branding, s.PrivacyChoiceMade && s.SendUsageStats, s.PrivacyChoiceMade && s.SendErrorReports)
            {
                Icon = AppInfo.Icon,
            };
            window.Chosen += LauncherApp.ApplyPrivacy;
            await window.ShowDialog(this);
        }

        // ---- Avvio, aggiornamenti, statistiche ----------------------------------------------------------

        private async void StartAutomatic()
        {
            if (!LauncherApp.Settings.PrivacyChoiceMade)
            {
                if (Shell.TakeInstallerPrivacyChoice() is { } choice) LauncherApp.ApplyPrivacy(choice.Usage, choice.Errors);
                else await AskPrivacy();
            }
            await _library.RefreshCatalogAsync();
            CheckForUpdates(manual: false);
            StartUsageTimer();
        }

        /// <summary>
        /// Cerca le nuove versioni di tutte le app e del launcher. Automatico: al massimo ogni 20 ore, in
        /// silenzio. Dal menu: sempre, con l'esito nella barra di stato.
        /// </summary>
        private async void CheckForUpdates(bool manual)
        {
            if (!manual && LauncherApp.Settings.LastUpdateCheck is DateTime last && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
            if (manual) SetStatus(T("update.checking"));
            int found = await _library.CheckUpdatesAsync();
            LauncherApp.Settings.LastUpdateCheck = DateTime.UtcNow;
            LauncherApp.SettingsStore.Save(LauncherApp.Settings);
            if (!manual) _ = SendUsageStatsAsync();
            if (_library.LauncherUpdate is { } self && (manual || !_selfUpdateShown))
            {
                _selfUpdateShown = true;
                ShowSelfUpdate(self);
            }
            if (manual) SetStatus(found == 0 ? T("update.none") : F("update.found", found));
        }

        private bool _selfUpdateShown;

        /// <summary>Nuova versione del launcher: la finestra comune degli aggiornamenti.</summary>
        private void ShowSelfUpdate(UpdateInfo update)
        {
            Action<string>? launch = Shell.IsInstalled
                ? setup =>
                {
                    Shell.LaunchUpdate(setup);
                    ExitApp();
                }
                : null;
            new UpdateWindow(AppInfo.Branding, AppInfo.Updates, update, launch) { Icon = AppInfo.Icon }.ShowDialog(this);
        }

        internal static async Task SendUsageStatsAsync()
        {
            var hadId = LauncherApp.Settings.InstallId;
            bool sent = await AppInfo.Usage.SendAsync(LauncherApp.Settings, Shell.IsInstalled);
            if (sent || LauncherApp.Settings.InstallId != hadId) LauncherApp.SettingsStore.Save(LauncherApp.Settings);
        }

        private int _unsavedMinutes;

        private void StartUsageTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            timer.Tick += (_, _) =>
            {
                if (!IsActive || !UsageStats.AddUsageMinute(LauncherApp.Settings)) return;
                if (++_unsavedMinutes < 5) return;
                _unsavedMinutes = 0;
                LauncherApp.SettingsStore.Save(LauncherApp.Settings);
            };
            timer.Start();
            Closed += (_, _) => timer.Stop();
        }
    }
}
