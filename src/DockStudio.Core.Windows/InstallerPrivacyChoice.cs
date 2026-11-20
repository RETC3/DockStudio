using System;
using Microsoft.Win32;

namespace DockStudio.Core.Windows
{
    /// <summary>
    /// La scelta fatta nella pagina "Aiutaci a migliorare" dell'installer. L'installer la scrive in
    /// HKCU\Software\&lt;App&gt;\Privacy (valori DWORD UsageStats ed ErrorReports, 0 o 1); l'app la legge al primo
    /// avvio, la copia nelle sue impostazioni e cancella la chiave. Se manca (aggiornamento, installazione
    /// silenziosa, versione portatile) l'app chiede all'utente con una finestra.
    /// </summary>
    public static class InstallerPrivacyChoice
    {
        public readonly record struct Choice(bool UsageStats, bool ErrorReports);

        public static string KeyPath(AppIdentity app) => $@"Software\{app.Name}\Privacy";

        /// <summary>Legge la scelta dell'installer e cancella la chiave; null se non c'è.</summary>
        public static Choice? Take(AppIdentity app)
        {
            try
            {
                Choice? choice;
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath(app)))
                {
                    if (key == null) return null;
                    choice = new Choice(IsOn(key.GetValue("UsageStats")), IsOn(key.GetValue("ErrorReports")));
                }
                Registry.CurrentUser.DeleteSubKeyTree(KeyPath(app), throwOnMissingSubKey: false);
                return choice;
            }
            catch
            {
                return null; // registro illeggibile: si chiede all'utente
            }
        }

        /// <summary>Scrive una scelta come farebbe l'installer (per le prove).</summary>
        public static void Write(AppIdentity app, Choice choice)
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath(app));
            key.SetValue("UsageStats", choice.UsageStats ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("ErrorReports", choice.ErrorReports ? 1 : 0, RegistryValueKind.DWord);
        }

        private static bool IsOn(object? value) => value switch
        {
            int i => i != 0,
            string s => s == "1",
            _ => false,
        };
    }
}
