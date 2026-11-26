using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DockPost
{
    /// <summary>Quello che DockPost chiede al sistema: aprire link e file, installer e aggiornamenti.</summary>
    public static class Shell
    {
        /// <summary>Estensioni che non si aprono con un clic da un allegato (si possono solo salvare).</summary>
        private static readonly string[] Dangerous =
        {
            ".exe", ".com", ".bat", ".cmd", ".msi", ".msix", ".appx", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
            ".wsf", ".wsh", ".scr", ".pif", ".lnk", ".url", ".hta", ".jar", ".reg", ".cpl", ".dll", ".iso", ".img",
            ".vhd", ".vhdx", ".chm", ".application", ".appref-ms", ".library-ms", ".settingcontent-ms", ".xll",
        };

        public static bool IsDangerous(string fileName) =>
            Dangerous.Contains(Path.GetExtension(fileName).ToLowerInvariant());

        /// <summary>Apre un link (solo http, https, mailto: gli altri sono già stati scartati dalla lettura).</summary>
        public static void OpenLink(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto")) return;
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }

        public static void OpenFile(string path) =>
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        public static bool IsInstalled => OperatingSystem.IsWindows() && DockStudio.Core.Windows.InnoSetup.IsInstalled;

        public static void LaunchUpdate(string setupPath)
        {
            if (OperatingSystem.IsWindows()) DockStudio.Core.Windows.InnoSetup.LaunchUpdate(setupPath);
        }

        public static (bool Usage, bool Errors)? TakeInstallerPrivacyChoice()
        {
            if (!OperatingSystem.IsWindows()) return null;
            return DockStudio.Core.Windows.InstallerPrivacyChoice.Take(AppInfo.Identity) is { } c ? (c.UsageStats, c.ErrorReports) : null;
        }
    }
}
