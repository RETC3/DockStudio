using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DockStudio.Core.Updates
{
    /// <summary>
    /// Segnalazioni automatiche degli errori, solo se l'utente le ha scelte. Si invia cosa si è rotto (tipo
    /// di errore e punto del codice) e in quale versione, con architettura e versione del sistema.
    /// Non si invia il messaggio dell'errore, che potrebbe contenere nomi di file o testo dell'utente, e dai
    /// punti del codice si tolgono percorsi e numeri di riga. Nessun codice dell'installazione.
    /// </summary>
    public sealed class ErrorReports
    {
        /// <summary>Al massimo tante segnalazioni per ogni avvio dell'app.</summary>
        public const int MaxPerSession = 5;

        /// <summary>Lunghezza massima dei punti del codice inviati.</summary>
        public const int MaxStackLength = 6000;

        private readonly UpdateClient _updates;
        private readonly HashSet<string> _sent = new();
        private readonly object _lock = new();

        public ErrorReports(UpdateClient updates) => _updates = updates;

        /// <summary>Dove si invia la segnalazione: il sito dell'app, oppure PREFISSO_ERRORS_URL per le prove.</summary>
        public string ReportUrl => _updates.App.Env("ERRORS_URL") ?? (_updates.App.StatsUrl ?? _updates.App.SiteUrl) + "/api/errore";

        /// <summary>True se si possono inviare segnalazioni: scelta fatta e opzione attiva.</summary>
        public static bool Allowed(IUsageStatsSettings settings) => settings.PrivacyChoiceMade && settings.SendErrorReports;

        private static readonly Regex SourceLocation = new(@"\s+in\s+.*?:(line|riga)\s+\d+", RegexOptions.Compiled);
        private static readonly Regex WindowsPath = new(@"[A-Za-z]:\\[^\s:*?""<>|()]*", RegexOptions.Compiled);
        private static readonly Regex UnixPath = new(@"(?<![\w.])/(Users|home|private|var|tmp|Volumes|mnt)/[^\s:()]*", RegexOptions.Compiled);
        private static readonly Regex UncPath = new(@"\\\\[^\s\\]+\\[^\s:()]*", RegexOptions.Compiled);

        /// <summary>
        /// Ripulisce i punti del codice: niente file sorgente e numeri di riga, niente percorsi
        /// (che possono contenere il nome dell'utente). Restano solo nomi di classi e metodi.
        /// </summary>
        public static string CleanStack(string? stack)
        {
            if (string.IsNullOrWhiteSpace(stack)) return "";
            var lines = stack.Replace("\r", "").Split('\n')
                .Select(l => SourceLocation.Replace(l, ""))
                .Select(l => WindowsPath.Replace(l, "<percorso>"))
                .Select(l => UncPath.Replace(l, "<percorso>"))
                .Select(l => UnixPath.Replace(l, "<percorso>"))
                .Select(l => l.Trim())
                .Where(l => l.Length > 0);
            var text = string.Join("\n", lines);
            return text.Length <= MaxStackLength ? text : text[..MaxStackLength];
        }

        /// <summary>Tipi dell'errore e degli errori interni, dal più esterno: "A ← B ← C".</summary>
        public static string Types(Exception ex)
        {
            var types = new List<string>();
            for (var e = ex; e != null && types.Count < 6; e = e.InnerException)
                types.Add(e.GetType().FullName ?? e.GetType().Name);
            return string.Join(" ← ", types);
        }

        /// <summary>Punti del codice dell'errore più interno (dove è nato) e di quello esterno.</summary>
        private static string Stack(Exception ex)
        {
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            var parts = new List<string> { CleanStack(inner.StackTrace) };
            if (!ReferenceEquals(inner, ex)) parts.Add("--- errore esterno ---\n" + CleanStack(ex.StackTrace));
            var text = string.Join("\n", parts.Where(p => p.Length > 0));
            return text.Length <= MaxStackLength ? text : text[..MaxStackLength];
        }

        public static string OperatingSystem
        {
            get
            {
                var v = Environment.OSVersion.Version;
                var name = System.OperatingSystem.IsWindows() ? "windows" : System.OperatingSystem.IsMacOS() ? "macos" : "linux";
                return $"{name} {v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
            }
        }

        /// <summary>Il contenuto inviato al sito: esattamente questi sette campi.</summary>
        /// <param name="app">Nome dell'app nelle statistiche (<see cref="AppIdentity.StatsName"/>).</param>
        /// <param name="fatal">True se l'app si è chiusa per questo errore.</param>
        public static string BuildReport(string app, Exception ex, Version version, string arch, string os, bool fatal = false) =>
            JsonSerializer.Serialize(new
            {
                app,
                version = $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}",
                arch,
                os,
                type = Types(ex),
                stack = Stack(ex),
                fatal,
            });

        /// <summary>
        /// Invia la segnalazione se l'utente l'ha scelta. Lo stesso errore si segnala una volta per avvio, e al
        /// massimo <see cref="MaxPerSession"/> errori diversi. Non segnala mai problemi: se il sito non risponde,
        /// pazienza.
        /// </summary>
        /// <param name="fatal">True se l'app si sta chiudendo per questo errore.</param>
        /// <returns>True se il sito ha ricevuto la segnalazione.</returns>
        public async Task<bool> SendAsync(IUsageStatsSettings settings, Exception ex, bool fatal = false,
            TimeSpan? timeout = null, CancellationToken ct = default)
        {
            if (!Allowed(settings)) return false;
            if (_updates.FeedOverride != null && _updates.App.Env("ERRORS_URL") == null) return false;

            try
            {
                var key = Types(ex) + "\n" + Stack(ex);
                lock (_lock)
                {
                    if (_sent.Count >= MaxPerSession || !_sent.Add(key)) return false;
                }

                var body = BuildReport(_updates.App.StatsName, ex, _updates.CurrentVersion, UsageStats.Architecture, OperatingSystem, fatal);
                using var http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd(_updates.App.UserAgent);
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(ReportUrl, content, ct);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
