using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DockStudio.Core.Suite;
using DockStudio.Core.Updates;

namespace DockStudio.Launcher
{
    /// <summary>Un'app installata: dove sta l'exe e che versione è.</summary>
    public sealed record InstalledApp(string Name, Version Version, string ExePath);

    /// <summary>Che cosa sta facendo il launcher con un'app (per la barra di avanzamento della scheda).</summary>
    public enum InstallPhase
    {
        Checking,
        Downloading,
        Installing,
    }

    /// <summary>L'app è aperta e non si chiude da sola: va chiusa prima di aggiornarla.</summary>
    public sealed class AppRunningException(string app) : Exception(Text.F("install.running", app));

    /// <summary>
    /// Le app installate e le installazioni fatte dal launcher. Un'app si trova in due modi: il registro della
    /// suite (ogni app si iscrive all'avvio) e, su Windows, l'elenco "App installate" (chiave Uninstall
    /// dell'installer), che c'è anche per un'app appena installata e mai aperta.
    /// </summary>
    public static class Installs
    {
        /// <summary>Le app del catalogo installate su questo computer, per slug.</summary>
        public static Dictionary<string, InstalledApp> Find(IEnumerable<CatalogApp> catalog)
        {
            var registered = new SuiteRegistry().Installed();
            var result = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in catalog)
            {
                string? exe = registered.FirstOrDefault(a => string.Equals(a.Name, app.Name, StringComparison.OrdinalIgnoreCase))?.ExePath;
                if (OperatingSystem.IsWindows() && (exe == null || !File.Exists(exe)))
                    exe = WindowsInstalls.ExePath(app.Name);
                if (exe == null || !File.Exists(exe)) continue;
                result[app.Slug] = new InstalledApp(app.Name, ExeVersion(exe), exe);
            }
            return result;
        }

        /// <summary>Versione scritta nell'exe (quella con cui è stato compilato).</summary>
        public static Version ExeVersion(string exe)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
            }
            catch
            {
                return new Version(0, 0, 0);
            }
        }

        /// <summary>True se l'app è aperta (tiene il suo segnale "è aperta").</summary>
        public static bool IsRunning(CatalogApp app)
        {
            if (!OperatingSystem.IsWindows()) return false;
            if (!Mutex.TryOpenExisting(app.RunningMutex, out var mutex)) return false;
            mutex.Dispose();
            return true;
        }

        /// <summary>Apre l'app, con gli argomenti indicati (per esempio "--radial" per il cerchio di DockPDF).</summary>
        public static void Launch(InstalledApp app, params string[] args)
        {
            var psi = new ProcessStartInfo(app.ExePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(app.ExePath)! };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }

        /// <summary>
        /// Chiede a un'app aperta di chiudersi, se lo sa fare: DockPDF dalla 0.1.1 (resta aperto in background).
        /// Aspetta al massimo 10 secondi; false se è ancora aperta.
        /// </summary>
        public static async Task<bool> AskToExitAsync(CatalogApp app, InstalledApp installed, CancellationToken ct)
        {
            if (!IsRunning(app)) return true;
            if (app.Slug != "dockpdf" || installed.Version < new Version(0, 1, 1)) return false;
            Launch(installed, "--exit");
            for (int i = 0; i < 40 && IsRunning(app); i++) await Task.Delay(250, ct);
            return !IsRunning(app);
        }

        /// <summary>
        /// Installa l'ultima versione dell'app (o la aggiorna): la scarica dal sito controllandone l'impronta,
        /// avvia l'installer senza domande e aspetta che finisca. Restituisce la versione installata, o null se
        /// l'app era già aggiornata.
        /// </summary>
        public static async Task<Version?> InstallAsync(CatalogApp app, InstalledApp? installed, IProgress<(InstallPhase Phase, double Value)> progress,
            CancellationToken ct, UpdateInfo? known = null)
        {
            progress.Report((InstallPhase.Checking, 0));
            var update = known ?? await app.Updates.CheckAsync(installed?.Version ?? new Version(0, 0, 0), ct);
            if (update == null) return null;
            if (installed != null && !await AskToExitAsync(app, installed, ct)) throw new AppRunningException(app.Name);

            var setup = await app.Updates.DownloadSetupAsync(update, new Progress<double>(p => progress.Report((InstallPhase.Downloading, p))), ct);
            try
            {
                // Aperta nel frattempo? L'installer non la può sostituire.
                if (installed != null && IsRunning(app)) throw new AppRunningException(app.Name);
                progress.Report((InstallPhase.Installing, 0));
                await RunSetupAsync(setup, ct);
            }
            finally
            {
                try { File.Delete(setup); } catch { /* resta nella cartella temporanea */ }
            }
            return update.Version;
        }

        /// <summary>
        /// Installer Inno Setup senza finestre né domande, per l'utente corrente: le attività predefinite
        /// dell'installer (collegamento nel menu Start, "Apri con"...) restano quelle scelte di solito.
        /// </summary>
        private static async Task RunSetupAsync(string setup, CancellationToken ct)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            using var process = Process.Start(new ProcessStartInfo(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-")
            {
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException(Text.T("install.notStarted"));
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0) throw new InvalidOperationException(Text.F("install.failed", process.ExitCode));
        }
    }
}
