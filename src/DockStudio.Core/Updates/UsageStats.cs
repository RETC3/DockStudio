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
    /// <summary>
    /// Le impostazioni che servono a statistiche anonime e segnalazioni degli errori: ogni app le ha nelle sue
    /// impostazioni. Finché l'utente non ha scelto (<see cref="PrivacyChoiceMade"/>) non si invia nulla.
    /// </summary>
    public interface IUsageStatsSettings
    {
        bool CheckForUpdates { get; }

        /// <summary>True quando l'utente ha scelto cosa condividere (nell'installer, al primo avvio o nelle impostazioni).</summary>
        bool PrivacyChoiceMade { get; }

        /// <summary>Statistiche d'uso anonime: app attiva e tempo di utilizzo.</summary>
        bool SendUsageStats { get; }

        /// <summary>Segnalazioni automatiche degli errori.</summary>
        bool SendErrorReports { get; }

        string? InstallId { get; set; }

        /// <summary>Minuti di utilizzo accumulati dall'ultimo invio delle statistiche.</summary>
        int UsageMinutes { get; set; }
    }

    /// <summary>
    /// Statistiche anonime, solo se l'utente le ha scelte: insieme al controllo degli aggiornamenti l'app
    /// comunica al sito un codice casuale, la versione, l'architettura, se è installata o portatile e i minuti
    /// di utilizzo dall'ultimo invio. Serve solo a contare le installazioni attive e a capire quanto si usa
    /// l'app. Il codice nasce al primo invio, non dipende dal PC né dall'utente e si cancella disattivando
    /// l'opzione. Nient'altro viene inviato: né file, né catture, né quello che si fa nell'app.
    /// </summary>
    public sealed class UsageStats
    {
        private static readonly Regex IdFormat = new("^[0-9a-f]{32}$");

        /// <summary>Tetto dei minuti accumulati tra un invio e l'altro (una settimana).</summary>
        public const int MaxPendingMinutes = 7 * 24 * 60;

        private readonly UpdateClient _updates;

        public UsageStats(UpdateClient updates) => _updates = updates;

        /// <summary>Dove si invia il ping: il sito dell'app, oppure PREFISSO_STATS_URL per le prove.</summary>
        public string PingUrl => _updates.App.Env("STATS_URL") ?? _updates.App.SiteUrl + "/api/ping";

        public static bool IsValidId(string? id) => id != null && IdFormat.IsMatch(id);

        /// <summary>True se si possono inviare le statistiche: scelta fatta e opzione attiva.</summary>
        public static bool Allowed(IUsageStatsSettings settings) => settings.PrivacyChoiceMade && settings.SendUsageStats;

        /// <summary>Codice anonimo dell'installazione: lo crea se manca o non è valido.</summary>
        public static string EnsureInstallId(IUsageStatsSettings settings)
        {
            if (!IsValidId(settings.InstallId))
                settings.InstallId = Guid.NewGuid().ToString("N");
            return settings.InstallId!;
        }

        /// <summary>
        /// Conta un minuto di utilizzo (l'app lo chiama ogni minuto in cui una sua finestra è in uso).
        /// Non conta nulla se le statistiche non sono state scelte.
        /// </summary>
        /// <returns>True se il contatore è cambiato.</returns>
        public static bool AddUsageMinute(IUsageStatsSettings settings)
        {
            if (!Allowed(settings)) return false;
            if (settings.UsageMinutes >= MaxPendingMinutes) return false;
            settings.UsageMinutes = Math.Max(0, settings.UsageMinutes) + 1;
            return true;
        }

        public static string Architecture => RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => "x64",
        };

        /// <summary>Il contenuto inviato al sito: esattamente questi cinque campi.</summary>
        public static string BuildPing(string id, Version version, string arch, bool installed, int minutes) =>
            JsonSerializer.Serialize(new
            {
                id,
                version = $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}",
                arch,
                kind = installed ? "setup" : "portable",
                minutes = Math.Clamp(minutes, 0, MaxPendingMinutes),
            });

        /// <summary>
        /// Invia il ping se le statistiche sono state scelte. Non segnala mai errori: se il sito non risponde,
        /// pazienza. Durante le prove degli aggiornamenti (PREFISSO_UPDATE_FEED) non invia nulla, a meno che
        /// PREFISSO_STATS_URL indichi dove mandarlo. Se il sito risponde, azzera i minuti inviati
        /// (le impostazioni vanno poi salvate dall'app).
        /// </summary>
        /// <param name="installed">True se l'app è installata, false se è la versione portatile.</param>
        /// <returns>True se il sito ha ricevuto il ping.</returns>
        public async Task<bool> SendAsync(IUsageStatsSettings settings, bool installed, CancellationToken ct = default)
        {
            if (!Allowed(settings) || !settings.CheckForUpdates) return false;
            if (_updates.FeedOverride != null && _updates.App.Env("STATS_URL") == null) return false;

            try
            {
                int minutes = settings.UsageMinutes;
                var body = BuildPing(EnsureInstallId(settings), _updates.CurrentVersion, Architecture, installed, minutes);
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd(_updates.App.UserAgent);
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(PingUrl, content, ct);
                if (!response.IsSuccessStatusCode) return false;
                // i minuti contati mentre il ping viaggiava restano per il prossimo invio
                settings.UsageMinutes = Math.Max(0, settings.UsageMinutes - minutes);
                return true;
            }
            catch
            {
                // statistiche: mai un errore per l'utente
                return false;
            }
        }
    }
}
