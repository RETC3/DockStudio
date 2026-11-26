using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DockPost.Engine;
using DockPost.Mail;
using DockPost.Views;
using DockStudio.Core;
using DockStudio.Core.Localization;
using DockStudio.Core.Suite;
using DockStudio.UI;

namespace DockPost
{
    /// <summary>
    /// Opzioni da riga di comando. "--sample" apre una casella di prova senza server (in una cartella temporanea):
    /// per provare l'interfaccia e per le immagini del sito. <paramref name="Automatic"/> false (solo nei controlli
    /// automatici) evita la domanda sulla privacy, il controllo degli aggiornamenti e le statistiche.
    /// </summary>
    public sealed record StartOptions(bool Sample, bool Automatic = true)
    {
        public static StartOptions Parse(string[] args) =>
            new(Array.Exists(args, a => a == "--sample"));
    }

    public sealed class App : Application
    {
        public static readonly JsonSettingsStore<Settings> SettingsStore = new(AppInfo.Identity);
        public static Settings Settings { get; private set; } = new();
        public static StartOptions Start { get; set; } = new(false);

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

        private static void WatchErrors()
        {
            Dispatcher.UIThread.UnhandledException += (_, args) =>
            {
                _ = AppInfo.Errors.SendAsync(Settings, args.Exception);
                args.Handled = true;
                if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow main })
                    main.ShowError(args.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is not Exception ex) return;
                try { System.Threading.Tasks.Task.Run(() => AppInfo.Errors.SendAsync(Settings, ex, fatal: true, TimeSpan.FromSeconds(3))).Wait(4000); }
                catch { }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                _ = AppInfo.Errors.SendAsync(Settings, args.Exception);
                args.SetObserved();
            };
        }

        /// <summary>La sessione della posta: quella vera nella cartella dei dati, o la casella di prova.</summary>
        public static MailSession CreateSession(StartOptions start)
        {
            if (start.Sample)
            {
                var folder = Path.Combine(Path.GetTempPath(), "DockPost-prova-" + Guid.NewGuid().ToString("N")[..8]);
                var session = new MailSession(folder, new MemorySecretStore(), null, offline: true);
                var account = SampleMailbox.Fill(session.Store, Languages.Current == "it");
                session.Accounts.Add(account);
                return session;
            }
            ISecretStore secrets = OperatingSystem.IsWindows() ? new Platform.WindowsCredentialStore() : new MemorySecretStore();
            MicrosoftSignIn? microsoft = null;
            if (OperatingSystem.IsWindows())
            {
                var cache = new Platform.ProtectedFile(Path.Combine(AppInfo.Identity.DataFolder, "microsoft.cache"));
                microsoft = new MicrosoftSignIn(cache.Load, cache.Save);
            }
            return new MailSession(AppInfo.Identity.DataFolder, secrets, microsoft);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var session = CreateSession(Start);
                desktop.MainWindow = new MainWindow(session, Start);
                desktop.ShutdownRequested += (_, _) =>
                {
                    SettingsStore.Save(Settings);
                    try { session.DisposeAsync().AsTask().Wait(3000); } catch { }
                };
            }
            var exe = Environment.ProcessPath;
            if (exe != null && !Start.Sample) new SuiteRegistry().Register(AppInfo.Identity.Name, AppInfo.Version, exe);
            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>Riporta in primo piano la finestra (seconda copia avviata).</summary>
        public void ShowMain()
        {
            if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }) return;
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Show();
            main.Activate();
        }
    }

    internal static class Program
    {
        /// <summary>Lo stesso nome sta in installer/DockPost.iss (AppMutex).</summary>
        public const string RunningMutexName = "DockPost.Running";

        [STAThread]
        public static int Main(string[] args)
        {
            App.Start = StartOptions.Parse(args);
            DockStudio.Core.Windows.SingleInstance? single = null;
            if (OperatingSystem.IsWindows() && !App.Start.Sample)
            {
                // Una sola finestra della posta: una seconda copia riporta in primo piano quella aperta.
                single = DockStudio.Core.Windows.SingleInstance.TryAcquire(AppInfo.Identity);
                if (single == null)
                {
                    DockStudio.Core.Windows.SingleInstance.RequestShow(AppInfo.Identity);
                    return 0;
                }
                single.ListenForShowRequests(() => Dispatcher.UIThread.Post(() => (Application.Current as App)?.ShowMain()));
            }
            using var running = new System.Threading.Mutex(false, RunningMutexName);
            try
            {
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally { if (OperatingSystem.IsWindows()) single?.Dispose(); }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}
