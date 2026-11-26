using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DockPost.Engine;
using DockPost.Mail;
using DockStudio.UI.Windows;
using UiTextBlock = Avalonia.Controls.TextBlock;

namespace DockPost.Views
{
    /// <summary>
    /// Aggiunta di un account: nome, indirizzo e password; DockPost riconosce il provider e prova la connessione
    /// prima di salvare. Per Outlook.com e Microsoft 365 l'accesso si fa nel browser. Le impostazioni dei server si
    /// possono correggere a mano. ShowDialog&lt;Account?&gt; restituisce l'account aggiunto, o null.
    /// </summary>
    public sealed class AccountWindow : DockWindow
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private readonly MailSession _mail;
        private readonly TextBox _name = new();
        private readonly TextBox _email = new();
        private readonly TextBox _password = new() { PasswordChar = '•', RevealPassword = false };
        private readonly UiTextBlock _passwordLabel;
        private readonly UiTextBlock _provider = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        private readonly UiTextBlock _note = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        private readonly UiTextBlock _error = new() { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        private readonly Expander _manual = new();
        private readonly TextBox _imapHost = new();
        private readonly NumericUpDown _imapPort = new() { Minimum = 1, Maximum = 65535, FormatString = "0", Increment = 1 };
        private readonly ComboBox _imapSecurity = new() { MinWidth = 120 };
        private readonly TextBox _smtpHost = new();
        private readonly NumericUpDown _smtpPort = new() { Minimum = 1, Maximum = 65535, FormatString = "0", Increment = 1 };
        private readonly ComboBox _smtpSecurity = new() { MinWidth = 120 };
        private readonly TextBox _user = new();
        private readonly Button _connect;
        private readonly Button _cancel;
        private readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4, MinHeight = 4 };
        private ProviderGuess? _guess;
        private string? _guessedFor;
        private CancellationTokenSource? _work;

        private static string P(string key) => DockPost.Text.T(key);
        private static string PF(string key, params object?[] args) => DockPost.Text.F(key, args);

        public AccountWindow(MailSession mail) : base(AppInfo.Branding)
        {
            _mail = mail;
            Title = P("account.title");
            Icon = AppInfo.Icon;
            Width = 520;

            foreach (var box in new[] { _imapSecurity, _smtpSecurity })
                box.ItemsSource = new[] { "SSL/TLS", "STARTTLS", P("account.noSecurity") };
            _email.PlaceholderText = "nome@esempio.it";
            _passwordLabel = Label(P("account.password"));
            _email.LostFocus += async (_, _) => await Detect(force: false);
            _email.TextChanged += (_, _) => { if (_guessedFor != null && _guessedFor != _email.Text?.Trim()) ShowGuess(null); };

            _manual.Header = P("account.manual");
            _manual.Content = BuildManual();
            _manual.HorizontalAlignment = HorizontalAlignment.Stretch;

            _connect = ButtonFor(P("account.connect"), async () => await Connect(), accent: true);
            _cancel = ButtonFor(P("common.cancel"), () => { _work?.Cancel(); Close(null); });

            SetBody(
                Text(P("account.heading"), "heading"),
                Text(P("account.intro"), "muted"),
                Label(P("account.name")), _name,
                Label(P("account.email")), _email,
                _passwordLabel, _password,
                _provider, _note,
                _manual,
                _busy, _error,
                Buttons(_cancel, _connect));
            Opened += (_, _) => _name.Focus();
        }

