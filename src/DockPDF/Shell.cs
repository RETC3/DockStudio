using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DockPDF
{
    /// <summary>Azioni sul sistema: aprire un file, mostrarlo nella cartella, installer e posizione del mouse.</summary>
    public static class Shell
    {
        public static void Open(string path)
        {
            if (OperatingSystem.IsMacOS()) Run("open", path);
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        /// <summary>Apre la cartella che contiene il file, con il file selezionato.</summary>
        public static void Reveal(string path)
        {
            if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (OperatingSystem.IsMacOS()) Run("open", "-R", path);
            else Open(Path.GetDirectoryName(path) ?? path);
        }

        /// <summary>True se DockPDF è stato installato con l'installer (accanto all'exe c'è il disinstallatore).</summary>
        public static bool IsInstalled => OperatingSystem.IsWindows() && DockStudio.Core.Windows.InnoSetup.IsInstalled;

        /// <summary>
        /// Avvia l'installer scaricato senza domande: aspetta che DockPDF sia chiuso, sostituisce l'exe e lo
        /// riapre. Chi chiama deve chiudere l'app subito dopo.
        /// </summary>
        public static void LaunchUpdate(string setupPath)
        {
            if (OperatingSystem.IsWindows()) DockStudio.Core.Windows.InnoSetup.LaunchUpdate(setupPath);
        }

        /// <summary>
        /// La scelta fatta nella pagina "Aiutaci a migliorare" dell'installer (che poi si cancella), o null se
        /// non c'è: aggiornamento, installazione silenziosa, versione portatile, macOS.
        /// </summary>
        public static (bool Usage, bool Errors)? TakeInstallerPrivacyChoice()
        {
            if (!OperatingSystem.IsWindows()) return null;
            return DockStudio.Core.Windows.InstallerPrivacyChoice.Take(AppInfo.Identity) is { } c ? (c.UsageStats, c.ErrorReports) : null;
        }

        /// <summary>Posizione del mouse sullo schermo in pixel fisici, se il sistema la dà.</summary>
        public static (int X, int Y)? CursorPosition()
        {
            if (!OperatingSystem.IsWindows()) return null;
            return GetCursorPos(out var p) ? (p.X, p.Y) : null;
        }

        private static void Run(string file, params string[] args)
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT point);
    }
}
