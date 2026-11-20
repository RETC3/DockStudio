using System;
using System.Threading;
using System.Threading.Tasks;
using Core = DockStudio.Core.Updates.ErrorReports;

namespace ScreenLove.Services
{
    /// <summary>Segnalazioni degli errori di ScreenLove: il servizio del nucleo DockStudio con le impostazioni dell'app.</summary>
    public static class ErrorReports
    {
        private static readonly Core Service = new(Updater.Client);

        public static string ReportUrl => Service.ReportUrl;

        public static Task<bool> SendAsync(AppSettings settings, Exception ex, bool fatal = false,
            TimeSpan? timeout = null, CancellationToken ct = default) =>
            Service.SendAsync(settings, ex, fatal, timeout, ct);
    }
}
