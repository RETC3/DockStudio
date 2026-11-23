using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using DockPDF.Engine;
using DockPDF.Views;

namespace DockPDF.Checks
{
    internal static partial class Program
    {
        /// <summary>L'editor con un modulo: aggiunte, testo corretto, campi compilati, salvataggio vero e finestre delle firme.</summary>
        private static void CheckEditor(MainWindow window, string work, string suffix)
        {
            SignatureStore.OverridePath = Path.Combine(work, "firme.json");
            var form = Path.Combine(work, "modulo-editor.pdf");
            File.Copy(MakeForm(work), form, overwrite: true);

            window.OpenTool(PdfTool.Edit);
            var editor = (EditorWorkspace)window.Current!;
            Wait(editor.LoadAsync(form, confirm: false));
            WaitFor(() => editor.Pages.First().HasImage);
            Check(editor.Pages.Count() == 1 && editor.Fields.Count == 3, "modulo aperto nell'editor con 3 campi");

            // modifiche come le farebbe l'utente
            editor.Add(new TextItem(0, new PageRect(330, 70, 220, 40), "Pratica n. 42 — urgente", FontFamilyKind.Sans, 14, Argb.FromRgb(0xD3, 0x2F, 0x2F), Bold: true));
            editor.Add(ShapeItem.Highlight(0, new PageRect(58, 60, 190, 26)));
            editor.Add(new LineItem(0, new PagePoint(320, 200), new PagePoint(420, 250), Argb.FromRgb(0x1E, 0x6F, 0xE0), 2, Arrow: true));
            var signature = new HandSignature
            {
                Strokes = new List<IReadOnlyList<PagePoint>> { new List<PagePoint> { new(0.02, 0.8), new(0.2, 0.1), new(0.4, 0.9), new(0.6, 0.2), new(0.98, 0.6) } },
                AspectRatio = 3,
            };
            editor.Add(new SignatureItem(0, new PageRect(330, 300, 180, 60), signature));
            var title = editor.BlocksOf(0).FirstOrDefault(b => b.Text == "Modulo di prova");
            Check(title != null, "riga \"Modulo di prova\" modificabile");
            if (title != null) editor.Add(new TextReplacement(0, title, "Domanda di iscrizione"));
            editor.SetField("nome", "Giulia Verdi");
            editor.SetField("accetto", "true");
            editor.Select(0);
            Pump(20);
            Save(window, "editor-" + suffix);

            editor.Undo();
            Check(!editor.FieldValues.TryGetValue("accetto", out var undone) || undone != "true", "annulla l'ultima modifica (la casella)");
            editor.SetField("accetto", "true");

            // salvataggio vero (senza finestra di scelta del file)
            var output = Path.Combine(work, $"modulo-editor-salvato-{suffix}.pdf");
            Wait(editor.SaveToAsync(output));
            Check(File.Exists(output), "PDF salvato");
            using (var saved = PdfFile.Open(output))
            {
                var text = saved.Text(0);
                Check(text.Contains("Pratica n. 42") && text.Contains("Domanda di iscrizione") && !text.Contains("Modulo di prova"), "testo aggiunto e testo corretto nel file");
                var fields = PdfForms.Read(output, null, saved);
                Check(fields.First(f => f.Name == "nome").Value == "Giulia Verdi" && fields.First(f => f.Name == "accetto").Value == "true", "modulo compilato nel file");
            }
            Check(editor.Items.Count == 0, "dopo il salvataggio si riparte dal file salvato");
            Pump(20);
            WaitFor(() => editor.Pages.First().HasImage);
            Save(window, "editor-salvato-" + suffix);

            // strumento "Modifica testo": blocchi evidenziati
            editor.SetTool(EditTool.EditText);
            Pump(10);
            Save(window, "editor-modifica-testo-" + suffix);
            editor.SetTool(EditTool.Select);

            // finestra della firma a mano, con un tratto disegnato
            var signWindow = new SignatureWindow();
            signWindow.Show();
            signWindow.Pad.AddStroke(new[] { new Point(40, 140), new Point(90, 60), new Point(130, 150), new Point(190, 70), new Point(260, 130), new Point(330, 90) });
            Pump();
            Check(signWindow.Result() is { Strokes.Count: 1, AspectRatio: > 2 }, "firma disegnata ritagliata attorno al tratto");
            Save(signWindow, "firma-a-mano-" + suffix);
            signWindow.Close();

            var digitalWindow = new DigitalSignWindow();
            digitalWindow.Show();
            Pump();
            Save(digitalWindow, "firma-digitale-" + suffix);
            digitalWindow.Close();

            window.ShowHome();
            SignatureStore.OverridePath = null;
        }
    }
}
