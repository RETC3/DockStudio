using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DockStudio.Core;
using DockStudio.Core.Localization;
using DockStudio.Core.Suite;
using DockStudio.Launcher.Views;
using DockStudio.UI;

namespace DockStudio.Launcher
{
    /// <summary>
    /// Opzioni da riga di comando: "--tray" resta nell'area di notifica senza finestra (avvio con Windows),
    /// "--exit" chiude la copia già aperta. <paramref name="Automatic"/> false (solo nei controlli automatici)
    /// evita quello che il launcher fa da solo: privacy, elenco dal sito, aggiornamenti e statistiche.
    /// </summary>
    public sealed record StartOptions(bool TrayOnly, bool Exit, bool Automatic = true)
    {
        public static readonly StartOptions None = new(false, false);

        public static StartOptions Parse(string[] args) => new(args.Contains("--tray"), args.Contains("--exit"));
    }

    public sealed class LauncherApp : Application
    {
        public static readonly JsonSettingsStore<Settings> SettingsStore = new(AppInfo.Identity);
        public static Settings Settings { get; private set; } = new();
        public static StartOptions Start { get; set; } = StartOptions.None;

        private IClassicDesktopStyleApplicationLifetime? _desktop;
        private MainWindow? _main;
        private TrayIcon? _tray;
        private Library? _library;
        private bool _automaticStarted;

        public static new LauncherApp? Current => Application.Current as LauncherApp;

        /// <summary>Lo stato condiviso da finestra e area di notifica.</summary>
        public Library Library => _library ??= new Library();

        public override void Initialize()
        {
            Settings = SettingsStore.Load();
            WatchErrors();
            Languages.Set(Settings.Language);
            var appearance = Enum.TryParse<Appearance>(Settings.Appearance, out var a) ? a : Appearance.System;
            DockTheme.Apply(this, AppInfo.Accent, appearance);
        }

        public static void ApplyPrivacy(bool usage, bool errors)
        {
            Settings.ApplyPrivacy(usage, errors);
            SettingsStore.Save(Settings);
        }

