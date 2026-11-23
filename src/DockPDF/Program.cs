using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DockPDF.Views;
using DockStudio.Core;
using DockStudio.Core.Localization;
using DockStudio.Core.Suite;
using DockStudio.UI;

namespace DockPDF
{
    /// <summary>
    /// Opzioni da riga di comando: file da aprire, "--tool &lt;nome&gt;" per aprire subito uno strumento,
    /// "--radial" per mostrare solo il cerchio degli strumenti (per esempio da un collegamento o all'avvio di Windows,
    /// con "--tray" per restare nell'area di notifica senza mostrare nulla).
    /// <paramref name="Automatic"/> false (solo nei controlli automatici) evita quello che l'app fa da sola
    /// all'apertura: la domanda sulla privacy, il controllo degli aggiornamenti e le statistiche.
    /// </summary>
    public sealed record StartOptions(IReadOnlyList<string> Files, PdfTool? Tool, bool Radial, bool TrayOnly, bool Automatic = true)
    {
        public static readonly StartOptions None = new(Array.Empty<string>(), null, false, false);

        public static StartOptions Parse(string[] args)
        {
            var files = new List<string>();
            PdfTool? tool = null;
            bool radial = false, tray = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--tool" && i + 1 < args.Length) tool = Tools.Parse(args[++i]);
                else if (args[i] == "--radial") radial = true;
                else if (args[i] == "--tray") tray = true;
                else if (!args[i].StartsWith("--", StringComparison.Ordinal) && File.Exists(args[i])) files.Add(Path.GetFullPath(args[i]));
            }
            return new StartOptions(files, tool, radial, tray);
        }
    }

    public sealed class App : Application
    {
        public static readonly JsonSettingsStore<Settings> SettingsStore = new(AppInfo.Identity);
        public static Settings Settings { get; private set; } = new();
        public static StartOptions Start { get; set; } = StartOptions.None;

        private IClassicDesktopStyleApplicationLifetime? _desktop;
        private MainWindow? _main;
        private RadialWindow? _radial;
        private TrayIcon? _tray;
        private bool _automaticStarted;

        public override void Initialize()
        {
            Settings = SettingsStore.Load();
            WatchErrors();
            Languages.Set(Settings.Language);
            var appearance = Enum.TryParse<Appearance>(Settings.Appearance, out var a) ? a : Appearance.System;
            DockTheme.Apply(this, AppInfo.Accent, appearance);
        }

        public static new App? Current => Application.Current as App;

        /// <summary>
        /// La scelta su statistiche ed errori: dall'installer, dalla finestra al primo avvio o dal menu.
        /// Senza statistiche si cancellano codice e minuti accumulati.
        /// </summary>
        public static void ApplyPrivacy(bool usage, bool errors)
        {
            Settings.ApplyPrivacy(usage, errors);
            SettingsStore.Save(Settings);
        }

        /// <summary>Errori non gestiti: si segnalano (solo se l'utente l'ha scelto) e, se possibile, si va avanti.</summary>
        private void WatchErrors()
        {
            Dispatcher.UIThread.UnhandledException += (_, args) =>
            {
                _ = AppInfo.Errors.SendAsync(Settings, args.Exception);
                // Un errore in un'azione non deve chiudere l'app: si mostra e si continua.
                args.Handled = true;
                _main?.ShowError(args.Exception);
            };
            // errore che chiude l'app: si prova a segnalarlo prima di uscire, al massimo per pochi secondi
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
                // Con l'icona nell'area di notifica DockPDF resta aperto anche senza finestre.
                desktop.ShutdownMode = Settings.KeepInTray ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnMainWindowClose;
                desktop.ShutdownRequested += (_, _) => SettingsStore.Save(Settings);
                if (Settings.KeepInTray) CreateTray();

                if (Start.Radial || Start.TrayOnly)
                {
                    if (Start.Radial) Dispatcher.UIThread.Post(() => ShowRadial());
                    // senza finestra principale: le cose automatiche partono quando si apre per la prima volta
                }
                else
                {
                    desktop.MainWindow = EnsureMain();
                }
            }
            // Iscrizione al registro delle app della suite (collegamenti e aggiornamenti coordinati).
            var exe = Environment.ProcessPath;
            if (exe != null) new SuiteRegistry().Register(AppInfo.Identity.Name, AppInfo.Version, exe);
            base.OnFrameworkInitializationCompleted();
        }

        private MainWindow EnsureMain()
        {
            if (_main != null) return _main;
            _main = new MainWindow(Start with { Automatic = Start.Automatic && !_automaticStarted });
            _automaticStarted = true;
            _main.Closed += (_, _) => _main = null;
            return _main;
        }

        /// <summary>Mostra la finestra principale (sulla home o sullo strumento indicato).</summary>
        public void ShowMain(PdfTool? tool = null, IReadOnlyList<string>? files = null)
        {
            _radial?.Close();
            var main = EnsureMain();
            if (_desktop != null && _desktop.MainWindow == null) _desktop.MainWindow = main;
            if (!main.IsVisible) main.Show();
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Activate();
            if (tool != null) main.OpenTool(tool.Value, files);
            else if (files is { Count: > 0 }) main.OpenFiles(files);
        }

        /// <summary>Il cerchio degli strumenti, vicino al mouse (per esempio sopra l'icona nell'area di notifica).</summary>
        public void ShowRadial()
        {
            if (_radial != null)
            {
                _radial.Close();
                return;
            }
            _radial = new RadialWindow();
            _radial.ToolChosen += tool => ShowMain(tool);
            _radial.HomeChosen += () => ShowMain();
            _radial.Closed += (_, _) => _radial = null;
            _radial.ShowNear(Shell.CursorPosition());
        }

        /// <summary>Chiude davvero DockPDF (anche dall'area di notifica).</summary>
        public void Exit()
        {
            _tray?.Dispose();
            _desktop?.Shutdown();
        }

        /// <summary>Icona nell'area di notifica: clic = cerchio degli strumenti; tasto destro = menu.</summary>
        private void CreateTray()
        {
            var menu = new NativeMenu();
            var open = new NativeMenuItem(Text.T("tray.open"));
            open.Click += (_, _) => ShowMain();
            menu.Items.Add(open);
            var radial = new NativeMenuItem(Text.T("tray.radial"));
            radial.Click += (_, _) => ShowRadial();
            menu.Items.Add(radial);
            menu.Items.Add(new NativeMenuItemSeparator());
            foreach (var tool in Tools.All.Where(Tools.IsReady))
            {
                var item = new NativeMenuItem(Tools.Name(tool));
                item.Click += (_, _) => ShowMain(tool);
                menu.Items.Add(item);
            }
            menu.Items.Add(new NativeMenuItemSeparator());
            var exit = new NativeMenuItem(Text.T("tray.exit"));
            exit.Click += (_, _) => Exit();
            menu.Items.Add(exit);

            _tray = new TrayIcon
            {
                Icon = AppInfo.Icon,
                ToolTipText = "DockPDF",
                Menu = menu,
            };
            _tray.Clicked += (_, _) => ShowRadial();
            TrayIcon.SetIcons(this, new TrayIcons { _tray });
        }
    }

    internal static class Program
    {
        /// <summary>Lo stesso nome sta in installer/DockPDF.iss (AppMutex).</summary>
        public const string RunningMutexName = "DockPDF.Running";

        [STAThread]
        public static int Main(string[] args)
        {
            App.Start = StartOptions.Parse(args);
            // Una sola copia: DockPDF vive nell'area di notifica. Se è già aperto, gli si chiede di mostrarsi.
            DockStudio.Core.Windows.SingleInstance? single = null;
            if (OperatingSystem.IsWindows())
            {
                single = DockStudio.Core.Windows.SingleInstance.TryAcquire(AppInfo.Identity);
                if (single == null)
                {
                    DockStudio.Core.Windows.SingleInstance.RequestShow(AppInfo.Identity);
                    return 0;
                }
                single.ListenForShowRequests(() => Dispatcher.UIThread.Post(() => App.Current?.ShowMain()));
            }
            // Segnale "DockPDF è aperto" per l'installer, che così può chiedere di chiuderlo prima di aggiornare.
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
            AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}
