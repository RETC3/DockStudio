using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace DockStudio.Core.Localization
{
    /// <summary>
    /// Lingua delle app DockStudio. L'italiano è la lingua base: ogni testo esiste in italiano, e se manca
    /// una traduzione si mostra quello. "auto" segue la lingua di sistema (italiano se il sistema è in
    /// italiano, altrimenti inglese).
    /// </summary>
    public static class Languages
    {
        public const string Base = "it";
        public const string Auto = "auto";

        /// <summary>Lingue disponibili, con il nome nella lingua stessa (per il menu delle impostazioni).</summary>
        public static readonly IReadOnlyDictionary<string, string> Supported = new Dictionary<string, string>
        {
            ["it"] = "Italiano",
            ["en"] = "English",
        };

        private static string _current = Detect(CultureInfo.CurrentUICulture);

        /// <summary>Codice della lingua in uso ("it", "en").</summary>
        public static string Current => _current;

        public static CultureInfo Culture => CultureInfo.GetCultureInfo(_current);

        public static event Action? Changed;

        /// <summary>Imposta la lingua: un codice supportato, oppure "auto" (o null) per quella di sistema.</summary>
        public static void Set(string? language)
        {
            var next = string.IsNullOrEmpty(language) || language == Auto || !Supported.ContainsKey(language)
                ? Detect(CultureInfo.CurrentUICulture)
                : language;
            if (next == _current) return;
            _current = next;
            Changed?.Invoke();
        }

        /// <summary>La lingua da usare per una cultura di sistema: la stessa se è supportata, altrimenti inglese.</summary>
        public static string Detect(CultureInfo culture)
        {
            var code = culture.TwoLetterISOLanguageName;
            return Supported.ContainsKey(code) ? code : "en";
        }
    }

    /// <summary>
    /// Testi di un componente (il nucleo, un'app) in più lingue. I testi stanno in file JSON incorporati
    /// nell'assembly, uno per lingua (Lang/it.json, Lang/en.json), con chiavi semplici:
    /// { "updates.notFound": "Non ci sono ancora versioni pubblicate di {0}." }.
    /// I segnaposto {0}, {1}... seguono string.Format.
    /// </summary>
    public sealed class Localizer
    {
        private readonly Dictionary<string, Dictionary<string, string>> _catalogs = new();

        /// <param name="assembly">Assembly che contiene i file di lingua.</param>
        /// <param name="prefix">Prefisso dei nomi delle risorse (es. "DockStudio.Core.Lang.").</param>
        public Localizer(Assembly assembly, string prefix)
        {
            foreach (var language in Languages.Supported.Keys)
            {
                using var stream = assembly.GetManifestResourceStream(prefix + language + ".json");
                if (stream == null) continue;
                _catalogs[language] = Parse(stream);
            }
            if (!_catalogs.ContainsKey(Languages.Base))
                throw new InvalidOperationException($"Manca il file della lingua base ({prefix}{Languages.Base}.json).");
        }

        /// <summary>Per le prove: cataloghi già pronti, lingua → (chiave → testo).</summary>
        public Localizer(IDictionary<string, Dictionary<string, string>> catalogs)
        {
            foreach (var (language, texts) in catalogs) _catalogs[language] = texts;
        }

        public IReadOnlyDictionary<string, string> Catalog(string language) =>
            _catalogs.TryGetValue(language, out var c) ? c : new Dictionary<string, string>();

        /// <summary>Il testo nella lingua in uso (o in italiano se manca la traduzione, o la chiave se manca del tutto).</summary>
        public string this[string key] => Get(key, Languages.Current);

        /// <summary>Il testo con i segnaposto riempiti.</summary>
        public string Format(string key, params object?[] args) =>
            string.Format(Languages.Culture, Get(key, Languages.Current), args);

        public string Get(string key, string language)
        {
            if (_catalogs.TryGetValue(language, out var catalog) && catalog.TryGetValue(key, out var text)) return text;
            if (_catalogs.TryGetValue(Languages.Base, out var fallback) && fallback.TryGetValue(key, out text)) return text;
            return key;
        }

        /// <summary>
        /// Differenze tra la lingua base e le traduzioni: chiavi mancanti o in più, segnaposto diversi.
        /// Lista vuota se tutto torna. Usato dai controlli automatici.
        /// </summary>
        public IReadOnlyList<string> Problems()
        {
            var problems = new List<string>();
            var baseCatalog = Catalog(Languages.Base);
            foreach (var language in _catalogs.Keys.Where(l => l != Languages.Base))
            {
                var catalog = _catalogs[language];
                foreach (var key in baseCatalog.Keys.Except(catalog.Keys)) problems.Add($"{language}: manca \"{key}\"");
                foreach (var key in catalog.Keys.Except(baseCatalog.Keys)) problems.Add($"{language}: \"{key}\" non esiste in italiano");
                foreach (var key in baseCatalog.Keys.Intersect(catalog.Keys))
                    if (!Placeholders(baseCatalog[key]).SetEquals(Placeholders(catalog[key])))
                        problems.Add($"{language}: segnaposto diversi in \"{key}\"");
            }
            foreach (var language in Languages.Supported.Keys.Where(l => !_catalogs.ContainsKey(l)))
                problems.Add($"{language}: file della lingua mancante");
            return problems;
        }

        private static HashSet<string> Placeholders(string text) =>
            System.Text.RegularExpressions.Regex.Matches(text, @"\{(\d+)(?:[:,][^}]*)?\}").Select(m => m.Groups[1].Value).ToHashSet();

        private static Dictionary<string, string> Parse(Stream stream) =>
            JsonSerializer.Deserialize<Dictionary<string, string>>(stream, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) ?? new Dictionary<string, string>();
    }
}
