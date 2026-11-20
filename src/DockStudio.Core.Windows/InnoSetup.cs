using System;
using System.Diagnostics;
using System.IO;

namespace DockStudio.Core.Windows
{
    /// <summary>Installazione con Inno Setup: com'è installata l'app e come si avvia l'aggiornamento.</summary>
    public static class InnoSetup
    {
        /// <summary>True se l'app è stata installata con l'installer (accanto all'exe c'è il disinstallatore).</summary>
        public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

        /// <summary>
        /// Avvia l'installer senza domande: aspetta che l'app sia chiusa, sostituisce l'exe e la riapre.
        /// Chi chiama deve chiudere l'app subito dopo.
        /// </summary>
        public static void LaunchUpdate(string setupPath)
        {
            Process.Start(new ProcessStartInfo(setupPath, "/SILENT /NOCANCEL /NORESTART /UPDATE=1")
            {
                UseShellExecute = true,
            });
        }
    }
}