        private void WatchErrors()
        {
            Dispatcher.UIThread.UnhandledException += (_, args) =>
            {
                _ = AppInfo.Errors.SendAsync(Settings, args.Exception);
                args.Handled = true;
                _main?.SetStatus(Text.F("status.unexpected", args.Exception.Message));
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is not Exception ex) return;
                try { System.Threading.Tasks.Task.Run(() => AppInfo.Errors.SendAsync(Settings, ex, fatal: true, TimeSpan.FromSeconds(3))).Wait(4000); }
                catch { /* niente */ }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                _ = AppInfo.Errors.SendAsync(Settings, args.Exception);
                args.SetObserved();
            };
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _desktop = desktop;
                bool tray = Settings.KeepInTray || Start.TrayOnly;
                desktop.ShutdownMode = tray ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnMainWindowClose;
                desktop.ShutdownRequested += (_, _) => SettingsStore.Save(Settings);
                if (tray) CreateTray();
                if (!Start.TrayOnly) desktop.MainWindow = EnsureMain();
                else if (Start.Automatic) Dispatcher.UIThread.Post(StartBackground);
            }
            // Il launcher si iscrive al registro della suite: così le app sanno che c'è e lasciano a lui l'area di notifica.
            var exe = Environment.ProcessPath;
            if (exe != null && Start.Automatic) new SuiteRegistry().Register(AppInfo.Identity.Name, AppInfo.Version, exe);
            base.OnFrameworkInitializationCompleted();
        }

        private MainWindow EnsureMain()
        {
            if (_main != null) return _main;
            _main = new MainWindow(Library, Start with { Automatic = Start.Automatic && !_automaticStarted });
            _automaticStarted = true;
            _main.Closed += (_, _) => _main = null;
            return _main;
        }

        /// <summary>Avviato nell'area di notifica: elenco e aggiornamenti si controllano anche senza finestra.</summary>
        private async void StartBackground()
        {
            _automaticStarted = true;
            await Library.RefreshCatalogAsync();
            if (Settings.LastUpdateCheck is DateTime last && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
            int found = await Library.CheckUpdatesAsync();
            Settings.LastUpdateCheck = DateTime.UtcNow;
            SettingsStore.Save(Settings);
            if (Settings.PrivacyChoiceMade) _ = MainWindow.SendUsageStatsAsync();
            if (found > 0 && _tray != null) _tray.ToolTipText = Text.F("tray.updates", found);
        }

        public void ShowMain()
        {
            var main = EnsureMain();
            if (_desktop != null && _desktop.MainWindow == null) _desktop.MainWindow = main;
            if (!main.IsVisible) main.Show();
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Activate();
            Library.RefreshInstalled();
        }

        /// <summary>Una richiesta da un'altra copia del launcher: mostrarsi o chiudersi.</summary>
        public void Handle(StartOptions request)
        {
            if (request.Exit) Exit();
            else if (!request.TrayOnly) ShowMain();
        }

        public void Exit()
        {
            _tray?.Dispose();
            _desktop?.Shutdown();
        }

        /// <summary>
        /// Icona nell'area di notifica: clic = finestra; tasto destro = le app installate (e il cerchio degli
        /// strumenti di DockPDF), così le app non mettono ciascuna la propria icona.
        /// </summary>
        private void CreateTray()
        {
            _tray = new TrayIcon { Icon = AppInfo.Icon, ToolTipText = "DockStudio" };
            _tray.Clicked += (_, _) => ShowMain();
            RebuildTrayMenu();
            Library.Changed += RebuildTrayMenu;
            TrayIcon.SetIcons(this, new TrayIcons { _tray });
        }

        private void RebuildTrayMenu()
        {
            if (_tray == null) return;
            var menu = new NativeMenu();
            var open = new NativeMenuItem(Text.T("tray.open"));
            open.Click += (_, _) => ShowMain();
            menu.Items.Add(open);
            menu.Items.Add(new NativeMenuItemSeparator());
            foreach (var app in Library.Apps)
            {
                if (!Library.Installed.TryGetValue(app.Slug, out var installed)) continue;
                var item = new NativeMenuItem(app.Name);
                item.Click += (_, _) => Installs.Launch(installed);
                menu.Items.Add(item);
                // DockPDF lascia al launcher l'area di notifica: da qui si apre il suo cerchio degli strumenti.
                if (app.Slug == "dockpdf")
                {
                    var radial = new NativeMenuItem(Text.T("tray.pdfRadial"));
                    radial.Click += (_, _) => Installs.Launch(installed, "--radial");
                    menu.Items.Add(radial);
                }
            }
            int updates = Library.PendingUpdates;
            if (updates > 0)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                var all = new NativeMenuItem(Text.F("tray.updateAll", updates));
                all.Click += (_, _) => { ShowMain(); _ = Library.UpdateAllAsync(); };
                menu.Items.Add(all);
            }
            menu.Items.Add(new NativeMenuItemSeparator());
            var exit = new NativeMenuItem(Text.T("tray.exit"));
            exit.Click += (_, _) => Exit();
            menu.Items.Add(exit);
            _tray.Menu = menu;
            _tray.ToolTipText = updates > 0 ? Text.F("tray.updates", updates) : "DockStudio";
        }
    }

    internal static class Program
    {
        /// <summary>Lo stesso nome sta in installer/DockStudio.iss (AppMutex) e in CatalogApp.RunningMutex.</summary>
        public const string RunningMutexName = "DockStudio.Running";

        [STAThread]
        public static int Main(string[] args)
        {
            LauncherApp.Start = StartOptions.Parse(args);
            DockStudio.Core.Windows.SingleInstance? single = null;
            if (OperatingSystem.IsWindows())
            {
                single = DockStudio.Core.Windows.SingleInstance.TryAcquire(AppInfo.Identity);
                if (single == null)
                {
                    DockStudio.Core.Windows.SingleInstance.RequestShow(AppInfo.Identity, args);
                    return 0;
                }
                if (LauncherApp.Start.Exit) return 0; // niente da chiudere
                single.ListenForShowRequests(received => Dispatcher.UIThread.Post(() => LauncherApp.Current?.Handle(StartOptions.Parse(received))), AppInfo.Identity);
            }
            using var running = new System.Threading.Mutex(false, RunningMutexName);
            try
            {
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                if (OperatingSystem.IsWindows()) single?.Dispose();
            }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<LauncherApp>().UsePlatformDetect().LogToTrace();
    }
}
