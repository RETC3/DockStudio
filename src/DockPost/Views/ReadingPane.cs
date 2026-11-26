using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using DockPost.Engine;
using static DockPost.Text;
using Paragraph = DockPost.Engine.Paragraph;
using UiTextBlock = Avalonia.Controls.TextBlock;

namespace DockPost.Views
{
    /// <summary>
    /// Riquadro di lettura: intestazioni, avviso delle immagini remote, allegati e testo disegnato dai blocchi
    /// (niente del messaggio si esegue; le immagini remote si scaricano solo su richiesta).
    /// </summary>
    public sealed class ReadingPane : Border
    {
        private static readonly HttpClient Images = CreateImageClient();
        private const long MaxRemoteImage = 10 * 1024 * 1024;

        private readonly StackPanel _header = new() { Spacing = 4, Margin = new Thickness(20, 16, 20, 8) };
        private readonly StackPanel _body = new() { Spacing = 8, Margin = new Thickness(20, 8, 20, 24) };
        private readonly ScrollViewer _scroll;
        private readonly UiTextBlock _placeholder = new() { Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
        private MessageContent? _content;
        private MessageRow? _row;
        private CancellationTokenSource? _imageCancel;

        public ReadingPane()
        {
            var stack = new StackPanel();
            stack.Children.Add(_header);
            var line = new Border { Height = 1, Margin = new Thickness(20, 0) };
            line.Bind(Border.BackgroundProperty, line.GetResourceObservable("DockLine"));
            stack.Children.Add(line);
            stack.Children.Add(_body);
            _scroll = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            this.Bind(BackgroundProperty, this.GetResourceObservable("DockSurface"));
            Child = new Panel { Children = { _placeholder, _scroll } };
            ShowEmpty();
        }

        /// <summary>Chiede di fidarsi sempre delle immagini di un mittente.</summary>
        public event Action<string>? TrustSender;


        private static HttpClient CreateImageClient()
        {
            var handler = new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = true, MaxAutomaticRedirections = 3 };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            // nessun dato che identifichi chi apre: solo l'indirizzo dell'immagine
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            return client;
        }

        public void ShowEmpty() => ShowPlaceholder(T("reading.empty"));

        public void ShowPlaceholder(string text)
        {
            _imageCancel?.Cancel();
            _content = null;
            _row = null;
            _placeholder.Text = text;
            _placeholder.IsVisible = true;
            _scroll.IsVisible = false;
        }

        public void Show(MessageRow row, MessageContent content, bool loadImages)
        {
            _imageCancel?.Cancel();
            _imageCancel = new CancellationTokenSource();
            _row = row;
            _content = content;
            _placeholder.IsVisible = false;
            _scroll.IsVisible = true;
            _scroll.Offset = default;
            BuildHeader(row, content, loadImages);
            BuildBody(content.Blocks, loadImages, _imageCancel.Token);
        }

        // ---- Intestazione ---------------------------------------------------------------------------------

