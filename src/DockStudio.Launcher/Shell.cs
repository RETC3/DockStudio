using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DockStudio.Launcher
{
    /// <summary>Azioni sul sistema: pagine web, installer del launcher, avvio con Windows, privacy dell'installer.</summary>
    public static class Shell
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* nessun browser: pazienza */ }
        }

        /// <summary>True se il launcher è stato installato con l'installer (accanto all'exe c'è il disinstallatore).</summary>
        public static bool IsInstalled => OperatingSystem.IsWindows() && DockStudio.Core.Windows.InnoSetup.IsInstalled;

        /// <summary>Avvia l'installer del launcher scaricato: aspetta che DockStudio sia chiuso e lo riapre.</summary>
        public static void LaunchUpdate(string setupPath)
        {
            if (OperatingSystem.IsWindows()) DockStudio.Core.Windows.InnoSetup.LaunchUpdate(setupPath);
        }

        public static (bool Usage, bool Errors)? TakeInstallerPrivacyChoice()
        {
            if (!OperatingSystem.IsWindows()) return null;
            return DockStudio.Core.Windows.InstallerPrivacyChoice.Take(AppInfo.Identity) is { } c ? (c.UsageStats, c.ErrorReports) : null;
        }

        /// <summary>True se DockStudio parte all'accesso a Windows (nell'area di notifica, senza finestra).</summary>
        public static bool StartsWithWindows => OperatingSystem.IsWindows() && ReadRun() != null;

        /// <summary>Attiva o toglie l'avvio all'accesso (la stessa voce che scrive l'installer).</summary>
        public static void SetStartWithWindows(bool on)
        {
            if (!OperatingSystem.IsWindows()) return;
            WriteRun(on);
        }

        [SupportedOSPlatform("windows")]
        private static string? ReadRun()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue("DockStudio") as string;
            }
            catch
            {
                return null;
            }
        }

        [SupportedOSPlatform("windows")]
        private static void WriteRun(bool on)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on && Environment.ProcessPath is { } exe) key.SetValue("DockStudio", $"\"{exe}\" --tray");
            else key.DeleteValue("DockStudio", throwOnMissingValue: false);
        }
    }
}
