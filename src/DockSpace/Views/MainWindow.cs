using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using DockSpace.Engine;
using DockSpace.Engine.Ntfs;
using DockStudio.Core.Licensing;
using DockStudio.Core.Localization;
using DockStudio.Core.Suite;
using DockStudio.Core.Updates;
using DockStudio.UI;
using DockStudio.UI.Windows;
using static DockSpace.Text;

namespace DockSpace.Views
{
    /// <summary>
    /// Finestra principale: in alto si sceglie cosa analizzare; a sinistra l'albero delle cartelle; a destra il
    /// contenuto della cartella scelta, i tipi di file e i file più grandi; in basso la mappa a riquadri.
    /// </summary>
    public sealed class MainWindow : Window
    {
        private const int LargestCount = 500;
        private const string ContentColumns = "16,*,80,60,84,84,70,100";

        private readonly ComboBox _drives = new() { MinWidth = 300 };
        private readonly CheckBox _fast = new();
        private readonly Button _scan = new() { Classes = { "accent" } };
        private readonly Button _pickFolder = new();
        private readonly Button _stop = new() { IsVisible = false };
        private readonly Button _menu = new() { Content = "⋯", Padding = new Thickness(12, 7) };
        private readonly ComboBox _measure = new() { MinWidth = 170 };
        private readonly Button _update = new() { IsVisible = false, Classes = { "accent" } };
        private UpdateInfo? _available;

        private readonly TreeView _tree = new();
        private readonly ListBox _content = new();
        private readonly ListBox _extensions = new();
        private readonly ListBox _largest = new();
        private readonly StackPanel _categoryBar = new() { Orientation = Orientation.Horizontal, Height = 14 };
        private readonly WrapPanel _categoryLegend = new() { Orientation = Orientation.Horizontal };
        private readonly TabControl _tabs = new();
        private readonly TreemapView _map = new();