        private void BuildHeader(MessageRow row, MessageContent content, bool loadImages)
        {
            _header.Children.Clear();
            var m = content.Message;
            _header.Children.Add(new SelectableTextBlock
            {
                Text = string.IsNullOrWhiteSpace(m.Subject) ? T("message.noSubject") : m.Subject,
                FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
            });
            var from = m.From.Mailboxes.FirstOrDefault();
            var fromLine = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            fromLine.Inlines!.Add(new Avalonia.Controls.Documents.Run(from == null ? "" : (string.IsNullOrWhiteSpace(from.Name) ? from.Address : from.Name)) { FontWeight = FontWeight.SemiBold });
            if (from != null && !string.IsNullOrWhiteSpace(from.Name))
                fromLine.Inlines.Add(new Avalonia.Controls.Documents.Run("  <" + from.Address + ">") { Foreground = Muted });
            _header.Children.Add(fromLine);
            var to = Composer.ToText(m.To);
            if (to.Length > 0) _header.Children.Add(Small(T("message.to") + " " + to));
            var cc = Composer.ToText(m.Cc);
            if (cc.Length > 0) _header.Children.Add(Small(T("message.cc") + " " + cc));
            _header.Children.Add(Small(m.Date.ToLocalTime().ToString("f", DockStudio.Core.Localization.Languages.Culture)));

            if (content.RemoteImages > 0 && !loadImages)
            {
                var banner = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                var show = new Button { Content = T("images.show"), Padding = new Thickness(10, 4) };
                var always = new Button { Content = T("images.always"), Padding = new Thickness(10, 4), Margin = new Thickness(6, 0, 0, 0) };
                show.Click += (_, _) => { if (_row != null && _content != null) Show(_row, _content, true); };
                always.Click += (_, _) =>
                {
                    if (from != null) TrustSender?.Invoke(from.Address.ToLowerInvariant());
                    if (_row != null && _content != null) Show(_row, _content, true);
                };
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { show, always } };
                DockPanel.SetDock(buttons, Dock.Right);
                banner.Children.Add(buttons);
                banner.Children.Add(new UiTextBlock { Text = T("images.blocked"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                _header.Children.Add(new Border
                {
                    Child = banner, Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(AppInfo.Accent, 0.12),
                });
            }

            if (content.Attachments.Count > 0)
            {
                var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var a in content.Attachments)
                {
                    var button = new Button
                    {
                        Content = $"📎 {a.FileName}  ({FormatSize(a.Size)})",
                        Margin = new Thickness(0, 0, 6, 6),
                        Padding = new Thickness(10, 4),
                    };
                    var menu = new MenuFlyout();
                    var open = new MenuItem { Header = T("attachment.open"), IsEnabled = !Shell.IsDangerous(a.FileName) };
                    var save = new MenuItem { Header = T("attachment.save") };
                    open.Click += (_, _) => OpenAttachment(a);
                    save.Click += async (_, _) => await SaveAttachment(a);
                    menu.Items.Add(open);
                    menu.Items.Add(save);
                    if (Shell.IsDangerous(a.FileName)) ToolTip.SetTip(button, T("attachment.dangerous"));
                    button.Flyout = menu;
                    wrap.Children.Add(button);
                }
                _header.Children.Add(wrap);
            }
        }

        private static IBrush Muted => (IBrush?)Application.Current?.FindResource(Application.Current.ActualThemeVariant, "DockMuted") ?? Brushes.Gray;

        private static UiTextBlock Small(string text) => new()
        {
            Text = text, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 13,
        };

        public static string FormatSize(long bytes)
        {
            var c = DockStudio.Core.Localization.Languages.Culture;
            if (bytes < 1024) return bytes.ToString(c) + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0", c) + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.#", c) + " MB";
        }

        private void OpenAttachment(AttachmentInfo a)
        {
            if (_content == null || Shell.IsDangerous(a.FileName)) return;
            var folder = Path.Combine(Path.GetTempPath(), "DockPost", Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, a.FileName);
            using (var fs = File.Create(path)) _content.WriteAttachment(a.Index, fs);
            Shell.OpenFile(path);
        }

        private async Task SaveAttachment(AttachmentInfo a)
        {
            if (_content == null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = a.FileName, Title = T("attachment.save") });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync();
            _content.WriteAttachment(a.Index, stream);
        }

        // ---- Testo ----------------------------------------------------------------------------------------

        private void BuildBody(IReadOnlyList<Block> blocks, bool loadImages, CancellationToken cancel)
        {
            _body.Children.Clear();
            foreach (var block in blocks.Take(5000))
            {
                Control? control = block switch
                {
                    Paragraph p => BuildParagraph(p),
                    RuleBlock => new Border { Height = 1, Background = Muted, Opacity = 0.4, Margin = new Thickness(0, 4) },
                    ImageBlock i => BuildImage(i, loadImages, cancel),
                    _ => null,
                };
                if (control == null) continue;
                if (block.QuoteLevel > 0)
                {
                    control = new Border
                    {
                        Child = control,
                        BorderBrush = new SolidColorBrush(AppInfo.Accent, 0.5),
                        BorderThickness = new Thickness(2, 0, 0, 0),
                        Padding = new Thickness(10, 0, 0, 0),
                        Margin = new Thickness(12 * (block.QuoteLevel - 1), 0, 0, 0),
                        Opacity = 0.85,
                    };
                }
                _body.Children.Add(control);
            }
        }

        private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

