using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DockStudio.Core.Updates
{
    /// <summary>Le impostazioni che servono alle statistiche anonime: ogni app le ha nelle sue impostazioni.</summary>
    public interface IUsageStatsSettings
    {
        bool CheckForUpdates { get; }
        bool SendUsageStats { get; }
        string? InstallId { get; set; }
    }

    /// <summary>
    /// Statistiche anonime: insieme al controllo degli aggiornamenti l'app comunica al sito un codice casuale,
    /// la versione, l'architettura e se è installata o portatile. Serve solo a contare le installazioni attive.
    /// Il codice nasce al primo invio, non dipende dal PC né dall'utente e si cancella disattivando
    /// l'opzione nelle impostazioni. Nient'altro viene inviato.
    /// </summary>
    public sealed class UsageStats
    {
        private static readonly Regex IdFormat = new("^[0-9a-f]{32}$");

        private readonly UpdateClient _updates;

        public UsageStats(UpdateClient updates) => _updates = updates;

        /// <summary>Dove si invia il ping: il sito dell'app, oppure PREFISSO_STATS_URL per le prove.</summary>
        public string PingUrl => _updates.App.Env("STATS_URL") ?? _updates.App.SiteUrl + "/api/ping";

        public static bool IsValidId(string? id) => id != null && IdFormat.IsMatch(id);

        /// <summary>Codice anonimo dell'installazione: lo crea se manca o non è valido.</summary>
        public static string EnsureInstallId(IUsageStatsSettings settings)
        {
            if (!IsValidId(settings.InstallId))
                settings.InstallId = Guid.NewGuid().ToString("N");
            return settings.InstallId!;
        }

        public static string Architecture => RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => "x64",
        };

        /// <summary>Il contenuto inviato al sito: esattamente questi quattro campi.</summary>
        public static string BuildPing(string id, Version version, string arch, bool installed) =>
            JsonSerializer.Serialize(new
            {
                id,
                version = $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}",
                arch,
                kind = installed ? "setup" : "portable",
            });

        /// <summary>
        /// Invia il ping se le statistiche sono attive. Non segnala mai errori: se il sito non risponde,
        /// pazienza. Durante le prove degli aggiornamenti (PREFISSO_UPDATE_FEED) non invia nulla, a meno che
        /// PREFISSO_STATS_URL indichi dove mandarlo.
        /// </summary>
        /// <param name="installed">True se l'app è installata, false se è la versione portatile.</param>
        public async Task SendAsync(IUsageStatsSettings settings, bool installed, CancellationToken ct = default)
        {
            if (!settings.SendUsageStats || !settings.CheckForUpdates) return;
            if (_updates.FeedOverride != null && _updates.App.Env("STATS_URL") == null) return;

            try
            {
                var body = BuildPing(EnsureInstallId(settings), _updates.CurrentVersion, Architecture, installed);
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd(_updates.App.UserAgent);
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var _ = await http.PostAsync(PingUrl, content, ct);
            }
            catch
            {
                // statistiche: mai un errore per l'utente
            }
        }
    }
}