        private readonly TextBlock _folderTitle = new() { Classes = { "heading" }, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _folderDetails = new() { Classes = { "muted" } };
        private readonly Button _up = new();
        private readonly TextBlock _status = new() { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ProgressBar _progress = new() { IsVisible = false, Width = 160, Height = 6, MinHeight = 6, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _welcome;
        private readonly Grid _workspace;

        private FsNode? _root;
        private FolderItem? _rootItem;
        private FsNode? _current;
        private FsNode? _selected;
        private bool _syncing;
        private CancellationTokenSource? _scanCancel;
        private CancellationTokenSource? _tabCancel;
        private readonly StartOptions _start;

        public MainWindow(StartOptions start)
        {
            _start = start;
            Classes.Add("dock");
            Title = "DockSpace";
            Icon = AppInfo.Icon;
            Width = App.Settings.Width ?? 1180;
            Height = App.Settings.Height ?? 760;
            MinWidth = 760;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _scan.Content = T("scan.start");
            _pickFolder.Content = T("scan.folder");
            _stop.Content = T("scan.stop");
            _fast.Content = T("scan.fast");
            ToolTip.SetTip(_fast, T("scan.fastTip"));
            _up.Content = T("folder.up");

            _scan.Click += (_, _) => StartSelectedDrive();
            _pickFolder.Click += async (_, _) => await PickFolder();
            _stop.Click += (_, _) => _scanCancel?.Cancel();
            _up.Click += (_, _) => { if (_current?.Parent != null) Navigate(_current.Parent); };
            _drives.SelectionChanged += (_, _) => UpdateFastOption();
            _menu.Flyout = BuildMenu();
            _measure.ItemsSource = new[] { T("measure.onDisk"), T("measure.length") };
            _measure.SelectedIndex = CurrentMeasure == SizeMeasure.Length ? 1 : 0;
            ToolTip.SetTip(_measure, T("measure.tip"));
            _measure.SelectionChanged += (_, _) => ChangeMeasure(_measure.SelectedIndex == 1 ? SizeMeasure.Length : SizeMeasure.OnDisk);

            var toolbar = new DockPanel { Margin = new Thickness(16, 12, 16, 8), LastChildFill = false };
            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            left.Children.Add(new LogoMark());
            left.Children.Add(new TextBlock { Text = "DockSpace", Classes = { "brand" }, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            left.Children.Add(_drives);
            left.Children.Add(_scan);
            left.Children.Add(_fast);
            left.Children.Add(_pickFolder);
            left.Children.Add(_stop);
            foreach (var c in left.Children) c.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(left, Dock.Left);
            DockPanel.SetDock(_menu, Dock.Right);
            DockPanel.SetDock(_measure, Dock.Right);
            DockPanel.SetDock(_update, Dock.Right);
            _update.Margin = new Thickness(0, 0, 8, 0);
            _update.VerticalAlignment = VerticalAlignment.Center;
            _update.Click += (_, _) => { if (_available != null) ShowUpdate(_available); };
            _measure.Margin = new Thickness(0, 0, 8, 0);
            _measure.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(left);
            toolbar.Children.Add(_menu);
            toolbar.Children.Add(_measure);
            toolbar.Children.Add(_update);

            _workspace = BuildWorkspace();
            _workspace.IsVisible = false;
            _welcome = BuildWelcome();

            var statusBar = new DockPanel { Margin = new Thickness(16, 6, 16, 8) };
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

            LoadDrives();
            _status.Text = Shell.IsAdministrator ? T("status.readyAdmin") : T("status.ready");

            Opened += (_, _) =>
            {
                if (_start.ScanPath != null) StartScan(_start.ScanPath, _start.Fast);
                if (_start.CheckUpdates) CheckForUpdates(manual: false);
            };
            Closing += (_, _) =>
            {
                _scanCancel?.Cancel();
                if (WindowState == WindowState.Normal)
                {
                    App.Settings.Width = Width;
                    App.Settings.Height = Height;
                }
            };
        }

        // ---- Costruzione dell'interfaccia ---------------------------------------------------------------

        private Border BuildWelcome()
        {
            var panel = new StackPanel { Spacing = 10, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = T("welcome.title"), Classes = { "title" }, TextAlignment = TextAlignment.Center });
            panel.Children.Add(new TextBlock { Text = T("welcome.body"), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
            return new Border { Child = panel, Margin = new Thickness(16) };
        }

        private Grid BuildWorkspace()
        {
            // Albero delle cartelle
            _tree.ItemTemplate = new FuncTreeDataTemplate<FolderItem>((item, _) => FolderRow(item), item => item.Children);
            _tree.SelectionChanged += (_, _) =>
            {
                if (_syncing || _tree.SelectedItem is not FolderItem item) return;
                Navigate(item.Node, fromTree: true);
            };
            _tree.ContextMenu = BuildContextMenu(() => (_tree.SelectedItem as FolderItem)?.Node);
            // Righe larghe quanto l'albero: nome a sinistra, dimensione e barra sempre visibili a destra.
            ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Disabled);
            _tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>().Template().OfType<ContentPresenter>().Name("PART_HeaderPresenter"))
            {
                Setters =
                {
                    new Setter(ContentPresenter.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                    new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
                },
            });
            var treeCard = new Border { Classes = { "card" }, Padding = new Thickness(4), Child = _tree };

            // Contenuto della cartella
            _content.ItemTemplate = new FuncDataTemplate<FsNode>((node, _) => ContentRow(node));
            _content.SelectionChanged += (_, _) => Select(_content.SelectedItem as FsNode, fromList: true);
            _content.DoubleTapped += (_, _) => Activate(_content.SelectedItem as FsNode);
            _content.KeyDown += OnListKeyDown;
            _content.ContextMenu = BuildContextMenu(() => _content.SelectedItem as FsNode);
            var contentPanel = new DockPanel();
            var header = HeaderRow(ContentColumns, "", T("col.name"), "", T("col.share"), T("col.size"), T("col.onDisk"), T("col.files"), T("col.modified"));
            DockPanel.SetDock(header, Dock.Top);
            contentPanel.Children.Add(header);
            contentPanel.Children.Add(_content);

            // Tipi di file
            _extensions.ItemTemplate = new FuncDataTemplate<ExtensionTotal>((e, _) => ExtensionRow(e));
            var typesPanel = new DockPanel();
            var typesTop = new StackPanel { Spacing = 8, Margin = new Thickness(8, 8, 8, 6) };
            typesTop.Children.Add(new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = _categoryBar });
            typesTop.Children.Add(_categoryLegend);
            var typesHeader = HeaderRow("16,120,*,90,90,64", "", T("col.extension"), T("col.category"), T("col.files"), T("col.size"), T("col.share"));
            var typesHead = new StackPanel { Children = { typesTop, typesHeader } };
            DockPanel.SetDock(typesHead, Dock.Top);
            typesPanel.Children.Add(typesHead);
            typesPanel.Children.Add(_extensions);

            // File più grandi
            _largest.ItemTemplate = new FuncDataTemplate<FsNode>((node, _) => LargestRow(node));
            _largest.SelectionChanged += (_, _) => Select(_largest.SelectedItem as FsNode, fromList: true);
            _largest.KeyDown += OnListKeyDown;
            _largest.ContextMenu = BuildContextMenu(() => _largest.SelectedItem as FsNode);
            var largestPanel = new DockPanel();
            var largestHeader = HeaderRow("16,*,90,120", "", T("col.file"), T("col.size"), T("col.modified"));
            DockPanel.SetDock(largestHeader, Dock.Top);
            largestPanel.Children.Add(largestHeader);
            largestPanel.Children.Add(_largest);

            foreach (var (tabTitle, panel) in new (string, Control)[] { (T("tab.content"), contentPanel), (T("tab.types"), typesPanel), (T("tab.largest"), largestPanel) })
                _tabs.Items.Add(new TabItem { Header = tabTitle, Content = panel, FontSize = 15, MinHeight = 36, Padding = new Thickness(12, 4) });
            _tabs.SelectionChanged += (s, e) =>
            {
                if (ReferenceEquals(e.Source, _tabs)) RefreshTabs();
            };

            var folderHeader = new DockPanel { Margin = new Thickness(4, 0, 4, 6) };
            DockPanel.SetDock(_up, Dock.Right);
            _up.VerticalAlignment = VerticalAlignment.Center;
            folderHeader.Children.Add(_up);
            folderHeader.Children.Add(new StackPanel { Spacing = 2, Children = { _folderTitle, _folderDetails } });

            var right = new DockPanel();
            DockPanel.SetDock(folderHeader, Dock.Top);
            right.Children.Add(folderHeader);
            right.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(4), Child = _tabs });

            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("360,6,*") };
            Grid.SetColumn(treeCard, 0);
            var vsplit = new GridSplitter { Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(vsplit, 1);
            Grid.SetColumn(right, 2);
            top.Children.Add(treeCard);
            top.Children.Add(vsplit);
            top.Children.Add(right);

            _map.NodeClicked += node => Select(node, fromList: false);
            _map.NodeActivated += Activate;
            _map.NodeContextRequested += (node, _) =>
            {
                var menu = BuildContextMenu(() => node);
                menu.Open(_map);
            };
            var mapCard = new Border { Classes = { "card" }, Padding = new Thickness(0), ClipToBounds = true, Child = _map };

            var grid = new Grid { RowDefinitions = new RowDefinitions("*,6,260"), Margin = new Thickness(16, 0, 16, 0) };
            Grid.SetRow(top, 0);
            var hsplit = new GridSplitter { Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Rows };
            Grid.SetRow(hsplit, 1);
            Grid.SetRow(mapCard, 2);
            grid.Children.Add(top);
            grid.Children.Add(hsplit);
            grid.Children.Add(mapCard);
            return grid;
        }

        private MenuFlyout BuildMenu()
        {
            var flyout = new MenuFlyout();
            var appearance = new MenuItem { Header = T("menu.appearance") };
            foreach (var a in Enum.GetValues<Appearance>())
            {
                var item = new MenuItem { Header = T("appearance." + a.ToString().ToLowerInvariant()) };
                item.Click += (_, _) =>
                {
                    App.Settings.Appearance = a.ToString();
                    DockTheme.SetAppearance(Application.Current!, a);
                    App.SettingsStore.Save(App.Settings);
                };
                appearance.Items.Add(item);
            }
            var check = new MenuItem { Header = T("menu.checkUpdates") };
            check.Click += (_, _) => CheckForUpdates(manual: true);
            flyout.Items.Add(check);
            var about = new MenuItem { Header = F("menu.about", "DockSpace") };
            about.Click += (_, _) => ShowAbout();
            flyout.Items.Add(appearance);
            flyout.Items.Add(new Separator());
            flyout.Items.Add(about);
            return flyout;
        }

        // ---- Aggiornamenti ------------------------------------------------------------------------------

        /// <summary>
        /// Cerca una nuova versione. Automatico: al massimo una volta al giorno, in silenzio; se c'è, compare il
        /// pulsante "Aggiorna". Dal menu: sempre, con l'esito nella barra di stato.
        /// </summary>
        private async void CheckForUpdates(bool manual)
        {
            if (!manual && App.Settings.LastUpdateCheck is DateTime last && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
            if (manual) _status.Text = T("update.checking");
            try
            {
                var update = await AppInfo.Updates.CheckAsync();
                App.Settings.LastUpdateCheck = DateTime.UtcNow;
                App.SettingsStore.Save(App.Settings);
                _available = update;
                _update.IsVisible = update != null;
                if (update != null)
                {
                    _update.Content = F("update.button", update.Version.ToString(3));
                    if (manual) ShowUpdate(update);
                }
                else if (manual)
                {
                    _status.Text = F("update.none", AppInfo.Updates.CurrentVersion.ToString(3));
                }
            }
            catch (Exception ex)
            {
                // Senza internet o con il sito irraggiungibile: in automatico non si disturba.
                if (manual) _status.Text = F("update.failed", ex.Message);
            }
        }

        private void ShowUpdate(UpdateInfo update)
        {
            Action<string>? launch = Shell.IsInstalled
                ? setup =>
                {
                    Shell.LaunchUpdate(setup);
                    // L'installer aspetta che DockSpace sia chiuso, poi lo riapre.
                    if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        desktop.Shutdown();
                }
                : null; // versione portatile: solo il link alla pagina
            var window = new UpdateWindow(AppInfo.Branding, AppInfo.Updates, update, launch) { Icon = AppInfo.Icon };
            window.ShowDialog(this);
        }

        private void ShowAbout()
        {
            var licenses = new LicenseManager(AppInfo.Identity.Name);
            var check = licenses.Check();
            var window = new AboutWindow(AppInfo.Branding, AppInfo.Version, portable: false, check,
                new SuiteRegistry().Installed(),
                openLicense: () =>
                {
                    var lw = new LicenseWindow(AppInfo.Branding, licenses) { Icon = AppInfo.Icon };
                    lw.Show(licenses.Check());
                }) { Icon = AppInfo.Icon };
            window.ShowDialog(this);
        }

        private ContextMenu BuildContextMenu(Func<FsNode?> target)
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = T("action.open") };
            var reveal = new MenuItem { Header = T("action.reveal") };
            var copy = new MenuItem { Header = T("action.copyPath") };
            var trash = new MenuItem { Header = T("action.trash") };
            open.Click += (_, _) => Try(() => { if (target() is { } n) Shell.Open(n.FullPath); });
            reveal.Click += (_, _) => Try(() => { if (target() is { } n) Shell.Reveal(n.FullPath); });
            copy.Click += async (_, _) =>
            {
                if (target() is { } n && Clipboard is { } cb) await cb.SetTextAsync(n.FullPath);
            };
            trash.Click += async (_, _) => { if (target() is { } n) await MoveToTrash(n); };
            menu.Items.Add(open);
            menu.Items.Add(reveal);
            menu.Items.Add(copy);
            menu.Items.Add(new Separator());
            menu.Items.Add(trash);
            menu.Opening += (_, e) =>
            {
                var n = target();
                if (n == null) { e.Cancel = true; return; }
                trash.IsEnabled = !n.IsRoot;
            };
            return menu;
        }

