using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using DockStudio.Core.Localization;
using DockStudio.Core.Updates;
using DockStudio.Launcher.Views;
using DockStudio.UI;

namespace DockStudio.Launcher.Checks
{
    public sealed class TestApp : Application
    {
        public override void Initialize() => DockTheme.Apply(this, AppInfo.Accent);
    }

    internal static class Program
    {
        private static int _failures;
        private static string _images = "";

        private static int Main(string[] args)
        {
            _images = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "dockstudio-launcher-checks"));
            Directory.CreateDirectory(_images);
            var work = Path.Combine(Path.GetTempPath(), "dockstudio-launcher-work-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(work);
            try
            {
                Section("Testi", CheckTexts);
                Section("Riga di comando", CheckStartOptions);
                Section("Catalogo delle app", CheckCatalog);
                Section("Versioni e download (feed locale)", () => CheckDownload(work));
                Section("App installate e aperte", CheckInstalls);
                Section("Finestra", CheckWindows);
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { /* resti nella cartella temporanea */ }
            }
            Console.WriteLine();
            Console.WriteLine($"Immagini: {_images}");
            Console.WriteLine(_failures == 0 ? "Tutti i controlli superati." : $"{_failures} controlli NON superati.");
            return _failures == 0 ? 0 : 1;
        }

