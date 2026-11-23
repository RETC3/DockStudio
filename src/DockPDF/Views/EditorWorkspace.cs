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
using DockPDF.Engine;
using SkiaSharp;
using static DockPDF.Text;

namespace DockPDF.Views
{
    /// <summary>Stile corrente per le modifiche nuove (e per quella selezionata).</summary>
    public sealed record EditStyle
    {
        public Argb Color { get; init; } = Argb.FromRgb(0x1D, 0x1F, 0x24);
        public double FontSize { get; init; } = 12;
        public FontFamilyKind Family { get; init; } = FontFamilyKind.Sans;
        public bool Bold { get; init; }
        public bool Italic { get; init; }
        public double StrokeWidth { get; init; } = 2;
        public Argb Highlight { get; init; } = new(0x80FFE14D);
    }

    /// <summary>
    /// Modifica e firma: le pagine del PDF una sotto l'altra, con gli strumenti per scrivere, correggere il testo,
    /// evidenziare, coprire, disegnare, aggiungere immagini e firme, compilare i moduli e firmare digitalmente.
    /// Le modifiche restano separate (spostabili, annullabili) finché non si salva.
    /// </summary>
    public sealed class EditorWorkspace : Workspace
    {
        private static readonly Argb[] Swatches =
        {
            Argb.FromRgb(0x1D, 0x1F, 0x24), Argb.FromRgb(0x1B, 0x2A, 0x6B), Argb.FromRgb(0x1E, 0x6F, 0xE0),
            Argb.FromRgb(0xD3, 0x2F, 0x2F), Argb.FromRgb(0x2E, 0x7D, 0x32), Argb.FromRgb(0xF5, 0x9E, 0x0B),
        };

        private static readonly double[] FontSizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64 };
        private static readonly double[] StrokeWidths = { 0.75, 1, 1.5, 2, 3, 4, 6, 8 };

        private readonly List<EditItem> _items = new();
        private readonly Stack<Snapshot> _undo = new();
        private readonly Dictionary<int, IReadOnlyList<PdfTextBlock>> _blocks = new();
        private readonly Dictionary<string, string> _fieldValues = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _fieldOriginal = new(StringComparer.Ordinal);
        private IReadOnlyList<FormField> _fields = Array.Empty<FormField>();

