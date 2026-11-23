using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using DockStudio.Core;
using DockStudio.Core.Localization;
using DockStudio.Core.Updates;
using DockStudio.UI;

namespace DockPDF
{
    /// <summary>Chi è DockPDF per il nucleo e per l'interfaccia comune.</summary>
    public static class AppInfo
    {
        public static readonly AppIdentity Identity = new(
            Name: "DockPDF",
            SiteUrl: "https://getdockstudio.it/dockpdf",
            // Riserva se il dominio non risponde: lo stesso sito su Cloudflare Pages.
            PagesUrl: "https://dockstudio.pages.dev/dockpdf",
            GitHubRepo: null,
            SetupAssetPattern: @"^DockPDF-Setup-[\d.]+\.exe$",
            EnvPrefix: "DOCKPDF",
            StatsUrl: AppIdentity.SuiteStatsUrl);

        /// <summary>Rosso: il colore che tutti associano ai PDF.</summary>
        public static readonly Color Accent = Color.Parse("#E0413A");

        private static WindowIcon? _icon;

        /// <summary>Icona delle finestre e dell'area di notifica (la stessa dell'exe).</summary>
        public static WindowIcon Icon => _icon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://DockPDF/Assets/app.ico")));

        /// <summary>Aggiornamenti: legge getdockstudio.it/dockpdf/latest.json e scarica da .../download/.</summary>
        public static readonly UpdateClient Updates = new(Identity, typeof(AppInfo).Assembly);

        /// <summary>Statistiche anonime e segnalazioni degli errori, solo se l'utente le ha scelte (a getdockstudio.it).</summary>
        public static readonly UsageStats Usage = new(Updates);
        public static readonly ErrorReports Errors = new(Updates);

        public static DockBranding Branding => new(Identity, Accent, Text.Strings["app.tagline"]);

        public static Version Version =>
            typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    /// <summary>Testi di DockPDF (Lang/*.json).</summary>
    public static class Text
    {
        public static readonly Localizer Strings = new(typeof(Text).Assembly, "DockPDF.Lang.");

        public static string T(string key) => Strings[key];

        public static string F(string key, params object?[] args) => Strings.Format(key, args);
    }

    /// <summary>Impostazioni salvate di DockPDF.</summary>
    public sealed class Settings : IUsageStatsSettings
    {
        /// <summary>DockPDF controlla sempre gli aggiornamenti (al massimo una volta al giorno).</summary>
        public bool CheckForUpdates => true;

        /// <summary>
        /// True quando l'utente ha scelto cosa condividere (installer, finestra al primo avvio o menu).
        /// Finché è false non si invia nulla.
        /// </summary>
        public bool PrivacyChoiceMade { get; set; }

        /// <summary>Statistiche d'uso anonime insieme al controllo degli aggiornamenti.</summary>
        public bool SendUsageStats { get; set; }

        /// <summary>Segnalazioni automatiche degli errori.</summary>
        public bool SendErrorReports { get; set; }

        /// <summary>Minuti di utilizzo dall'ultimo invio delle statistiche.</summary>
        public int UsageMinutes { get; set; }

        /// <summary>Codice casuale dell'installazione per le statistiche; null finché non serve.</summary>
        public string? InstallId { get; set; }

        /// <summary>Registra la scelta; senza statistiche si cancellano codice e minuti accumulati.</summary>
        public void ApplyPrivacy(bool usage, bool errors)
        {
            PrivacyChoiceMade = true;
            SendUsageStats = usage;
            SendErrorReports = errors;
            if (usage) return;
            InstallId = null;
            UsageMinutes = 0;
        }

        public string Appearance { get; set; } = "System";
        public string Language { get; set; } = Languages.Auto;

        /// <summary>
        /// DockPDF resta nell'area di notifica quando si chiude la finestra: un clic sull'icona apre il cerchio
        /// degli strumenti.
        /// </summary>
        public bool KeepInTray { get; set; } = true;

        /// <summary>True dopo aver spiegato, la prima volta, che chiudere la finestra lascia DockPDF nell'area di notifica.</summary>
        public bool TrayHintShown { get; set; }

        /// <summary>Ultima cartella usata per aprire o salvare.</summary>
        public string? LastFolder { get; set; }

        /// <summary>Ultimo controllo automatico degli aggiornamenti (UTC): al massimo uno al giorno.</summary>
        public DateTime? LastUpdateCheck { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
    }
}
