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
    /// <summary>Opzioni da riga di comando: "--scan &lt;percorso&gt;" avvia subito la scansione, "--fast" la vuole veloce (NTFS).</summary>
    public sealed record StartOptions(string? ScanPath, bool Fast)
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
            Languages.Set(Settings.Language);
            var appearance = Enum.TryParse<Appearance>(Settings.Appearance, out var a) ? a : Appearance.System;
            DockTheme.Apply(this, AppInfo.Accent, appearance);
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
        [STAThread]
        public static int Main(string[] args)
        {
            App.Start = StartOptions.Parse(args);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}
