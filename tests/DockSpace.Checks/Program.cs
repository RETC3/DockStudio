using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using DockSpace;
using DockSpace.Engine;
using DockSpace.Engine.Ntfs;
using DockSpace.Views;
using DockStudio.Core.Localization;
using DockStudio.Core.Updates;
using DockStudio.UI.Windows;
using DockStudio.UI;

namespace DockSpace.Checks
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
            _images = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "dockspace-checks"));
            Directory.CreateDirectory(_images);
            var sample = Path.Combine(Path.GetTempPath(), "dockspace-sample-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                CreateSample(sample);
                Section("Testi", CheckTexts);
                Section("Dimensioni leggibili", CheckSizes);
                Section("Scansione classica", () => CheckClassicScan(sample));
                Section("Interruzione della scansione", () => CheckCancel(sample));
                Section("Spostamento nel Cestino (solo albero)", () => CheckRemove(sample));
                Section("Tipi di file e file più grandi", () => CheckTypes(sample));
                Section("Mappa a riquadri", () => CheckTreemap(sample));
                if (OperatingSystem.IsWindows()) Section("Scansione veloce NTFS", () => CheckNtfs(sample));
                Section("Aggiornamenti", CheckUpdates);
                Section("Statistiche ed errori", CheckPrivacy);
                Section("Finestra principale", () => CheckWindow(sample));
                // Copertina del sito (site/public/apps/dockspace/copertina.png): DOCKSPACE_COVER=<file png>.
                if (Environment.GetEnvironmentVariable("DOCKSPACE_COVER") is { Length: > 0 } cover)
                    Section("Copertina del sito", () => MakeCover(cover));
                Section("Tasti negli elenchi", CheckListKeys);
            }
            finally
            {
                try { Directory.Delete(sample, recursive: true); } catch { /* resti nella cartella temporanea */ }
            }

            Console.WriteLine();
            Console.WriteLine($"Immagini: {_images}");
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

        // ---- Cartella di prova ---------------------------------------------------------------------------
        //   sample/
        //     big.mp4           300 000
        //     note.txt              1 200
        //     vuoto.bin                 0
        //     foto/  a.jpg 50 000, b.png 25 000, raw/ c.cr2 120 000
        //     lavoro/ doc.pdf 80 000, dati.zip 200 000, senza 3 000
        //     collegamento -> foto   (giunzione: da non seguire)
        private const long Total = 300_000 + 1_200 + 0 + 50_000 + 25_000 + 120_000 + 80_000 + 200_000 + 3_000;

        private static void CreateSample(string root)
        {
            void File(string rel, int size)
            {
                var path = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var fs = System.IO.File.Create(path);
                fs.SetLength(size);
            }
            File("big.mp4", 300_000);
            File("note.txt", 1_200);
            File("vuoto.bin", 0);
            File(@"foto/a.jpg", 50_000);
            File(@"foto/b.png", 25_000);
            File(@"foto/raw/c.cr2", 120_000);
            File(@"lavoro/doc.pdf", 80_000);
            File(@"lavoro/dati.zip", 200_000);
            File(@"lavoro/senza", 3_000);
            if (OperatingSystem.IsWindows())
            {
                var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(root, "collegamento")}\" \"{Path.Combine(root, "foto")}\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
                p!.WaitForExit();
            }
        }

        // ---- Controlli -----------------------------------------------------------------------------------

        private static void CheckTexts()
        {
            var problems = Text.Strings.Problems();
            Check(problems.Count == 0, "italiano e inglese hanno le stesse chiavi e gli stessi segnaposto"
                                       + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));
            foreach (FileCategory c in Enum.GetValues<FileCategory>())
                Check(Text.Strings.Get(Palette.Key(c), "en") != Palette.Key(c), $"nome della famiglia {c}");
        }

        private static void CheckSizes()
        {
            var it = CultureInfo.GetCultureInfo("it");
            Check(Sizes.Format(512, it) == "512 byte", "512 byte");
            Check(Sizes.Format(1536, it) == "1,5 KB", "1536 → 1,5 KB: " + Sizes.Format(1536, it));
            Check(Sizes.Format(5L * 1024 * 1024 * 1024, it) == "5 GB", "5 GB");
            Check(Sizes.Format(123L * 1024 * 1024, it) == "123 MB", "123 MB");
            Check(Sizes.Percent(0.1234, it) == "12,3 %", "percentuale");
        }

        private static void CheckClassicScan(string sample)
        {
            var progress = new ScanProgress();
            var root = FolderScanner.Scan(sample, progress);
            // Su disco: ogni file arrotondato ai cluster (i file di prova non sono compressi né sparsi).
            long cluster = ClusterSize(sample);
            long onDisk = SampleSizes.Sum(s => (s + cluster - 1) / cluster * cluster);
            Check(root.OnDisk == onDisk && root.Size == onDisk, $"spazio su disco {root.OnDisk:N0} = {onDisk:N0} (cluster {cluster} byte), misura predefinita");
            Check(root.Length == Total, $"dimensione dei file {root.Length:N0} = {Total:N0}");
            FsTree.UseMeasure(root, SizeMeasure.Length);
            Check(root.Size == Total, $"con la misura 'dimensione dei file' il totale è {Total:N0}");
            Check(root.FileCount == 9, $"9 file (trovati {root.FileCount})");
            Check(root.FolderCount == 3, $"3 cartelle, la giunzione non si segue (trovate {root.FolderCount})");
            Check(progress.Files == 9 && progress.Bytes == Total, "avanzamento coerente con il risultato");
            Check(root.Children.Select(c => c.Size).SequenceEqual(root.Children.Select(c => c.Size).OrderByDescending(s => s)),
                "contenuto ordinato dal più grande");
            Check(root.Children[0].Name == "big.mp4", "primo elemento: big.mp4");
            var foto = root.Children.First(c => c.Name == "foto");
            Check(foto.Size == 195_000 && foto.FileCount == 3 && foto.FolderCount == 1, "totali della cartella foto");
            var raw = foto.Children.First(c => c.Name == "raw");
            Check(raw.Children[0].FullPath == Path.Combine(sample, "foto", "raw", "c.cr2"), "percorso completo");
            Check(Math.Abs(foto.ShareOfParent - 195_000.0 / Total) < 1e-9, "quota nella cartella superiore");
            Check(root.Children.Any(c => c.Name == "vuoto.bin" && c.Size == 0 && c.OnDisk == 0), "file vuoti presenti, senza spazio su disco");
            FsTree.UseMeasure(root, SizeMeasure.OnDisk);
            Check(root.Size == onDisk && root.Children.Select(c => c.Size).SequenceEqual(root.Children.Select(c => c.OnDisk)),
                "si torna allo spazio su disco");
        }

        private static readonly long[] SampleSizes = { 300_000, 1_200, 0, 50_000, 25_000, 120_000, 80_000, 200_000, 3_000 };

        private static long ClusterSize(string path)
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path))!;
            return GetDiskFreeSpaceW(root, out uint spc, out uint bps, out _, out _) ? (long)spc * bps : 4096;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool GetDiskFreeSpaceW(string root, out uint spc, out uint bps, out uint free, out uint total);

        private static void CheckCancel(string sample)
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            bool canceled = false;
            try { FolderScanner.Scan(sample, null, cts.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "una scansione annullata si ferma con OperationCanceledException");
        }

        private static void CheckRemove(string sample)
        {
            var root = FolderScanner.Scan(sample);
            FsTree.UseMeasure(root, SizeMeasure.Length);
            var foto = root.Children.First(c => c.Name == "foto");
            var raw = foto.Children.First(c => c.Name == "raw");
            Check(FsTree.Remove(raw), "rimozione di una cartella");
            Check(root.Size == Total - 120_000 && root.FileCount == 8 && root.FolderCount == 2, "totali della radice aggiornati");
            Check(foto.Size == 75_000 && foto.FileCount == 2 && foto.FolderCount == 0, "totali della cartella aggiornati");
            Check(raw.Parent == null && !FsTree.Remove(raw), "un elemento tolto non si toglie due volte");
            Check(!FsTree.Remove(root), "la radice non si toglie");
            var lavoro = root.Children.First(c => c.Name == "lavoro");
            FsTree.Remove(lavoro.Children.First(c => c.Name == "dati.zip"));
            Check(root.Children.Select(c => c.Size).SequenceEqual(root.Children.Select(c => c.Size).OrderByDescending(s => s)),
                "ordine della radice ancora dal più grande dopo le rimozioni");
        }

        private static void CheckTypes(string sample)
        {
            var root = FolderScanner.Scan(sample);
            FsTree.UseMeasure(root, SizeMeasure.Length);
            var ext = FileTypes.ByExtension(root);
            Check(ext[0].Extension == "mp4" && ext[0].Category == FileCategory.Video, "estensione più grande: mp4 (video)");
            Check(ext.Sum(e => e.Size) == Total && ext.Sum(e => e.Count) == 9, "i totali per estensione tornano");
            Check(ext.Any(e => e.Extension == "" && e.Size == 3_000), "file senza estensione");
            var cats = FileTypes.ByCategory(ext);
            Check(cats.First(c => c.Category == FileCategory.Image).Size == 195_000, "immagini: jpg + png + cr2");
            var largest = FileTypes.Largest(root, 3);
            Check(largest.Select(f => f.Name).SequenceEqual(new[] { "big.mp4", "dati.zip", "c.cr2" }), "i 3 file più grandi, in ordine");
            Check(FileTypes.Largest(root, 100).Count == 9, "con un limite alto si hanno tutti i file");
        }

        private static void CheckTreemap(string sample)
        {
            var root = FolderScanner.Scan(sample);
            FsTree.UseMeasure(root, SizeMeasure.Length);
            var bounds = new TreemapRect(0, 0, 800, 500);
            var top = Treemap.Squarify(root.Children, bounds).ToList();
            Check(top.Count == root.Children.Count(c => c.Size > 0), "un riquadro per ogni elemento non vuoto");
            double area = top.Sum(t => t.Rect.Area);
            Check(Math.Abs(area - bounds.Area) < 1, $"i riquadri coprono tutta l'area ({area:0} di {bounds.Area:0})");
            foreach (var (node, rect) in top)
            {
                double expected = bounds.Area * node.Size / root.Size;
                if (Math.Abs(rect.Area - expected) > 1) { Check(false, $"area proporzionale per {node.Name}"); return; }
            }
            Check(true, "aree proporzionali alle dimensioni");
            bool overlap = false;
            for (int i = 0; i < top.Count; i++)
                for (int j = i + 1; j < top.Count; j++)
                {
                    var a = top[i].Rect; var b = top[j].Rect;
                    if (a.X + 0.01 < b.X + b.Width && b.X + 0.01 < a.X + a.Width && a.Y + 0.01 < b.Y + b.Height && b.Y + 0.01 < a.Y + a.Height) overlap = true;
                }
            Check(!overlap, "nessuna sovrapposizione tra i riquadri");
            // Gli elementi minuscoli finiscono per forza in una striscia sottile: si guardano quelli sopra il 5%.
            double worst = top.Where(t => t.Rect.Area > bounds.Area * 0.05)
                .Max(t => Math.Max(t.Rect.Width / t.Rect.Height, t.Rect.Height / t.Rect.Width));
            Check(worst < 3, $"riquadri grandi quasi quadrati (rapporto peggiore {worst:0.0})");

            var tiles = Treemap.Layout(root, bounds);
            var c = tiles.FirstOrDefault(t => t.Node.Name == "c.cr2");
            Check(c.Node != null && c.Depth == 2, "i file dentro le sottocartelle sono nella mappa, alla profondità giusta");
            var hit = Treemap.HitTest(tiles, c.Rect.X + c.Rect.Width / 2, c.Rect.Y + c.Rect.Height / 2);
            Check(hit?.Node.Name == "c.cr2", "il clic trova il riquadro più interno");
        }

        private static void CheckNtfs(string sample)
        {
            var system = Path.GetPathRoot(Environment.SystemDirectory)!;
            Check(NtfsScanner.IsNtfsDriveRoot(system), $"{system} è un disco NTFS");
            Check(!NtfsScanner.IsNtfsDriveRoot(Path.Combine(system, "Windows")), "una cartella non è la radice di un disco");
            if (!Shell.IsAdministrator)
            {
                bool denied = false;
                try { NtfsScanner.Scan(system); }
                catch (UnauthorizedAccessException) { denied = true; }
                Check(denied, "senza amministratore: UnauthorizedAccessException (l'app propone il riavvio)");
                Console.WriteLine("  (avvia i controlli come amministratore per confrontare la scansione veloce con quella classica)");
                return;
            }

            var watch = Stopwatch.StartNew();
            var fast = NtfsScanner.Scan(system);
            var fastTime = watch.Elapsed;
            Console.WriteLine($"  scansione veloce: {fastTime.TotalSeconds:0.0} s, {fast.FileCount:N0} file, {fast.FolderCount:N0} cartelle, {fast.Size:N0} byte");
            Check(fast.FileCount > 10_000, "trovati molti file");

            // Confronto sulla cartella di prova: file nostri, senza collegamenti fisici. (Cartelle di sistema come
            // Windows\Fonts non vanno bene: molti file sono collegati anche in WinSxS e la scansione veloce li
            // conta una volta sola, nella prima posizione.)
            var classic = FolderScanner.Scan(sample);
            var node = Find(fast, sample);
            Check(node != null, "la cartella di prova è nell'albero veloce");
            if (node != null)
            {
                Check(node.FileCount == classic.FileCount, $"stessi file ({node.FileCount} / {classic.FileCount})");
                Check(node.Length == classic.Length, $"stessa dimensione dei file ({node.Length:N0} / {classic.Length:N0})");
                Check(node.OnDisk == classic.OnDisk, $"stesso spazio su disco ({node.OnDisk:N0} / {classic.OnDisk:N0})");
                var raw = Find(fast, Path.Combine(sample, "foto", "raw"));
                Check(raw?.Children.FirstOrDefault()?.Name == "c.cr2" && raw.Length == 120_000, "sottocartelle e nomi uguali");
                Console.WriteLine($"  disco intero: {Sizes.Format(fast.OnDisk)} su disco, {Sizes.Format(fast.Length)} di file");
            }
        }

        private static void CheckPrivacy()
        {
            var s = new Settings();
            Check(!s.PrivacyChoiceMade && !UsageStats.Allowed(s) && !ErrorReports.Allowed(s), "finché non si sceglie non parte nulla");
            Check(!UsageStats.AddUsageMinute(s) && s.UsageMinutes == 0, "senza scelta i minuti non si contano");
            s.ApplyPrivacy(usage: true, errors: false);
            Check(UsageStats.Allowed(s) && !ErrorReports.Allowed(s), "scelta: solo statistiche");
            Check(UsageStats.AddUsageMinute(s) && s.UsageMinutes == 1, "con le statistiche i minuti si contano");
            UsageStats.EnsureInstallId(s);
            Check(UsageStats.IsValidId(s.InstallId), "codice casuale creato");
            s.ApplyPrivacy(usage: false, errors: true);
            Check(s.InstallId == null && s.UsageMinutes == 0 && ErrorReports.Allowed(s), "togliendo le statistiche si cancellano codice e minuti");

            Check(AppInfo.Usage.PingUrl == "https://getdockstudio.it/api/ping", "statistiche verso getdockstudio.it/api/ping");
            Check(AppInfo.Errors.ReportUrl == "https://getdockstudio.it/api/errore", "errori verso getdockstudio.it/api/errore");
            var ping = UsageStats.BuildPing(AppInfo.Identity.StatsName, new string('a', 32), new Version(0, 1, 0), "x64", true, 12);
            Check(ping.Contains("\"app\":\"dockspace\""), "il ping porta il nome dell'app: " + ping);
            var report = ErrorReports.BuildReport(AppInfo.Identity.StatsName, new InvalidOperationException(@"C:\Users\Mario\segreto.txt"),
                new Version(0, 1, 0), "x64", "Windows");
            Check(report.Contains("dockspace") && !report.Contains("Mario"), "la segnalazione non contiene il messaggio dell'errore");
        }

        private static void CheckUpdates()
        {
            var client = AppInfo.Updates;
            Check(client.Feeds.First() == "https://getdockstudio.it/dockspace/latest.json", "fonte principale: getdockstudio.it/dockspace/latest.json");
            Check(client.Feeds.Contains("https://dockstudio.pages.dev/dockspace/latest.json"), "riserva: dockstudio.pages.dev");
            Check(client.IsTrustedDownload("https://getdockstudio.it/dockspace/download/DockSpace-Setup-9.0.0.exe"), "download accettato dal sito");
            Check(!client.IsTrustedDownload("https://getdockstudio.it/dockpdf/download/DockSpace-Setup-9.0.0.exe"), "download di un'altra app rifiutato");
            Check(!client.IsTrustedDownload("https://example.com/download/DockSpace-Setup-9.0.0.exe"), "download da un altro sito rifiutato");

            // Il formato scritto da scripts/make-app-latest.ps1.
            var dir = Path.Combine(Path.GetTempPath(), "dockspace-update-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                var setup = Path.Combine(dir, "DockSpace-Setup-9.0.0.exe");
                File.WriteAllBytes(setup, new byte[] { 1, 2, 3, 4, 5 });
                string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(setup))).ToLowerInvariant();
                string Json(string url, string hash) =>
                    "{ \"tag_name\": \"v9.0.0\", \"name\": \"DockSpace 9.0.0\", \"html_url\": \"https://getdockstudio.it/dockspace/\", " +
                    "\"body\": \"Novità di prova\", \"assets\": [ { \"name\": \"DockSpace-Setup-9.0.0.exe\", \"size\": 5, " +
                    $"\"browser_download_url\": \"{url}\", \"sha256\": \"{hash}\" }} ] }}";
                var site = client.Parse(Json("https://getdockstudio.it/dockspace/download/DockSpace-Setup-9.0.0.exe", sha), new Version(0, 1, 0));
                Check(site?.Version == new Version(9, 0, 0) && site.SetupSha256 == sha && site.Notes == "Novità di prova", "latest.json letto");
                Check(site?.PageUrl == "https://getdockstudio.it/dockspace/", "pagina delle novità sul sito");
                Check(client.Parse(Json("x", sha), new Version(9, 0, 0)) == null, "nessun aggiornamento se la versione è la stessa");

                // Download vero da un feed locale (variabile DOCKSPACE_UPDATE_FEED), con controllo dell'impronta.
                var feed = Path.Combine(dir, "latest.json");
                Environment.SetEnvironmentVariable("DOCKSPACE_UPDATE_FEED", new Uri(feed).AbsoluteUri);
                File.WriteAllText(feed, Json(new Uri(setup).AbsoluteUri, sha));
                var update = client.CheckAsync().GetAwaiter().GetResult();
                Check(update != null, "aggiornamento trovato dal feed locale");
                var downloaded = client.DownloadSetupAsync(update!, null).GetAwaiter().GetResult();
                Check(File.ReadAllBytes(downloaded).Length == 5, "installer scaricato e impronta verificata");
                File.WriteAllText(feed, Json(new Uri(setup).AbsoluteUri, new string('0', 64)));
                bool rejected = false;
                try { client.DownloadSetupAsync(client.CheckAsync().GetAwaiter().GetResult()!, null).GetAwaiter().GetResult(); }
                catch (IOException) { rejected = true; }
                Check(rejected, "installer con impronta sbagliata rifiutato");
            }
            finally
            {
                Environment.SetEnvironmentVariable("DOCKSPACE_UPDATE_FEED", null);
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static FsNode? Find(FsNode root, string path)
        {
            var rel = Path.GetRelativePath(root.Name, path);
            var node = root;
            foreach (var part in rel.Split(Path.DirectorySeparatorChar))
            {
                node = node.Children.FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
                if (node == null) return null;
            }
            return node;
        }

        /// <summary>
        /// La copertina della pagina di DockSpace sul sito: la finestra vera, in italiano e tema chiaro, su C:\Windows
        /// (cartella uguale su ogni PC, senza nomi personali), con la cartella più grande aperta.
        /// </summary>
        private static void MakeCover(string file)
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var root = FolderScanner.Scan(folder);
            Languages.Set("it");
            DockTheme.SetAppearance(Application.Current!, Appearance.Light);
            var window = new MainWindow(new StartOptions(null, false, Automatic: false)) { Width = 1200, Height = 760 };
            window.Show();
            window.ShowResult(root);
            Pump();
            var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.GetFullPath(file), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Check(File.Exists(file), $"copertina {file} ({root.FileCount:N0} file in {folder})");
            window.Close();
        }

        /// <summary>
        /// Righe degli elenchi riciclate: scorrendo con le frecce Avalonia chiede per un attimo righe vuote
        /// (errore segnalato il 2026-11-25: NullReferenceException in Swatch). Cartella con molti file, così le
        /// righe si riciclano davvero.
        /// </summary>
        private static void CheckListKeys()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dockspace-keys-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                for (int i = 0; i < 400; i++)
                {
                    using var fs = File.Create(Path.Combine(dir, $"file{i:000}.{(i % 3 == 0 ? "jpg" : i % 3 == 1 ? "pdf" : "zip")}"));
                    fs.SetLength(1000 + i * 37);
                }
                Directory.CreateDirectory(Path.Combine(dir, "sotto"));
                var root = FolderScanner.Scan(dir);
                Languages.Set("it");
                var window = new MainWindow(new StartOptions(null, false, Automatic: false)) { Width = 1180, Height = 760 };
                window.Show();
                window.ShowResult(root);
                Pump();

                foreach (var template in window.RowTemplatesForChecks)
                {
                    bool ok;
                    try { ok = template.Build(null) != null; }
                    catch (Exception ex) { ok = false; Console.WriteLine("    " + ex.GetType().Name + ": " + ex.Message); }
                    Check(ok, $"riga vuota senza errori ({template.GetType().Name})");
                }

                window.FocusContentList();
                Pump();
                Exception? error = null;
                try
                {
                    foreach (var key in new[] { (Avalonia.Input.Key.Down, 150), (Avalonia.Input.Key.PageDown, 5), (Avalonia.Input.Key.Up, 80),
                                                (Avalonia.Input.Key.End, 1), (Avalonia.Input.Key.Home, 1), (Avalonia.Input.Key.PageUp, 3) })
                        for (int i = 0; i < key.Item2; i++)
                        {
                            window.KeyPress(key.Item1, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                            window.KeyRelease(key.Item1, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                            Dispatcher.UIThread.RunJobs();
                        }
                                    }
                catch (Exception ex) { error = ex; }
                Check(error == null, "frecce, pagina su/giù, Home e Fine nell'elenco senza errori" + (error != null ? $": {error.GetType().Name}: {error.Message}" : ""));
                Check(window.ContentSelectedIndex == 0, $"dopo Home/PagUp la selezione torna in cima (riga {window.ContentSelectedIndex})");
                window.Close();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static void CheckWindow(string sample)
        {
            AppBuilder.Configure<TestApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            var root = FolderScanner.Scan(sample);
            foreach (var language in new[] { "it", "en" })
                foreach (var dark in new[] { false, true })
                {
                    Languages.Set(language);
                    DockTheme.SetAppearance(Application.Current!, dark ? Appearance.Dark : Appearance.Light);
                    var window = new MainWindow(new StartOptions(null, false, Automatic: false)) { Width = 1180, Height = 760 };
                    window.Show();
                    Pump();
                    Save(window, $"benvenuto-{language}-{(dark ? "scuro" : "chiaro")}");
                    window.ShowResult(root);
                    Pump();
                    Save(window, $"contenuto-{language}-{(dark ? "scuro" : "chiaro")}");
                    window.ShowTab(1);
                    Pump();
                    Save(window, $"tipi-{language}-{(dark ? "scuro" : "chiaro")}");
                    window.ShowTab(2);
                    Pump();
                    Save(window, $"grandi-{language}-{(dark ? "scuro" : "chiaro")}");
                    window.Close();

                    var privacy = new PrivacyWindow(AppInfo.Branding);
                    privacy.Show();
                    Pump();
                    Save(privacy, $"privacy-{language}-{(dark ? "scuro" : "chiaro")}");
                    privacy.Close();
                }
            Languages.Set("it");
        }

        private static void Pump()
        {
            // Le schede "tipi" e "più grandi" si calcolano su un altro thread: si aspetta un attimo.
            for (int i = 0; i < 10; i++)
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
