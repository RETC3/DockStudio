using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DockPDF.Engine;
using static DockPDF.Text;

namespace DockPDF.Views
{
    /// <summary>Un PDF caricato in uno strumento: percorso, password (se serve) e documento aperto per le anteprime.</summary>
    public sealed class OpenedPdf : IDisposable
    {
        public OpenedPdf(string path, string? password, PdfFile file)
        {
            Path = path;
            Password = password;
            File = file;
        }

        public string Path { get; }
        public string? Password { get; }
        public PdfFile File { get; }
        public string Name => System.IO.Path.GetFileName(Path);
        public int PageCount => File.PageCount;

        public void Dispose() => File.Dispose();
    }

    /// <summary>
    /// Base delle aree di lavoro: contenuto al centro, barra in basso con il riepilogo, l'esito dell'ultima
    /// operazione e il pulsante principale.
    /// </summary>
    public abstract class Workspace : DockPanel, IDisposable
    {
        private readonly TextBlock _result = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly Button _openResult = new() { IsVisible = false };
        private readonly Button _revealResult = new() { IsVisible = false };
        private string? _resultPath;

        protected Workspace(MainWindow main, PdfTool tool)
        {
            Main = main;
            Tool = tool;
            Margin = new Thickness(16, 0, 16, 0);
            LastChildFill = true;
            Focusable = true;
            _openResult.Content = T("result.open");
            _revealResult.Content = T("result.reveal");
            _openResult.Click += (_, _) => { if (_resultPath != null) Try(() => Shell.Open(_resultPath)); };
            _revealResult.Click += (_, _) => { if (_resultPath != null) Try(() => Shell.Reveal(_resultPath)); };
        }

        public PdfTool Tool { get; }
        protected MainWindow Main { get; }

        /// <summary>Aggiunge (o carica) i file indicati, per esempio trascinati o passati dal cerchio.</summary>
        public abstract Task AddFilesAsync(IReadOnlyList<string> files);

        /// <summary>Costruisce l'area: <paramref name="body"/> al centro, la barra con <paramref name="actions"/> in basso.</summary>
        protected void Build(Control body, Control summary, params Control[] actions)
        {
            var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
            var right = Ui.Row(8, actions);
            DockPanel.SetDock(right, Dock.Right);
            bar.Children.Add(right);
            var left = Ui.Column(2, summary, Ui.Row(8, _result, _openResult, _revealResult));
            bar.Children.Add(left);
            DockPanel.SetDock(bar, Dock.Bottom);
            Children.Add(bar);
            Children.Add(body);
        }

        /// <summary>Esito dell'ultima operazione, con i pulsanti per aprire il file o la cartella.</summary>
        protected void ShowResult(string message, string? path = null, bool error = false)
        {
            _result.Text = message;
            _result.Classes.Set("error", error);
            _result.Classes.Set("success", !error);
            _resultPath = path;
            bool folder = path != null && Directory.Exists(path);
            _openResult.IsVisible = path != null;
            _openResult.Content = T(folder ? "result.openFolder" : "result.open");
            _revealResult.IsVisible = path != null && !folder;
        }

        protected void ClearResult()
        {
            _result.Text = "";
            _resultPath = null;
            _openResult.IsVisible = _revealResult.IsVisible = false;
        }

        /// <summary>
        /// Apre un PDF chiedendo la password se serve. Null se il file non è un PDF valido (con il messaggio) o se
        /// si annulla la password.
        /// </summary>
        protected async Task<OpenedPdf?> OpenPdf(string path)
        {
            try
            {
                return new OpenedPdf(path, null, await Task.Run(() => PdfFile.Open(path)));
            }
            catch (PdfPasswordException)
            {
                var password = await Main.AskPassword(path);
                if (password == null)
                {
                    ShowResult(F("open.canceled", Path.GetFileName(path)), error: true);
                    return null;
                }
                return new OpenedPdf(path, password, await Task.Run(() => PdfFile.Open(path, password)));
            }
            catch (Exception ex) when (ex is PdfFormatException or IOException or UnauthorizedAccessException)
            {
                ShowResult(F("open.failed", Path.GetFileName(path)), error: true);
                return null;
            }
        }

