using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using DockPDF.Views;
using DockStudio.Core.Localization;
using DockStudio.UI;
using SkiaSharp;

namespace DockPDF.Checks
{
    public sealed class TestApp : Application
    {
        public override void Initialize() => DockTheme.Apply(this, AppInfo.Accent);
    }

    internal static partial class Program
    {
        static partial void RunUiChecks(string work)
        {
            Section("Testi", CheckTexts);
            Section("Riga di comando", CheckStartOptions);
            Section("Finestre", () => CheckWindows(work));
        }

        private static void CheckTexts()
        {
            var problems = Text.Strings.Problems();
            Check(problems.Count == 0, "italiano e inglese hanno le stesse chiavi e gli stessi segnaposto"
                                       + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));
            foreach (var tool in Tools.All)
            {
                Check(Text.Strings.Get($"tool.{Tools.Key(tool)}.short", "en") != $"tool.{Tools.Key(tool)}.short", $"testi dello strumento {tool}");
                Check(Tools.ShortName(tool).Length <= 13, $"nome corto per il cerchio: {Tools.ShortName(tool)}");
            }
        }

        private static void CheckStartOptions()
        {
            var o = StartOptions.Parse(new[] { "--tool", "merge", "--radial" });
            Check(o.Tool == PdfTool.Merge && o.Radial && o.Files.Count == 0, "--tool merge --radial");
            Check(StartOptions.Parse(new[] { "--tool", "pdfToImages" }).Tool == PdfTool.PdfToImages, "--tool pdfToImages");
            Check(StartOptions.Parse(new[] { "C:\\non-esiste.pdf" }).Files.Count == 0, "i file che non esistono si ignorano");
        }

        private static void CheckWindows(string work)
        {
            AppBuilder.Configure<TestApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            var a = MakeSample(work, "Contratto.pdf", 3, "Contratto");
            var b = MakeSample(work, "Allegato.pdf", 2, "Allegato");
            var photo = Path.Combine(work, "Foto.jpg");
            using (var bmp = new SKBitmap(800, 600))
            {
                using (var c = new SKCanvas(bmp)) c.Clear(SKColors.CadetBlue);
                File.WriteAllBytes(photo, bmp.Encode(SKEncodedImageFormat.Jpeg, 85).ToArray());
            }

            foreach (var language in new[] { "it", "en" })
                foreach (var dark in new[] { false, true })
                {
                    Languages.Set(language);
                    DockTheme.SetAppearance(Application.Current!, dark ? Appearance.Dark : Appearance.Light);
                    string suffix = $"{language}-{(dark ? "scuro" : "chiaro")}";
                    var none = StartOptions.None with { Automatic = false };

                    var window = new MainWindow(none) { Width = 1100, Height = 720 };
                    window.Show();
                    Pump();
                    Save(window, "home-" + suffix);

                    // Unisci: due PDF, una pagina girata e una spostata
                    window.OpenTool(PdfTool.Merge);
                    var pages = (PagesWorkspace)window.Current!;
                    Wait(pages.AddFilesAsync(new[] { a, b }));
                    WaitFor(() => pages.Tiles.All(t => t.HasThumbnail));
                    var tiles = pages.Tiles.ToList();
                    Check(tiles.Count == 5, "5 pagine da 2 file");
                    pages.Click(tiles[1], Avalonia.Input.KeyModifiers.None);
                    pages.Rotate(tiles[1], 90);
                    pages.Move(tiles[4], 0);
                    var order = pages.Tiles.Select(t => (t.Source.Name, t.Index, t.Rotation)).ToList();
                    Check(order[0] == ("Allegato.pdf", 1, 0) && order[2] == ("Contratto.pdf", 1, 90), "ordine e rotazione dopo le modifiche");
                    Pump();
                    Save(window, "unisci-" + suffix);

                    window.OpenTool(PdfTool.Split, new[] { a });
                    Pump();
                    WaitFor(() => window.Current is SplitWorkspace);
                    Pump(30);
                    Save(window, "dividi-" + suffix);

                    window.OpenTool(PdfTool.Protect, new[] { b });
                    Pump(30);
                    Save(window, "proteggi-" + suffix);

                    window.OpenTool(PdfTool.ImagesToPdf, new[] { photo, photo });
                    Pump(30);
                    Check(((ImagesWorkspace)window.Current!).Images.Count == 2, "due immagini in lista");
                    Save(window, "immagini-" + suffix);

                    window.OpenTool(PdfTool.PdfToImages, new[] { a });
                    Pump(30);
                    Save(window, "in-immagini-" + suffix);
                    window.ShowHome();
                    window.Close();

                    // Il cerchio degli strumenti, con uno spicchio illuminato
                    var radial = new RadialWindow();
                    radial.Show();
                    radial.Menu.Hover = 1;
                    Pump();
                    Save(radial, "cerchio-" + suffix);
                    radial.Menu.Hover = -1;
                    Pump();
                    Save(radial, "cerchio-centro-" + suffix);
                    radial.Close();
                }
            Languages.Set("it");
        }

        private static void Pump(int rounds = 10)
        {
            for (int i = 0; i < rounds; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(15);
            }
            Dispatcher.UIThread.RunJobs();
        }

        private static void Wait(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!task.IsCompleted && DateTime.UtcNow < until)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            Check(task.IsCompletedSuccessfully, "caricamento dei file" + (task.Exception != null ? ": " + task.Exception.GetBaseException().Message : ""));
        }

        private static void WaitFor(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!condition() && DateTime.UtcNow < until)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            Dispatcher.UIThread.RunJobs();
        }

        private static void Save(Avalonia.Controls.Window window, string name)
        {
            var frame = window.CaptureRenderedFrame();
            var file = Path.Combine(_images, name + ".png");
            frame?.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Check(frame != null && File.Exists(file), $"immagine {name}.png");
        }
    }
}
