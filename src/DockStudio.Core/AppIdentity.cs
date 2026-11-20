using System;
using System.IO;

namespace DockStudio.Core
{
    /// <summary>
    /// Chi è l'app: nome, sito e repository delle release. Ogni app della suite ne crea una e la passa ai
    /// servizi del nucleo, che così non contengono nomi o indirizzi scritti a mano.
    /// </summary>
    /// <param name="Name">Nome dell'app, usato per cartelle, user agent e file temporanei (es. "ScreenLove").</param>
    /// <param name="SiteUrl">Sito ufficiale, senza barra finale: da lì arrivano latest.json, download e statistiche.</param>
    /// <param name="PagesUrl">Indirizzo di riserva dello stesso sito (Cloudflare Pages), o null.</param>
    /// <param name="GitHubRepo">Repository pubblico delle release ("proprietario/nome"), o null se non c'è.</param>
    /// <param name="SetupAssetPattern">Espressione regolare del nome dell'installer tra i file della release.</param>
    /// <param name="EnvPrefix">Prefisso delle variabili d'ambiente di prova (es. "SCREENLOVE" → SCREENLOVE_UPDATE_FEED).</param>
    /// <param name="StatsUrl">
    /// Dove mandare statistiche e segnalazioni degli errori (senza barra finale). Null = il sito dell'app;
    /// le nuove app usano <see cref="SuiteStatsUrl"/>, il sito di DockStudio che raccoglie i dati di tutte.
    /// </param>
    public sealed record AppIdentity(
        string Name,
        string SiteUrl,
        string? PagesUrl,
        string? GitHubRepo,
        string SetupAssetPattern,
        string EnvPrefix,
        string? StatsUrl = null)
    {
        /// <summary>Il sito della suite, che raccoglie statistiche ed errori di tutte le app (pagina /statistiche).</summary>
        public const string SuiteStatsUrl = "https://getdockstudio.it";

        public string UserAgent => Name + "-Updater";

        /// <summary>Nome dell'app nelle statistiche: il nome in minuscolo (es. "screenlove", "dockpdf").</summary>
        public string StatsName => Name.ToLowerInvariant();

        /// <summary>Cartella dei dati dell'utente (impostazioni): %APPDATA%\Nome su Windows, ~/.config/Nome su macOS e Linux.</summary>
        public string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);

        /// <summary>Valore di una variabile d'ambiente con il prefisso dell'app, o null.</summary>
        public string? Env(string suffix) => Environment.GetEnvironmentVariable(EnvPrefix + "_" + suffix);
    }
}
