using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DockPDF.Engine;
using static DockPDF.Text;

namespace DockPDF.Views
{
    /// <summary>
    /// Organizza e Unisci: le pagine di uno o più PDF come miniature, da riordinare trascinandole, girare,
    /// eliminare e salvare in un PDF nuovo. A sinistra i file caricati.
    /// </summary>
    public sealed class PagesWorkspace : Workspace
    {
        public const double TileWidth = 150;

        private readonly List<OpenedPdf> _files = new();
        private readonly WrapPanel _pages = new() { Orientation = Orientation.Horizontal };
        private readonly StackPanel _fileList = new() { Spacing = 6 };
        private readonly TextBlock _summary = new() { Classes = { "muted" } };
        private readonly Button _save = new() { Classes = { "accent" } };
        private readonly Button _rotateLeft = new();
        private readonly Button _rotateRight = new();
        private readonly Button _delete = new();
        private readonly Button _selectAll = new();
        private readonly Control _emptyHint;
        private PageTile? _anchor;

        public PagesWorkspace(MainWindow main, PdfTool tool) : base(main, tool)
        {
            _save.Content = T("pages.save");
            _save.Click += async (_, _) => await SaveAsync();
            _rotateLeft.Content = "↺  " + T("pages.rotateLeft");
            _rotateRight.Content = "↻  " + T("pages.rotateRight");
            _delete.Content = T("pages.delete");
            _selectAll.Content = T("pages.selectAll");
            _rotateLeft.Click += (_, _) => RotateSelected(-90);
            _rotateRight.Click += (_, _) => RotateSelected(90);
            _delete.Click += (_, _) => DeleteSelected();
            _selectAll.Click += (_, _) => SelectAll();

            // A sinistra i file
            var add = Ui.Button(T("pages.addFiles"), async () => await PickFilesAsync());
            add.HorizontalAlignment = HorizontalAlignment.Stretch;
            add.HorizontalContentAlignment = HorizontalAlignment.Center;
            var side = Ui.Card(Ui.Column(10,
                Ui.Label(T("pages.files"), "heading"),
                new ScrollViewer { Content = _fileList, MaxHeight = 420 },
                add), new Thickness(14));
            side.Width = 250;
            side.VerticalAlignment = VerticalAlignment.Top;
            side.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(side, Dock.Left);

            // Al centro le pagine
            var commands = Ui.Row(8, _rotateLeft, _rotateRight, _delete, _selectAll);
            commands.Margin = new Thickness(0, 0, 0, 10);
            DockPanel.SetDock(commands, Dock.Top);
            var pick = Ui.Button(T("pages.addFiles"), async () => await PickFilesAsync(), accent: true);
            pick.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyHint = DropHint(T(tool == PdfTool.Merge ? "pages.emptyMerge" : "pages.emptyOrganize"), pick);
            var scroller = new ScrollViewer { Content = _pages, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var board = Ui.Card(new Panel { Children = { scroller, _emptyHint } }, new Thickness(10));
            // Fondo grigio chiaro: i fogli bianchi si staccano anche nel tema chiaro.
            board.Bind(BackgroundProperty, board.GetResourceObservable("DockBackground"));
            var center = new DockPanel();
            center.Children.Add(commands);
            center.Children.Add(board);

            var body = new DockPanel();
            body.Children.Add(side);
            body.Children.Add(center);
            Build(body, _summary, _save);

            Pick.AcceptDrops(this, Pick.IsPdf, files => _ = AddFilesAsync(files));
            KeyDown += OnKeyDown;
            UpdateState();
        }

        /// <summary>Le pagine nell'ordine attuale.</summary>
        internal IEnumerable<PageTile> Tiles => _pages.Children.OfType<PageTile>();

        private IEnumerable<PageTile> Selected => Tiles.Where(t => t.IsSelected);

        // ---- File ---------------------------------------------------------------------------------------

        private async Task PickFilesAsync()
        {
            var files = await Pick.Files(this, T("pages.addTitle"), multiple: true, Pick.PdfFiles);
            if (files.Count > 0) await AddFilesAsync(files);
        }

        public override async Task AddFilesAsync(IReadOnlyList<string> files)
        {
            foreach (var path in files.Where(Pick.IsPdf))
            {
                var opened = await OpenPdf(path);
                if (opened == null) continue;
                _files.Add(opened);
                var tiles = new List<PageTile>();
                for (int i = 0; i < opened.PageCount; i++)
                {
                    var tile = new PageTile(this, opened, i);
                    tiles.Add(tile);
                    _pages.Children.Add(tile);
                }
                AddFileRow(opened);
                _ = RenderThumbnailsAsync(opened, tiles);
            }
            UpdateState();
        }

        private void AddFileRow(OpenedPdf file)
        {
            var remove = new Button { Content = "✕", Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(remove, T("pages.removeFile"));
            var info = Ui.Column(0,
                new TextBlock { Text = file.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = F("pages.fileDetails", file.PageCount), Classes = { "muted" }, FontSize = 12 });
            var row = new DockPanel();
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(info);
            ToolTip.SetTip(info, file.Path);
            remove.Click += (_, _) =>
            {
                foreach (var tile in Tiles.Where(t => t.Source == file).ToList()) _pages.Children.Remove(tile);
                _fileList.Children.Remove(row);
                _files.Remove(file);
                file.Dispose();
                UpdateState();
            };
            _fileList.Children.Add(row);
        }

        /// <summary>Disegna le miniature una alla volta, fuori dall'interfaccia (PDFium lavora su un thread alla volta).</summary>
        private static async Task RenderThumbnailsAsync(OpenedPdf file, IReadOnlyList<PageTile> tiles)
        {
            foreach (var tile in tiles)
            {
                try
                {
                    var bitmap = await Task.Run(() =>
                    {
                        using var skia = file.File.Render(tile.Index, (int)(TileWidth * 1.5));
                        return Thumbs.ToAvalonia(skia);
                    });
                    tile.SetThumbnail(bitmap);
                }
                catch (ObjectDisposedException)
                {
                    return; // file tolto nel frattempo
                }
                catch (Exception)
                {
                    tile.SetBroken();
                }
            }
        }

        // ---- Selezione e comandi ------------------------------------------------------------------------

        /// <summary>Clic su una pagina: sola, con Ctrl in aggiunta, con Maiusc l'intervallo dall'ultima.</summary>
        internal void Click(PageTile tile, KeyModifiers modifiers)
        {
            var list = Tiles.ToList();
            if (modifiers.HasFlag(KeyModifiers.Shift) && _anchor != null && list.Contains(_anchor))
            {
                int a = list.IndexOf(_anchor), b = list.IndexOf(tile);
                if (!modifiers.HasFlag(KeyModifiers.Control)) foreach (var t in list) t.IsSelected = false;
                for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) list[i].IsSelected = true;
            }
            else if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta))
            {
                tile.IsSelected = !tile.IsSelected;
                _anchor = tile;
            }
            else
            {
                foreach (var t in list) t.IsSelected = t == tile;
                _anchor = tile;
            }
            Focus();
            UpdateState();
        }

