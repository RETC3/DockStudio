using System;
using System.Threading;
using System.Threading.Tasks;
using Core = DockStudio.Core.Updates.UsageStats;

namespace ScreenLove.Services
{
    /// <summary>Statistiche anonime di ScreenLove: il servizio del nucleo DockStudio con le impostazioni dell'app.</summary>
    public static class UsageStats
    {
        private static readonly Core Service = new(Updater.Client);

        public static string PingUrl => Service.PingUrl;
        public static string Architecture => Core.Architecture;
        public static bool IsValidId(string? id) => Core.IsValidId(id);
        public static string EnsureInstallId(AppSettings settings) => Core.EnsureInstallId(settings);

        public static string BuildPing(string id, Version version, string arch, bool installed, int minutes = 0) =>
            Core.BuildPing(id, version, arch, installed, minutes);

        public static bool AddUsageMinute(AppSettings settings) => Core.AddUsageMinute(settings);

        public static Task<bool> SendAsync(AppSettings settings, CancellationToken ct = default) =>
            Service.SendAsync(settings, Updater.IsInstalled, ct);
    }
}
