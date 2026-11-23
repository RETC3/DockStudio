using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DockPDF.Engine;
using static DockPDF.Text;

namespace DockPDF.Views
{
    /// <summary>Strumenti che lavorano su un PDF alla volta: il file in alto, le opzioni sotto, il pulsante in basso.</summary>
    public abstract class SingleFileWorkspace : Workspace
    {
        protected readonly FileSlot Slot;
        protected readonly TextBlock Summary = new() { Classes = { "muted" } };
        protected readonly Button Action = new() { Classes = { "accent" }, IsEnabled = false };

        protected SingleFileWorkspace(MainWindow main, PdfTool tool, string actionText) : base(main, tool)
        {
            Slot = new FileSlot(this, OpenPdf);
            Slot.FileLoaded += file =>
            {
                ClearResult();
                Action.IsEnabled = true;
                OnLoaded(file);
            };
            Action.Content = actionText;
            Action.Click += async (_, _) => { if (Slot.File != null) await RunToolAsync(Slot.File); };
        }

        /// <summary>Mette insieme l'area: il file e poi le opzioni dello strumento.</summary>
        protected void BuildSingle(Control options)
        {
            var column = Ui.Column(12, Slot, Ui.Card(options));
            column.MaxWidth = 760;
            column.HorizontalAlignment = HorizontalAlignment.Left;
            column.Width = double.NaN;
            Build(new ScrollViewer { Content = column, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, Summary, Action);
        }

        public override Task AddFilesAsync(IReadOnlyList<string> files) =>
            files.FirstOrDefault(Pick.IsPdf) is { } pdf ? Slot.LoadAsync(pdf) : Task.CompletedTask;

        protected virtual void OnLoaded(OpenedPdf file) { }

        protected abstract Task RunToolAsync(OpenedPdf file);

        /// <summary>Cartella di uscita: quella del file, finché non se ne sceglie un'altra.</summary>
        protected sealed class FolderChoice
        {
            private readonly TextBlock _label = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            private string? _chosen;

            public FolderChoice(Workspace owner)
            {
                var change = Ui.Button(T("folder.change"), async () =>
                {
                    var folder = await Pick.Folder(owner, T("folder.title"), Folder);
                    if (folder != null) { _chosen = folder; Refresh(); }
                });
                var row = new DockPanel();
                DockPanel.SetDock(change, Dock.Right);
                row.Children.Add(change);
                row.Children.Add(_label);
                View = Ui.Column(6, Ui.Label(T("folder.label"), "heading"), row);
            }

            public Control View { get; }
            public string? FileFolder { get; set; }
            public string? Folder => _chosen ?? FileFolder;

            public void Refresh() => _label.Text = Folder ?? T("folder.sameAsFile");
        }
    }

    // ---- Dividi -----------------------------------------------------------------------------------------

    public sealed class SplitWorkspace : SingleFileWorkspace
    {
        private readonly RadioButton _each = new() { GroupName = "split", IsChecked = true };
        private readonly RadioButton _everyN = new() { GroupName = "split" };
        private readonly RadioButton _ranges = new() { GroupName = "split" };
        private readonly RadioButton _extract = new() { GroupName = "split" };
        private readonly NumericUpDown _n = new() { Minimum = 1, Maximum = 9999, Value = 2, Increment = 1, FormatString = "0", Width = 130 };
        private readonly TextBox _rangeText = new() { Width = 260 };
        private readonly TextBox _extractText = new() { Width = 260 };
        private readonly TextBlock _rangeError = new() { Classes = { "error" }, IsVisible = false };
        private readonly FolderChoice _folder;

        public SplitWorkspace(MainWindow main) : base(main, PdfTool.Split, T("split.run"))
        {
            _each.Content = T("split.each");
            _everyN.Content = T("split.everyN");
            _ranges.Content = T("split.ranges");
            _extract.Content = T("split.extract");
            _rangeText.PlaceholderText = T("split.rangesHint");
            _extractText.PlaceholderText = T("split.extractHint");
            _rangeText.GotFocus += (_, _) => _ranges.IsChecked = true;
            _extractText.GotFocus += (_, _) => _extract.IsChecked = true;
            _n.GotFocus += (_, _) => _everyN.IsChecked = true;
            _folder = new FolderChoice(this);
            _folder.Refresh();

            BuildSingle(Ui.Column(12,
                Ui.Label(T("split.how"), "heading"),
                _each,
                Ui.Row(10, _everyN, _n),
                Ui.Row(10, _ranges, _rangeText),
                Ui.Row(10, _extract, _extractText),
                _rangeError,
                _folder.View));
        }

