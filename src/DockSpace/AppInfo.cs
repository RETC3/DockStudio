using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using DockStudio.Core;
using DockStudio.Core.Localization;
using DockStudio.UI;

namespace DockSpace
{
    /// <summary>Chi è DockSpace per il nucleo e per l'interfaccia comune.</summary>
    public static class AppInfo
    {
        public static readonly AppIdentity Identity = new(
            Name: "DockSpace",
            SiteUrl: "https://getdockstudio.it/dockspace",
            PagesUrl: null,
            GitHubRepo: null,
            SetupAssetPattern: @"^DockSpace-Setup-[\d.]+\.exe$",
            EnvPrefix: "DOCKSPACE",
            StatsUrl: AppIdentity.SuiteStatsUrl);

        /// <summary>Verde acqua: distinto dal blu della suite e dal colore di ScreenLove.</summary>
        public static readonly Color Accent = Color.Parse("#0F9D8A");

        private static WindowIcon? _icon;

        /// <summary>Icona delle finestre (la stessa dell'exe).</summary>
        public static WindowIcon Icon => _icon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://DockSpace/Assets/app.ico")));

        public static DockBranding Branding => new(Identity, Accent, Text.Strings["app.tagline"]);

        public static Version Version =>
            typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    /// <summary>Testi di DockSpace (Lang/*.json).</summary>
    public static class Text
    {
        public static readonly Localizer Strings = new(typeof(Text).Assembly, "DockSpace.Lang.");

        public static string T(string key) => Strings[key];

        public static string F(string key, params object?[] args) => Strings.Format(key, args);
    }

    /// <summary>Impostazioni salvate di DockSpace.</summary>
    public sealed class Settings
    {
        public string Appearance { get; set; } = "System";
        public string Language { get; set; } = Languages.Auto;
        public string? LastPath { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
    }
}
