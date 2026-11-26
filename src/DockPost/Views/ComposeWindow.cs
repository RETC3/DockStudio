using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DockPost.Engine;
using DockPost.Mail;
using static DockPost.Text;
using UiTextBlock = Avalonia.Controls.TextBlock;

namespace DockPost.Views
{
    /// <summary>
    /// Scrittura di un messaggio: mittente (se ci sono più account), destinatari, oggetto, testo e allegati.
    /// Chiudendo con del testo scritto si chiede conferma.
    /// </summary>
    public sealed class ComposeWindow : Window
    {
        private const long AttachmentWarning = 20L * 1024 * 1024;

        private readonly MailSession _mail;
        private readonly Draft _draft;
        private readonly ComboBox _from = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _to = new();
        private readonly TextBox _cc = new();
        private readonly TextBox _bcc = new();
        private readonly TextBox _subject = new();
        private readonly TextBox _body = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 200 };
        private readonly WrapPanel _attachments = new();
        private readonly UiTextBlock _status = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _send = new() { Classes = { "accent" } };
        private readonly Grid _ccRow;
        private bool _sent;
        private bool _confirmedClose;
        private readonly string _initialBody;

        public ComposeWindow(MailSession mail, Draft draft)
        {
            _mail = mail;
            _draft = draft;
            _initialBody = draft.Body;
            Classes.Add("dock");
            Icon = AppInfo.Icon;
            Width = 780;
            Height = 680;
            MinWidth = 520;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _from.ItemsSource = mail.Accounts.Items.Select(a => a.Title).ToList();
            _from.SelectedIndex = Math.Max(0, mail.Accounts.Items.ToList().FindIndex(a => a.Id == draft.AccountId));
            _to.Text = draft.To;
            _cc.Text = draft.Cc;
            _bcc.Text = draft.Bcc;
            _subject.Text = draft.Subject;
            _body.Text = draft.Body;
            _to.PlaceholderText = T("compose.toHint");
            _subject.TextChanged += (_, _) => UpdateTitle();
            UpdateTitle();

            _send.Content = "➤  " + T("compose.send");
            _send.Click += async (_, _) => await Send();
            var attach = new Button { Content = "📎  " + T("compose.attach") };
            attach.Click += async (_, _) => await PickAttachments();
            var showCc = new Button { Content = T("compose.showCc"), Padding = new Thickness(8, 4) };

            var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), RowSpacing = 6 };
            void Field(int row, string label, Control box)
            {
                var l = new UiTextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
                Grid.SetRow(l, row);
                Grid.SetRow(box, row);
                Grid.SetColumn(box, 1);
                fields.Children.Add(l);
                fields.Children.Add(box);
            }
            Field(0, T("compose.from"), _from);
            var toRow = new DockPanel();
            DockPanel.SetDock(showCc, Dock.Right);
            showCc.Margin = new Thickness(6, 0, 0, 0);
            toRow.Children.Add(showCc);
            toRow.Children.Add(_to);
            Field(1, T("compose.to"), toRow);
            _ccRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), ColumnSpacing = 6 };
            var bccLabel = new UiTextBlock { Text = T("compose.bcc"), VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
            Grid.SetColumn(bccLabel, 1);
            Grid.SetColumn(_bcc, 2);
            _ccRow.Children.Add(_cc);
            _ccRow.Children.Add(bccLabel);
            _ccRow.Children.Add(_bcc);
            Field(2, T("compose.cc"), _ccRow);
            Field(3, T("compose.subject"), _subject);
            bool ccVisible = !string.IsNullOrWhiteSpace(draft.Cc) || !string.IsNullOrWhiteSpace(draft.Bcc);
            foreach (var c in fields.Children.Where(c => Grid.GetRow(c) == 2)) c.IsVisible = ccVisible;
            showCc.IsVisible = !ccVisible;
            showCc.Click += (_, _) =>
            {
                foreach (var c in fields.Children.Where(c => Grid.GetRow(c) == 2)) c.IsVisible = true;
                showCc.IsVisible = false;
                _cc.Focus();
            };
            if (mail.Accounts.Items.Count < 2)
                foreach (var c in fields.Children.Where(c => Grid.GetRow(c) == 0)) c.IsVisible = false;

            foreach (var path in draft.Attachments) AddAttachmentChip(path, null);
            foreach (var carried in draft.CarriedAttachments) AddAttachmentChip(null, carried.Name);

            var actions = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            right.Children.Add(attach);
            right.Children.Add(_send);
            DockPanel.SetDock(right, Dock.Right);
            actions.Children.Add(right);
            actions.Children.Add(_status);

            var root = new DockPanel { Margin = new Thickness(16) };
            DockPanel.SetDock(fields, Dock.Top);
            DockPanel.SetDock(_attachments, Dock.Top);
            DockPanel.SetDock(actions, Dock.Bottom);
            _attachments.Margin = new Thickness(80, 6, 0, 0);
            _body.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(fields);
            root.Children.Add(_attachments);
            root.Children.Add(actions);
            root.Children.Add(_body);
            Content = root;

            KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; await Send(); }
            };
            Opened += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_to.Text)) _to.Focus();
                else { _body.Focus(); _body.CaretIndex = 0; }
            };
            Closing += async (_, e) =>
            {
                if (_sent || _confirmedClose || !HasWork()) return;
                e.Cancel = true;
                var confirm = new ConfirmWindow(T("compose.discardTitle"), T("compose.discardBody"), T("compose.discard"));
                if (await confirm.ShowDialog<bool>(this))
                {
                    _confirmedClose = true;
                    Close();
                }
            };
        }

        public event Action? Sent;

        private bool HasWork() =>
            (_body.Text ?? "") != _initialBody || _draft.Attachments.Count > 0 && _initialBody.Length == 0 ||
            (!string.IsNullOrWhiteSpace(_to.Text) && _draft.To.Length == 0) || (!string.IsNullOrWhiteSpace(_subject.Text) && _draft.Subject.Length == 0);

        private void UpdateTitle() =>
            Title = string.IsNullOrWhiteSpace(_subject.Text) ? T("compose.title") : _subject.Text;

        private void AddAttachmentChip(string? path, string? carriedName)
        {
            var name = carriedName ?? Path.GetFileName(path!);
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3), Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(AppInfo.Accent, 0.12),
            };
            var remove = new Button { Content = "✕", Padding = new Thickness(6, 0), Background = Brushes.Transparent, FontSize = 11 };
            remove.Click += (_, _) =>
            {
                if (path != null) _draft.Attachments.Remove(path);
                else _draft.CarriedAttachments.RemoveAll(c => c.Name == carriedName);
                _attachments.Children.Remove(chip);
            };
            string size = path != null && File.Exists(path) ? ReadingPane.FormatSize(new FileInfo(path).Length) :
                carriedName != null ? ReadingPane.FormatSize(_draft.CarriedAttachments.First(c => c.Name == carriedName).Data.Length) : "";
            chip.Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4,
                Children = { new UiTextBlock { Text = $"📎 {name}  ({size})", VerticalAlignment = VerticalAlignment.Center }, remove },
            };
            _attachments.Children.Add(chip);
        }

        private async Task PickAttachments()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = T("compose.attach") });
            foreach (var f in files)
            {
                var path = f.TryGetLocalPath();
                if (path == null || _draft.Attachments.Contains(path)) continue;
                _draft.Attachments.Add(path);
                AddAttachmentChip(path, null);
            }
        }

        private async Task Send()
        {
            if (!_send.IsEnabled) return;
            var accounts = _mail.Accounts.Items;
            if (_from.SelectedIndex >= 0 && _from.SelectedIndex < accounts.Count) _draft.AccountId = accounts[_from.SelectedIndex].Id;
            _draft.To = _to.Text ?? "";
            _draft.Cc = _cc.Text ?? "";
            _draft.Bcc = _bcc.Text ?? "";
            _draft.Subject = _subject.Text ?? "";
            _draft.Body = _body.Text ?? "";

            if (string.IsNullOrWhiteSpace(_draft.To + _draft.Cc + _draft.Bcc)) { _status.Text = T("compose.noRecipients"); return; }
            try { Composer.ParseAddresses(_draft.To); Composer.ParseAddresses(_draft.Cc); Composer.ParseAddresses(_draft.Bcc); }
            catch (AddressException ex) { _status.Text = F("compose.badAddress", ex.Address); return; }
            if (string.IsNullOrWhiteSpace(_draft.Subject))
            {
                var confirm = new ConfirmWindow(T("compose.noSubjectTitle"), T("compose.noSubjectBody"), T("compose.send"));
                if (!await confirm.ShowDialog<bool>(this)) return;
            }
            long total = _draft.Attachments.Where(File.Exists).Sum(p => new FileInfo(p).Length) + _draft.CarriedAttachments.Sum(c => (long)c.Data.Length);
            if (total > AttachmentWarning)
            {
                var confirm = new ConfirmWindow(T("compose.bigTitle"), F("compose.bigBody", ReadingPane.FormatSize(total)), T("compose.send"));
                if (!await confirm.ShowDialog<bool>(this)) return;
            }

            _send.IsEnabled = false;
            _status.Text = T("compose.sending");
            try
            {
                await _mail.SendAsync(_draft, CancellationToken.None);
                _sent = true;
                Sent?.Invoke();
                Close();
            }
            catch (Exception ex)
            {
                _status.Text = F("compose.failed", MainWindow.Explain(ex));
                _send.IsEnabled = true;
            }
        }

        // ---- Per i controlli automatici ------------------------------------------------------------------
        internal string BodyText => _body.Text ?? "";
    }
}