        private static void CheckTexts()
        {
            var problems = Text.Strings.Problems();
            Check(problems.Count == 0, "italiano e inglese hanno le stesse chiavi e gli stessi segnaposto"
                                       + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));
        }

        private static void CheckStartOptions()
        {
            Check(StartOptions.Parse(new[] { "--tray" }) is { TrayOnly: true, Exit: false }, "--tray");
            Check(StartOptions.Parse(new[] { "--exit" }) is { TrayOnly: false, Exit: true }, "--exit");
            Check(StartOptions.Parse(Array.Empty<string>()) is { TrayOnly: false, Exit: false }, "senza argomenti");
        }

        private static void CheckCatalog()
        {
            var apps = Catalog.Embedded();
            Check(apps.Count == 11, $"elenco incluso: 11 app ({apps.Count})");
            Check(apps.Count(a => a.IsAvailable) == 3, "disponibili: ScreenLove, DockSpace e DockPDF");
            Check(apps[0].Slug == "screenlove" && apps[0].Identity.SiteUrl == "https://screenlove.it" && apps[0].Identity.GitHubRepo == "RETC3/ScreenLove",
                "ScreenLove si scarica da screenlove.it (riserva GitHub)");
            var pdf = apps.First(a => a.Slug == "dockpdf");
            Check(pdf.Identity.SiteUrl == "https://getdockstudio.it/dockpdf" && pdf.RunningMutex == "DockPDF.Running", "DockPDF: getdockstudio.it/dockpdf e segnale DockPDF.Running");
            Check(pdf.Updates.IsTrustedDownload("https://getdockstudio.it/dockpdf/download/DockPDF-Setup-0.2.0.exe")
                  && !pdf.Updates.IsTrustedDownload("https://example.com/DockPDF-Setup-0.2.0.exe"), "si scarica solo dal sito dell'app");

            var odd = Catalog.Parse("""
                { "versione": 1, "app": [
                  { "slug": "../x", "nome": "Strana" },
                  { "slug": "dockcalc", "nome": "DockCalc", "stato": "disponibile", "pagina": "https://altro.example/dockcalc/" },
                  { "slug": "dockmap", "nome": "Dock Map" } ] }
                """);
            Check(odd.Count == 1 && odd[0].Slug == "dockcalc", "slug e nomi non semplici scartati");
            Check(odd[0].Page == "https://getdockstudio.it/dockcalc/", "pagine fuori dal sito sostituite con quella della suite");
            Check(odd[0].Letter == "D" && odd[0].Color == "#2447F5", "lettera e colore predefiniti per un'app nuova");
        }

        private static void CheckDownload(string work)
        {
            // Un "installer" finto e un latest.json locale: niente rete e niente installazione vera.
            var setup = Path.Combine(work, "DockPDF-Setup-0.2.0.exe");
            var bytes = RandomNumberGenerator.GetBytes(300_000);
            File.WriteAllBytes(setup, bytes);
            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var feed = Path.Combine(work, "latest.json");
            void WriteFeed(string hash) => File.WriteAllText(feed, $$"""
                { "tag_name": "v0.2.0", "html_url": "https://getdockstudio.it/dockpdf/novita/", "body": "Novità di prova",
                  "assets": [ { "name": "DockPDF-Setup-0.2.0.exe", "browser_download_url": "{{new Uri(setup).AbsoluteUri}}", "size": {{bytes.Length}}, "sha256": "{{hash}}" } ] }
                """);
            WriteFeed(sha);
            Environment.SetEnvironmentVariable("DOCKPDF_UPDATE_FEED", new Uri(feed).AbsoluteUri);
            try
            {
                var pdf = Catalog.Embedded().First(a => a.Slug == "dockpdf");
                var update = pdf.Updates.CheckAsync(new Version(0, 0, 0)).GetAwaiter().GetResult();
                Check(update?.Version == new Version(0, 2, 0), "non installata: si propone l'ultima versione (0.2.0)");
                Check(pdf.Updates.CheckAsync(new Version(0, 1, 0)).GetAwaiter().GetResult() != null, "0.1.0 installata: aggiornamento a 0.2.0");
                Check(pdf.Updates.CheckAsync(new Version(0, 2, 0)).GetAwaiter().GetResult() == null, "0.2.0 installata: nessun aggiornamento");

                var downloaded = pdf.Updates.DownloadSetupAsync(update!, null).GetAwaiter().GetResult();
                Check(File.ReadAllBytes(downloaded).SequenceEqual(bytes), "installer scaricato e impronta SHA-256 controllata");
                File.Delete(downloaded);

                WriteFeed(new string('0', 64));
                var wrong = pdf.Updates.CheckAsync(new Version(0, 0, 0)).GetAwaiter().GetResult()!;
                bool refused = false;
                try { pdf.Updates.DownloadSetupAsync(wrong, null).GetAwaiter().GetResult(); }
                catch (IOException) { refused = true; }
                Check(refused, "impronta sbagliata: installer rifiutato");
            }
            finally
            {
                Environment.SetEnvironmentVariable("DOCKPDF_UPDATE_FEED", null);
            }
        }

        private static void CheckInstalls()
        {
            var own = typeof(AppInfo).Assembly.Location;
            Check(Installs.ExeVersion(own) == new Version(0, 1, 0), $"versione letta dal file ({Installs.ExeVersion(own)})");
            var fake = new CatalogApp("dockprova", "DockProva" + Guid.NewGuid().ToString("N")[..6], "#000000", "P", "", "disponibile", "https://getdockstudio.it/dockprova/");
            Check(!Installs.IsRunning(fake), "app chiusa: nessun segnale");
            using (new Mutex(false, fake.RunningMutex))
                Check(Installs.IsRunning(fake), "app aperta: segnale trovato");
            var found = Installs.Find(new[] { fake });
            Check(found.Count == 0, "un'app mai installata non risulta installata");
        }

        private static void CheckWindows()
        {
            AppBuilder.Configure<TestApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            var apps = Catalog.Embedded();
            foreach (var slug in new[] { "screenlove", "dockspace", "dockpdf" })
                Check(new AppTile(apps.First(a => a.Slug == slug)).HasIcon, $"icona vera di {slug}");
            Check(!new AppTile(apps.First(a => a.Slug == "dockplay")).HasIcon, "DockPlay: lettera colorata");

            InstalledApp I(string name, string v) => new(name, Version.Parse(v), $@"C:\Programmi\{name}\{name}.exe");
            var installed = new Dictionary<string, InstalledApp>
            {
                ["screenlove"] = I("ScreenLove", "0.6.0"),
                ["dockspace"] = I("DockSpace", "0.1.0"),
            };
            var updates = new Dictionary<string, UpdateInfo>
            {
                ["dockspace"] = new(new Version(0, 1, 1), "https://getdockstudio.it/dockspace/novita/", "", null, null, 0),
            };

            foreach (var language in new[] { "it", "en" })
                foreach (var dark in new[] { false, true })
                {
                    Languages.Set(language);
                    DockTheme.SetAppearance(Application.Current!, dark ? Appearance.Dark : Appearance.Light);
                    string suffix = $"{language}-{(dark ? "scuro" : "chiaro")}";

                    var library = new Library(apps);
                    library.SetForChecks(installed, updates, new Dictionary<string, Job> { ["dockpdf"] = new(InstallPhase.Downloading, 0.42) });
                    var window = new MainWindow(library, StartOptions.None with { Automatic = false }) { Width = 1040, Height = 760 };
                    window.Show();
                    Pump();
                    Save(window, "launcher-" + suffix);

                    if (language == "it" && !dark)
                    {
                        // Installazione fallita, poi tutte installate e aggiornate; poi finestra stretta.
                        library.SetForChecks(installed, updates, new Dictionary<string, Job> { ["dockpdf"] = new(InstallPhase.Checking, 0, Text.F("install.running", "DockPDF")) });
                        Pump();
                        Save(window, "launcher-errore");
                        var all = new Dictionary<string, InstalledApp>(installed) { ["dockpdf"] = I("DockPDF", "0.1.0") };
                        library.SetForChecks(all, new Dictionary<string, UpdateInfo>(), new Dictionary<string, Job>());
                        Pump();
                        Save(window, "launcher-tutte");
                        // Copertina per il sito (site/public/apps/dockstudio/copertina.png): DOCKSTUDIO_COVER=<png>
                        if (Environment.GetEnvironmentVariable("DOCKSTUDIO_COVER") is { Length: > 0 } cover)
                        {
                            window.Width = 1200;
                            window.Height = 760;
                            Pump();
                            window.CaptureRenderedFrame()?.Save(cover, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                            Check(File.Exists(cover), $"copertina {cover}");
                        }
                        window.Width = 460;
                        window.Height = 820;
                        Pump();
                        Save(window, "launcher-stretta");
                        var none = new Library(apps);
                        none.SetForChecks(new Dictionary<string, InstalledApp>(), new Dictionary<string, UpdateInfo>(), new Dictionary<string, Job>());
                        var empty = new MainWindow(none, StartOptions.None with { Automatic = false }) { Width = 1040, Height = 760 };
                        empty.Show();
                        Pump();
                        Save(empty, "launcher-vuoto");
                        empty.Close();
                    }
                    window.Close();
                }
            Languages.Set("it");
        }

        // ---- Aiuti --------------------------------------------------------------------------------------

        private static void Section(string name, Action body)
        {
            Console.WriteLine();
            Console.WriteLine($"== {name}");
            try { body(); }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine($"  ERRORE  {ex}");
            }
        }

        private static void Check(bool ok, string what)
        {
            if (!ok) _failures++;
            Console.WriteLine($"  {(ok ? "ok    " : "NO    ")}{what}");
        }

        private static void Pump(int rounds = 10)
        {
            for (int i = 0; i < rounds; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(15);
            }
            Dispatcher.UIThread.RunJobs();
        }

        private static void Save(Avalonia.Controls.Window window, string name)
        {
            var frame = window.CaptureRenderedFrame();
            var file = Path.Combine(_images, name + ".png");
            frame?.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Check(frame != null && File.Exists(file), $"immagine {name}.png");
        }
    }
}
