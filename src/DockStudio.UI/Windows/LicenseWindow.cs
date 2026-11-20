using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DockStudio.Core.Licensing;

namespace DockStudio.UI.Windows
{
    /// <summary>La licenza in uso e il pulsante per inserirne una (file dockstudio.license).</summary>
    public sealed class LicenseWindow : DockWindow
    {
        private readonly LicenseManager _licenses;
        private readonly StackPanel _details = new() { Spacing = 10 };
        private readonly TextBlock _message = new() { IsVisible = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        public LicenseWindow(DockBranding branding, LicenseManager licenses) : base(branding)
        {
            _licenses = licenses;
            Title = F("license.title", branding.App.Name);
            SetBody(
                Text(F("license.title", branding.App.Name), "title"),
                Card(_details),
                _message,
                Buttons(ButtonFor(T("license.insert"), Pick), ButtonFor(T("common.close"), Close, accent: true)));
            Show(licenses.Check());
        }

        /// <summary>Mostra l'esito di un controllo della licenza.</summary>
        public void Show(LicenseCheck check)
        {
            _details.Children.Clear();
            _details.Children.Add(Text(check.Description, "heading"));
            if (check.Info is { } info && check.Status != LicenseStatus.Invalid)
            {
                _details.Children.Add(Facts(
                    (T("license.licensee"), info.Licensee),
                    (T("license.code"), info.Id),
                    (T("license.seats"), info.Seats.ToString(DockStudio.Core.Localization.Languages.Culture)),
                    (T("license.expires"), info.Expires?.ToString("d", DockStudio.Core.Localization.Languages.Culture) ?? T("license.noExpiry")),
                    (T("license.apps"), info.Apps is { Count: > 0 } apps ? string.Join(", ", apps) : T("license.allApps"))));
            }
            else
            {
                _details.Children.Add(Text(T("license.freeInfo"), "muted"));
            }
        }

        /// <summary>Installa il testo di un file di licenza e aggiorna la finestra.</summary>
        public LicenseCheck Install(string text)
        {
            var result = _licenses.Install(text);
            bool ok = result.Status is LicenseStatus.Valid or LicenseStatus.Expired;
            _message.Classes.Set("error", !ok);
            _message.Classes.Set("success", ok);
            _message.Text = ok ? T("license.installed") : T("license.rejected");
            _message.IsVisible = true;
            Show(ok ? _licenses.Check() : result);
            return result;
        }

        private async void Pick()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = T("license.pickerTitle"),
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType(T("license.fileType")) { Patterns = new[] { "*.license" } } },
            });
            var file = files.FirstOrDefault();
            if (file == null) return;
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            Install(await reader.ReadToEndAsync());
        }
    }
}
