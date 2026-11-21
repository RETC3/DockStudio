using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using DockSpace.Views;
using DockStudio.Core;
using DockStudio.Core.Localization;
using DockStudio.Core.Suite;
using DockStudio.UI;

namespace DockSpace
{
    /// <summary>
    /// Opzioni da riga di comando: "--scan &lt;percorso&gt;" avvia subito la scansione, "--fast" la vuole veloce (NTFS).
    /// <paramref name="Automatic"/> false (solo nei controlli automatici) evita quello che l'app fa da sola all'apertura:
    /// la domanda sulla privacy, il controllo degli aggiornamenti e le statistiche.
    /// </summary>
    public sealed record StartOptions(string? ScanPath, bool Fast, bool Automatic = true)
    {
        public static StartOptions Parse(string[] args)
        {
            string? path = null;
            bool fast = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--scan" && i + 1 < args.Length) path = args[++i];
                else if (args[i] == "--fast") fast = true;
            }
            return new StartOptions(path, fast);
        }
    }

    public sealed class App : Application
    {
        public static readonly JsonSettingsStore<Settings> SettingsStore = new(AppInfo.Identity);
        public static Settings Settings { get; private set; } = new();
        public static StartOptions Start { get; set; } = new(null, false);

        public override void Initialize()
        {
            Settings = SettingsStore.Load();
            WatchErrors();
            Languages.Set(Settings.Language);
            var appearance = Enum.TryParse<Appearance>(Settings.Appearance, out var a) ? a : Appearance.System;
            DockTheme.Apply(this, AppInfo.Accent, appearance);
        }

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
        private static void WatchErrors()
        {
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, args) =>
            {
                _ = AppInfo.Errors.SendAsync(Settings, args.Exception);
                // Un errore in un'azione non deve chiudere l'app con l'analisi in corso: si mostra e si continua.
                args.Handled = true;
                if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow main })
                    main.ShowError(args.Exception);
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
                desktop.MainWindow = new MainWindow(Start);
                desktop.ShutdownRequested += (_, _) => SettingsStore.Save(Settings);
            }
            // Iscrizione al registro delle app della suite (collegamenti e aggiornamenti coordinati).
            var exe = Environment.ProcessPath;
            if (exe != null) new SuiteRegistry().Register(AppInfo.Identity.Name, AppInfo.Version, exe);
            base.OnFrameworkInitializationCompleted();
        }
    }

    internal static class Program
    {
        /// <summary>Lo stesso nome sta in installer/DockSpace.iss (AppMutex).</summary>
        public const string RunningMutexName = "DockSpace.Running";

        [STAThread]
        public static int Main(string[] args)
        {
            App.Start = StartOptions.Parse(args);
            // Segnale "DockSpace è aperto" per l'installer, che così può chiedere di chiuderlo prima di
            // aggiornare o disinstallare. Non impedisce di aprire più finestre di DockSpace.
            using var running = new System.Threading.Mutex(false, RunningMutexName);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}
