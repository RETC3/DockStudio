using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using DockStudio.UI.Windows;
using SkiaSharp;

namespace DockPDF.Views
{
    /// <summary>
    /// Il simbolo di DockPDF disegnato in vettoriale (nitido a ogni dimensione): quadrato rosso con un foglio
    /// dall'angolo piegato. Stesso disegno di site/public/apps/dockpdf/logo.svg e dell'icona dell'exe.
    /// </summary>
    public sealed class LogoMark : Control
    {
        private static readonly IBrush Soft = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
        private static readonly Geometry Page = Geometry.Parse("M9,6 L18.5,6 L23,10.5 L23,26 L9,26 Z");
        private static readonly Geometry Fold = Geometry.Parse("M18.5,6 L23,10.5 L18.5,10.5 Z");

        public LogoMark()
        {
            Width = 28;
            Height = 28;
        }

        public override void Render(DrawingContext context)
        {
            double s = Math.Min(Bounds.Width, Bounds.Height) / 32.0;
            var accent = new SolidColorBrush(AppInfo.Accent);
            context.DrawRectangle(accent, null, new Rect(s, s, 30 * s, 30 * s), 7 * s, 7 * s);
            using (context.PushTransform(Matrix.CreateScale(s, s)))
            {
                context.DrawGeometry(Brushes.White, null, Page);
                context.DrawGeometry(accent, null, Fold);
                context.DrawGeometry(Soft, null, Fold);
            }
            foreach (var (x, y, w) in new[] { (12.0, 14.5, 8.0), (12.0, 18.0, 8.0), (12.0, 21.5, 5.0) })
                context.DrawRectangle(accent, null, new Rect(x * s, y * s, w * s, 1.6 * s));
        }
    }

    /// <summary>Il simbolo di uno strumento (linee su una griglia di 24×24) nel colore del testo.</summary>
    public sealed class ToolIcon : Control
    {
        public static readonly StyledProperty<IBrush?> StrokeProperty =
            AvaloniaProperty.Register<ToolIcon, IBrush?>(nameof(Stroke));

        private readonly Geometry _geometry;

        public ToolIcon(PdfTool tool, double size = 24)
        {
            _geometry = Tools.Icon(tool);
            Width = size;
            Height = size;
            AffectsRender<ToolIcon>(StrokeProperty);
        }

        public IBrush? Stroke
        {
            get => GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            double s = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
            var pen = new Pen(Stroke ?? Brushes.Gray, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            using (context.PushTransform(Matrix.CreateScale(s, s)))
                context.DrawGeometry(null, pen, _geometry);
        }
    }

    /// <summary>Immagini Skia (pagine disegnate da PDFium) per Avalonia.</summary>
    public static class Thumbs
    {
        public static Bitmap ToAvalonia(SKBitmap bitmap) => new(
            PixelFormat.Bgra8888, AlphaFormat.Premul, bitmap.GetPixels(),
            new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), bitmap.RowBytes);
    }

    /// <summary>Scelta di file e cartelle con le finestre del sistema; ricorda l'ultima cartella usata.</summary>
    public static class Pick
    {
        public static readonly FilePickerFileType PdfFiles = new("PDF") { Patterns = new[] { "*.pdf" }, MimeTypes = new[] { "application/pdf" } };
        public static readonly FilePickerFileType ImageFiles = new(Text.T("pick.images"))
        {
            Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.webp" },
        };

        public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

        public static bool IsPdf(string path) => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        private static async Task<IStorageFolder?> StartFolder(TopLevel top) =>
            App.Settings.LastFolder is { } last && Directory.Exists(last)
                ? await top.StorageProvider.TryGetFolderFromPathAsync(last)
                : null;

        private static void Remember(string? path)
        {
            var folder = path == null ? null : Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (folder != null) App.Settings.LastFolder = folder;
        }

