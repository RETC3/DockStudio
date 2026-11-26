using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using DockStudio.Core;
using DockStudio.Core.Localization;
using DockStudio.Core.Updates;
using DockStudio.UI;

namespace DockPost
{
    /// <summary>Chi è DockPost per il nucleo e per l'interfaccia comune.</summary>
    public static class AppInfo
    {
        public static readonly AppIdentity Identity = new(
            Name: "DockPost",
            SiteUrl: "https://getdockstudio.it/dockpost",
            PagesUrl: "https://dockstudio.pages.dev/dockpost",
            GitHubRepo: null,
            SetupAssetPattern: @"^DockPost-Setup-[\d.]+\.exe$",
            EnvPrefix: "DOCKPOST",
            StatsUrl: AppIdentity.SuiteStatsUrl);

        /// <summary>Arancio: caldo, distinto dal blu della suite, dal verde di DockSpace e dal rosso di DockPDF.</summary>
        public static readonly Color Accent = Color.Parse("#F07A1A");

        private static WindowIcon? _icon;

        public static WindowIcon Icon => _icon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://DockPost/Assets/app.ico")));

        public static readonly UpdateClient Updates = new(Identity, typeof(AppInfo).Assembly);
        public static readonly UsageStats Usage = new(Updates);
        public static readonly ErrorReports Errors = new(Updates);

        public static DockBranding Branding => new(Identity, Accent, Text.Strings["app.tagline"]);

        public static Version Version => typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    /// <summary>Testi di DockPost (Lang/*.json).</summary>
    public static class Text
    {
        public static readonly Localizer Strings = new(typeof(Text).Assembly, "DockPost.Lang.");

        public static string T(string key) => Strings[key];

        public static string F(string key, params object?[] args) => Strings.Format(key, args);
    }

    /// <summary>Impostazioni salvate di DockPost (gli account stanno a parte, in accounts.json).</summary>
    public sealed class Settings : IUsageStatsSettings
    {
        public bool CheckForUpdates => true;
        public bool PrivacyChoiceMade { get; set; }
        public bool SendUsageStats { get; set; }
        public bool SendErrorReports { get; set; }
        public int UsageMinutes { get; set; }
        public string? InstallId { get; set; }

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
        public DateTime? LastUpdateCheck { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public double FolderPaneWidth { get; set; } = 230;
        public double ListPaneWidth { get; set; } = 380;

        /// <summary>Ogni quanti minuti controllare la posta nuova (0 = solo a mano).</summary>
        public int CheckEveryMinutes { get; set; } = 5;

        /// <summary>Mittenti di cui mostrare sempre le immagini remote (indirizzi in minuscolo).</summary>
        public System.Collections.Generic.List<string> TrustedImageSenders { get; set; } = new();
    }
}
