using System;
using Avalonia.Controls;
using Avalonia.Layout;
using DockStudio.Core.Localization;

namespace DockStudio.UI.Windows
{
    /// <summary>
    /// "Aiutaci a migliorare": la scelta di statistiche d'uso anonime e segnalazioni degli errori, uguale in
    /// tutte le app. Le caselle partono vuote; chiudere la finestra senza confermare vale come "nessuna delle
    /// due". La scelta arriva con <see cref="Chosen"/>: è l'app a salvarla nelle sue impostazioni.
    /// </summary>
    public sealed class PrivacyWindow : DockWindow
    {
        private readonly CheckBox _usage;
        private readonly CheckBox _errors;
        private bool _reported;

        /// <summary>La scelta fatta: statistiche d'uso, segnalazioni degli errori.</summary>
        public event Action<bool, bool>? Chosen;

        public PrivacyWindow(DockBranding branding, bool usage = false, bool errors = false) : base(branding)
        {
            Title = P("privacy.title", branding.App.Name);
            _usage = Option(P("privacy.usage"), P("privacy.usageDetail"), usage);
            _errors = Option(P("privacy.errors"), P("privacy.errorsDetail"), errors);

            SetBody(
                Text(Title, "title"),
                Text(P("privacy.intro")),
                Card(new StackPanel { Spacing = 12, Children = { _usage, _errors } }),
                Text(P("privacy.local")),
                Text(P("privacy.downloads"), "muted"),
                Buttons(ButtonFor(P("privacy.confirm"), Confirm, accent: true)));

            Closed += (_, _) => Report(false, false);
        }

        public bool UsageStats => _usage.IsChecked == true;

        public bool ErrorReports => _errors.IsChecked == true;

        private void Confirm()
        {
            Report(UsageStats, ErrorReports);
            Close();
        }

        private void Report(bool usage, bool errors)
        {
            if (_reported) return;
            _reported = true;
            Chosen?.Invoke(usage, errors);
        }

        private static CheckBox Option(string title, string detail, bool value) => new()
        {
            IsChecked = value,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = new StackPanel
            {
                Spacing = 2,
                Children = { Text(title, "heading"), Text(detail, "muted") },
            },
        };

        /// <summary>Testi dell'informativa: stanno nel nucleo, condivisi con le finestre WPF e con l'installer.</summary>
        private static string P(string key, params object?[] args) =>
            args.Length == 0 ? CoreText.Strings[key] : CoreText.Strings.Format(key, args);
    }
}