        /// <summary>
        /// Esegue un lavoro lungo fuori dall'interfaccia, con i pulsanti indicati disattivati e il messaggio nella
        /// barra di stato. Gli errori diventano un messaggio, non una chiusura dell'app.
        /// </summary>
        protected async Task<bool> RunAsync(string busy, Func<Task> work, params Control[] disable)
        {
            ClearResult();
            Main.SetStatus(busy);
            foreach (var c in disable) c.IsEnabled = false;
            try
            {
                await work();
                Main.SetStatus(T("status.ready"));
                return true;
            }
            catch (PdfPasswordException ex)
            {
                ShowResult(F("open.passwordNeeded", ex.File), error: true);
            }
            catch (Exception ex)
            {
                ShowResult(F("result.failed", ex.Message), error: true);
                _ = AppInfo.Errors.SendAsync(App.Settings, ex);
            }
            finally
            {
                foreach (var c in disable) c.IsEnabled = true;
            }
            Main.SetStatus(T("status.ready"));
            return false;
        }

        protected void Try(Action action)
        {
            try { action(); }
            catch (Exception ex) { ShowResult(F("result.failed", ex.Message), error: true); }
        }

        /// <summary>Testo d'aiuto al centro dell'area vuota: dove trascinare i file.</summary>
        protected static Control DropHint(string text, Button pick) =>
            new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = text, Classes = { "muted" }, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
                    pick,
                },
            };

        public virtual void Dispose() { }
    }

    /// <summary>
    /// Il PDF su cui lavora uno strumento a file singolo: area in cui trascinarlo o sceglierlo e, una volta
    /// caricato, anteprima della prima pagina, nome e numero di pagine.
    /// </summary>
    public sealed class FileSlot : Border
    {
        private readonly Workspace _owner;
        private readonly Func<string, Task<OpenedPdf?>> _open;
        private readonly Image _preview = new() { Width = 90, Height = 120, Stretch = Stretch.Uniform };
        private readonly TextBlock _name = new() { Classes = { "heading" }, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _details = new() { Classes = { "muted" } };
        private readonly StackPanel _empty;
        private readonly DockPanel _loaded;

        public FileSlot(Workspace owner, Func<string, Task<OpenedPdf?>> open)
        {
            _owner = owner;
            _open = open;
            // Aspetto delle schede (lo stile Border.card del tema non vale per le classi derivate).
            this.Bind(BackgroundProperty, this.GetResourceObservable("DockSurface"));
            this.Bind(BorderBrushProperty, this.GetResourceObservable("DockLine"));
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(10);
            Padding = new Thickness(16);
            MinHeight = 152;

            var pick = Ui.Button(T("slot.pick"), async () => await PickAsync(), accent: true);
            _empty = new StackPanel
            {
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = T("slot.drop"), Classes = { "muted" }, TextAlignment = TextAlignment.Center },
                    pick,
                },
            };
            pick.HorizontalAlignment = HorizontalAlignment.Center;

            var change = Ui.Button(T("slot.change"), async () => await PickAsync());
            var frame = new Border { Child = _preview, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 16, 0) };
            frame.Bind(BorderBrushProperty, frame.GetResourceObservable("DockLine"));
            _loaded = new DockPanel { IsVisible = false };
            DockPanel.SetDock(frame, Dock.Left);
            DockPanel.SetDock(change, Dock.Right);
            _loaded.Children.Add(frame);
            _loaded.Children.Add(change);
            _loaded.Children.Add(Ui.Column(4, _name, _details));
            change.VerticalAlignment = VerticalAlignment.Top;

            Child = new Panel { Children = { _empty, _loaded } };
            Pick.AcceptDrops(this, Pick.IsPdf, files => _ = LoadAsync(files[0]));
        }

        public OpenedPdf? File { get; private set; }

        public event Action<OpenedPdf>? FileLoaded;

        private async Task PickAsync()
        {
            var files = await Pick.Files(_owner, T("slot.pickTitle"), false, Pick.PdfFiles);
            if (files.Count > 0) await LoadAsync(files[0]);
        }

        public async Task LoadAsync(string path)
        {
            var opened = await _open(path);
            if (opened == null) return;
            File?.Dispose();
            File = opened;
            _name.Text = opened.Name;
            _details.Text = F(opened.File.IsEncrypted ? "slot.detailsProtected" : "slot.details", opened.PageCount,
                System.IO.Path.GetDirectoryName(opened.Path));
            _empty.IsVisible = false;
            _loaded.IsVisible = true;
            _preview.Source = null;
            try
            {
                var bitmap = await Task.Run(() =>
                {
                    using var skia = opened.File.Render(0, 180);
                    return Thumbs.ToAvalonia(skia);
                });
                if (File == opened) _preview.Source = bitmap;
            }
            catch (ObjectDisposedException) { /* cambiato nel frattempo */ }
            FileLoaded?.Invoke(opened);
        }

        public void Unload()
        {
            File?.Dispose();
            File = null;
        }
    }
}
