using System;
using System.Threading;
using System.Threading.Tasks;
using DockStudio.Core;
using DockStudio.Core.Updates;
using DockStudio.Core.Windows;

namespace ScreenLove.Services
{
    /// <summary>
    /// Aggiornamenti di ScreenLove: l'identità dell'app (sito, repository, nome dell'installer) e il collegamento
    /// ai servizi del nucleo DockStudio (UpdateClient per controllo e download, InnoSetup per l'installazione).
    /// </summary>
    public static class Updater
    {
        public const string SiteUrl = "https://screenlove.it";

        /// <summary>Indirizzo di Cloudflare Pages dello stesso sito: riserva se il dominio non risponde.</summary>
        public const string PagesUrl = "https://screenlove.pages.dev";

        public static readonly AppIdentity Identity = new(
            Name: "ScreenLove",
            SiteUrl: SiteUrl,
            PagesUrl: PagesUrl,
            GitHubRepo: "RETC3/ScreenLove",
            SetupAssetPattern: @"^ScreenLove-Setup-[\d.]+\.exe$",
            EnvPrefix: "SCREENLOVE");

        public static readonly UpdateClient Client = new(Identity, typeof(Updater).Assembly);

        public static string ReleasesPage => Client.ReleasesPage;
        public static Version CurrentVersion => Client.CurrentVersion;
        public static bool IsInstalled => InnoSetup.IsInstalled;

        public static Task<UpdateInfo?> CheckAsync(CancellationToken ct = default) => Client.CheckAsync(ct);
        public static UpdateInfo? Parse(string json, Version current) => Client.Parse(json, current);
        public static bool IsTrustedDownload(string url) => Client.IsTrustedDownload(url);

        public static Task<string> DownloadSetupAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default) =>
            Client.DownloadSetupAsync(update, progress, ct);

        /// <summary>Avvia l'installer in modalità silenziosa. Chi chiama deve chiudere l'app subito dopo.</summary>
        public static void LaunchSetup(string setupPath) => InnoSetup.LaunchUpdate(setupPath);
    }
}
