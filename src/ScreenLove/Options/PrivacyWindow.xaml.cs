using System.Windows;
using DockStudio.Core.Localization;

namespace ScreenLove.Options
{
    /// <summary>
    /// "Aiutaci a migliorare ScreenLove": la scelta di statistiche ed errori, al primo avvio quando l'installer
    /// non l'ha già chiesta (aggiornamento, versione portatile, installazione silenziosa). Le caselle partono
    /// vuote; chiudere la finestra vale come "nessuna delle due".
    /// </summary>
    public partial class PrivacyWindow : Window
    {
        private bool _confirmed;

        public PrivacyWindow()
        {
            InitializeComponent();
            var t = CoreText.Strings;
            Title = t.Format("privacy.title", "ScreenLove");
            TitleText.Text = Title;
            IntroText.Text = t["privacy.intro"];
            StatsTitle.Text = t["privacy.usage"];
            StatsDetail.Text = t["privacy.usageDetail"];
            ErrorsTitle.Text = t["privacy.errors"];
            ErrorsDetail.Text = t["privacy.errorsDetail"];
            LocalText.Text = "Le tue catture, i tuoi file e quello che fai in ScreenLove restano sul tuo computer e non vengono mai inviati. "
                             + "Puoi cambiare idea in qualsiasi momento dalle Impostazioni.";
            DownloadsText.Text = t["privacy.downloads"];
            ConfirmButton.Content = t["privacy.confirm"];
            Closed += (_, _) => { if (!_confirmed) Apply(usage: false, errors: false); };
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Apply(StatsBox.IsChecked == true, ErrorsBox.IsChecked == true);
            _confirmed = true;
            Close();
        }

        /// <summary>Salva la scelta nelle impostazioni dell'app.</summary>
        public static void Apply(bool usage, bool errors)
        {
            var s = App.Settings;
            s.PrivacyChoiceMade = true;
            s.SendUsageStats = usage;
            s.SendErrorReports = errors;
            if (!usage)
            {
                s.InstallId = null;
                s.UsageMinutes = 0;
            }
            s.Save();
        }
    }
}
