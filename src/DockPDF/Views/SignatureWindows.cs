using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DockPDF.Engine;
using DockStudio.UI.Windows;
using SkiaSharp;

namespace DockPDF.Views
{
    /// <summary>
    /// Le firme a mano salvate per riusarle, in %APPDATA%\DockStudio\DockPDF\firme.json (solo su questo computer).
    /// </summary>
    public static class SignatureStore
    {
        private static string PathOf() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DockStudio", "DockPDF", "firme.json");

        /// <summary>Per i controlli automatici: un file diverso da quello vero.</summary>
        internal static string? OverridePath { get; set; }

        private static string File => OverridePath ?? PathOf();

        public static IReadOnlyList<HandSignature> Load()
        {
            try
            {
                if (!System.IO.File.Exists(File)) return Array.Empty<HandSignature>();
                return JsonSerializer.Deserialize<List<StoredSignature>>(System.IO.File.ReadAllText(File))?
                    .Select(s => s.ToSignature()).ToList() ?? new List<HandSignature>();
            }
            catch (Exception)
            {
                return Array.Empty<HandSignature>();
            }
        }

        public static void Add(HandSignature signature) => Save(Load().Append(signature).TakeLast(8).ToList());

        public static void Remove(HandSignature signature) => Save(Load().Where(s => !Same(s, signature)).ToList());

        private static bool Same(HandSignature a, HandSignature b) =>
            JsonSerializer.Serialize(StoredSignature.From(a)) == JsonSerializer.Serialize(StoredSignature.From(b));