        internal void Rotate(PageTile tile, int degrees)
        {
            tile.Rotation += degrees;
            UpdateState();
        }

        internal void Delete(PageTile tile)
        {
            _pages.Children.Remove(tile);
            UpdateState();
        }

        private void RotateSelected(int degrees)
        {
            foreach (var t in Selected) t.Rotation += degrees;
            UpdateState();
        }

        private void DeleteSelected()
        {
            foreach (var t in Selected.ToList()) _pages.Children.Remove(t);
            UpdateState();
        }

        private void SelectAll()
        {
            bool all = Tiles.All(t => t.IsSelected);
            foreach (var t in Tiles) t.IsSelected = !all;
            UpdateState();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                foreach (var t in Tiles) t.IsSelected = true;
                UpdateState();
                e.Handled = true;
            }
        }

        // ---- Trascinamento per riordinare ---------------------------------------------------------------

        /// <summary>Mentre si trascina una pagina: si sposta subito nel punto in cui si trova il mouse.</summary>
        internal void DragOver(PageTile dragged, PointerEventArgs e)
        {
            var at = e.GetPosition(_pages);
            var target = Tiles.FirstOrDefault(t => t != dragged && t.Bounds.Contains(at));
            if (target == null) return;
            int from = _pages.Children.IndexOf(dragged), to = _pages.Children.IndexOf(target);
            // A metà della pagina di destinazione: prima o dopo, a seconda del lato.
            bool after = at.X > target.Bounds.Center.X;
            if (from < to && !after) to--;
            else if (from > to && after) to++;
            if (to == from) return;
            _pages.Children.RemoveAt(from);
            _pages.Children.Insert(Math.Clamp(to, 0, _pages.Children.Count), dragged);
            UpdateState();
        }

        /// <summary>Sposta una pagina in una posizione (per i controlli automatici e la tastiera).</summary>
        internal void Move(PageTile tile, int index)
        {
            _pages.Children.Remove(tile);
            _pages.Children.Insert(Math.Clamp(index, 0, _pages.Children.Count), tile);
            UpdateState();
        }

        // ---- Stato e salvataggio ------------------------------------------------------------------------

        private void UpdateState()
        {
            var tiles = Tiles.ToList();
            bool multi = _files.Count > 1;
            for (int i = 0; i < tiles.Count; i++) tiles[i].SetPosition(i + 1, multi);
            int selected = tiles.Count(t => t.IsSelected);
            _rotateLeft.IsEnabled = _rotateRight.IsEnabled = _delete.IsEnabled = selected > 0;
            _selectAll.IsEnabled = tiles.Count > 0;
            _selectAll.Content = tiles.Count > 0 && selected == tiles.Count ? T("pages.selectNone") : T("pages.selectAll");
            _save.IsEnabled = tiles.Count > 0;
            _emptyHint.IsVisible = _files.Count == 0;
            _summary.Text = tiles.Count == 0 ? ""
                : selected > 0 ? F("pages.summarySelected", tiles.Count, _files.Count, selected)
                : F("pages.summary", tiles.Count, _files.Count);
        }

        private async Task SaveAsync()
        {
            var pages = Tiles.Select(t => new PageRef(t.Source.Path, t.Index, t.Rotation)).ToList();
            if (pages.Count == 0) return;
            var first = _files.FirstOrDefault();
            string suggested = Tool == PdfTool.Merge || _files.Count > 1
                ? T("pages.mergedName")
                : F("pages.organizedName", Path.GetFileNameWithoutExtension(first?.Name ?? "documento"));
            var output = await Pick.SaveFile(this, T("pages.saveTitle"), suggested, Pick.PdfFiles, first == null ? null : Path.GetDirectoryName(first.Path));
            if (output == null) return;
            var passwords = _files.ToDictionary(f => Path.GetFullPath(f.Path), f => f.Password, StringComparer.OrdinalIgnoreCase);
            bool ok = await RunAsync(T("pages.saving"), () => Task.Run(() =>
            {
                using var sources = new PdfSources(p => passwords.TryGetValue(p, out var pwd) ? pwd : null);
                sources.Compose(pages, output);
            }), _save);
            if (ok) ShowResult(F("pages.saved", Path.GetFileName(output), pages.Count), output);
        }

        public override void Dispose()
        {
            foreach (var f in _files) f.Dispose();
            _files.Clear();
        }
    }

    /// <summary>
    /// Una pagina nella griglia: miniatura (girata come verrà salvata), numero, file d'origine e, con il mouse
    /// sopra, i pulsanti per girarla o eliminarla. Si trascina per spostarla.
    /// </summary>
    public sealed class PageTile : Border
    {
        private readonly PagesWorkspace _owner;
        private readonly Image _image = new() { Stretch = Stretch.Uniform };
        private readonly LayoutTransformControl _turn;
        private readonly TextBlock _number = new() { FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly TextBlock _origin = new() { Classes = { "muted" }, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly StackPanel _actions;
        private readonly Border _sheet;
        private int _rotation;
        private bool _selected;
        private Point? _pressedAt;
        private bool _dragging;

        public PageTile(PagesWorkspace owner, OpenedPdf source, int index)
        {
            _owner = owner;
            Source = source;
            Index = index;
            Width = PagesWorkspace.TileWidth + 20;
            Padding = new Thickness(8);
            Margin = new Thickness(4);
            CornerRadius = new CornerRadius(8);
            BorderThickness = new Thickness(2);
            BorderBrush = Brushes.Transparent;
            Background = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.Hand);

            // Foglio bianco grande quanto la pagina (in proporzione) anche prima che arrivi la miniatura.
            var (w, h) = source.File.PageSize(index);
            double ratio = h / Math.Max(1, w);
            _sheet = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Width = PagesWorkspace.TileWidth,
                Height = Math.Min(PagesWorkspace.TileWidth * 1.6, PagesWorkspace.TileWidth * ratio),
                Child = _image,
            };
            _turn = new LayoutTransformControl { Child = _sheet, HorizontalAlignment = HorizontalAlignment.Center };
            var frame = new Border { Height = PagesWorkspace.TileWidth * 1.45, Child = _turn };
            _turn.VerticalAlignment = VerticalAlignment.Center;

            Button Small(string text, string tip, Action click)
            {
                var b = new Button { Content = text, Padding = new Thickness(7, 2), FontSize = 13 };
                ToolTip.SetTip(b, tip);
                b.Click += (_, e) => { click(); e.Handled = true; };
                return b;
            }
            _actions = Ui.Row(4,
                Small("↺", T("pages.rotateLeft"), () => _owner.Rotate(this, -90)),
                Small("↻", T("pages.rotateRight"), () => _owner.Rotate(this, 90)),
                Small("✕", T("pages.delete"), () => _owner.Delete(this)));
            _actions.HorizontalAlignment = HorizontalAlignment.Right;
            _actions.VerticalAlignment = VerticalAlignment.Top;
            _actions.IsVisible = false;

            Child = Ui.Column(4, new Panel { Children = { frame, _actions } }, _number, _origin);
            PointerEntered += (_, _) => _actions.IsVisible = true;
            PointerExited += (_, _) => _actions.IsVisible = false;
        }

        public OpenedPdf Source { get; }

        /// <summary>Posizione della pagina nel file d'origine (da 0).</summary>
        public int Index { get; }

        /// <summary>Rotazione in più rispetto all'originale, in gradi (0, 90, 180, 270).</summary>
        public int Rotation
        {
            get => _rotation;
            set
            {
                _rotation = ((value % 360) + 360) % 360;
                _turn.LayoutTransform = new RotateTransform(_rotation);
            }
        }

        public bool IsSelected
        {
            get => _selected;
            set
            {
                _selected = value;
                BorderBrush = value ? new SolidColorBrush(AppInfo.Accent) : Brushes.Transparent;
                Background = value ? new SolidColorBrush(Color.FromArgb(22, AppInfo.Accent.R, AppInfo.Accent.G, AppInfo.Accent.B)) : Brushes.Transparent;
            }
        }

        public bool HasThumbnail => _image.Source != null;

        public void SetThumbnail(Avalonia.Media.Imaging.Bitmap bitmap) => _image.Source = bitmap;

        public void SetBroken() => _sheet.Child = new TextBlock { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };

        /// <summary>Numero nel PDF finale e, con più file, da quale file e pagina viene.</summary>
        public void SetPosition(int number, bool showOrigin)
        {
            _number.Text = number.ToString();
            _origin.IsVisible = showOrigin || Index + 1 != number;
            _origin.Text = F("pages.origin", Path.GetFileNameWithoutExtension(Source.Name), Index + 1);
            ToolTip.SetTip(this, F("pages.originTip", Source.Name, Index + 1));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _pressedAt = e.GetPosition(this);
            _dragging = false;
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_pressedAt is not { } start) return;
            var now = e.GetPosition(this);
            if (!_dragging && Math.Abs(now.X - start.X) + Math.Abs(now.Y - start.Y) < 8) return;
            if (!_dragging)
            {
                _dragging = true;
                Opacity = 0.55;
            }
            _owner.DragOver(this, e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_pressedAt == null) return;
            _pressedAt = null;
            e.Pointer.Capture(null);
            Opacity = 1;
            if (!_dragging) _owner.Click(this, e.KeyModifiers);
            _dragging = false;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _pressedAt = null;
            _dragging = false;
            Opacity = 1;
        }
    }
}
