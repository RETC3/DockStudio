using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using DockStudio.Core;
using DockStudio.Core.Localization;
using DockStudio.Core.Updates;
using DockStudio.UI;

namespace DockStudio.Launcher
{
    /// <summary>Chi è il launcher DockStudio per il nucleo e per l'interfaccia comune.</summary>
    public static class AppInfo
    {
        public static readonly AppIdentity Identity = new(
            Name: "DockStudio",
            SiteUrl: "https://getdockstudio.it/dockstudio",
            // Riserva se il dominio non risponde: lo stesso sito su Cloudflare Pages.
            PagesUrl: "https://dockstudio.pages.dev/dockstudio",
            GitHubRepo: null,
            SetupAssetPattern: @"^DockStudio-Setup-[\d.]+\.exe$",
            EnvPrefix: "DOCKSTUDIO",
            StatsUrl: AppIdentity.SuiteStatsUrl);

        /// <summary>Il sito della suite: elenco delle app (apps.json) e pagine.</summary>
        public const string SuiteSite = "https://getdockstudio.it";

        /// <summary>Il blu della suite, lo stesso del sito.</summary>
        public static readonly Color Accent = Color.Parse("#2447F5");

        private static WindowIcon? _icon;

        /// <summary>Icona delle finestre e dell'area di notifica (la stessa dell'exe).</summary>
        public static WindowIcon Icon => _icon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://DockStudio/Assets/app.ico")));

        /// <summary>Aggiornamenti del launcher: getdockstudio.it/dockstudio/latest.json.</summary>
        public static readonly UpdateClient Updates = new(Identity, typeof(AppInfo).Assembly);

        /// <summary>Statistiche anonime e segnalazioni degli errori, solo se l'utente le ha scelte (a getdockstudio.it).</summary>
        public static readonly UsageStats Usage = new(Updates);
        public static readonly ErrorReports Errors = new(Updates);

        public static DockBranding Branding => new(Identity, Accent, Text.Strings["app.tagline"]);

        public static Version Version =>
            typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    /// <summary>Testi del launcher (Lang/*.json).</summary>
    public static class Text
    {
        public static readonly Localizer Strings = new(typeof(Text).Assembly, "DockStudio.Launcher.Lang.");

        public static string T(string key) => Strings[key];

        public static string F(string key, params object?[] args) => Strings.Format(key, args);
    }

    /// <summary>Impostazioni salvate del launcher (%APPDATA%\DockStudio\settings.json, accanto al registro delle app).</summary>
    public sealed class Settings : IUsageStatsSettings
    {
        public bool CheckForUpdates => true;
        public bool PrivacyChoiceMade { get; set; }
        public bool SendUsageStats { get; set; }
        public bool SendErrorReports { get; set; }
        public int UsageMinutes { get; set; }
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

        /// <summary>Chiudendo la finestra DockStudio resta nell'area di notifica.</summary>
        public bool KeepInTray { get; set; } = true;

        /// <summary>True dopo aver spiegato, la prima volta, che chiudere la finestra lascia DockStudio nell'area di notifica.</summary>
        public bool TrayHintShown { get; set; }

        /// <summary>Ultimo controllo automatico degli aggiornamenti di tutte le app (UTC): al massimo uno ogni 20 ore.</summary>
        public DateTime? LastUpdateCheck { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
    }
}