        private static void Save(IReadOnlyList<HandSignature> list)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(list.Select(StoredSignature.From).ToList()));
        }

        /// <summary>Forma semplice per il JSON.</summary>
        private sealed class StoredSignature
        {
            public List<List<double[]>>? Strokes { get; set; }
            public string? Name { get; set; }
            public string? Image { get; set; }
            public uint Color { get; set; }
            public double Aspect { get; set; }
            public double Stroke { get; set; }

            public static StoredSignature From(HandSignature s) => new()
            {
                Strokes = s.Strokes?.Select(st => st.Select(p => new[] { Math.Round(p.X, 4), Math.Round(p.Y, 4) }).ToList()).ToList(),
                Name = s.TypedName,
                Image = s.Image == null ? null : Convert.ToBase64String(s.Image),
                Color = s.Color.Value,
                Aspect = s.AspectRatio,
                Stroke = s.StrokeRatio,
            };

            public HandSignature ToSignature() => new()
            {
                Strokes = Strokes?.Select(st => (IReadOnlyList<PagePoint>)st.Select(p => new PagePoint(p[0], p[1])).ToList()).ToList(),
                TypedName = Name,
                Image = Image == null ? null : Convert.FromBase64String(Image),
                Color = new Argb(Color),
                AspectRatio = Aspect > 0 ? Aspect : 3,
                StrokeRatio = Stroke > 0 ? Stroke : 0.035,
            };
        }
    }

    /// <summary>Anteprima di una firma (nel menu delle firme salvate).</summary>
    public sealed class SignaturePreview : Control
    {
        private readonly HandSignature _signature;

        public SignaturePreview(HandSignature signature) => _signature = signature;

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Brushes.White, null, new Rect(Bounds.Size), 4, 4);
            EditDrawing.DrawSignature(context, _signature, new Rect(Bounds.Size).Deflate(4));
        }
    }

    /// <summary>Riquadro in cui disegnare la firma con il mouse, la penna o il dito.</summary>
    public sealed class SignaturePad : Control
    {
        private readonly List<List<Point>> _strokes = new();
        private List<Point>? _current;

        public SignaturePad()
        {
            Height = 190;
            Cursor = new Cursor(StandardCursorType.Cross);
        }

        public Argb Ink { get; set; } = new(0xFF1B2A6B);

        public double StrokeWidth => 3;

        public bool IsEmpty => _strokes.All(s => s.Count == 0);

        public event Action? Changed;

        public void Clear()
        {
            _strokes.Clear();
            InvalidateVisual();
            Changed?.Invoke();
        }

        /// <summary>Per i controlli automatici.</summary>
        internal void AddStroke(IEnumerable<Point> points)
        {
            _strokes.Add(points.ToList());
            InvalidateVisual();
            Changed?.Invoke();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            _current = new List<Point> { e.GetPosition(this) };
            _strokes.Add(_current);
            e.Pointer.Capture(this);
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_current == null) return;
            var p = e.GetPosition(this);
            var last = _current[^1];
            if (Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) < 1.2) return;
            _current.Add(p);
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _current = null;
            e.Pointer.Capture(null);
            Changed?.Invoke();
        }

        public override void Render(DrawingContext context)
        {
            var r = new Rect(Bounds.Size);
            context.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(200, 204, 212)), 1), r, 8, 8);
            // linea su cui firmare
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(210, 214, 222)), 1, new DashStyle(new double[] { 4, 4 }, 0)),
                new Point(24, r.Height * 0.72), new Point(r.Width - 24, r.Height * 0.72));
            EditDrawing.DrawStrokes(context, _strokes.Select(s => (IReadOnlyList<Point>)s), EditDrawing.Brush(Ink), StrokeWidth);
        }

        /// <summary>
        /// La firma ritagliata attorno ai tratti, con le coordinate tra 0 e 1 e le proporzioni del ritaglio.
        /// </summary>
        public HandSignature ToSignature()
        {
            var points = _strokes.SelectMany(s => s).ToList();
            double pad = StrokeWidth * 1.5;
            double x1 = points.Min(p => p.X) - pad, y1 = points.Min(p => p.Y) - pad;
            double x2 = points.Max(p => p.X) + pad, y2 = points.Max(p => p.Y) + pad;
            double w = Math.Max(1, x2 - x1), h = Math.Max(1, y2 - y1);
            return new HandSignature
            {
                Strokes = _strokes.Where(s => s.Count > 0)
                    .Select(s => (IReadOnlyList<PagePoint>)s.Select(p => new PagePoint((p.X - x1) / w, (p.Y - y1) / h)).ToList()).ToList(),
                AspectRatio = w / h,
                StrokeRatio = StrokeWidth / h,
                Color = Ink,
            };
        }
    }

    /// <summary>
    /// Nuova firma a mano: disegnata, scritta (in corsivo) o da un'immagine. ShowDialog&lt;HandSignature?&gt;
    /// restituisce null se si annulla.
    /// </summary>
    public sealed class SignatureWindow : DockWindow
    {
        private static readonly Argb Blue = new(0xFF1B2A6B);
        private static readonly Argb Black = new(0xFF111111);

        private readonly TabControl _tabs = new();
        private readonly SignaturePad _pad = new();
        private readonly TextBox _name = new();
        private readonly SignaturePreviewBase _typedPreview;
        private readonly Image _imagePreview = new() { Height = 150, Stretch = Stretch.Uniform };
        private readonly CheckBox _remember = new() { IsChecked = true };
        private readonly Button _ok;
        private byte[]? _image;
        private Argb _color = Blue;
        private HandSignature _typed = new() { TypedName = "" };

        public SignatureWindow() : base(AppInfo.Branding)
        {
            Title = DockPDF.Text.T("signature.title");
            Icon = AppInfo.Icon;
            Width = 640;

            // Disegna
            var clear = ButtonFor(DockPDF.Text.T("signature.clear"), () => _pad.Clear());
            var drawTab = new StackPanel { Spacing = 8, Children = { Text(DockPDF.Text.T("signature.drawHint"), "muted"), _pad, clear } };
            clear.HorizontalAlignment = HorizontalAlignment.Left;

            // Scrivi
            _name.PlaceholderText = DockPDF.Text.T("signature.namePlaceholder");
            _typedPreview = new SignaturePreviewHost(() => _typed) { Height = 120 };
            _name.TextChanged += (_, _) =>
            {
                _typed = new HandSignature { TypedName = _name.Text ?? "", Color = _color };
                _typedPreview.InvalidateVisual();
                UpdateOk();
            };
            var typeTab = new StackPanel { Spacing = 8, Children = { Text(DockPDF.Text.T("signature.typeHint"), "muted"), _name, _typedPreview } };

            // Immagine
            var pickImage = ButtonFor(DockPDF.Text.T("signature.pickImage"), async () =>
            {
                var files = await Pick.Files(this, DockPDF.Text.T("signature.pickImage"), false, Pick.ImageFiles);
                if (files.Count == 0) return;
                _image = TransparentBackground(File.ReadAllBytes(files[0]));
                _imagePreview.Source = EditDrawing.Image(_image);
                UpdateOk();
            });
            pickImage.HorizontalAlignment = HorizontalAlignment.Left;
            var imageTab = new StackPanel { Spacing = 8, Children = { Text(DockPDF.Text.T("signature.imageHint"), "muted"), pickImage, _imagePreview } };

            _tabs.ItemsSource = new[]
            {
                new TabItem { Header = DockPDF.Text.T("signature.draw"), Content = drawTab },
                new TabItem { Header = DockPDF.Text.T("signature.type"), Content = typeTab },
                new TabItem { Header = DockPDF.Text.T("signature.image"), Content = imageTab },
            };
            _tabs.SelectionChanged += (_, _) => UpdateOk();
            _pad.Changed += UpdateOk;

            var blue = new RadioButton { Content = DockPDF.Text.T("signature.blue"), GroupName = "ink", IsChecked = true };
            var black = new RadioButton { Content = DockPDF.Text.T("signature.black"), GroupName = "ink" };
            blue.IsCheckedChanged += (_, _) => SetColor(blue.IsChecked == true ? Blue : Black);
            _remember.Content = DockPDF.Text.T("signature.remember");

            _ok = ButtonFor(DockPDF.Text.T("signature.ok"), Confirm, accent: true);
            _ok.IsEnabled = false;
            SetBody(
                Text(DockPDF.Text.T("signature.title"), "heading"),
                _tabs,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { blue, black } },
                _remember,
                Text(DockPDF.Text.T("signature.note"), "muted"),
                Buttons(ButtonFor(DockPDF.Text.T("common.cancel"), () => Close(null)), _ok));
        }

        /// <summary>True se la firma va ricordata per le prossime volte.</summary>
        public bool Remember => _remember.IsChecked == true;

        /// <summary>Per i controlli automatici.</summary>
        internal SignaturePad Pad => _pad;

        private void SetColor(Argb color)
        {
            _color = color;
            _pad.Ink = color;
            _pad.InvalidateVisual();
            _typed = _typed with { Color = color };
            _typedPreview.InvalidateVisual();
        }

        private void UpdateOk() => _ok.IsEnabled = _tabs.SelectedIndex switch
        {
            0 => !_pad.IsEmpty,
            1 => !string.IsNullOrWhiteSpace(_name.Text),
            _ => _image != null,
        };

        internal HandSignature? Result() => _tabs.SelectedIndex switch
        {
            0 when !_pad.IsEmpty => _pad.ToSignature(),
            1 when !string.IsNullOrWhiteSpace(_name.Text) => new HandSignature { TypedName = _name.Text!.Trim(), Color = _color },
            2 when _image != null => new HandSignature { Image = _image },
            _ => null,
        };

        private void Confirm()
        {
            if (Result() is { } signature) Close(signature);
        }

        /// <summary>
        /// Foto o scansione della firma: lo sfondo chiaro diventa trasparente e l'immagine si ritaglia attorno al
        /// tratto. Restituisce un PNG.
        /// </summary>
        internal static byte[] TransparentBackground(byte[] data)
        {
            using var decoded = SKBitmap.Decode(data) ?? throw new InvalidDataException();
            using var source = decoded.Copy(SKColorType.Bgra8888);
            int w = source.Width, h = source.Height;
            using var output = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = source.GetPixel(x, y);
                    double luminance = 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
                    // più è chiaro, più è trasparente (bordi morbidi tra 170 e 225)
                    double alpha = Math.Clamp((225 - luminance) / 55.0, 0, 1) * (c.Alpha / 255.0);
                    output.SetPixel(x, y, new SKColor(c.Red, c.Green, c.Blue, (byte)Math.Round(alpha * 255)));
                    if (alpha > 0.1)
                    {
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    }
                }
            if (maxX < 0) throw new InvalidDataException(DockPDF.Text.T("signature.imageEmpty"));
            int pad = Math.Max(2, (maxX - minX) / 50);
            var crop = new SKRectI(Math.Max(0, minX - pad), Math.Max(0, minY - pad), Math.Min(w, maxX + pad + 1), Math.Min(h, maxY + pad + 1));
            using var cropped = new SKBitmap(crop.Width, crop.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            output.ExtractSubset(cropped, crop);
            using var image = SKImage.FromBitmap(cropped);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }

        /// <summary>Anteprima che legge la firma corrente a ogni disegno.</summary>
        private sealed class SignaturePreviewHost : SignaturePreviewBase
        {
            private readonly Func<HandSignature> _get;

            public SignaturePreviewHost(Func<HandSignature> get) => _get = get;

            protected override HandSignature Current => _get();
        }
    }

    public abstract class SignaturePreviewBase : Control
    {
        protected abstract HandSignature Current { get; }

        public override void Render(DrawingContext context)
        {
            var r = new Rect(Bounds.Size);
            context.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(200, 204, 212)), 1), r, 8, 8);
            EditDrawing.DrawSignature(context, Current, r.Deflate(new Thickness(24, 12)));
        }
    }

    /// <summary>
    /// Firma digitale: scelta del certificato (archivio personale, smart card o token, oppure file .pfx/.p12), motivo,
    /// luogo e se mostrarla sulla pagina. ShowDialog&lt;DigitalSignRequest?&gt; restituisce null se si annulla.
    /// </summary>
    public sealed class DigitalSignWindow : DockWindow
    {
        private readonly ListBox _list = new() { MaxHeight = 220 };
        private readonly TextBox _reason = new();
        private readonly TextBox _location = new();
        private readonly CheckBox _visible = new() { IsChecked = true };
        private readonly TextBlock _error = new() { Classes = { "error" }, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        private readonly List<X509Certificate2> _certificates = new();
        private readonly Button _ok;

        public DigitalSignWindow() : base(AppInfo.Branding)
        {
            Title = DockPDF.Text.T("digital.title");
            Icon = AppInfo.Icon;
            Width = 600;
            _reason.PlaceholderText = DockPDF.Text.T("digital.reasonPlaceholder");
            _location.PlaceholderText = DockPDF.Text.T("digital.locationPlaceholder");
            _visible.Content = DockPDF.Text.T("digital.visible");
            var pfx = ButtonFor(DockPDF.Text.T("digital.pfx"), async () => await LoadPfxAsync());
            var refresh = ButtonFor(DockPDF.Text.T("digital.refresh"), Refresh);
            _ok = ButtonFor(DockPDF.Text.T("digital.ok"), Confirm, accent: true);
            _list.SelectionChanged += (_, _) => _ok.IsEnabled = _list.SelectedIndex >= 0;
            Refresh();

            SetBody(
                Text(DockPDF.Text.T("digital.title"), "heading"),
                Text(DockPDF.Text.T("digital.body"), "muted"),
                _list,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { pfx, refresh } },
                _error,
                Text(DockPDF.Text.T("digital.reason"), "heading"), _reason,
                Text(DockPDF.Text.T("digital.location"), "heading"), _location,
                _visible,
                Text(DockPDF.Text.T("digital.note"), "muted"),
                Buttons(ButtonFor(DockPDF.Text.T("common.cancel"), () => Close(null)), _ok));
        }

        /// <summary>True se la firma deve comparire su una pagina (si sceglie dove con un clic).</summary>
        public bool Visible => _visible.IsChecked == true;

        private void Refresh()
        {
            _certificates.Clear();
            _certificates.AddRange(DigitalSigning.Certificates());
            ShowList();
            if (_certificates.Count == 0) ShowError(DockPDF.Text.T("digital.none"));
            else _error.IsVisible = false;
        }

        private void ShowList()
        {
            _list.ItemsSource = _certificates.Select(c => new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = DigitalSigning.DisplayName(c), FontWeight = FontWeight.SemiBold },
                    new TextBlock
                    {
                        Text = DockPDF.Text.F("digital.certDetails", c.GetNameInfo(X509NameType.SimpleName, true), c.NotAfter.ToString("d", DockStudio.Core.Localization.Languages.Culture)),
                        Classes = { "muted" }, FontSize = 12,
                    },
                },
            }).ToList();
            _list.SelectedIndex = _certificates.Count > 0 ? 0 : -1;
            _ok.IsEnabled = _list.SelectedIndex >= 0;
        }

        private async System.Threading.Tasks.Task LoadPfxAsync()
        {
            var type = new Avalonia.Platform.Storage.FilePickerFileType(DockPDF.Text.T("digital.pfxType")) { Patterns = new[] { "*.pfx", "*.p12" } };
            var files = await Pick.Files(this, DockPDF.Text.T("digital.pfx"), false, type);
            if (files.Count == 0) return;
            var password = await new PasswordWindow(Path.GetFileName(files[0]), false).ShowDialog<string?>(this);
            if (password == null) return;
            try
            {
                var cert = DigitalSigning.LoadPfx(files[0], password);
                _certificates.Insert(0, cert);
                ShowList();
                _error.IsVisible = false;
            }
            catch (CryptographicException)
            {
                ShowError(DockPDF.Text.T("digital.pfxFailed"));
            }
        }

        private void ShowError(string text)
        {
            _error.Text = text;
            _error.IsVisible = true;
        }

        private void Confirm()
        {
            if (_list.SelectedIndex < 0) return;
            Close(new DigitalSignRequest(_certificates[_list.SelectedIndex],
                string.IsNullOrWhiteSpace(_reason.Text) ? null : _reason.Text.Trim(),
                string.IsNullOrWhiteSpace(_location.Text) ? null : _location.Text.Trim()));
        }
    }
}
