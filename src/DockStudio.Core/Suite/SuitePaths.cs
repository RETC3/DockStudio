using System;
using System.IO;

namespace DockStudio.Core.Suite
{
    /// <summary>
    /// Cartelle condivise da tutte le app DockStudio: licenza e registro delle app installate.
    /// Quella dell'utente vale per lui solo; quella del computer la scrive l'installer aziendale per tutti.
    /// </summary>
    public static class SuitePaths
    {
        public const string SuiteName = "DockStudio";

        /// <summary>Variabile d'ambiente per le prove: sostituisce entrambe le cartelle con una sola.</summary>
        public static string? Override => Environment.GetEnvironmentVariable("DOCKSTUDIO_SUITE_FOLDER");

        /// <summary>%APPDATA%\DockStudio su Windows (~/.config/DockStudio su macOS e Linux).</summary>
        public static string UserFolder => Override ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), SuiteName);

        /// <summary>%ProgramData%\DockStudio su Windows. Su macOS la cartella giusta la sceglierà DockStudio.Core.Mac.</summary>
        public static string MachineFolder => Override ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), SuiteName);
    }
}