        private readonly StackPanel _pagesPanel = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
        private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly Dictionary<EditTool, ToggleButton> _toolButtons = new();
        private readonly StackPanel _styleBar = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        private readonly ComboBox _size = new() { Width = 86 };
        private readonly ComboBox _family = new() { Width = 170 };
        private readonly ToggleButton _bold = new() { Content = "G", FontWeight = FontWeight.Bold, Padding = new Thickness(10, 5) };
        private readonly ToggleButton _italic = new() { Content = "C", FontStyle = FontStyle.Italic, Padding = new Thickness(10, 5) };
        private readonly TextBlock _zoomLabel = new() { VerticalAlignment = VerticalAlignment.Center, Width = 48, TextAlignment = TextAlignment.Center };
        private readonly Button _undoButton = new();
        private readonly Button _save = new() { Classes = { "accent" }, IsEnabled = false };
        private readonly TextBlock _summary = new() { Classes = { "muted" } };
        private readonly Border _notice = new() { IsVisible = false, Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 8) };
        private readonly TextBlock _noticeText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _noticeAction = new() { IsVisible = false };
        private readonly Control _emptyHint;
        private readonly Panel _board;
        private Button _signatureButton = null!;

        private OpenedPdf? _file;
        private bool _syncingStyle;
        private TextBox? _inline;
        private int _inlineIndex = -1;

        // In attesa di essere messi sulla pagina con un clic
        private byte[]? _pendingImage;
        private HandSignature? _pendingSignature;
        private DigitalSignRequest? _digital;

        private sealed record Snapshot(List<EditItem> Items, Dictionary<string, string> Fields, DigitalSignRequest? Digital);

        public EditorWorkspace(MainWindow main, PdfTool tool) : base(main, tool)
        {
            var tools = BuildToolBar();
            var style = BuildStyleBar();
            var bars = Ui.Column(8, tools, style);
            bars.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(bars, Dock.Top);

            _notice.Child = new DockPanel { Children = { _noticeAction, _noticeText } };
            DockPanel.SetDock(_noticeAction, Dock.Right);
            _noticeAction.Margin = new Thickness(12, 0, 0, 0);
            _notice.Background = new SolidColorBrush(Color.FromArgb(36, 27, 42, 107));
            DockPanel.SetDock(_notice, Dock.Top);

            var open = Ui.Button(T("editor.open"), async () => await PickAsync(), accent: true);
            open.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyHint = DropHint(T(tool == PdfTool.Sign ? "editor.emptySign" : "editor.empty"), open);
            _scroller.Content = _pagesPanel;
            _board = new Panel { Children = { _scroller, _emptyHint } };
            var board = Ui.Card(_board, new Thickness(0));
            board.Bind(BackgroundProperty, board.GetResourceObservable("DockBackground"));
            board.ClipToBounds = true;

            var body = new DockPanel();
            body.Children.Add(bars);
            body.Children.Add(_notice);
            body.Children.Add(board);

            _save.Content = T("editor.save");
            _save.Click += async (_, _) => await SaveAsync();
            Build(body, _summary, _save);

            Pick.AcceptDrops(this, p => Pick.IsPdf(p) || (_file != null && Pick.IsImage(p)), files => _ = DroppedAsync(files));
            KeyDown += OnKeyDown;
            _scroller.AddHandler(PointerWheelChangedEvent, (_, e) =>
            {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
                SetZoom(Zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15));
                e.Handled = true;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            SetTool(tool == PdfTool.Sign ? EditTool.Signature : EditTool.Select, ask: false);
            UpdateState();
        }

        // ---- Stato condiviso con le pagine --------------------------------------------------------------

        public PdfFile Document => _file?.File ?? throw new InvalidOperationException("Nessun PDF aperto");
        public IReadOnlyList<EditItem> Items => _items;
        public EditTool ActiveTool { get; private set; } = EditTool.Select;
        public EditStyle Style { get; private set; } = new();
        public int? SelectedIndex { get; private set; }
        public int EditingIndex => _inlineIndex;
        public double Zoom { get; private set; } = 1.25;
        public PageRect? DigitalBox => _digital?.Box;
        public int DigitalPage => _digital?.Page ?? -1;

        internal IEnumerable<EditorPage> Pages => _pagesPanel.Children.OfType<EditorPage>();
        internal IReadOnlyList<FormField> Fields => _fields;
        internal IReadOnlyDictionary<string, string> FieldValues => _fieldValues;
        internal DigitalSignRequest? Digital => _digital;

        public IReadOnlyList<PdfTextBlock> BlocksOf(int page)
        {
            if (_file == null) return Array.Empty<PdfTextBlock>();
            if (!_blocks.TryGetValue(page, out var blocks))
            {
                try { blocks = _file.File.TextBlocks(page); }
                catch (Exception) { blocks = Array.Empty<PdfTextBlock>(); }
                // i blocchi già sostituiti non si propongono di nuovo
                _blocks[page] = blocks;
            }
            var replaced = _items.OfType<TextReplacement>().Where(r => r.Page == page).Select(r => r.Block).ToHashSet();
            return blocks.Where(b => !replaced.Contains(b)).ToList();
        }

        // ---- Barre degli strumenti ----------------------------------------------------------------------

        private Control BuildToolBar()
        {
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            void Add(EditTool tool, string glyph, string key)
            {
                var button = new ToggleButton
                {
                    Content = new ContentIcon(glyph, T(key)),
                    Padding = new Thickness(10, 6),
                    Margin = new Thickness(0, 0, 6, 6),
                };
                ToolTip.SetTip(button, T(key + "Tip"));
                button.Click += (_, _) => SetTool(tool);
                _toolButtons[tool] = button;
                row.Children.Add(button);
            }
            Add(EditTool.Select, "M5,3 L5,19 L9.5,14.5 L12.5,21 L15,20 L12,13.5 L18,13.5 Z", "editor.tool.select");
            Add(EditTool.EditText, "M4,5 H14 M9,5 V19 M15,13 L19,9 L21,11 L17,15 L15,15 Z", "editor.tool.editText");
            Add(EditTool.Text, "M5,5 H19 M12,5 V19 M9,19 H15", "editor.tool.text");
            Add(EditTool.Highlight, "M4,20 H20 M7,16 L14,5 L18,8 L11,19 Z", "editor.tool.highlight");
            Add(EditTool.Whiteout, "M4,8 H20 V17 H4 Z M8,17 V20 M16,17 V20", "editor.tool.whiteout");
            Add(EditTool.Rectangle, "M4,6 H20 V18 H4 Z", "editor.tool.rectangle");
            Add(EditTool.Ellipse, "M12,5 A8,7 0 1 1 11.99,5 Z", "editor.tool.ellipse");
            Add(EditTool.Line, "M5,19 L19,5", "editor.tool.line");
            Add(EditTool.Arrow, "M5,19 L19,5 M11,5 H19 V13", "editor.tool.arrow");
            Add(EditTool.Pen, "M4,17 C7,9 10,9 11,13 C12,17 15,16 20,7", "editor.tool.pen");
            Add(EditTool.Image, "M3,5 H21 V19 H3 Z M3,16 L8,11 L12,15 L15,12 L21,18", "editor.tool.image");

            // Firma a mano: un menu con le firme salvate
            _signatureButton = new Button { Content = new ContentIcon("M3,17 C6,8 8,8 8,13 C8,17 11,9 13,12 C14,14 16,13 18,11 M3,21 H21", T("editor.tool.signature") + " ▾"), Padding = new Thickness(10, 6), Margin = new Thickness(0, 0, 6, 6) };
            ToolTip.SetTip(_signatureButton, T("editor.tool.signatureTip"));
            _signatureButton.Click += (_, _) => ShowSignatureMenu();
            row.Children.Add(_signatureButton);

            var digital = new Button { Content = new ContentIcon("M12,3 L19,6 V11 C19,16 16,19 12,21 C8,19 5,16 5,11 V6 Z M9,12 L11,14 L15,10", T("editor.tool.digital")), Padding = new Thickness(10, 6), Margin = new Thickness(0, 0, 6, 6) };
            ToolTip.SetTip(digital, T("editor.tool.digitalTip"));
            digital.Click += async (_, _) => await AskDigitalSignatureAsync();
            row.Children.Add(digital);

            _undoButton.Content = "↶  " + T("editor.undo");
            _undoButton.Margin = new Thickness(12, 0, 6, 6);
            _undoButton.Click += (_, _) => Undo();
            ToolTip.SetTip(_undoButton, T("editor.undoTip"));
            row.Children.Add(_undoButton);
            return row;
        }

        private Control BuildStyleBar()
        {
            foreach (var color in Swatches)
            {
                var swatch = new Button
                {
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(12),
                    Background = EditDrawing.Brush(color),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Gray,
                };
                swatch.Click += (_, _) => ChangeStyle(Style with { Color = color });
                _styleBar.Children.Add(swatch);
            }
            _family.ItemsSource = new[] { T("font.sans"), T("font.serif"), T("font.mono"), T("font.script") };
            _family.SelectedIndex = 0;
            _family.SelectionChanged += (_, _) => { if (!_syncingStyle) ChangeStyle(Style with { Family = (FontFamilyKind)Math.Max(0, _family.SelectedIndex) }); };
            _size.SelectionChanged += (_, _) =>
            {
                if (_syncingStyle || _size.SelectedItem is not string s) return;
                double v = double.Parse(s.Replace(" pt", ""), System.Globalization.CultureInfo.InvariantCulture);
                ChangeStyle(IsTextTool ? Style with { FontSize = v } : Style with { StrokeWidth = v });
            };
            _bold.Click += (_, _) => ChangeStyle(Style with { Bold = _bold.IsChecked == true });
            _italic.Click += (_, _) => ChangeStyle(Style with { Italic = _italic.IsChecked == true });
            ToolTip.SetTip(_bold, T("editor.bold"));
            ToolTip.SetTip(_italic, T("editor.italic"));

            var zoomOut = Ui.Button("−", () => SetZoom(Zoom / 1.2));
            var zoomIn = Ui.Button("+", () => SetZoom(Zoom * 1.2));
            var fit = Ui.Button(T("editor.fitWidth"), FitWidth);
            ToolTip.SetTip(zoomOut, T("editor.zoomOut"));
            ToolTip.SetTip(zoomIn, T("editor.zoomIn"));

            var bar = new DockPanel();
            var zoom = Ui.Row(4, zoomOut, _zoomLabel, zoomIn, fit);
            DockPanel.SetDock(zoom, Dock.Right);
            bar.Children.Add(zoom);
            _styleBar.Children.Add(new Border { Width = 8 });
            _styleBar.Children.Add(_size);
            _styleBar.Children.Add(_family);
            _styleBar.Children.Add(_bold);
            _styleBar.Children.Add(_italic);
            foreach (var c in _styleBar.Children) c.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(_styleBar);
            SyncStyleBar();
            return bar;
        }

        private bool IsTextTool => ActiveTool is EditTool.Text or EditTool.EditText
                                   || (SelectedIndex is int i && i < _items.Count && _items[i] is TextItem or TextReplacement);

        /// <summary>Mostra nella barra lo stile dello strumento (o della modifica selezionata).</summary>
        private void SyncStyleBar()
        {
            _syncingStyle = true;
            bool text = IsTextTool;
            var sizes = (text ? FontSizes : StrokeWidths).Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture) + " pt").ToList();
            _size.ItemsSource = sizes;
            double current = text ? Style.FontSize : Style.StrokeWidth;
            _size.SelectedItem = sizes.FirstOrDefault(s => s == current.ToString(System.Globalization.CultureInfo.InvariantCulture) + " pt");
            ToolTip.SetTip(_size, T(text ? "editor.fontSize" : "editor.strokeWidth"));
            _family.SelectedIndex = (int)Style.Family;
            _family.IsVisible = _bold.IsVisible = _italic.IsVisible = text;
            _bold.IsChecked = Style.Bold;
            _italic.IsChecked = Style.Italic;
            _zoomLabel.Text = $"{Zoom * 100:0}%";
            _syncingStyle = false;
        }

        /// <summary>Nuovo stile: vale per le prossime modifiche e, se ce n'è una selezionata, anche per quella.</summary>
        private void ChangeStyle(EditStyle style)
        {
            Style = style;
            if (SelectedIndex is int i && i < _items.Count)
            {
                var item = _items[i];
                EditItem changed = item switch
                {
                    TextItem t => t with { Color = style.Color, Size = style.FontSize, Family = style.Family, Bold = style.Bold, Italic = style.Italic },
                    TextReplacement r => r with { Block = r.Block with { Color = style.Color, Size = style.FontSize, Family = style.Family, Bold = style.Bold, Italic = style.Italic } },
                    ShapeItem s when s.Multiply || s.Stroke.A == 0 => s,
                    ShapeItem s => s with { Stroke = style.Color, StrokeWidth = style.StrokeWidth },
                    LineItem l => l with { Color = style.Color, Width = style.StrokeWidth },
                    InkItem ink => ink with { Color = style.Color, Width = style.StrokeWidth },
                    SignatureItem sig => sig with { Signature = sig.Signature with { Color = style.Color } },
                    _ => item,
                };
                if (!Equals(changed, item))
                {
                    PushUndo();
                    _items[i] = changed;
                    if (_inline != null && _inlineIndex == i) StyleInline(_inline, changed);
                    InvalidatePage(changed.Page);
                }
            }
            SyncStyleBar();
            UpdateState();
        }

        // ---- Strumenti ----------------------------------------------------------------------------------

        public void SetTool(EditTool tool, bool ask = true)
        {
            CommitInlineEditor();
            if (tool != EditTool.Image) _pendingImage = null;
            if (tool != EditTool.Signature) _pendingSignature = null;
            ActiveTool = tool;
            foreach (var (t, b) in _toolButtons) b.IsChecked = t == tool;
            if (tool != EditTool.Select) Select(null);
            SyncStyleBar();
            foreach (var page in Pages) page.InvalidateItems();
            Main.SetStatus(T("editor.hint." + tool.ToString().ToLowerInvariant()));
            if (ask && tool == EditTool.Image) _ = PickImageAsync();
        }

        private async Task PickImageAsync()
        {
            var files = await Pick.Files(this, T("editor.imageTitle"), false, Pick.ImageFiles);
            if (files.Count == 0)
            {
                SetTool(EditTool.Select);
                return;
            }
            _pendingImage = ImageBytes(files[0]);
            Main.SetStatus(T("editor.hint.image"));
        }

        /// <summary>I byte da mettere nel PDF: JPEG e PNG così come sono, gli altri formati convertiti in PNG.</summary>
        internal static byte[] ImageBytes(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png") return bytes;
            using var bitmap = SKBitmap.Decode(bytes) ?? throw new InvalidDataException(Path.GetFileName(path));
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        /// <summary>Clic sulla pagina con un'immagine, una firma o la firma digitale da posizionare.</summary>
        internal void PlaceAt(int page, PageRect? drawn, PagePoint at)
        {
            if (ActiveTool == EditTool.Image && _pendingImage != null)
            {
                var size = EditDrawing.Image(_pendingImage)?.Size ?? new Size(400, 300);
                double ratio = size.Width / Math.Max(1, size.Height);
                var box = drawn ?? Centered(at, 200, 200 / ratio, page);
                Add(new ImageItem(page, box, _pendingImage));
                _pendingImage = null;
                SetTool(EditTool.Select);
            }
            else if (ActiveTool == EditTool.Signature && _pendingSignature != null)
            {
                double ratio = _pendingSignature.Strokes != null ? _pendingSignature.AspectRatio
                    : _pendingSignature.Image != null && EditDrawing.Image(_pendingSignature.Image) is { } img ? img.Size.Width / Math.Max(1, img.Size.Height)
                    : 3.2;
                var box = drawn ?? Centered(at, 60 * ratio, 60, page);
                Add(new SignatureItem(page, box, _pendingSignature));
                SetTool(EditTool.Select);
            }
            else if (ActiveTool == EditTool.DigitalSignature && _digital != null)
            {
                PushUndo();
                _digital = _digital with { Page = page, Box = drawn ?? Centered(at, 220, 70, page) };
                SetTool(EditTool.Select);
                UpdateState();
                InvalidatePage(page);
            }
            else if (ActiveTool == EditTool.Signature)
            {
                ShowSignatureMenu();
            }
        }

        private PageRect Centered(PagePoint at, double w, double h, int page)
        {
            var g = Pages.ElementAt(page).Geometry;
            double x = Math.Clamp(at.X - w / 2, 0, Math.Max(0, g.ViewWidth - w));
            double y = Math.Clamp(at.Y - h / 2, 0, Math.Max(0, g.ViewHeight - h));
            return new PageRect(x, y, w, h);
        }

        // ---- Firme --------------------------------------------------------------------------------------

        private void ShowSignatureMenu()
        {
            var flyout = new MenuFlyout();
            var saved = SignatureStore.Load();
            foreach (var signature in saved)
            {
                var item = new MenuItem { Header = new SignaturePreview(signature) { Width = 160, Height = 46 } };
                ToolTip.SetTip(item, T("signature.use"));
                item.Click += (_, _) => UseSignature(signature);
                flyout.Items.Add(item);
            }
            if (saved.Count > 0) flyout.Items.Add(new Separator());
            var create = new MenuItem { Header = T("signature.new") };
            create.Click += async (_, _) => await NewSignatureAsync();
            flyout.Items.Add(create);
            if (saved.Count > 0)
            {
                var forget = new MenuItem { Header = T("signature.forget") };
                foreach (var signature in saved)
                {
                    var item = new MenuItem { Header = new SignaturePreview(signature) { Width = 120, Height = 34 } };
                    item.Click += (_, _) => SignatureStore.Remove(signature);
                    forget.Items.Add(item);
                }
                flyout.Items.Add(forget);
            }
            flyout.ShowAt(_signatureButton);
        }

        private async Task NewSignatureAsync()
        {
            var window = new SignatureWindow();
            var signature = await window.ShowDialog<HandSignature?>(Main);
            if (signature == null) return;
            if (window.Remember) SignatureStore.Add(signature);
            UseSignature(signature);
        }

        internal void UseSignature(HandSignature signature)
        {
            SetTool(EditTool.Signature, ask: false);
            _pendingSignature = signature;
            Main.SetStatus(T("editor.hint.signaturePlace"));
        }

        private async Task AskDigitalSignatureAsync()
        {
            if (_file == null) { await PickAsync(); if (_file == null) return; }
            var window = new DigitalSignWindow();
            var request = await window.ShowDialog<DigitalSignRequest?>(Main);
            if (request == null) return;
            PushUndo();
            _digital = request with { Box = null };
            UpdateState();
            if (window.Visible)
            {
                SetTool(EditTool.DigitalSignature, ask: false);
                Main.SetStatus(T("editor.hint.digitalsignature"));
            }
        }

        // ---- Modifiche ----------------------------------------------------------------------------------

        public int Add(EditItem item, bool select = true)
        {
            PushUndo();
            _items.Add(item);
            if (select) SelectedIndex = _items.Count - 1;
            InvalidatePage(item.Page);
            UpdateState();
            return _items.Count - 1;
        }

        public void Select(int? index)
        {
            SelectedIndex = index;
            if (index is int i && i < _items.Count)
            {
                Style = _items[i] switch
                {
                    TextItem t => Style with { Color = t.Color, FontSize = t.Size, Family = t.Family, Bold = t.Bold, Italic = t.Italic },
                    TextReplacement r => Style with { Color = r.Block.Color, FontSize = r.Block.Size, Family = r.Block.Family, Bold = r.Block.Bold, Italic = r.Block.Italic },
                    ShapeItem { Multiply: false, Stroke.A: > 0 } s => Style with { Color = s.Stroke, StrokeWidth = s.StrokeWidth },
                    LineItem l => Style with { Color = l.Color, StrokeWidth = l.Width },
                    InkItem ink => Style with { Color = ink.Color, StrokeWidth = ink.Width },
                    _ => Style,
                };
            }
            SyncStyleBar();
            foreach (var page in Pages) page.InvalidateItems();
            UpdateState();
        }

        /// <summary>Durante il trascinamento: la modifica cambia subito, l'annullamento si registra a fine trascinamento.</summary>
        internal void ReplaceLive(int index, EditItem item)
        {
            _items[index] = item;
            InvalidatePage(item.Page);
        }

        internal void CommitLive(int index, EditItem original)
        {
            var now = _items[index];
            _items[index] = original;
            PushUndo();
            _items[index] = now;
            UpdateState();
        }

        private void RemoveAt(int index)
        {
            PushUndo();
            int page = _items[index].Page;
            _items.RemoveAt(index);
            SelectedIndex = null;
            InvalidatePage(page);
            UpdateState();
        }

        private void PushUndo()
        {
            _undo.Push(new Snapshot(_items.ToList(), new Dictionary<string, string>(_fieldValues), _digital));
            _undoButton.IsEnabled = true;
        }

        public void Undo()
        {
            CommitInlineEditor();
            if (_undo.Count == 0) return;
            var s = _undo.Pop();
            _items.Clear();
            _items.AddRange(s.Items);
            bool fieldsChanged = !s.Fields.SequenceEqual(_fieldValues);
            _fieldValues.Clear();
            foreach (var (k, v) in s.Fields) _fieldValues[k] = v;
            _digital = s.Digital;
            SelectedIndex = null;
            foreach (var page in Pages)
            {
                page.InvalidateItems();
                if (fieldsChanged) page.ApplyZoom();
            }
            UpdateState();
        }

        private void InvalidatePage(int page)
        {
            if (page >= 0 && page < _pagesPanel.Children.Count && _pagesPanel.Children[page] is EditorPage p) p.InvalidateItems();
        }

        // ---- Testo in linea -----------------------------------------------------------------------------

        /// <summary>Correggere un testo già presente: si crea la sostituzione e si apre l'editor sopra.</summary>
        internal void StartReplacement(PdfTextBlock block, EditorPage page)
        {
            int index = Add(new TextReplacement(page.Index, block, block.Text));
            EditText(index, page);
        }

        /// <summary>Apre l'editor in linea per un testo nuovo o una sostituzione.</summary>
        internal void EditText(int index, EditorPage page)
        {
            CommitInlineEditor();
            var item = _items[index];
            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = item is TextItem ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Padding = new Thickness(0),
                MinHeight = 0,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(AppInfo.Accent),
                Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                Text = item switch { TextItem t => t.Text, TextReplacement r => r.NewText, _ => "" },
            };
            // Il riquadro di modifica ha sempre un aspetto chiaro: sta sopra una pagina bianca.
            StyleInline(box, item);
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { CommitInlineEditor(); e.Handled = true; }
            };
            box.LostFocus += (_, _) => CommitInlineEditor();
            box.TextChanged += (_, _) =>
            {
                if (_inlineIndex < 0 || _inlineIndex >= _items.Count) return;
                _items[_inlineIndex] = _items[_inlineIndex] switch
                {
                    TextItem t => t with { Text = box.Text ?? "" },
                    TextReplacement r => r with { NewText = box.Text ?? "" },
                    var other => other,
                };
            };
            _inline = box;
            _inlineIndex = index;
            SelectedIndex = index;
            page.Overlay.Children.Add(box);
            page.InvalidateItems();
            box.AttachedToVisualTree += (_, _) =>
            {
                box.Focus();
                box.CaretIndex = box.Text?.Length ?? 0;
            };
            SyncStyleBar();
        }

        private void StyleInline(TextBox box, EditItem item)
        {
            double z = Zoom;
            switch (item)
            {
                case TextItem t:
                    box.FontFamily = EditDrawing.Family(t.Family);
                    box.FontSize = t.Size * z;
                    box.FontWeight = t.Bold ? FontWeight.Bold : FontWeight.Normal;
                    box.FontStyle = t.Italic ? FontStyle.Italic : FontStyle.Normal;
                    box.Foreground = EditDrawing.Brush(t.Color);
                    box.Width = Math.Max(40, t.Box.Width * z);
                    Canvas.SetLeft(box, t.Box.X * z - 1);
                    Canvas.SetTop(box, t.Box.Y * z - 1);
                    break;
                case TextReplacement r:
                    var b = r.Block;
                    box.FontFamily = EditDrawing.Family(b.Family);
                    box.FontSize = b.Size * z;
                    box.FontWeight = b.Bold ? FontWeight.Bold : FontWeight.Normal;
                    box.FontStyle = b.Italic ? FontStyle.Italic : FontStyle.Normal;
                    box.Foreground = EditDrawing.Brush(b.Color);
                    box.MinWidth = Math.Max(40, b.Box.Width * z + 12);
                    box.Background = Brushes.White;
                    Canvas.SetLeft(box, b.Box.X * z - 2);
                    Canvas.SetTop(box, (b.Baseline - b.Size * 0.95) * z - 2);
                    break;
            }
        }

        /// <summary>Chiude l'editor in linea: un testo nuovo vuoto si elimina, il resto resta come scritto.</summary>
        public void CommitInlineEditor()
        {
            if (_inline == null) return;
            var box = _inline;
            int index = _inlineIndex;
            _inline = null;
            _inlineIndex = -1;
            (box.Parent as Canvas)?.Children.Remove(box);
            if (index >= 0 && index < _items.Count)
            {
                var item = _items[index];
                if (item is TextItem t && string.IsNullOrWhiteSpace(t.Text))
                {
                    _items.RemoveAt(index);
                    if (_undo.Count > 0) _undo.Pop(); // l'aggiunta del testo vuoto non conta
                    SelectedIndex = null;
                }
                else if (item is TextReplacement r && r.NewText == r.Block.Text)
                {
                    _items.RemoveAt(index); // nessun cambiamento: il testo resta com'era
                    if (_undo.Count > 0) _undo.Pop();
                    SelectedIndex = null;
                }
                else if (item is TextItem text)
                {
                    // il riquadro si adatta al testo scritto
                    double h = EditDrawing.Text(text.Text, text.Family, text.Size, text.Color, text.Bold, text.Italic, 1, text.Box.Width).Height;
                    _items[index] = text with { Box = text.Box with { Height = h } };
                }
                InvalidatePage(item.Page);
            }
            _undoButton.IsEnabled = _undo.Count > 0;
            UpdateState();
        }

        // ---- Moduli -------------------------------------------------------------------------------------

        /// <summary>I campi del modulo della pagina, come controlli sopra i riquadri.</summary>
        internal void LayoutFields(EditorPage page, Canvas canvas)
        {
            canvas.Children.Clear();
            double z = Zoom;
            var fill = new SolidColorBrush(Color.FromArgb(210, 226, 235, 255));
            foreach (var field in _fields)
                foreach (var widget in field.Widgets.Where(w => w.Page == page.Index))
                {
                    var r = EditDrawing.Scale(widget.Box, z);
                    Control control;
                    string value = _fieldValues.TryGetValue(field.Name, out var v) ? v : field.Value;
                    switch (field.Kind)
                    {
                        case FormFieldKind.Text:
                        {
                            var box = new TextBox
                            {
                                Text = value,
                                AcceptsReturn = field.Multiline,
                                TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                                FontSize = Math.Clamp(Math.Min(12, widget.Box.Height * 0.62), 6, 14) * z,
                                Padding = new Thickness(2 * z, 0),
                                MinHeight = 0,
                                VerticalContentAlignment = field.Multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
                                Background = fill,
                                Foreground = Brushes.Black,
                                BorderThickness = new Thickness(0),
                                IsReadOnly = field.ReadOnly,
                            };
                            box.LostFocus += (_, _) => SetField(field.Name, box.Text ?? "");
                            control = box;
                            break;
                        }
                        case FormFieldKind.CheckBox:
                        case FormFieldKind.Radio:
                        {
                            bool radio = field.Kind == FormFieldKind.Radio;
                            bool on = radio ? value == widget.OnState : value == "true";
                            var toggle = new Border
                            {
                                Background = fill,
                                BorderBrush = new SolidColorBrush(Color.FromRgb(90, 110, 170)),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(radio ? r.Height / 2 : 2),
                                Cursor = new Cursor(StandardCursorType.Hand),
                                Child = new TextBlock
                                {
                                    Text = on ? (radio ? "●" : "✓") : "",
                                    FontSize = r.Height * 0.8,
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Foreground = Brushes.Black,
                                },
                            };
                            if (!field.ReadOnly)
                                toggle.PointerPressed += (_, e) =>
                                {
                                    e.Handled = true;
                                    if (radio) SetField(field.Name, on ? "Off" : widget.OnState ?? "Off");
                                    else SetField(field.Name, on ? "false" : "true");
                                    foreach (var p in Pages) LayoutFields(p, p.FieldsCanvas);
                                };
                            control = toggle;
                            break;
                        }
                        default:
                        {
                            var combo = new ComboBox
                            {
                                ItemsSource = field.Options,
                                SelectedItem = field.Options.Contains(value) ? value : null,
                                FontSize = Math.Clamp(widget.Box.Height * 0.55, 6, 13) * z,
                                Padding = new Thickness(2 * z, 0),
                                MinHeight = 0,
                                Background = fill,
                                IsEnabled = !field.ReadOnly,
                            };
                            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) SetField(field.Name, s); };
                            control = combo;
                            break;
                        }
                    }
                    control.Width = Math.Max(8, r.Width);
                    control.Height = Math.Max(8, r.Height);
                    ToolTip.SetTip(control, field.Name);
                    Canvas.SetLeft(control, r.X);
                    Canvas.SetTop(control, r.Y);
                    canvas.Children.Add(control);
                }
        }

        internal void SetField(string name, string value)
        {
            string current = _fieldValues.TryGetValue(name, out var v) ? v : _fieldOriginal.GetValueOrDefault(name, "");
            if (current == value) return;
            PushUndo();
            _fieldValues[name] = value;
            UpdateState();
        }

        private Dictionary<string, string> ChangedFields() =>
            _fieldValues.Where(kv => _fieldOriginal.GetValueOrDefault(kv.Key) != kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);

        // ---- File ---------------------------------------------------------------------------------------

        private async Task PickAsync()
        {
            var files = await Pick.Files(this, T("editor.openTitle"), false, Pick.PdfFiles);
            if (files.Count > 0) await LoadAsync(files[0]);
        }

        private async Task DroppedAsync(IReadOnlyList<string> files)
        {
            if (files.FirstOrDefault(Pick.IsPdf) is { } pdf)
            {
                await LoadAsync(pdf);
                return;
            }
            if (_file != null && files.FirstOrDefault(Pick.IsImage) is { } image)
            {
                _pendingImage = ImageBytes(image);
                ActiveTool = EditTool.Image;
                foreach (var (t, b) in _toolButtons) b.IsChecked = t == EditTool.Image;
                Main.SetStatus(T("editor.hint.image"));
            }
        }

        public override async Task AddFilesAsync(IReadOnlyList<string> files)
        {
            if (files.FirstOrDefault(Pick.IsPdf) is { } pdf) await LoadAsync(pdf);
        }

        /// <summary>Apre un PDF nell'editor (se ci sono modifiche non salvate, chiede prima).</summary>
        public async Task LoadAsync(string path, bool confirm = true)
        {
            if (confirm && HasChanges)
            {
                bool discard = await new ConfirmWindow(T("editor.discardTitle"), T("editor.discardBody"), T("editor.discard"), T("common.cancel")).ShowDialog<bool>(Main);
                if (!discard) return;
            }
            var opened = await OpenPdf(path);
            if (opened == null) return;
            CommitInlineEditor();
            _file?.Dispose();
            _file = opened;
            _items.Clear();
            _undo.Clear();
            _blocks.Clear();
            _fieldValues.Clear();
            _fieldOriginal.Clear();
            _digital = null;
            SelectedIndex = null;
            ClearResult();

            try
            {
                _fields = await Task.Run(() => PdfForms.Read(opened.Path, opened.Password, opened.File));
            }
            catch (Exception)
            {
                _fields = Array.Empty<FormField>(); // modulo non leggibile: si può comunque scrivere sopra
            }
            foreach (var f in _fields) _fieldOriginal[f.Name] = f.Value;

            _pagesPanel.Children.Clear();
            for (int i = 0; i < opened.PageCount; i++)
            {
                var page = new EditorPage(this, i, opened.File.Geometry(i));
                _pagesPanel.Children.Add(page);
            }
            FitWidth();
            int signatures = 0;
            try { signatures = opened.File.Signatures().Count; } catch (Exception) { /* niente */ }
            ShowNotice(signatures > 0 ? T("editor.alreadySigned")
                : _fields.Count > 0 ? F("editor.formNotice", _fields.Count)
                : null);
            UpdateState();
        }

        private bool HasChanges => _items.Count > 0 || ChangedFields().Count > 0 || _digital != null;

        private void ShowNotice(string? text, string? action = null, Action? onAction = null)
        {
            _notice.IsVisible = text != null;
            _noticeText.Text = text;
            _noticeAction.IsVisible = action != null;
            _noticeAction.Content = action;
            _noticeAction.Click -= NoticeClicked;
            _noticeHandler = onAction;
            if (action != null) _noticeAction.Click += NoticeClicked;
        }

        private Action? _noticeHandler;

        private void NoticeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _noticeHandler?.Invoke();

        // ---- Zoom ---------------------------------------------------------------------------------------

        public void SetZoom(double zoom)
        {
            CommitInlineEditor();
            Zoom = Math.Clamp(zoom, 0.25, 4);
            foreach (var page in Pages) page.ApplyZoom();
            SyncStyleBar();
        }

        private void FitWidth()
        {
            var widest = Pages.Select(p => p.Geometry.ViewWidth).DefaultIfEmpty(595).Max();
            double available = _scroller.Bounds.Width > 100 ? _scroller.Bounds.Width - 48 : 860;
            SetZoom(Math.Min(2, available / widest));
        }

        // ---- Tastiera -----------------------------------------------------------------------------------

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (_inline != null) return; // si sta scrivendo
            if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Undo(); e.Handled = true; }
            else if (e.Key is Key.Delete or Key.Back && SelectedIndex is int i && i < _items.Count) { RemoveAt(i); e.Handled = true; }
            else if (e.Key == Key.Escape) { SetTool(EditTool.Select); e.Handled = true; }
            else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && SelectedIndex is int s && s < _items.Count)
            {
                double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
                double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
                double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
                PushUndo();
                _items[s] = EditDrawing.Move(_items[s], dx, dy);
                InvalidatePage(_items[s].Page);
                e.Handled = true;
            }
        }

        // ---- Stato e salvataggio ------------------------------------------------------------------------

        private void UpdateState()
        {
            bool loaded = _file != null;
            _emptyHint.IsVisible = !loaded;
            _undoButton.IsEnabled = _undo.Count > 0;
            _save.IsEnabled = loaded && HasChanges;
            _save.Content = T(_digital != null ? "editor.saveSigned" : "editor.save");
            if (!loaded)
            {
                _summary.Text = "";
                return;
            }
            int changes = _items.Count + ChangedFields().Count;
            var parts = new List<string> { F("editor.summaryPages", _file!.PageCount) };
            if (changes > 0) parts.Add(F("editor.summaryChanges", changes));
            if (_digital != null)
                parts.Add(F(_digital.Box == null ? "editor.summaryDigitalInvisible" : "editor.summaryDigital", DigitalSigning.DisplayName(_digital.Certificate)));
            _summary.Text = string.Join(" · ", parts);
        }

        /// <summary>L'insieme delle modifiche da applicare (anche per i controlli automatici).</summary>
        internal EditSet BuildEdits()
        {
            var edits = new EditSet();
            edits.Items.AddRange(_items);
            foreach (var (k, v) in ChangedFields()) edits.Fields[k] = v;
            return edits;
        }

        private async Task SaveAsync()
        {
            CommitInlineEditor();
            if (_file == null || !HasChanges) return;
            var name = Path.GetFileNameWithoutExtension(_file.Path);
            var output = await Pick.SaveFile(this, T("editor.saveTitle"), F(_digital != null ? "editor.signedName" : "editor.editedName", name),
                Pick.PdfFiles, Path.GetDirectoryName(_file.Path));
            if (output == null) return;
            await SaveToAsync(output);
        }

        /// <summary>Salva in <paramref name="output"/> e riapre il file salvato, così si continua da lì.</summary>
        internal async Task<bool> SaveToAsync(string output)
        {
            if (_file == null) return false;
            var file = _file;
            var edits = BuildEdits();
            var digital = _digital;
            bool ok = await RunAsync(T(digital != null ? "editor.signing" : "editor.saving"),
                () => Task.Run(() => PdfEditor.Apply(file.Path, file.Password, edits, output, digital)), _save);
            if (!ok) return false;
            await LoadAsync(output, confirm: false);
            string message = F(digital != null ? "editor.savedSigned" : "editor.saved", Path.GetFileName(output));
            if (digital != null)
            {
                var checks = await Task.Run(() => DigitalSigning.Verify(output));
                if (!checks.Any(c => c.Valid && c.CoversWholeFile)) message = T("editor.signCheckFailed");
            }
            ShowResult(message, output);
            return true;
        }

        public override void Dispose()
        {
            _file?.Dispose();
            _file = null;
        }
    }

    /// <summary>Simbolo (linee su griglia 24×24) e testo per i pulsanti dell'editor.</summary>
    internal sealed class ContentIcon : StackPanel
    {
        public ContentIcon(string geometry, string text)
        {
            Orientation = Orientation.Horizontal;
            Spacing = 6;
            var icon = new PathIcon(Geometry.Parse(geometry)) { Width = 16, Height = 16 };
            Children.Add(icon);
            Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        }

        /// <summary>Il simbolo disegnato a tratto, nel colore del testo del pulsante.</summary>
        private sealed class PathIcon : Control
        {
            private readonly Geometry _geometry;

            public PathIcon(Geometry geometry) => _geometry = geometry;

            public override void Render(DrawingContext context)
            {
                double s = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
                var brush = (Parent as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Foreground ?? Brushes.Gray;
                var pen = new Pen(brush, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                using (context.PushTransform(Matrix.CreateScale(s, s)))
                    context.DrawGeometry(null, pen, _geometry);
            }
        }
    }
}
