using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DockStudio.Launcher
{
    /// <summary>
    /// Le app nell'elenco "App installate" di Windows: chiavi Uninstall scritte dagli installer Inno Setup
    /// (per l'utente in HKCU, per tutto il computer in HKLM, quando le installerà il pacchetto aziende).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class WindowsInstalls
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <summary>Percorso dell'exe dell'app installata (cartella di installazione + Nome.exe), o null.</summary>
        public static string? ExePath(string name)
        {
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using var uninstall = root.OpenSubKey(UninstallKey);
                    if (uninstall == null) continue;
                    foreach (var id in uninstall.GetSubKeyNames())
                    {
                        if (!id.EndsWith("_is1", StringComparison.OrdinalIgnoreCase)) continue; // solo installer Inno Setup
                        using var key = uninstall.OpenSubKey(id);
                        if (!string.Equals(key?.GetValue("DisplayName") as string, name, StringComparison.OrdinalIgnoreCase)) continue;
                        if (key?.GetValue("InstallLocation") is not string folder || folder.Length == 0) continue;
                        var exe = Path.Combine(folder, name + ".exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
                catch
                {
                    // chiave illeggibile: si prova l'altra
                }
            }
            return null;
        }
    }
}
