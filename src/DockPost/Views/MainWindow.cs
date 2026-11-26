using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DockPost.Engine;
using DockPost.Mail;
using DockStudio.Core.Licensing;
using DockStudio.Core.Localization;
using DockStudio.Core.Suite;
using DockStudio.Core.Updates;
using DockStudio.UI;
using DockStudio.UI.Windows;
using static DockPost.Text;
using UiTextBlock = Avalonia.Controls.TextBlock;

namespace DockPost.Views
{
    /// <summary>
    /// Finestra principale a tre riquadri: account e cartelle a sinistra, messaggi al centro, lettura a destra.
    /// In alto le azioni sui messaggi e la ricerca; in basso lo stato della sincronizzazione.
    /// </summary>
    public sealed class MainWindow : Window
    {
        private readonly MailSession _mail;
        private readonly StartOptions _start;

        private readonly Button _new = new() { Classes = { "accent" } };
        private readonly Button _reply = new();
        private readonly Button _replyAll = new();
        private readonly Button _forward = new();
        private readonly Button _delete = new();
        private readonly Button _read = new();
        private readonly Button _flag = new();
        private readonly Button _move = new();
        private readonly Button _sync = new();
        private readonly TextBox _search = new() { Margin = new Thickness(10, 0, 10, 6) };
        private readonly Button _menu = new() { Content = "⋯", Padding = new Thickness(12, 7) };
        private readonly Button _update = new() { IsVisible = false, Classes = { "accent" } };
        private UpdateInfo? _available;

