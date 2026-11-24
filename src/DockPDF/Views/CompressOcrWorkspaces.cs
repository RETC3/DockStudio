using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using DockPDF.Engine;
using DockStudio.Core.Localization;
using DockStudio.Ocr;
using static DockPDF.Text;

namespace DockPDF.Views
{
    internal static class Sizes
    {
        public static string Format(long bytes)
        {
            var culture = Languages.Culture;
            if (bytes < 1024) return $"{bytes} byte";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString(bytes < 10 * 1024 ? "0.0" : "0", culture) + " KB";
            return (bytes / (1024.0 * 1024)).ToString(bytes < 10L * 1024 * 1024 ? "0.0" : "0", culture) + " MB";
        }
    }

    // ---- Comprimi -----------------------------------------------------------------------------------------

    public sealed class CompressWorkspace : SingleFileWorkspace
    {
        private readonly RadioButton _high = new() { GroupName = "level" };
        private readonly RadioButton _medium = new() { GroupName = "level", IsChecked = true };
        private readonly RadioButton _low = new() { GroupName = "level" };

        public CompressWorkspace(MainWindow main) : base(main, PdfTool.Compress, T("compress.run"))
        {
            Control Option(RadioButton radio, string key)
            {
                radio.Content = Ui.Column(2, Ui.Label(T(key), "heading"), Ui.Label(T(key + "Body"), "muted"));
                radio.Margin = new Avalonia.Thickness(0, 0, 0, 4);
                return radio;
            }
            BuildSingle(Ui.Column(12,
                Ui.Label(T("compress.how"), "heading"),
                Option(_high, "compress.high"),
                Option(_medium, "compress.medium"),
                Option(_low, "compress.low"),
                Ui.Label(T("compress.note"), "muted")));
        }

        private CompressLevel Level => _high.IsChecked == true ? CompressLevel.High : _low.IsChecked == true ? CompressLevel.Low : CompressLevel.Medium;

        protected override void OnLoaded(OpenedPdf file) =>
            Summary.Text = F("compress.summary", file.PageCount, Sizes.Format(new FileInfo(file.Path).Length));

        protected override async Task RunToolAsync(OpenedPdf file)
        {
            var name = Path.GetFileNameWithoutExtension(file.Path);
            var output = await Pick.SaveFile(this, T("compress.saveTitle"), F("compress.name", name), Pick.PdfFiles, Path.GetDirectoryName(file.Path));
            if (output == null) return;
            var level = Level;
            CompressResult? result = null;
            var progress = new Progress<double>(p => Main.SetStatus(F("compress.progress", (int)(p * 100))));
            bool ok = await RunAsync(T("compress.running"), () => Task.Run(() =>
                result = PdfCompress.Compress(file.Path, file.Password, output, level, progress)), Action);
            if (!ok || result == null) return;
            if (!result.Saved) ShowResult(T("compress.notSmaller"), error: true);
            else ShowResult(F("compress.done", Sizes.Format(result.Before), Sizes.Format(result.After), (int)Math.Round(result.Saving * 100)), output);
        }
    }

    // ---- Riconosci il testo (OCR) ------------------------------------------------------------------------

    public sealed class OcrWorkspace : SingleFileWorkspace
    {
        private readonly ComboBox _language = new() { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly CheckBox _skip = new() { IsChecked = true };
        private readonly TextBlock _state = new() { TextWrapping = TextWrapping.Wrap };
        private readonly Button _copy = new() { IsVisible = false };
        private readonly System.Collections.Generic.IReadOnlyList<OcrLanguage> _languages;
        private string _lastText = "";

        public OcrWorkspace(MainWindow main) : base(main, PdfTool.Ocr, T("ocr.run"))
        {
            IReadOnlyListOrEmpty(out _languages);
            _language.ItemsSource = _languages.Select(l => l.Name).ToList();
            var preferred = AppInfo.Ocr.DefaultLanguage;
            int index = _languages.ToList().FindIndex(l => l.Tag == preferred);
            _language.SelectedIndex = _languages.Count == 0 ? -1 : Math.Max(0, index);
            _skip.Content = T("ocr.skip");
            _copy.Content = T("ocr.copy");
            _copy.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(_lastText);
                Main.SetStatus(T("ocr.copied"));
            };
            _state.Text = T("ocr.empty");

            var options = Ui.Column(12,
                Ui.Label(T("ocr.heading"), "heading"),
                _state,
                Ui.Label(T("ocr.language"), "heading"),
                _language,
                _skip,
                Ui.Label(T("ocr.note"), "muted"),
                _copy);
            if (_languages.Count == 0)
            {
                var missing = Ui.Label(T("ocr.noLanguages"), "error");
                options.Children.Insert(1, missing);
                _language.IsEnabled = false;
            }
            BuildSingle(options);
        }

        private static void IReadOnlyListOrEmpty(out System.Collections.Generic.IReadOnlyList<OcrLanguage> languages)
        {
            try { languages = AppInfo.Ocr.Languages; }
            catch (Exception) { languages = Array.Empty<OcrLanguage>(); }
        }

        protected override void OnLoaded(OpenedPdf file)
        {
            _copy.IsVisible = false;
            int withText = Enumerable.Range(0, file.PageCount).Count(i => { try { return file.File.HasText(i); } catch { return false; } });
            Summary.Text = F("split.summary", file.PageCount);
            _state.Text = withText == 0 ? T("ocr.scanned")
                : withText == file.PageCount ? T("ocr.allText")
                : F("ocr.someText", withText, file.PageCount);
            Action.IsEnabled = _languages.Count > 0;
        }

        protected override async Task RunToolAsync(OpenedPdf file)
        {
            if (_languages.Count == 0) return;
            var name = Path.GetFileNameWithoutExtension(file.Path);
            var output = await Pick.SaveFile(this, T("ocr.saveTitle"), F("ocr.name", name), Pick.PdfFiles, Path.GetDirectoryName(file.Path));
            if (output == null) return;
            string? language = _language.SelectedIndex >= 0 ? _languages[_language.SelectedIndex].Tag : null;
            bool skip = _skip.IsChecked == true;
            OcrSummary? summary = null;
            var progress = new Progress<(int Page, int Count)>(p =>
                Main.SetStatus(p.Page < p.Count ? F("ocr.progress", p.Page + 1, p.Count) : T("ocr.saving")));
            bool ok = await RunAsync(T("ocr.running"), async () =>
                summary = await Task.Run(() => PdfOcr.MakeSearchableAsync(file.Path, file.Password, output, AppInfo.Ocr, language, skip, progress)), Action);
            if (!ok || summary == null) return;
            _lastText = summary.Text;
            _copy.IsVisible = _lastText.Length > 0;
            ShowResult(summary.PagesRecognized == 0
                ? T("ocr.nothing")
                : F("ocr.done", summary.PagesRecognized, summary.Words, Path.GetFileName(output)), output);
        }
    }
}