        /// <summary>Un paragrafo con i suoi stili; i link si aprono con un clic (e mostrano dove portano).</summary>
        internal static Control BuildParagraph(Paragraph p)
        {
            var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = double.NaN };
            switch (p.Style)
            {
                case BlockStyle.Heading1: text.FontSize = 22; text.FontWeight = FontWeight.SemiBold; break;
                case BlockStyle.Heading2: text.FontSize = 18; text.FontWeight = FontWeight.SemiBold; break;
                case BlockStyle.Heading3: text.FontSize = 16; text.FontWeight = FontWeight.SemiBold; break;
                case BlockStyle.Preformatted: text.FontFamily = Mono; text.TextWrapping = TextWrapping.Wrap; break;
            }
            var links = new List<(int Start, int End, string Url)>();
            int position = 0;
            foreach (var run in p.Runs)
            {
                var inline = new Avalonia.Controls.Documents.Run(run.Text);
                if (run.Bold) inline.FontWeight = FontWeight.Bold;
                if (run.Italic) inline.FontStyle = FontStyle.Italic;
                if (run.Monospace) inline.FontFamily = Mono;
                if (run.Underline || run.Link != null) inline.TextDecorations = TextDecorations.Underline;
                if (run.Link != null)
                {
                    inline.Foreground = new SolidColorBrush(AppInfo.Accent);
                    links.Add((position, position + run.Text.Length, run.Link));
                }
                text.Inlines!.Add(inline);
                position += run.Text.Length;
            }

            if (links.Count > 0)
            {
                string? LinkAt(Point point)
                {
                    var hit = text.TextLayout.HitTestPoint(point);
                    if (!hit.IsInside) return null;
                    int index = hit.TextPosition;
                    foreach (var l in links)
                        if (index >= l.Start && index < l.End) return l.Url;
                    return null;
                }
                Point pressedAt = default;
                text.PointerMoved += (_, e) =>
                {
                    var url = LinkAt(e.GetPosition(text) - new Point(text.Padding.Left, text.Padding.Top));
                    text.Cursor = url != null ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Ibeam);
                    ToolTip.SetTip(text, url);
                };
                text.AddHandler(InputElement.PointerPressedEvent, (_, e) => pressedAt = e.GetPosition(text), Avalonia.Interactivity.RoutingStrategies.Tunnel);
                text.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
                {
                    var at = e.GetPosition(text);
                    if (e.InitialPressMouseButton != MouseButton.Left || Math.Abs(at.X - pressedAt.X) > 4 || Math.Abs(at.Y - pressedAt.Y) > 4) return;
                    if (LinkAt(at - new Point(text.Padding.Left, text.Padding.Top)) is { } url) Shell.OpenLink(url);
                }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            }

            if (p.Bullet == null) return text;
            var row = new DockPanel { Margin = new Thickness(18 * Math.Max(0, p.ListLevel - 1), 0, 0, 0) };
            var bullet = new UiTextBlock { Text = p.Bullet, MinWidth = 22, Margin = new Thickness(0, 0, 4, 0) };
            DockPanel.SetDock(bullet, Dock.Left);
            row.Children.Add(bullet);
            row.Children.Add(text);
            return row;
        }

        private static Control? BuildImage(ImageBlock block, bool loadImages, CancellationToken cancel)
        {
            var image = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left };
            if (block.Data != null)
            {
                var bitmap = Decode(block.Data);
                if (bitmap == null) return null;
                image.Source = bitmap;
                image.MaxWidth = bitmap.Size.Width;
                return image;
            }
            if (block.RemoteUrl == null || !loadImages) return null;
            _ = LoadRemote(image, block.RemoteUrl, cancel);
            return image;
        }

        private static Bitmap? Decode(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                var bitmap = new Bitmap(ms);
                return bitmap.PixelSize.Width * (long)bitmap.PixelSize.Height > 40_000_000 ? null : bitmap;
            }
            catch (Exception) { return null; }
        }

        private static async Task LoadRemote(Image image, string url, CancellationToken cancel)
        {
            try
            {
                using var response = await Images.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxRemoteImage) return;
                await using var stream = await response.Content.ReadAsStreamAsync(cancel);
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancel)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > MaxRemoteImage) return;
                }
                var bitmap = Decode(buffer.ToArray());
                if (bitmap == null || cancel.IsCancellationRequested) return;
                image.Source = bitmap;
                image.MaxWidth = bitmap.Size.Width;
            }
            catch (Exception) { /* immagine non disponibile: si lascia vuoto */ }
        }
    }
}
