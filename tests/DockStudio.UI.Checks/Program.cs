using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DockStudio.Core;
using DockStudio.Core.Licensing;
using DockStudio.Core.Localization;
using DockStudio.Core.Suite;
using DockStudio.Core.Updates;
using DockStudio.UI;
using DockStudio.UI.Windows;

namespace DockStudio.UI.Checks
{
    public sealed class TestApp : Application
    {
        public override void Initialize() => DockTheme.Apply(this, Color.Parse("#FF3D7F"));
    }

    internal static class Program
    {
        private static int _failures;
        private static string _dir = "";

        private static readonly AppIdentity Identity = new("ScreenLove", "https://screenlove.it", null, null, @"^ScreenLove-Setup-[\d.]+\.exe$", "DOCKSTUDIO_UI_CHECKS");
        private static readonly DockBranding Branding = new(Identity, Color.Parse("#FF3D7F"), "Catture, editor e video dello schermo.");

        private static int Main(string[] args)
        {
            _dir = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "dockstudio-ui-checks"));
            Directory.CreateDirectory(_dir);
            Console.WriteLine($"Cartella delle immagini: {_dir}");

            AppBuilder.Configure<TestApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            Section("Testi dell'interfaccia", CheckTexts);
            foreach (var language in new[] { "it", "en" })
                foreach (var variant in new[] { "chiaro", "scuro" })
                    Section($"Finestre ({language}, {variant})", () => CheckWindows(language, variant));
            Section("Finestra della licenza: inserimento", CheckLicenseInstall);

            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "Tutti i controlli superati." : $"{_failures} controlli NON superati.");
            return _failures == 0 ? 0 : 1;
        }

        private static void Section(string name, Action body)
        {
            Console.WriteLine();
            Console.WriteLine($"== {name}");
            try { body(); }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERRORE  eccezione: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                _failures++;
            }
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "OK    " : "ERRORE")}  {what}");
            if (!ok) _failures++;
        }

        private static void CheckTexts()
        {
            var problems = UiText.Strings.Problems();
            Check(problems.Count == 0, "italiano e inglese hanno le stesse chiavi e gli stessi segnaposto"
                                       + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));
        }

        private static UpdateInfo SampleUpdate() => new(new Version(0, 6, 0), "https://screenlove.it/novita",
            "- Nuovo: editor con le maniglie.\n- Migliorato: memoria più leggera.\n- Corretto: cattura su più monitor.",
            "https://screenlove.it/download/ScreenLove-Setup-0.6.0.exe", "ScreenLove-Setup-0.6.0.exe", 75_000_000);

        private static LicenseInfo SampleLicense() => new()
        {
            Id = "DS-7KQ2-M9XF-3TPA",
            Licensee = "Rossi Srl",
            Seats = 10,
            Issued = new DateTime(2026, 11, 1),
            Expires = new DateTime(2027, 12, 31),
        };

        private static void CheckWindows(string language, string variant)
        {
            Languages.Set(language);
            DockTheme.SetAppearance(Application.Current!, variant == "scuro" ? Appearance.Dark : Appearance.Light);

            var (priv, pub) = LicenseFile.CreateKeyPair();
            var licenses = new LicenseManager("ScreenLove", pub, () => new DateTime(2027, 1, 15));
            var business = licenses.Evaluate(LicenseFile.Sign(SampleLicense(), priv));
            var apps = new List<SuiteApp>
            {
                new() { Name = "ScreenLove", Version = "0.6.0", ExePath = "x", Platform = "windows" },
                new() { Name = "DockPDF", Version = "1.0.0", ExePath = "y", Platform = "windows" },
            };

            var client = new UpdateClient(Identity, typeof(Program).Assembly);
            var windows = new (string Name, Window Window)[]
            {
                ("informazioni", new AboutWindow(Branding, new Version(0, 6, 0), false, business, apps, () => { })),
                ("informazioni-gratuita", new AboutWindow(Branding, new Version(0, 6, 0), true, new LicenseCheck(LicenseStatus.None, null, null), Array.Empty<SuiteApp>())),
                ("aggiornamento", new UpdateWindow(Branding, client, SampleUpdate(), _ => { })),
                ("aggiornamento-portatile", new UpdateWindow(Branding, client, SampleUpdate(), null)),
                ("licenza", WithLicense(new LicenseWindow(Branding, licenses), business)),
            };

            foreach (var (name, window) in windows)
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var frame = window.CaptureRenderedFrame();
                string file = Path.Combine(_dir, $"{name}-{language}-{variant}.png");
                frame?.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);

                var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.Length > 0).ToList();
                var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible).Select(b => b.Content as string ?? "").ToList();
                var all = texts.Concat(buttons).ToList();
                var untranslated = all.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^[a-z]+\.[a-zA-Z]+$")).ToList();
                bool fits = window.Bounds.Height <= 720 && window.Bounds.Width <= 520;
                Check(frame != null && untranslated.Count == 0 && fits,
                    $"{name}: disegnata ({window.Bounds.Width:0}×{window.Bounds.Height:0}), nessun testo senza traduzione"
                    + (untranslated.Count > 0 ? " — " + string.Join(", ", untranslated) : ""));
                window.Close();
            }

            var englishWords = new[] { "Close", "Version", "License" };
            var italianWords = new[] { "Chiudi", "Versione", "Licenza" };
            var about = new AboutWindow(Branding, new Version(0, 6, 0), false, business, apps);
            about.Show();
            Dispatcher.UIThread.RunJobs();
            var words = string.Join(" ", about.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            var expected = language == "it" ? italianWords : englishWords;
            Check(expected.All(words.Contains), $"testi nella lingua giusta ({string.Join(", ", expected)})");
            Check(words.Contains("DockPDF 1.0.0") && !words.Contains("ScreenLove 0.6.0"), "elenco delle app: le altre app, non quella in uso");
            about.Close();
        }

        private static LicenseWindow WithLicense(LicenseWindow window, LicenseCheck check)
        {
            window.Show(check);
            return window;
        }

        private static void CheckLicenseInstall()
        {
            Languages.Set("it");
            var folder = Path.Combine(Path.GetTempPath(), "dockstudio-ui-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("DOCKSTUDIO_SUITE_FOLDER", folder);
            try
            {
                var (priv, pub) = LicenseFile.CreateKeyPair();
                var licenses = new LicenseManager("ScreenLove", pub, () => new DateTime(2027, 1, 15));
                var window = new LicenseWindow(Branding, licenses);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                string Words() => string.Join(" ", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

                Check(Words().Contains("Versione gratuita"), "senza licenza: versione gratuita");
                Check(window.Install("non è una licenza").Status == LicenseStatus.Invalid && Words().Contains("non è una licenza valida"),
                    "file sbagliato: rifiutato con un messaggio");
                Check(window.Install(LicenseFile.Sign(SampleLicense(), priv)).Status == LicenseStatus.Valid
                      && Words().Contains("Rossi Srl") && Words().Contains("Licenza installata"),
                    "licenza giusta: installata e mostrata");
                Check(File.Exists(Path.Combine(folder, LicenseFile.FileName)), "file salvato nella cartella della suite");
                window.CaptureRenderedFrame()?.Save(Path.Combine(_dir, "licenza-installata-it.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                window.Close();
            }
            finally
            {
                Environment.SetEnvironmentVariable("DOCKSTUDIO_SUITE_FOLDER", null);
                try { Directory.Delete(folder, true); } catch { }
            }
        }
    }
}