        public static async Task<IReadOnlyList<string>> Files(Control owner, string title, bool multiple, params FilePickerFileType[] types)
        {
            var top = TopLevel.GetTopLevel(owner)!;
            var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = multiple,
                FileTypeFilter = types,
                SuggestedStartLocation = await StartFolder(top),
            });
            var paths = result.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (paths.Count > 0) Remember(paths[0]);
            return paths;
        }

        public static async Task<string?> SaveFile(Control owner, string title, string suggestedName, FilePickerFileType type, string? folder = null)
        {
            var top = TopLevel.GetTopLevel(owner)!;
            var start = folder != null && Directory.Exists(folder) ? await top.StorageProvider.TryGetFolderFromPathAsync(folder) : await StartFolder(top);
            var ext = type.Patterns?.FirstOrDefault()?.TrimStart('*', '.') ?? "pdf";
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                DefaultExtension = ext,
                FileTypeChoices = new[] { type },
                ShowOverwritePrompt = true,
                SuggestedStartLocation = start,
            });
            var path = file?.TryGetLocalPath();
            Remember(path);
            return path;
        }

        public static async Task<string?> Folder(Control owner, string title, string? current)
        {
            var top = TopLevel.GetTopLevel(owner)!;
            var start = current != null && Directory.Exists(current) ? await top.StorageProvider.TryGetFolderFromPathAsync(current) : await StartFolder(top);
            var result = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });
            var path = result.FirstOrDefault()?.TryGetLocalPath();
            Remember(path);
            return path;
        }

        /// <summary>I file trascinati (solo quelli sul disco).</summary>
        public static IReadOnlyList<string> Dropped(DragEventArgs e) =>
            e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? new List<string>();

        /// <summary>Rende <paramref name="target"/> un punto in cui lasciare i file; <paramref name="accept"/> decide quali.</summary>
        public static void AcceptDrops(Control target, Func<string, bool> accept, Action<IReadOnlyList<string>> dropped)
        {
            DragDrop.SetAllowDrop(target, true);
            target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            {
                e.DragEffects = Dropped(e).Any(accept) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            });
            target.AddHandler(DragDrop.DropEvent, (_, e) =>
            {
                var files = Dropped(e).Where(accept).ToList();
                if (files.Count > 0) dropped(files);
                e.Handled = true;
            });
        }
    }

    /// <summary>Chiede la password di un PDF protetto. ShowDialog&lt;string?&gt; restituisce null se si annulla.</summary>
    public sealed class PasswordWindow : DockWindow
    {
        public PasswordWindow(string fileName, bool wrong) : base(AppInfo.Branding)
        {
            Title = DockPDF.Text.T("password.title");
            Icon = AppInfo.Icon;
            var box = new TextBox { PasswordChar = '•', PlaceholderText = DockPDF.Text.T("password.placeholder") };
            var message = Text(DockPDF.Text.F(wrong ? "password.wrong" : "password.body", fileName));
            if (wrong) message.Classes.Add("error");
            void Confirm() => Close(box.Text ?? "");
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Confirm(); };
            SetBody(
                Text(DockPDF.Text.T("password.title"), "heading"),
                message,
                box,
                Buttons(
                    ButtonFor(DockPDF.Text.T("common.cancel"), () => Close(null)),
                    ButtonFor(DockPDF.Text.T("password.open"), Confirm, accent: true)));
            Opened += (_, _) => box.Focus();
        }
    }

    /// <summary>Domanda di conferma con due pulsanti; ShowDialog&lt;bool&gt; restituisce true se si conferma.</summary>
    public sealed class ConfirmWindow : DockWindow
    {
        public ConfirmWindow(string title, string message, string confirm, string cancel) : base(AppInfo.Branding)
        {
            Title = title;
            Icon = AppInfo.Icon;
            SetBody(
                Text(title, "heading"),
                Text(message),
                Buttons(
                    ButtonFor(cancel, () => Close(false)),
                    ButtonFor(confirm, () => Close(true), accent: true)));
        }
    }

    /// <summary>Piccoli aiuti per costruire l'interfaccia in codice.</summary>
    internal static class Ui
    {
        public static TextBlock Label(string text, params string[] classes)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            foreach (var c in classes) block.Classes.Add(c);
            return block;
        }

        public static Button Button(string text, Action onClick, bool accent = false)
        {
            var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
            if (accent) button.Classes.Add("accent");
            button.Click += (_, _) => onClick();
            return button;
        }

        public static StackPanel Row(double spacing, params Control[] children)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
            foreach (var c in children)
            {
                c.VerticalAlignment = VerticalAlignment.Center;
                panel.Children.Add(c);
            }
            return panel;
        }

        public static StackPanel Column(double spacing, params Control[] children)
        {
            var panel = new StackPanel { Spacing = spacing };
            foreach (var c in children) panel.Children.Add(c);
            return panel;
        }

        public static Border Card(Control child, Thickness? padding = null) =>
            new() { Classes = { "card" }, Child = child, Padding = padding ?? new Thickness(18) };

        /// <summary>Un colore del tema (DockSurface, DockMuted...) per il tema attivo.</summary>
        public static IBrush Brush(Control owner, string key, IBrush fallback) =>
            owner.TryFindResource(key, owner.ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;
    }
}