        // ---- Righe degli elenchi ------------------------------------------------------------------------

        private static Grid Row(string columns, params Control[] cells)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 8, Background = Brushes.Transparent };
            for (int i = 0; i < cells.Length; i++)
            {
                Grid.SetColumn(cells[i], i);
                cells[i].VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(cells[i]);
            }
            return grid;
        }

        private static Control HeaderRow(string columns, params string[] titles)
        {
            var cells = titles.Select((t, i) => (Control)new TextBlock
            {
                Text = t,
                Classes = { "muted" },
                FontSize = 12,
                TextAlignment = i >= 3 && columns.Split(',')[i] != "*" ? TextAlignment.Right : TextAlignment.Left,
            }).ToArray();
            var row = Row(columns, cells);
            row.Margin = new Thickness(12, 6, 12, 4);
            return row;
        }

        private static TextBlock Cell(string text, bool right = false, bool muted = false)
        {
            var t = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = right ? TextAlignment.Right : TextAlignment.Left };
            if (muted) t.Classes.Add("muted");
            return t;
        }

        private static Control Swatch(FsNode node) => new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(node.IsDirectory ? 2 : 6),
            Background = node.IsDirectory ? new SolidColorBrush(Color.Parse("#9AA3AD")) : Palette.Brush(FileTypes.CategoryOf(node)),
        };

        private static ProgressBar ShareBar(double fraction)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = fraction, Height = 6, MinHeight = 6, MinWidth = 40 };
            return bar;
        }

        private static string Culture(long bytes) => Sizes.Format(bytes, Languages.Culture);

        private static string Count(long n) => n.ToString("N0", Languages.Culture);

        private static string Date(DateTime utc) =>
            utc == DateTime.MinValue ? "" : utc.ToLocalTime().ToString("d", Languages.Culture);

        private static string DisplayName(FsNode node) =>
            node.Inaccessible ? F("item.inaccessible", node.Name) : node.Name;

        private Control FolderRow(FolderItem item)
        {
            var name = Cell(DisplayName(item.Node));
            var size = Cell(Culture(item.Node.Size), right: true, muted: true);
            var bar = ShareBar(item.Node.ShareOfParent);
            bar.Width = 46;
            var row = Row("*,Auto,46", name, size, bar);
            item.Changed += () =>
            {
                size.Text = Culture(item.Node.Size);
                bar.Value = item.Node.ShareOfParent;
            };
            return row;
        }

        /// <summary>Colonna di una dimensione: quella usata per l'ordinamento in evidenza, l'altra attenuata.</summary>
        private static TextBlock SizeCell(long bytes, bool active) => Cell(Culture(bytes), right: true, muted: !active);

        private Control ContentRow(FsNode node)
        {
            var row = Row(ContentColumns,
                Swatch(node),
                Cell(DisplayName(node)),
                ShareBar(node.ShareOfParent),
                Cell(Sizes.Percent(node.ShareOfParent, Languages.Culture), right: true, muted: true),
                SizeCell(node.Length, CurrentMeasure == SizeMeasure.Length),
                SizeCell(node.OnDisk, CurrentMeasure == SizeMeasure.OnDisk),
                Cell(node.IsDirectory ? Count(node.FileCount) : "", right: true, muted: true),
                Cell(Date(node.LastWriteUtc), right: true, muted: true));
            return row;
        }

        private Control ExtensionRow(ExtensionTotal e)
        {
            long total = _current?.Size ?? 0;
            double share = total > 0 ? (double)e.Size / total : 0;
            return Row("16,120,*,90,90,64",
                new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Palette.Brush(e.Category) },
                Cell(e.Extension.Length == 0 ? T("types.noExtension") : "." + e.Extension),
                Cell(T(Palette.Key(e.Category)), muted: true),
                Cell(Count(e.Count), right: true, muted: true),
                Cell(Culture(e.Size), right: true),
                Cell(Sizes.Percent(share, Languages.Culture), right: true, muted: true));
        }

        private Control LargestRow(FsNode node)
        {
            var name = new StackPanel { Spacing = 0 };
            name.Children.Add(Cell(node.Name));
            var where = node.Parent?.FullPath ?? "";
            var path = Cell(where, muted: true);
            path.FontSize = 11;
            name.Children.Add(path);
            return Row("16,*,90,120", Swatch(node), name, Cell(Culture(node.Size), right: true), Cell(Date(node.LastWriteUtc), right: true, muted: true));
        }

        // ---- Dischi e scansione -------------------------------------------------------------------------

        private sealed record DriveChoice(string Root, string Label, bool Ntfs)
        {
            public override string ToString() => Label;
        }

        private void LoadDrives()
        {
            var items = new List<DriveChoice>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name : $"{d.Name}  {d.VolumeLabel}";
                    string text = F("drive.label", label, Culture(d.AvailableFreeSpace), Culture(d.TotalSize));
                    items.Add(new DriveChoice(d.RootDirectory.FullName, text, OperatingSystem.IsWindows() && string.Equals(d.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)));
                }
                catch
                {
                    // disco non leggibile (lettore vuoto, rete scollegata): si salta
                }
            }
            _drives.ItemsSource = items;
            var system = Path.GetPathRoot(Environment.SystemDirectory);
            _drives.SelectedItem = items.FirstOrDefault(i => string.Equals(i.Root, system, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
            UpdateFastOption();
        }

        private void UpdateFastOption()
        {
            bool ntfs = _drives.SelectedItem is DriveChoice { Ntfs: true };
            _fast.IsVisible = OperatingSystem.IsWindows();
            _fast.IsEnabled = ntfs;
            if (!ntfs) _fast.IsChecked = false;
        }

        private void StartSelectedDrive()
        {
            if (_drives.SelectedItem is DriveChoice d) StartScan(d.Root, _fast.IsChecked == true);
        }

        private async Task PickFolder()
        {
            var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = T("scan.folderTitle"), AllowMultiple = false });
            var path = result.FirstOrDefault()?.TryGetLocalPath();
            if (path != null) StartScan(path, fast: false);
        }

        private async void StartScan(string path, bool fast)
        {
            _scanCancel?.Cancel();
            var cancel = new CancellationTokenSource();
            _scanCancel = cancel;
            var progress = new ScanProgress();
            bool useNtfs = fast && OperatingSystem.IsWindows() && NtfsScanner.IsNtfsDriveRoot(path);

            SetScanning(true);
            var watch = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (_, _) => ShowProgress(progress, useNtfs);
            timer.Start();

            FsNode? result = null;
            string? note = null;
            try
            {
                result = await Task.Run(() => Scan(path, useNtfs, progress, cancel.Token, out note), cancel.Token);
            }
            catch (OperationCanceledException)
            {
                _status.Text = T("status.canceled");
            }
            catch (UnauthorizedAccessException) when (useNtfs)
            {
                timer.Stop();
                SetScanning(false);
                await OfferElevation(path);
                return;
            }
            catch (Exception ex)
            {
                _status.Text = F("status.failed", ex.Message);
            }
            finally
            {
                timer.Stop();
                if (ReferenceEquals(_scanCancel, cancel)) SetScanning(false);
            }

            if (result == null || cancel.IsCancellationRequested) return;
            App.Settings.LastPath = path;
            if (CurrentMeasure != SizeMeasure.OnDisk) FsTree.UseMeasure(result, CurrentMeasure);
            ShowResult(result);
            _status.Text = Summary(result, watch.Elapsed, useNtfs && note == null) + (note != null ? "  " + note : "");
        }

        private static FsNode Scan(string path, bool ntfs, ScanProgress progress, CancellationToken cancel, out string? note)
        {
            note = null;
            if (ntfs && OperatingSystem.IsWindows())
            {
                try { return NtfsScanner.Scan(path, progress, cancel); }
                catch (NotSupportedException)
                {
                    // Tabella NTFS non leggibile (es. molto frammentata): si fa la scansione classica.
                    note = T("status.fastFallback");
                }
            }
            return FolderScanner.Scan(path, progress, cancel);
        }

        private async Task OfferElevation(string drive)
        {
            var ask = new ConfirmWindow(T("elevate.title"), T("elevate.body"), T("elevate.confirm"));
            if (await ask.ShowDialog<bool>(this))
            {
                if (Shell.RestartElevated(drive)) Close();
                else _status.Text = T("elevate.denied");
            }
            else
            {
                _status.Text = T("elevate.skipped");
            }
        }

        private void SetScanning(bool scanning)
        {
            _scan.IsEnabled = !scanning;
            _pickFolder.IsEnabled = !scanning;
            _drives.IsEnabled = !scanning;
            _fast.IsEnabled = !scanning && _drives.SelectedItem is DriveChoice { Ntfs: true };
            _stop.IsVisible = scanning;
            _progress.IsVisible = scanning;
            _progress.IsIndeterminate = true;
        }

        private void ShowProgress(ScanProgress p, bool ntfs)
        {
            if (ntfs && p.Fraction is { } f && p.Files == 0)
            {
                _progress.IsIndeterminate = false;
                _progress.Maximum = 1;
                _progress.Value = f;
                _status.Text = F("status.readingTable", Sizes.Percent(f, Languages.Culture));
                return;
            }
            _status.Text = F("status.scanning", Count(p.Files), Count(p.Folders), Culture(p.Bytes), p.CurrentPath ?? "");
        }

        private static string Summary(FsNode root, TimeSpan elapsed, bool ntfs)
        {
            var text = F(ntfs ? "status.doneFast" : "status.done", elapsed.TotalSeconds.ToString("0.0", Languages.Culture),
                Count(root.FileCount), Count(root.FolderCount), Culture(root.Size));
            try
            {
                var drive = new DriveInfo(root.Name);
                if (string.Equals(Path.GetFullPath(root.Name).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase) && drive.IsReady)
                {
                    long used = drive.TotalSize - drive.TotalFreeSpace;
                    text += "  " + F("status.driveUsed", Culture(used));
                }
            }
            catch
            {
                // non è un disco: niente confronto con lo spazio usato
            }
            return text;
        }

        // ---- Navigazione --------------------------------------------------------------------------------

        /// <summary>Misura scelta: spazio su disco (predefinita) o dimensione dei file.</summary>
        private static SizeMeasure CurrentMeasure =>
            Enum.TryParse<SizeMeasure>(App.Settings.Measure, out var m) ? m : SizeMeasure.OnDisk;

        /// <summary>Cambia la misura: si riordina tutto e si ridisegnano albero, elenco, mappa e schede.</summary>
        internal void ChangeMeasure(SizeMeasure measure)
        {
            if (measure == CurrentMeasure) return;
            App.Settings.Measure = measure.ToString();
            App.SettingsStore.Save(App.Settings);
            _measure.SelectedIndex = measure == SizeMeasure.Length ? 1 : 0;
            if (_root == null) return;
            var current = _current ?? _root;
            FsTree.UseMeasure(_root, measure);
            _rootItem = FolderItem.CreateRoot(_root);
            _tree.ItemsSource = new[] { _rootItem };
            Navigate(current);
        }

        /// <summary>Per i controlli: scheda da mostrare (0 contenuto, 1 tipi, 2 file più grandi).</summary>
        internal void ShowTab(int index) => _tabs.SelectedIndex = index;

        internal void ShowResult(FsNode root)
        {
            _root = root;
            _rootItem = FolderItem.CreateRoot(root);
            _tree.ItemsSource = new[] { _rootItem };
            _welcome.IsVisible = false;
            _workspace.IsVisible = true;
            Navigate(root);
            Dispatcher.UIThread.Post(() =>
            {
                if (_tree.ContainerFromItem(_rootItem) is TreeViewItem rootContainer) rootContainer.IsExpanded = true;
            }, DispatcherPriority.Loaded);
        }

        /// <summary>Mostra una cartella: contenuto, mappa, intestazione, e la seleziona nell'albero.</summary>
        private void Navigate(FsNode folder, bool fromTree = false)
        {
            if (!folder.IsDirectory) folder = folder.Parent ?? folder;
            _current = folder;
            _selected = null;
            _folderTitle.Text = folder.FullPath;
            UpdateFolderDetails();
            _up.IsEnabled = folder.Parent != null;
            _content.ItemsSource = folder.Children.ToList();
            _map.Folder = folder;
            _map.Selected = null;
            RefreshTabs();
            if (!fromTree) SyncTree(folder);
        }

        private void UpdateFolderDetails()
        {
            if (_current == null) return;
            _folderDetails.Text = F("folder.details", Culture(_current.OnDisk), Culture(_current.Length), Count(_current.FileCount), Count(_current.FolderCount));
        }

        /// <summary>Apre i rami dell'albero fino alla cartella e la seleziona.</summary>
        private void SyncTree(FsNode folder)
        {
            if (_rootItem?.Find(folder) is not { } target) return;
            _syncing = true;
            try
            {
                var chain = new List<FolderItem>();
                for (var i = target; i != null; i = i.Parent) chain.Add(i);
                chain.Reverse();
                ItemsControl parent = _tree;
                foreach (var item in chain)
                {
                    parent.UpdateLayout();
                    if (parent.ContainerFromItem(item) is not TreeViewItem container) break;
                    if (!ReferenceEquals(item, target)) container.IsExpanded = true;
                    parent = container;
                }
                _tree.SelectedItem = target;
                if (parent is TreeViewItem tvi) tvi.BringIntoView();
            }
            finally
            {
                _syncing = false;
            }
        }

        private void Select(FsNode? node, bool fromList)
        {
            _selected = node;
            _map.Selected = node;
            if (node == null || fromList) return;
            // Clic sulla mappa: se l'elemento è nel contenuto diretto lo si evidenzia nell'elenco.
            if (_tabs.SelectedIndex == 0 && node.Parent == _current)
            {
                _content.SelectedItem = node;
                _content.ScrollIntoView(node);
            }
        }

        /// <summary>Doppio clic: si entra nelle cartelle; un file si mostra nella sua cartella.</summary>
        private void Activate(FsNode? node)
        {
            if (node == null) return;
            if (node.IsDirectory) Navigate(node);
            else if (node.Parent != null && node.Parent != _current)
            {
                Navigate(node.Parent);
                Select(node, fromList: false);
            }
        }

        private async void OnListKeyDown(object? sender, KeyEventArgs e)
        {
            var node = (sender as ListBox)?.SelectedItem as FsNode;
            if (node == null) return;
            if (e.Key == Key.Enter) { Activate(node); e.Handled = true; }
            else if (e.Key == Key.Back && _current?.Parent != null) { Navigate(_current.Parent); e.Handled = true; }
            else if (e.Key == Key.Delete) { e.Handled = true; await MoveToTrash(node); }
        }

        // ---- Schede "Tipi di file" e "File più grandi" --------------------------------------------------

        private async void RefreshTabs()
        {
            _tabCancel?.Cancel();
            var folder = _current;
            if (folder == null) return;
            var cancel = new CancellationTokenSource();
            _tabCancel = cancel;
            try
            {
                switch (_tabs.SelectedIndex)
                {
                    case 1:
                    {
                        var totals = await Task.Run(() => FileTypes.ByExtension(folder, cancel.Token), cancel.Token);
                        if (cancel.IsCancellationRequested) return;
                        ShowTypes(folder, totals);
                        break;
                    }
                    case 2:
                    {
                        var largest = await Task.Run(() => FileTypes.Largest(folder, LargestCount, cancel.Token), cancel.Token);
                        if (cancel.IsCancellationRequested) return;
                        _largest.ItemsSource = largest;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // si è cambiata cartella o scheda nel frattempo
            }
        }

        private void ShowTypes(FsNode folder, IReadOnlyList<ExtensionTotal> totals)
        {
            _extensions.ItemsSource = totals;
            _categoryBar.Children.Clear();
            _categoryLegend.Children.Clear();
            double width = Math.Max(200, _tabs.Bounds.Width - 40);
            foreach (var c in FileTypes.ByCategory(totals))
            {
                double share = folder.Size > 0 ? (double)c.Size / folder.Size : 0;
                if (share > 0.002)
                    _categoryBar.Children.Add(new Border { Width = Math.Max(2, width * share), Background = Palette.Brush(c.Category) });
                var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 16, 4) };
                legend.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Palette.Brush(c.Category), VerticalAlignment = VerticalAlignment.Center });
                legend.Children.Add(new TextBlock { Text = $"{T(Palette.Key(c.Category))}  {Culture(c.Size)}", FontSize = 12 });
                _categoryLegend.Children.Add(legend);
            }
        }

        // ---- Cestino ------------------------------------------------------------------------------------

        private async Task MoveToTrash(FsNode node)
        {
            if (node.IsRoot) return;
            var detail = node.IsDirectory
                ? F("trash.detailFolder", Culture(node.Size), Count(node.FileCount))
                : Culture(node.Size);
            var ask = new ConfirmWindow(T("trash.title"), F("trash.body", node.FullPath), T("trash.confirm"), detail);
            if (!await ask.ShowDialog<bool>(this)) return;

            var path = node.FullPath;
            try
            {
                await Task.Run(() => Shell.MoveToTrash(path));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _status.Text = F("trash.failed", ex.Message);
                return;
            }

            var parent = node.Parent!;
            FsTree.Remove(node);
            var parentItem = _rootItem?.Find(parent);
            if (node.IsDirectory) parentItem?.RemoveChild(node);
            parentItem?.RaiseChangedUpwards();
            _status.Text = F("trash.done", node.Name, Culture(node.Size));

            if (_current != null && node.Contains(_current)) Navigate(parent);
            else
            {
                _content.ItemsSource = _current?.Children.ToList();
                UpdateFolderDetails();
                _map.Invalidate();
                RefreshTabs();
            }
        }

        private void Try(Action action)
        {
            try { action(); }
            catch (Exception ex) { _status.Text = F("status.actionFailed", ex.Message); }
        }
    }
}