        private static UiTextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 4, 0, -4), FontSize = 13 };

        private Control BuildManual()
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,90,Auto"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                ColumnSpacing = 8, RowSpacing = 8, Margin = new Thickness(0, 6, 0, 0),
            };
            void Row(int r, string label, Control host, Control? port, Control? security)
            {
                var l = new UiTextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(l, r);
                grid.Children.Add(l);
                Grid.SetRow(host, r);
                Grid.SetColumn(host, 1);
                if (port == null) Grid.SetColumnSpan(host, 3);
                grid.Children.Add(host);
                if (port != null) { Grid.SetRow(port, r); Grid.SetColumn(port, 2); grid.Children.Add(port); }
                if (security != null) { Grid.SetRow(security, r); Grid.SetColumn(security, 3); grid.Children.Add(security); }
            }
            Row(0, "IMAP", _imapHost, _imapPort, _imapSecurity);
            Row(1, "SMTP", _smtpHost, _smtpPort, _smtpSecurity);
            Row(2, P("account.user"), _user, null, null);
            return grid;
        }

        private static int Index(Security s) => s switch { Security.Tls => 0, Security.StartTls => 1, _ => 2 };
        private static Security FromIndex(int i) => i switch { 0 => Security.Tls, 1 => Security.StartTls, _ => Security.None };

        /// <summary>Riconosce il provider dall'indirizzo e riempie le impostazioni dei server.</summary>
        private async Task<bool> Detect(bool force)
        {
            var email = _email.Text?.Trim() ?? "";
            if (Providers.DomainOf(email) == null) return false;
            if (!force && _guessedFor == email) return true;
            _work?.Cancel();
            _work = new CancellationTokenSource();
            Busy(true);
            try
            {
                var guess = await Providers.DetectAsync(email, Http, _work.Token);
                if (_email.Text?.Trim() != email) return false;
                ShowGuess(guess);
                _guessedFor = email;
                return guess != null;
            }
            catch (OperationCanceledException) { return false; }
            finally { Busy(false); }
        }

        private void ShowGuess(ProviderGuess? guess)
        {
            _guess = guess;
            _guessedFor = null;
            _provider.IsVisible = guess != null;
            _note.IsVisible = guess?.NoteKey != null;
            bool microsoft = guess?.Auth == AuthKind.Microsoft;
            _password.IsVisible = _passwordLabel.IsVisible = !microsoft;
            _connect.Content = microsoft ? P("account.signInMicrosoft") : P("account.connect");
            if (guess == null) return;
            _provider.Text = guess.Source == "guess" ? P("account.guessed") : PF("account.recognized", guess.Name);
            if (guess.NoteKey != null) _note.Text = P(guess.NoteKey);
            _imapHost.Text = guess.Imap.Host;
            _imapPort.Value = guess.Imap.Port;
            _imapSecurity.SelectedIndex = Index(guess.Imap.Security);
            _smtpHost.Text = guess.Smtp.Host;
            _smtpPort.Value = guess.Smtp.Port;
            _smtpSecurity.SelectedIndex = Index(guess.Smtp.Security);
            _user.Text = guess.UserName;
            if (guess.Source == "guess") _manual.IsExpanded = true;
        }

        private void Busy(bool busy)
        {
            _busy.IsVisible = busy;
            _connect.IsEnabled = !busy;
        }

        private void Error(string? text)
        {
            _error.Text = text;
            _error.IsVisible = text != null;
        }

        private async Task Connect()
        {
            Error(null);
            var email = _email.Text?.Trim() ?? "";
            if (Providers.DomainOf(email) == null) { Error(P("account.badEmail")); return; }
            if (_guessedFor != email && !await Detect(force: true) && _guess == null) { Error(P("account.notFound")); return; }
            var guess = _guess!;
            var account = new Account
            {
                DisplayName = (_name.Text ?? "").Trim(),
                Email = email,
                UserName = string.IsNullOrWhiteSpace(_user.Text) ? email : _user.Text.Trim(),
                Imap = new ServerSettings((_imapHost.Text ?? "").Trim(), (int)(_imapPort.Value ?? 993), FromIndex(_imapSecurity.SelectedIndex)),
                Smtp = new ServerSettings((_smtpHost.Text ?? "").Trim(), (int)(_smtpPort.Value ?? 465), FromIndex(_smtpSecurity.SelectedIndex)),
                Auth = guess.Auth,
                ProviderId = guess.ProviderId,
                ServerSavesSent = guess.ServerSavesSent,
            };
            if (account.Imap.Host.Length == 0 || account.Smtp.Host.Length == 0) { _manual.IsExpanded = true; Error(P("account.missingServer")); return; }
            if (account.Imap.Security == Security.None || account.Smtp.Security == Security.None)
            {
                Error(P("account.insecure"));
                return;
            }

            string? password = null;
            _work?.Cancel();
            _work = new CancellationTokenSource();
            var cancel = _work.Token;
            Busy(true);
            try
            {
                if (account.Auth == AuthKind.Microsoft)
                {
                    if (_mail.Microsoft is not { IsConfigured: true } ms) { Error(P("account.microsoftUnavailable")); return; }
                    var signedIn = await ms.SignInAsync(email, cancel);
                    account.UserName = signedIn;
                }
                else
                {
                    password = _password.Text ?? "";
                    if (password.Length == 0) { Error(P("account.noPassword")); return; }
                }
                // prova con un archivio di segreti temporaneo: la password si salva solo se la connessione riesce
                var trial = new MemorySecretStore();
                if (password != null) trial.Write(account.SecretKey, password);
                await using (var client = new MailClient(account, _mail.Store, trial, _mail.Microsoft))
                    await client.TestAsync(cancel);
                _mail.AddAccount(account, password);
                Close(account);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Error(MainWindow.Explain(ex is MailException ? ex : MailException.Wrap(ex, account.Imap.Host)));
                if (ex is MailException { Kind: MailErrorKind.Network or MailErrorKind.Certificate }) _manual.IsExpanded = true;
            }
            finally { Busy(false); }
        }
    }
}