        protected override void OnLoaded(OpenedPdf file)
        {
            _folder.FileFolder = Path.GetDirectoryName(file.Path);
            _folder.Refresh();
            Summary.Text = F("split.summary", file.PageCount);
        }

        protected override async Task RunToolAsync(OpenedPdf file)
        {
            _rangeError.IsVisible = false;
            var mode = _everyN.IsChecked == true ? SplitMode.EveryN
                : _ranges.IsChecked == true ? SplitMode.Ranges
                : _extract.IsChecked == true ? SplitMode.Extract
                : SplitMode.EachPage;
            List<PageRange>? ranges = null;
            if (mode is SplitMode.Ranges or SplitMode.Extract)
            {
                var text = mode == SplitMode.Ranges ? _rangeText.Text : _extractText.Text;
                if (!PageRanges.TryParse(text, file.PageCount, out var parsed))
                {
                    _rangeError.Text = F("split.rangesInvalid", file.PageCount);
                    _rangeError.IsVisible = true;
                    return;
                }
                ranges = parsed;
            }
            int n = (int)(_n.Value ?? 2);
            var folder = _folder.Folder ?? Path.GetDirectoryName(file.Path)!;
            IReadOnlyList<string> created = Array.Empty<string>();
            bool ok = await RunAsync(T("split.running"), () => Task.Run(() =>
                created = PdfTools.Split(file.Path, file.Password, mode, n, ranges, folder)), Action);
            if (!ok) return;
            if (created.Count == 1) ShowResult(F("split.doneOne", Path.GetFileName(created[0])), created[0]);
            else ShowResult(F("split.done", created.Count), folder);
        }
    }

    // ---- PDF → immagini ---------------------------------------------------------------------------------

    public sealed class ExportWorkspace : SingleFileWorkspace
    {
        private readonly RadioButton _all = new() { GroupName = "pages", IsChecked = true };
        private readonly RadioButton _some = new() { GroupName = "pages" };
        private readonly TextBox _rangeText = new() { Width = 220 };
        private readonly ComboBox _format = new() { Width = 300 };
        private readonly ComboBox _dpi = new() { Width = 200 };
        private readonly TextBlock _rangeError = new() { Classes = { "error" }, IsVisible = false };
        private readonly FolderChoice _folder;
        private static readonly int[] Resolutions = { 72, 150, 300 };

        public ExportWorkspace(MainWindow main) : base(main, PdfTool.PdfToImages, T("export.run"))
        {
            _all.Content = T("export.all");
            _some.Content = T("export.some");
            _rangeText.PlaceholderText = T("split.extractHint");
            _rangeText.GotFocus += (_, _) => _some.IsChecked = true;
            _format.ItemsSource = new[] { T("export.png"), T("export.jpg") };
            _format.SelectedIndex = 0;
            _dpi.ItemsSource = Resolutions.Select(d => F("export.dpi" + d, d)).ToArray();
            _dpi.SelectedIndex = 1;
            _folder = new FolderChoice(this);
            _folder.Refresh();

            BuildSingle(Ui.Column(12,
                Ui.Label(T("export.pages"), "heading"),
                _all,
                Ui.Row(10, _some, _rangeText),
                _rangeError,
                Ui.Row(24,
                    Ui.Column(6, Ui.Label(T("export.format"), "heading"), _format),
                    Ui.Column(6, Ui.Label(T("export.quality"), "heading"), _dpi)),
                _folder.View));
        }

        protected override void OnLoaded(OpenedPdf file)
        {
            _folder.FileFolder = Path.GetDirectoryName(file.Path);
            _folder.Refresh();
            Summary.Text = F("split.summary", file.PageCount);
        }

        protected override async Task RunToolAsync(OpenedPdf file)
        {
            _rangeError.IsVisible = false;
            List<PageRange>? ranges = null;
            if (_some.IsChecked == true)
            {
                if (!PageRanges.TryParse(_rangeText.Text, file.PageCount, out var parsed))
                {
                    _rangeError.Text = F("split.rangesInvalid", file.PageCount);
                    _rangeError.IsVisible = true;
                    return;
                }
                ranges = parsed;
            }
            string format = _format.SelectedIndex == 1 ? "jpg" : "png";
            int dpi = Resolutions[Math.Max(0, _dpi.SelectedIndex)];
            var folder = _folder.Folder ?? Path.GetDirectoryName(file.Path)!;
            IReadOnlyList<string> created = Array.Empty<string>();
            var progress = new Progress<double>(p => Main.SetStatus(F("export.progress", (int)(p * 100))));
            bool ok = await RunAsync(T("export.running"), () => Task.Run(() =>
                created = PdfTools.PdfToImages(file.Path, file.Password, ranges, dpi, format, folder, progress)), Action);
            if (!ok) return;
            if (created.Count == 1) ShowResult(F("export.doneOne", Path.GetFileName(created[0])), created[0]);
            else ShowResult(F("export.done", created.Count), folder);
        }
    }

