using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DockStudio.Core;
using DockStudio.Core.Suite;
using DockStudio.Core.Updates;

namespace DockStudio.Launcher
{
    /// <summary>
    /// Un'app della suite com'è descritta sul sito (getdockstudio.it/apps.json): nome, colore, lettera,
    /// descrizione breve e stato (disponibile, in sviluppo, in arrivo).
    /// </summary>
    public sealed record CatalogApp(string Slug, string Name, string Color, string Letter, string Description, string Status, string Page)
    {
        /// <summary>Si scarica e si installa già.</summary>
        public bool IsAvailable => Status == "disponibile";

        /// <summary>
        /// Da dove arrivano versioni e installer: getdockstudio.it/&lt;app&gt;/ per le app della suite, il suo sito
        /// (con GitHub come riserva) per ScreenLove, che è nata prima della suite.
        /// </summary>
        public AppIdentity Identity => Slug == "screenlove"
            ? new AppIdentity("ScreenLove", "https://screenlove.it", "https://screenlove.pages.dev", "RETC3/ScreenLove",
                @"^ScreenLove-Setup-[\d.]+\.exe$", "SCREENLOVE")
            : new AppIdentity(Name, $"{AppInfo.SuiteSite}/{Slug}", $"https://dockstudio.pages.dev/{Slug}", null,
                $@"^{Name}-Setup-[\d.]+\.exe$", Name.ToUpperInvariant(), AppIdentity.SuiteStatsUrl);

        private UpdateClient? _updates;

        /// <summary>Ultima versione e installer dell'app (le versioni si confrontano con quella installata).</summary>
        public UpdateClient Updates => _updates ??= new UpdateClient(Identity, typeof(CatalogApp).Assembly);

        /// <summary>
        /// Il segnale "l'app è aperta" che l'app tiene finché gira (lo stesso che controlla il suo installer):
        /// non si aggiorna un'app aperta.
        /// </summary>
        public string RunningMutex => Slug == "screenlove" ? "ScreenLove.SingleInstance" : Name + ".Running";
    }

    /// <summary>
    /// L'elenco delle app: quello del sito se risponde (salvato per la volta dopo), altrimenti l'ultima copia
    /// salvata, altrimenti quello incluso nel launcher.
    /// </summary>
    public static class Catalog
    {
        public static string CacheFile => Path.Combine(SuitePaths.UserFolder, "catalog.json");

        /// <summary>Elenco disponibile subito, senza rete: copia salvata o incluso.</summary>
        public static IReadOnlyList<CatalogApp> Local()
        {
            try
            {
                if (File.Exists(CacheFile) && Parse(File.ReadAllText(CacheFile)) is { Count: > 0 } cached) return cached;
            }
            catch
            {
                // copia rovinata: si usa quella inclusa
            }
            return Embedded();
        }

        public static IReadOnlyList<CatalogApp> Embedded()
        {
            using var stream = typeof(Catalog).Assembly.GetManifestResourceStream("DockStudio.Launcher.apps.json")!;
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>Scarica l'elenco aggiornato dal sito e lo salva; null se il sito non risponde.</summary>
        public static async Task<IReadOnlyList<CatalogApp>?> RefreshAsync(CancellationToken ct = default)
        {
            var url = AppInfo.Identity.Env("CATALOG") ?? AppInfo.SuiteSite + "/apps.json";
            try
            {
                string json;
                if (new Uri(url).IsFile) json = await File.ReadAllTextAsync(new Uri(url).LocalPath, ct);
                else
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Identity.UserAgent);
                    json = await http.GetStringAsync(url, ct);
                }
                var apps = Parse(json);
                if (apps.Count == 0) return null;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
                    await File.WriteAllTextAsync(CacheFile, json, ct);
                }
                catch
                {
                    // non salvata: la prossima volta si riscarica
                }
                return apps;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }

        /// <summary>Legge apps.json ({ "versione": 1, "app": [ { slug, nome, colore, icona, breve, stato, pagina } ] }).</summary>
        public static IReadOnlyList<CatalogApp> Parse(string json)
        {
            var list = new List<CatalogApp>();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (!doc.RootElement.TryGetProperty("app", out var apps) || apps.ValueKind != JsonValueKind.Array) return list;
            foreach (var a in apps.EnumerateArray())
            {
                string? slug = Str(a, "slug"), name = Str(a, "nome");
                // Solo nomi semplici: diventano cartelle, indirizzi e nomi di file.
                if (slug == null || name == null || !IsSimple(slug) || !IsSimple(name)) continue;
                list.Add(new CatalogApp(slug, name, Str(a, "colore") ?? "#2447F5", Str(a, "icona") ?? name[..1],
                    Str(a, "breve") ?? "", Str(a, "stato") ?? "arrivo",
                    Str(a, "pagina") is { } p && p.StartsWith(AppInfo.SuiteSite + "/", StringComparison.Ordinal) ? p : $"{AppInfo.SuiteSite}/{slug}/"));
            }
            return list;
        }

        private static bool IsSimple(string s) => s.Length is > 0 and <= 40 && s.All(char.IsAsciiLetterOrDigit);

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }
}
