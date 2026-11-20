using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using DockStudio.Core.Licensing;
using DockStudio.Core.Suite;

namespace DockStudio.UI.Windows
{
    /// <summary>Informazioni sull'app: versione, licenza, sito e altre app DockStudio installate.</summary>
    public sealed class AboutWindow : DockWindow
    {
        /// <param name="version">Versione in uso.</param>
        /// <param name="portable">True se è la versione portatile (senza installer).</param>
        /// <param name="license">Licenza in uso (da LicenseManager.Check).</param>
        /// <param name="otherApps">App della suite installate (da SuiteRegistry.Installed); quella corrente viene esclusa.</param>
        /// <param name="openLicense">Apre la finestra della licenza, o null per non mostrare il pulsante.</param>
        public AboutWindow(DockBranding branding, Version version, bool portable, LicenseCheck license,
            IReadOnlyList<SuiteApp> otherApps, Action? openLicense = null) : base(branding)
        {
            var app = branding.App;
            Title = F("about.title", app.Name);

            string versionText = version.ToString(3);
            var others = otherApps.Where(a => !string.Equals(a.Name, app.Name, StringComparison.OrdinalIgnoreCase)).ToList();

            var suite = new StackPanel { Spacing = 4 };
            suite.Children.Add(Text(T("about.suite"), "heading"));
            if (others.Count == 0)
                suite.Children.Add(Text(T("about.suiteEmpty"), "muted"));
            foreach (var other in others)
                suite.Children.Add(Text($"{other.Name} {other.Version}"));

            var buttons = new List<Control> { ButtonFor(T("about.website"), () => OpenUrl(app.SiteUrl)) };
            if (openLicense != null) buttons.Add(ButtonFor(T("about.manageLicense"), openLicense));
            buttons.Add(ButtonFor(T("common.close"), Close, accent: true));

            SetBody(
                Text(app.Name, "brand"),
                Text(branding.Tagline, "muted"),
                Text(F(portable ? "about.portable" : "about.version", versionText)),
                Card(new StackPanel
                {
                    Spacing = 4,
                    Children = { Text(T("about.license"), "heading"), Text(license.Description) },
                }),
                Card(suite),
                Text(T("about.suiteName"), "muted"),
                Buttons(buttons.ToArray()));
        }
    }
}