    // ---- Proteggi e Sblocca -----------------------------------------------------------------------------

    public sealed class ProtectWorkspace : SingleFileWorkspace
    {
        private readonly bool _protect;
        private readonly TextBox _password = new() { PasswordChar = '•', Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBox _confirm = new() { PasswordChar = '•', Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly CheckBox _print = new() { IsChecked = true };
        private readonly CheckBox _copy = new() { IsChecked = true };
        private readonly TextBlock _error = new() { Classes = { "error" }, IsVisible = false };
        private readonly TextBlock _state = new() { TextWrapping = TextWrapping.Wrap };

        public ProtectWorkspace(MainWindow main, bool protect)
            : base(main, protect ? PdfTool.Protect : PdfTool.Unlock, T(protect ? "protect.run" : "unlock.run"))
        {
            _protect = protect;
            if (protect)
            {
                _password.PlaceholderText = T("protect.password");
                _confirm.PlaceholderText = T("protect.confirm");
                _print.Content = T("protect.allowPrint");
                _copy.Content = T("protect.allowCopy");
                BuildSingle(Ui.Column(12,
                    Ui.Label(T("protect.heading"), "heading"),
                    Ui.Label(T("protect.body"), "muted"),
                    _password, _confirm, _error,
                    Ui.Label(T("protect.limits"), "heading"),
                    _print, _copy,
                    Ui.Label(T("protect.note"), "muted")));
            }
            else
            {
                BuildSingle(Ui.Column(12,
                    Ui.Label(T("unlock.heading"), "heading"),
                    _state,
                    Ui.Label(T("unlock.body"), "muted")));
                _state.Text = T("unlock.empty");
            }
        }

        protected override void OnLoaded(OpenedPdf file)
        {
            if (_protect)
            {
                Summary.Text = F(file.File.IsEncrypted ? "protect.summaryProtected" : "split.summary", file.PageCount);
                return;
            }
            _state.Text = !file.File.IsEncrypted ? T("unlock.notProtected")
                : file.Password != null ? T("unlock.withPassword")
                : T("unlock.restrictionsOnly");
            Action.IsEnabled = file.File.IsEncrypted;
            Summary.Text = F("split.summary", file.PageCount);
        }

        protected override async Task RunToolAsync(OpenedPdf file)
        {
            _error.IsVisible = false;
            string password = _password.Text ?? "";
            if (_protect)
            {
                if (password.Length == 0) { ShowError(T("protect.empty")); return; }
                if (password != (_confirm.Text ?? "")) { ShowError(T("protect.mismatch")); return; }
            }
            var name = Path.GetFileNameWithoutExtension(file.Path);
            var output = await Pick.SaveFile(this, T(_protect ? "protect.saveTitle" : "unlock.saveTitle"),
                F(_protect ? "protect.name" : "unlock.name", name), Pick.PdfFiles, Path.GetDirectoryName(file.Path));
            if (output == null) return;
            bool print = _print.IsChecked == true, copy = _copy.IsChecked == true;
            bool ok = await RunAsync(T(_protect ? "protect.running" : "unlock.running"), () => Task.Run(() =>
            {
                if (_protect) PdfTools.Protect(file.Path, file.Password, output, password, allowPrint: print, allowCopy: copy);
                else PdfTools.Unlock(file.Path, file.Password, output);
            }), Action);
            if (!ok) return;
            ShowResult(F(_protect ? "protect.done" : "unlock.done", Path.GetFileName(output)), output);
            if (_protect) _password.Text = _confirm.Text = "";
        }

        private void ShowError(string text)
        {
            _error.Text = text;
            _error.IsVisible = true;
        }
    }

    // ---- Immagini → PDF ---------------------------------------------------------------------------------

    public sealed class ImagesWorkspace : Workspace
    {
        private readonly List<string> _images = new();
        private readonly StackPanel _list = new() { Spacing = 6 };
        private readonly ComboBox _layout = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly CheckBox _margin = new();
        private readonly TextBlock _summary = new() { Classes = { "muted" } };
        private readonly Button _create = new() { Classes = { "accent" }, IsEnabled = false };
        private readonly Control _emptyHint;

        public ImagesWorkspace(MainWindow main) : base(main, PdfTool.ImagesToPdf)
        {
            _create.Content = T("images.run");
            _create.Click += async (_, _) => await CreateAsync();
            _layout.ItemsSource = new[] { T("images.fit"), T("images.a4"), T("images.letter") };
            _layout.SelectedIndex = 0;
            _layout.SelectionChanged += (_, _) => _margin.IsEnabled = _layout.SelectedIndex > 0;
            _margin.Content = T("images.margin");
            _margin.IsEnabled = false;

            var add = Ui.Button(T("images.add"), async () => await PickAsync());
            var pick = Ui.Button(T("images.add"), async () => await PickAsync(), accent: true);
            pick.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyHint = DropHint(T("images.empty"), pick);
            var listCard = Ui.Card(new Panel
            {
                Children = { new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, _emptyHint },
            }, new Thickness(10));

            var options = Ui.Card(Ui.Column(12,
                Ui.Label(T("images.page"), "heading"), _layout, _margin,
                Ui.Label(T("images.note"), "muted"),
                add), new Thickness(16));
            options.Width = 300;
            options.VerticalAlignment = VerticalAlignment.Top;
            options.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(options, Dock.Right);
            var body = new DockPanel();
            body.Children.Add(options);
            body.Children.Add(listCard);
            Build(body, _summary, _create);
            Pick.AcceptDrops(this, Pick.IsImage, files => _ = AddFilesAsync(files));
        }

        internal IReadOnlyList<string> Images => _images;

        private async Task PickAsync()
        {
            var files = await Pick.Files(this, T("images.addTitle"), multiple: true, Pick.ImageFiles);
            if (files.Count > 0) await AddFilesAsync(files);
        }

        public override Task AddFilesAsync(IReadOnlyList<string> files)
        {
            foreach (var f in files.Where(Pick.IsImage)) _images.Add(f);
            Refresh();
            return Task.CompletedTask;
        }

        private void Refresh()
        {
            _list.Children.Clear();
            for (int i = 0; i < _images.Count; i++) _list.Children.Add(Row(i));
            _emptyHint.IsVisible = _images.Count == 0;
            _create.IsEnabled = _images.Count > 0;
            _summary.Text = _images.Count == 0 ? "" : F("images.summary", _images.Count);
        }

        private Control Row(int index)
        {
            var path = _images[index];
            var thumb = new Image { Width = 56, Height = 56, Stretch = Stretch.Uniform };
            _ = LoadThumb(path, thumb);
            Button Small(string text, string tip, bool enabled, Action click)
            {
                var b = new Button { Content = text, Padding = new Thickness(8, 2), IsEnabled = enabled };
                ToolTip.SetTip(b, tip);
                b.Click += (_, _) => { click(); Refresh(); };
                return b;
            }
            var buttons = Ui.Row(4,
                Small("↑", T("images.up"), index > 0, () => (_images[index - 1], _images[index]) = (_images[index], _images[index - 1])),
                Small("↓", T("images.down"), index < _images.Count - 1, () => (_images[index + 1], _images[index]) = (_images[index], _images[index + 1])),
                Small("✕", T("images.remove"), true, () => _images.RemoveAt(index)));
            var row = new DockPanel { Margin = new Thickness(4) };
            var number = new TextBlock { Text = (index + 1).ToString(), Width = 28, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(number, Dock.Left);
            DockPanel.SetDock(thumb, Dock.Left);
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(number);
            row.Children.Add(thumb);
            row.Children.Add(buttons);
            var name = new TextBlock { Text = Path.GetFileName(path), Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(name, path);
            row.Children.Add(name);
            return row;
        }

        private static async Task LoadThumb(string path, Image target)
        {
            try
            {
                var bitmap = await Task.Run(() =>
                {
                    using var stream = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(stream, 112);
                });
                target.Source = bitmap;
            }
            catch (Exception)
            {
                // immagine non leggibile: resta senza anteprima, l'errore arriva alla creazione del PDF
            }
        }

        private async Task CreateAsync()
        {
            if (_images.Count == 0) return;
            var first = _images[0];
            var output = await Pick.SaveFile(this, T("images.saveTitle"),
                _images.Count == 1 ? Path.GetFileNameWithoutExtension(first) : T("images.name"),
                Pick.PdfFiles, Path.GetDirectoryName(first));
            if (output == null) return;
            var layout = _layout.SelectedIndex switch { 1 => PageLayout.A4, 2 => PageLayout.Letter, _ => PageLayout.FitImage };
            double margin = layout != PageLayout.FitImage && _margin.IsChecked == true ? 10 : 0;
            var images = _images.ToList();
            bool ok = await RunAsync(T("images.running"), () => Task.Run(() => PdfTools.ImagesToPdf(images, output, layout, margin)), _create);
            if (ok) ShowResult(F("images.done", Path.GetFileName(output), images.Count), output);
        }
    }
}