        private readonly ListBox _folders = new();
        private readonly ListBox _messages = new();
        private readonly UiTextBlock _listTitle = new() { Classes = { "heading" }, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly UiTextBlock _listEmpty = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), IsVisible = false };
        private readonly ReadingPane _reading = new();
        private readonly UiTextBlock _status = new() { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ProgressBar _progress = new() { IsVisible = false, IsIndeterminate = true, Width = 120, Height = 4, MinHeight = 4, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _welcome;
        private readonly Grid _workspace;

        private long? _folderId;
        private MessageRow? _selected;
        private long? _shownId;
        private CancellationTokenSource? _openCancel;
        private CancellationTokenSource? _syncCancel;
        private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
        private readonly DispatcherTimer _autoSync = new();
        private bool _refreshQueued;
        private bool _rebuilding;

        public MainWindow(MailSession mail, StartOptions start)
        {
            _mail = mail;
            _start = start;
            Classes.Add("dock");
            Title = "DockPost";
            Icon = AppInfo.Icon;
            Width = App.Settings.Width ?? 1280;
            Height = App.Settings.Height ?? 800;
            MinWidth = 860;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _new.Content = "✉  " + T("action.new");
            _reply.Content = T("action.reply");
            _replyAll.Content = T("action.replyAll");
            _forward.Content = T("action.forward");
            _delete.Content = T("action.delete");
            _read.Content = T("action.markRead");
            _flag.Content = T("action.flag");
            _move.Content = T("action.move");
            _sync.Content = "⟳  " + T("action.sync");
            _search.PlaceholderText = T("search.watermark");
            ToolTip.SetTip(_search, T("search.tip"));
            ToolTip.SetTip(_delete, T("action.deleteTip"));
            ToolTip.SetTip(_sync, T("action.syncTip"));

            _new.Click += (_, _) => Compose(null, DraftKind.New);
            _reply.Click += (_, _) => Respond(DraftKind.Reply);
            _replyAll.Click += (_, _) => Respond(DraftKind.ReplyAll);
            _forward.Click += (_, _) => Respond(DraftKind.Forward);
            _delete.Click += async (_, _) => await DeleteSelected();
            _read.Click += async (_, _) => { if (_selected is { } r) await _mail.SetFlagsAsync(r, !r.Seen, null); };
            _flag.Click += async (_, _) => { if (_selected is { } r) await _mail.SetFlagsAsync(r, null, !r.Flagged); };
            _move.Flyout = new MenuFlyout();
            ((MenuFlyout)_move.Flyout).Opening += (_, _) => FillMoveMenu((MenuFlyout)_move.Flyout!);
            _sync.Click += (_, _) => SyncNow();
            _menu.Flyout = BuildMenu();
            _update.Click += (_, _) => { if (_available != null) ShowUpdate(_available); };
            _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); LoadMessages(keepSelection: false); };
            _search.TextChanged += (_, _) => { _searchDelay.Stop(); _searchDelay.Start(); };
            _search.KeyDown += (_, e) => { if (e.Key == Key.Escape) _search.Text = ""; };

            var toolbar = new DockPanel { Margin = new Thickness(12, 10, 12, 8), LastChildFill = false };
            var left = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
            left.Children.Add(new LogoMark());
            left.Children.Add(new UiTextBlock { Text = "DockPost", Classes = { "brand" }, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            foreach (var b in new Control[] { _new, Gap(), _reply, _replyAll, _forward, Gap(), _delete, _move, _read, _flag, Gap(), _sync })
                left.Children.Add(b);
            foreach (var c in left.Children) c.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(left, Dock.Left);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            right.Children.Add(_update);
            right.Children.Add(_menu);
            foreach (var c in right.Children) c.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(right, Dock.Right);
            toolbar.Children.Add(right);
            toolbar.Children.Add(left);
            toolbar.LastChildFill = true;

            _workspace = BuildWorkspace();
            _welcome = BuildWelcome();

            var statusBar = new DockPanel { Margin = new Thickness(14, 4, 14, 6) };
            DockPanel.SetDock(_progress, Dock.Right);
            statusBar.Children.Add(_progress);
            statusBar.Children.Add(_status);

            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            DockPanel.SetDock(statusBar, Dock.Bottom);
            root.Children.Add(toolbar);
            root.Children.Add(statusBar);
            root.Children.Add(new Panel { Children = { _welcome, _workspace } });
            Content = root;

            KeyDown += OnKey;
            _mail.Changed += QueueRefresh;
            _mail.Failed += (account, ex) => Dispatcher.UIThread.Post(() => ShowFailure(account, ex));
            _mail.Progress += p => Dispatcher.UIThread.Post(() =>
            {
                if (p.Done < p.Total) _status.Text = F("status.syncing", p.Folder);
            });
            _reading.TrustSender += address =>
            {
                if (!App.Settings.TrustedImageSenders.Contains(address)) App.Settings.TrustedImageSenders.Add(address);
                App.SettingsStore.Save(App.Settings);
            };

            _autoSync.Tick += (_, _) => SyncNow();
            RefreshAll();
            UpdateButtons();

            Opened += (_, _) =>
            {
                if (_start.Automatic) StartAutomatic();
                if (_mail.Accounts.Items.Count > 0 && !_mail.Offline) SyncNow();
                if (App.Settings.CheckEveryMinutes > 0)
                {
                    _autoSync.Interval = TimeSpan.FromMinutes(App.Settings.CheckEveryMinutes);
                    _autoSync.Start();
                }
            };
            Closing += (_, _) =>
            {
                _syncCancel?.Cancel();
                _autoSync.Stop();
                if (WindowState == WindowState.Normal)
                {
                    App.Settings.Width = Width;
                    App.Settings.Height = Height;
                }
                if (_workspace.ColumnDefinitions.Count >= 3)
                {
                    App.Settings.FolderPaneWidth = _workspace.ColumnDefinitions[0].ActualWidth;
                    App.Settings.ListPaneWidth = _workspace.ColumnDefinitions[2].ActualWidth;
                }
            };
        }

        private static Control Gap() => new Border { Width = 6 };

        // ---- Costruzione ----------------------------------------------------------------------------------

        private Border BuildWelcome()
        {
            var panel = new StackPanel { Spacing = 12, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new LogoMark { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center });
            panel.Children.Add(new UiTextBlock { Text = T("welcome.title"), Classes = { "title" }, TextAlignment = TextAlignment.Center });
            panel.Children.Add(new UiTextBlock { Text = T("welcome.body"), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
            var add = new Button { Content = T("welcome.add"), Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            add.Click += async (_, _) => await AddAccount();
            panel.Children.Add(add);
            return new Border { Child = panel, Margin = new Thickness(16) };
        }

        private Grid BuildWorkspace()
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"{Clamp(App.Settings.FolderPaneWidth, 160, 420)},4,{Clamp(App.Settings.ListPaneWidth, 260, 700)},4,*"),
                Margin = new Thickness(12, 0, 12, 0),
            };
            grid.ColumnDefinitions[0].MinWidth = 150;
            grid.ColumnDefinitions[2].MinWidth = 240;
            grid.ColumnDefinitions[4].MinWidth = 280;

            // Cartelle: righe pronte (ListBoxItem), poche e senza modello di riga.
            _folders.Background = Brushes.Transparent;
            _folders.SelectionChanged += (_, _) =>
            {
                if (!_rebuilding && _folders.SelectedItem is ListBoxItem { Tag: FolderInfo f } && f.Id != _folderId)
                {
                    _folderId = f.Id;
                    LoadMessages(keepSelection: false);
                }
            };
            grid.Children.Add(Card(_folders, 0));

            // Messaggi: elenco virtualizzato; il modello gestisce anche l'elemento null (righe riciclate).
            _messages.ItemTemplate = new FuncDataTemplate<MessageRow>((row, _) => row == null ? new Panel() : MessageRowView(row));
            _messages.SelectionMode = SelectionMode.Single;
            _messages.SelectionChanged += (_, _) => OpenSelected();
            _messages.DoubleTapped += (_, _) => { if (_selected != null) OpenInWindow(_selected); };
            _messages.ContextMenu = BuildMessageMenu();
            var listHeader = new Border { Padding = new Thickness(14, 10, 14, 6), Child = _listTitle };
            var listPanel = new DockPanel();
            DockPanel.SetDock(_search, Dock.Top);
            DockPanel.SetDock(listHeader, Dock.Top);
            listPanel.Children.Add(listHeader);
            listPanel.Children.Add(_search);
            listPanel.Children.Add(new Panel { Children = { _messages, _listEmpty } });
            var listCard = Card(listPanel, 2);
            grid.Children.Add(listCard);

            var readCard = new Border { Classes = { "card" }, Padding = new Thickness(0), ClipToBounds = true, Child = _reading };
            Grid.SetColumn(readCard, 4);
            grid.Children.Add(readCard);

            foreach (var col in new[] { 1, 3 })
            {
                var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
                Grid.SetColumn(splitter, col);
                grid.Children.Add(splitter);
            }
            return grid;
        }

        private static double Clamp(double v, double min, double max) => double.IsNaN(v) || v <= 0 ? min : Math.Clamp(v, min, max);

        private static Border Card(Control child, int column)
        {
            var card = new Border { Classes = { "card" }, Padding = new Thickness(0, 4), ClipToBounds = true, Child = child };
            Grid.SetColumn(card, column);
            return card;
        }

        private static string FolderTitle(FolderInfo f) => f.Kind switch
        {
            FolderKind.Inbox => T("folder.inbox"),
            FolderKind.Drafts => T("folder.drafts"),
            FolderKind.Sent => T("folder.sent"),
            FolderKind.Archive => T("folder.archive"),
            FolderKind.Junk => T("folder.junk"),
            FolderKind.Trash => T("folder.trash"),
            _ => f.Name,
        };

        private static string FolderIcon(FolderKind kind) => kind switch
        {
            FolderKind.Inbox => "📥",
            FolderKind.Drafts => "📝",
            FolderKind.Sent => "📤",
            FolderKind.Archive => "🗄",
            FolderKind.Junk => "⚠",
            FolderKind.Trash => "🗑",
            _ => "📁",
        };

        private Control MessageRowView(MessageRow row)
        {
            var weight = row.Seen ? FontWeight.Normal : FontWeight.SemiBold;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("4,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Margin = new Thickness(0, 4) };
            var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = row.Seen ? Brushes.Transparent : new SolidColorBrush(AppInfo.Accent), Margin = new Thickness(0, 2) };
            Grid.SetRowSpan(bar, 3);
            grid.Children.Add(bar);
            var sender = new UiTextBlock { Text = row.Sender, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 8, 0) };
            Grid.SetColumn(sender, 1);
            grid.Children.Add(sender);
            var date = new UiTextBlock { Text = FormatDate(row.Date), Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(date, 2);
            grid.Children.Add(date);
            var subject = new UiTextBlock
            {
                Text = string.IsNullOrWhiteSpace(row.Subject) ? T("message.noSubject") : row.Subject,
                FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13, Margin = new Thickness(8, 1, 8, 0),
            };
            if (!row.Seen) subject.Foreground = new SolidColorBrush(AppInfo.Accent);
            Grid.SetColumn(subject, 1);
            Grid.SetRow(subject, 1);
            grid.Children.Add(subject);
            var marks = new UiTextBlock { Text = (row.HasAttachments ? "📎" : "") + (row.Flagged ? " ⚑" : "") + (row.Answered ? " ↩" : ""), FontSize = 12 };
            if (row.Flagged) marks.Foreground = new SolidColorBrush(Color.Parse("#D9480F"));
            Grid.SetColumn(marks, 2);
            Grid.SetRow(marks, 1);
            grid.Children.Add(marks);
            var preview = new UiTextBlock { Text = row.Preview, Classes = { "muted" }, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 1, 0, 0) };
            Grid.SetColumn(preview, 1);
            Grid.SetColumnSpan(preview, 2);
            Grid.SetRow(preview, 2);
            grid.Children.Add(preview);
            return grid;
        }

        /// <summary>Oggi: l'ora; questa settimana: giorno e ora; quest'anno: giorno e mese; prima: la data intera.</summary>
        public static string FormatDate(DateTimeOffset date, DateTime? today = null)
        {
            var c = Languages.Culture;
            var local = date.ToLocalTime().DateTime;
            var now = today ?? DateTime.Now;
            if (local.Date == now.Date) return local.ToString("t", c);
            if (local.Date > now.Date.AddDays(-6) && local.Date < now.Date) return local.ToString("ddd ", c) + local.ToString("t", c);
            if (local.Year == now.Year) return local.ToString("d MMM", c);
            return local.ToString("d", c);
        }

        private MenuFlyout BuildMenu()
        {
            var flyout = new MenuFlyout();
            var add = new MenuItem { Header = T("menu.addAccount") };
            add.Click += async (_, _) => await AddAccount();
            flyout.Items.Add(add);
            var remove = new MenuItem { Header = T("menu.removeAccount") };
            flyout.Items.Add(remove);
            flyout.Opening += (_, _) =>
            {
                remove.Items.Clear();
                foreach (var a in _mail.Accounts.Items)
                {
                    var item = new MenuItem { Header = a.Title };
                    item.Click += async (_, _) => await RemoveAccount(a);
                    remove.Items.Add(item);
                }
                remove.IsEnabled = remove.Items.Count > 0;
            };
            flyout.Items.Add(new Separator());
            var appearance = new MenuItem { Header = T("menu.appearance") };
            foreach (var a in Enum.GetValues<Appearance>())
            {
                var item = new MenuItem { Header = T("appearance." + a.ToString().ToLowerInvariant()) };
                item.Click += (_, _) =>
                {
                    App.Settings.Appearance = a.ToString();
                    DockTheme.SetAppearance(Application.Current!, a);
                    App.SettingsStore.Save(App.Settings);
                    RefreshAll();
                };
                appearance.Items.Add(item);
            }
            flyout.Items.Add(appearance);
            var privacy = new MenuItem { Header = T("menu.privacy") };
            privacy.Click += async (_, _) => await AskPrivacy();
            flyout.Items.Add(privacy);
            var check = new MenuItem { Header = T("menu.checkUpdates") };
            check.Click += (_, _) => CheckForUpdates(manual: true);
            flyout.Items.Add(check);
            flyout.Items.Add(new Separator());
            var about = new MenuItem { Header = F("menu.about", "DockPost") };
            about.Click += (_, _) => ShowAbout();
            flyout.Items.Add(about);
            return flyout;
        }

        private ContextMenu BuildMessageMenu()
        {
            var menu = new ContextMenu();
            void Add(string key, Action action)
            {
                var item = new MenuItem { Header = T(key) };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
            Add("action.reply", () => Respond(DraftKind.Reply));
            Add("action.replyAll", () => Respond(DraftKind.ReplyAll));
            Add("action.forward", () => Respond(DraftKind.Forward));
            menu.Items.Add(new Separator());
            Add("action.toggleRead", async () => { if (_selected is { } r) await _mail.SetFlagsAsync(r, !r.Seen, null); });
            Add("action.flag", async () => { if (_selected is { } r) await _mail.SetFlagsAsync(r, null, !r.Flagged); });
            Add("action.openWindow", () => { if (_selected is { } r) OpenInWindow(r); });
            menu.Items.Add(new Separator());
            Add("action.delete", async () => await DeleteSelected());
            menu.Opening += (_, e) => { if (_selected == null) e.Cancel = true; };
            return menu;
        }

        private void FillMoveMenu(MenuFlyout flyout)
        {
            flyout.Items.Clear();
            if (_selected is not { } row || _mail.AccountOfFolder(row.FolderId) is not { } account) return;
            foreach (var f in _mail.Store.Folders(account.Id).Where(f => f.Id != row.FolderId))
            {
                var item = new MenuItem { Header = new string(' ', f.Depth * 3) + FolderIcon(f.Kind) + "  " + FolderTitle(f) };
                item.Click += async (_, _) => await Run(async () =>
                {
                    await _mail.MoveAsync(row, f.Id);
                    _status.Text = F("status.moved", FolderTitle(f));
                });
                flyout.Items.Add(item);
            }
        }

        // ---- Contenuto ------------------------------------------------------------------------------------

        private void QueueRefresh()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _refreshQueued = false;
                RefreshAll();
            }, DispatcherPriority.Background);
        }

        /// <summary>Ricostruisce l'elenco delle cartelle (con i non letti) e ricarica i messaggi della cartella scelta.</summary>
        public void RefreshAll()
        {
            bool hasAccounts = _mail.Accounts.Items.Count > 0;
            _welcome.IsVisible = !hasAccounts;
            _workspace.IsVisible = hasAccounts;
            if (!hasAccounts)
            {
                _folders.ItemsSource = null;
                _folderId = null;
                return;
            }

            var items = new List<ListBoxItem>();
            ListBoxItem? selected = null;
            foreach (var account in _mail.Accounts.Items)
            {
                items.Add(new ListBoxItem
                {
                    Content = new UiTextBlock { Text = account.Email, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                    IsEnabled = false, Padding = new Thickness(12, 10, 12, 4), Opacity = 1,
                });
                var folders = _mail.Store.Folders(account.Id);
                if (folders.Count == 0)
                {
                    items.Add(new ListBoxItem { Content = new UiTextBlock { Text = T("folder.loading"), Classes = { "muted" } }, IsEnabled = false, Padding = new Thickness(24, 4, 12, 4) });
                    continue;
                }
                foreach (var f in folders)
                {
                    var row = new DockPanel();
                    if (f.Unread > 0)
                    {
                        var count = new UiTextBlock { Text = f.Unread.ToString(Languages.Culture), FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(AppInfo.Accent), Margin = new Thickness(6, 0, 0, 0) };
                        DockPanel.SetDock(count, Dock.Right);
                        row.Children.Add(count);
                    }
                    row.Children.Add(new UiTextBlock
                    {
                        Text = FolderIcon(f.Kind) + "  " + FolderTitle(f),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        FontWeight = f.Unread > 0 ? FontWeight.SemiBold : FontWeight.Normal,
                    });
                    var item = new ListBoxItem { Content = row, Tag = f, Padding = new Thickness(16 + 14 * f.Depth, 6, 12, 6) };
                    items.Add(item);
                    if (f.Id == _folderId || (_folderId == null && selected == null && f.Kind == FolderKind.Inbox)) selected = item;
                }
            }
            _rebuilding = true;
            try
            {
                _folders.ItemsSource = items;
                if (selected != null)
                {
                    _folderId = ((FolderInfo)selected.Tag!).Id;
                    _folders.SelectedItem = selected;
                }
            }
            finally { _rebuilding = false; }
            LoadMessages(keepSelection: true);
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            int unread = _mail.Accounts.Items.SelectMany(a => _mail.Store.Folders(a.Id)).Where(f => f.Kind == FolderKind.Inbox).Sum(f => f.Unread);
            Title = unread > 0 ? $"DockPost ({unread})" : "DockPost";
        }

        private void LoadMessages(bool keepSelection)
        {
            if (_folderId is not { } folderId) return;
            var folder = _mail.Store.FolderById(folderId);
            if (folder == null) return;
            var query = SearchQuery.Parse(_search.Text ?? "");
            bool searching = !query.IsEmpty;
            // la ricerca guarda in tutte le cartelle dell'account
            var rows = searching
                ? _mail.Store.Messages(null, query, 2000, folder.AccountId)
                : _mail.Store.Messages(folderId);
            _listTitle.Text = searching ? F("list.results", rows.Count) : FolderTitle(folder);
            var keep = keepSelection ? _selected?.Id : null;
            _messages.ItemsSource = rows;
            _listEmpty.Text = searching ? T("list.noResults") : T("list.empty");
            _listEmpty.IsVisible = rows.Count == 0;
            if (keep != null && rows.FirstOrDefault(r => r.Id == keep) is { } again)
            {
                _selected = again;
                _messages.SelectedItem = again;
            }
            else if (!keepSelection || _selected != null && rows.All(r => r.Id != _selected.Id))
            {
                _selected = null;
                _shownId = null;
                _reading.ShowEmpty();
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool one = _selected != null;
            foreach (var b in new[] { _reply, _replyAll, _forward, _delete, _read, _flag, _move }) b.IsEnabled = one;
            _new.IsEnabled = _mail.Accounts.Items.Count > 0;
            _sync.IsEnabled = _mail.Accounts.Items.Count > 0 && !_mail.Offline;
            _read.Content = _selected is { Seen: true } ? T("action.markUnread") : T("action.markRead");
            _flag.Content = _selected is { Flagged: true } ? T("action.unflag") : T("action.flag");
        }

        private async void OpenSelected()
        {
            var row = _messages.SelectedItem as MessageRow;
            // l'elenco ricaricato (posta nuova, letto/non letto) riseleziona lo stesso messaggio: non lo si ridisegna
            if (row != null && row.Id == _shownId)
            {
                _selected = row;
                UpdateButtons();
                return;
            }
            _selected = row;
            _shownId = null;
            UpdateButtons();
            _openCancel?.Cancel();
            if (row == null) { _reading.ShowEmpty(); return; }
            var cancel = (_openCancel = new CancellationTokenSource()).Token;
            if (_mail.Store.BodyPath(row.Id) == null) _reading.ShowPlaceholder(T("reading.loading"));
            try
            {
                var content = await _mail.OpenAsync(row, cancel);
                if (cancel.IsCancellationRequested) return;
                if (content == null) { _reading.ShowPlaceholder(T("reading.unavailable")); return; }
                var sender = row.FromAddress.ToLowerInvariant();
                _reading.Show(row, content, App.Settings.TrustedImageSenders.Contains(sender));
                _shownId = row.Id;
                if (!row.Seen)
                {
                    _selected = row with { Seen = true };
                    await _mail.SetFlagsAsync(row, true, null);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested) _reading.ShowPlaceholder(Explain(ex));
            }
        }

        private async void OpenInWindow(MessageRow row)
        {
            try
            {
                var content = await _mail.OpenAsync(row, CancellationToken.None);
                if (content == null) return;
                var pane = new ReadingPane();
                pane.Show(row, content, App.Settings.TrustedImageSenders.Contains(row.FromAddress.ToLowerInvariant()));
                var window = new Window
                {
                    Title = string.IsNullOrWhiteSpace(row.Subject) ? T("message.noSubject") : row.Subject,
                    Icon = AppInfo.Icon, Width = 820, Height = 720, Content = pane, Classes = { "dock" },
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                window.Show(this);
            }
            catch (Exception ex) { _status.Text = Explain(ex); }
        }

        // ---- Azioni ---------------------------------------------------------------------------------------

        private void OnKey(object? sender, KeyEventArgs e)
        {
            if (e.Source is TextBox) return;
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            switch (e.Key)
            {
                case Key.N when ctrl: Compose(null, DraftKind.New); break;
                case Key.R when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift): Respond(DraftKind.ReplyAll); break;
                case Key.R when ctrl: Respond(DraftKind.Reply); break;
                case Key.F when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift): Respond(DraftKind.Forward); break;
                case Key.E when ctrl: _search.Focus(); break;
                case Key.Q when ctrl: if (_selected is { } r) _ = _mail.SetFlagsAsync(r, true, null); break;
                case Key.U when ctrl: if (_selected is { } u) _ = _mail.SetFlagsAsync(u, false, null); break;
                case Key.Delete: _ = DeleteSelected(); break;
                case Key.F5: SyncNow(); break;
                default: return;
            }
            e.Handled = true;
        }

        private async Task DeleteSelected()
        {
            if (_selected is not { } row) return;
            var folder = _mail.Store.FolderById(row.FolderId);
            if (folder?.Kind == FolderKind.Trash || _mail.Store.Folders(folder?.AccountId ?? "").All(f => f.Kind != FolderKind.Trash))
            {
                var confirm = new ConfirmWindow(T("delete.title"), T("delete.forever"), T("action.delete"));
                if (!await confirm.ShowDialog<bool>(this)) return;
            }
            int index = _messages.SelectedIndex;
            await Run(async () =>
            {
                bool gone = await _mail.DeleteAsync(row);
                _status.Text = gone ? T("status.deletedForever") : T("status.deleted");
            });
            // si passa al messaggio successivo, come nei programmi di posta più diffusi
            if (_messages.ItemCount > 0) _messages.SelectedIndex = Math.Min(index, _messages.ItemCount - 1);
        }

        private async Task Run(Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex) { _status.Text = Explain(ex); }
        }

        private void Respond(DraftKind kind)
        {
            if (_selected is not { } row) return;
            Compose(row, kind);
        }

        private async void Compose(MessageRow? original, DraftKind kind)
        {
            if (_mail.Accounts.Items.Count == 0) return;
            try
            {
                Draft draft;
                Account account;
                if (original == null)
                {
                    account = _folderId is { } f && _mail.AccountOfFolder(f) is { } a ? a : _mail.Accounts.Items[0];
                    draft = new Draft { AccountId = account.Id };
                    if (account.Signature.Length > 0) draft.Body = "\n\n-- \n" + account.Signature;
                }
                else
                {
                    account = _mail.AccountOfFolder(original.FolderId) ?? _mail.Accounts.Items[0];
                    var content = await _mail.OpenAsync(original, CancellationToken.None);
                    if (content == null) return;
                    draft = Composer.Respond(kind, content, account, T("compose.replyPrefix"), T("compose.forwardPrefix"),
                        T("compose.quoteHeader"), T("compose.forwardHeader"), Languages.Culture);
                    if (kind != DraftKind.Forward) draft.Answers = (original.FolderId, original.Uid);
                }
                var window = new ComposeWindow(_mail, draft);
                window.Sent += () => _status.Text = T("status.sent");
                window.Show(this);
            }
            catch (Exception ex) { _status.Text = Explain(ex); }
        }

        private async Task AddAccount()
        {
            var window = new AccountWindow(_mail);
            var added = await window.ShowDialog<Account?>(this);
            if (added == null) return;
            _folderId = null;
            RefreshAll();
            SyncNow();
        }

        private async Task RemoveAccount(Account account)
        {
            var confirm = new ConfirmWindow(T("remove.title"), F("remove.body", account.Email), T("remove.confirm"));
            if (!await confirm.ShowDialog<bool>(this)) return;
            await _mail.RemoveAccountAsync(account);
            _folderId = null;
            _selected = null;
            _reading.ShowEmpty();
            RefreshAll();
        }

        private async void SyncNow()
        {
            if (_mail.Offline || _mail.IsSyncing || _mail.Accounts.Items.Count == 0) return;
            _syncCancel = new CancellationTokenSource();
            _progress.IsVisible = true;
            _status.Text = T("status.syncStart");
            _lastFailure = null;
            try { await _mail.SyncAllAsync(_syncCancel.Token); }
            finally
            {
                _progress.IsVisible = false;
                if (_lastFailure == null) _status.Text = F("status.synced", DateTime.Now.ToString("t", Languages.Culture));
            }
        }

        private string? _lastFailure;

        private void ShowFailure(Account account, Exception ex)
        {
            _lastFailure = account.Email + ": " + Explain(ex);
            _status.Text = _lastFailure;
        }

        /// <summary>Spiegazione dell'errore con parole dell'app, non del server.</summary>
        public static string Explain(Exception ex) => ex is MailException m
            ? m.Kind switch
            {
                MailErrorKind.Authentication => T("error.auth"),
                MailErrorKind.Certificate => F("error.certificate", m.Server),
                MailErrorKind.SignIn => T("error.signIn"),
                MailErrorKind.Server => F("error.server", m.Message),
                _ => F("error.network", m.Server),
            }
            : F("error.other", ex.Message);

        // ---- Avvio, privacy, aggiornamenti ----------------------------------------------------------------

        private async void StartAutomatic()
        {
            if (!App.Settings.PrivacyChoiceMade)
            {
                if (Shell.TakeInstallerPrivacyChoice() is { } choice) App.ApplyPrivacy(choice.Usage, choice.Errors);
                else await AskPrivacy();
            }
            CheckForUpdates(manual: false);
            StartUsageTimer();
        }

        private async Task AskPrivacy()
        {
            var s = App.Settings;
            var window = new PrivacyWindow(AppInfo.Branding, s.PrivacyChoiceMade && s.SendUsageStats, s.PrivacyChoiceMade && s.SendErrorReports) { Icon = AppInfo.Icon };
            window.Chosen += App.ApplyPrivacy;
            await window.ShowDialog(this);
        }

        private int _unsavedMinutes;

        private void StartUsageTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            timer.Tick += (_, _) =>
            {
                if (!IsActive || !UsageStats.AddUsageMinute(App.Settings)) return;
                if (++_unsavedMinutes < 5) return;
                _unsavedMinutes = 0;
                App.SettingsStore.Save(App.Settings);
            };
            timer.Start();
            Closed += (_, _) => timer.Stop();
        }

        private static async Task SendUsageStatsAsync()
        {
            var hadId = App.Settings.InstallId;
            bool sent = await AppInfo.Usage.SendAsync(App.Settings, Shell.IsInstalled);
            if (sent || App.Settings.InstallId != hadId) App.SettingsStore.Save(App.Settings);
        }

        internal void ShowError(Exception ex) => _status.Text = F("status.unexpected", ex.Message);

        private async void CheckForUpdates(bool manual)
        {
            if (!manual && App.Settings.LastUpdateCheck is DateTime last && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
            if (manual) _status.Text = T("update.checking");
            try
            {
                var update = await AppInfo.Updates.CheckAsync();
                App.Settings.LastUpdateCheck = DateTime.UtcNow;
                App.SettingsStore.Save(App.Settings);
                if (!manual) _ = SendUsageStatsAsync();
                _available = update;
                _update.IsVisible = update != null;
                if (update != null)
                {
                    _update.Content = F("update.button", update.Version.ToString(3));
                    if (manual) ShowUpdate(update);
                }
                else if (manual) _status.Text = F("update.none", AppInfo.Updates.CurrentVersion.ToString(3));
            }
            catch (Exception ex)
            {
                if (manual) _status.Text = F("update.failed", ex.Message);
            }
        }

        private void ShowUpdate(UpdateInfo update)
        {
            Action<string>? launch = Shell.IsInstalled
                ? setup =>
                {
                    Shell.LaunchUpdate(setup);
                    if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        desktop.Shutdown();
                }
                : null;
            new UpdateWindow(AppInfo.Branding, AppInfo.Updates, update, launch) { Icon = AppInfo.Icon }.ShowDialog(this);
        }

        private void ShowAbout()
        {
            var licenses = new LicenseManager(AppInfo.Identity.Name);
            var window = new AboutWindow(AppInfo.Branding, AppInfo.Version, portable: false, licenses.Check(),
                new SuiteRegistry().Installed(),
                openLicense: () =>
                {
                    var lw = new LicenseWindow(AppInfo.Branding, licenses) { Icon = AppInfo.Icon };
                    lw.Show(licenses.Check());
                }) { Icon = AppInfo.Icon };
            window.ShowDialog(this);
        }

        // ---- Per i controlli automatici ------------------------------------------------------------------

        internal void SelectMessage(int index) => _messages.SelectedIndex = index;
        internal void Search(string text) { _search.Text = text; _searchDelay.Stop(); LoadMessages(false); }
        internal int MessageCount => _messages.ItemCount;
        internal IDataTemplate? MessageTemplate => _messages.ItemTemplate;
        internal string StatusText => _status.Text ?? "";
    }
}
