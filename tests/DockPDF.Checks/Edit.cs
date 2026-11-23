using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DockPDF.Engine;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using SkiaSharp;

namespace DockPDF.Checks
{
    internal static partial class Program
    {
        static partial void RunEditChecks(string work)
        {
            Section("Coordinate delle pagine girate", CheckGeometry);
            Section("Aggiunte sulle pagine (anche girate)", () => CheckOverlay(work));
            Section("Testo nuovo", () => CheckNewText(work));
            Section("Modifica del testo esistente", () => CheckReplaceText(work));
            Section("Firma a mano", () => CheckHandSignature(work));
            Section("Moduli compilabili", () => CheckForms(work));
            Section("Firma digitale", () => CheckDigitalSignature(work));
        }

        private static bool IsInk(SKColor c) => c.Red < 200 || c.Green < 200 || c.Blue < 200;

        private static void CheckGeometry()
        {
            foreach (int rotation in new[] { 0, 90, 180, 270 })
            {
                var g = new PageGeometry(10, 20, 610, 820, rotation);
                var view = g.ToView(100, 700);
                var back = g.FromView(view.X, view.Y);
                bool ok = Math.Abs(back.X - 100) < 1e-6 && Math.Abs(back.Y - 700) < 1e-6;
                // La matrice per disegnare deve portare lo stesso punto della vista nello spazio non girato (dall'alto).
                var (a, b, c, d, e, f) = g.ViewToUnrotatedTopDown();
                double ux = a * view.X + c * view.Y + e, uy = b * view.X + d * view.Y + f;
                ok &= Math.Abs(ux - 90) < 1e-6 && Math.Abs(uy - 120) < 1e-6;
                Check(ok, $"rotazione {rotation}: andata e ritorno, matrice di disegno");
            }
        }

        /// <summary>PDF con quattro pagine A4 girate di 0, 90, 180 e 270 gradi, con un segno in alto a sinistra (non girato).</summary>
        private static string MakeRotated(string folder)
        {
            var path = Path.Combine(folder, "girate.pdf");
            using var doc = new PdfDocument();
            foreach (int rotation in new[] { 0, 90, 180, 270 })
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(595);
                page.Height = XUnit.FromPoint(842);
                using (var gfx = XGraphics.FromPdfPage(page))
                    gfx.DrawString($"Rotazione {rotation}", new XFont("Arial", 20), XBrushes.Black, new XPoint(60, 100));
                page.Elements.SetInteger("/Rotate", rotation);
            }
            doc.Save(path);
            return path;
        }

        private static void CheckOverlay(string work)
        {
            var path = MakeRotated(work);
            var edits = new EditSet();
            for (int p = 0; p < 4; p++)
            {
                edits.Items.Add(new ShapeItem(p, new PageRect(50, 60, 40, 30), Argb.Transparent, Argb.FromRgb(220, 30, 30), 0));
                edits.Items.Add(ShapeItem.Highlight(p, new PageRect(200, 300, 100, 20)));
                edits.Items.Add(new LineItem(p, new PagePoint(100, 400), new PagePoint(200, 400), Argb.FromRgb(0, 0, 200), 3, Arrow: true));
            }
            var output = Path.Combine(work, "girate-modificate.pdf");
            PdfEditor.Apply(path, null, edits, output);
            using var file = PdfFile.Open(output);
            Check(file.PageCount == 4, "4 pagine");
            for (int p = 0; p < 4; p++)
            {
                var g = file.Geometry(p);
                using var bmp = file.Render(p, (int)Math.Round(g.ViewWidth));
                var red = bmp.GetPixel(70, 75);
                var outside = bmp.GetPixel(20, 20);
                var yellow = bmp.GetPixel(250, 310);
                var blue = bmp.GetPixel(150, 400);
                Check(red.Red > 180 && red.Green < 90 && outside == SKColors.White, $"pagina girata di {g.Rotation}°: rettangolo al suo posto ({red})");
                Check(yellow.Red > 230 && yellow.Blue < 200 && yellow.Blue > 60, $"pagina {g.Rotation}°: evidenziatore trasparente ({yellow})");
                Check(blue.Blue > 150 && blue.Red < 80, $"pagina {g.Rotation}°: linea ({blue})");
                if (p == 1) Save(bmp, "aggiunte-girata-90");
            }
            Check(file.Text(0).Contains("Rotazione 0"), "il testo originale resta");
        }

        private static void CheckNewText(string work)
        {
            var path = MakeSample(work, "testo-nuovo.pdf", 1);
            var edits = new EditSet();
            edits.Items.Add(new TextItem(0, new PageRect(60, 300, 400, 100), "Città: Perugia — àèìòù €", FontFamilyKind.Sans, 18, Argb.FromRgb(20, 20, 160)));
            edits.Items.Add(new TextItem(0, new PageRect(60, 400, 120, 200), "Una frase lunga che deve andare a capo da sola", FontFamilyKind.Serif, 14, Argb.Black, Bold: true));
            var output = Path.Combine(work, "testo-nuovo-fatto.pdf");
            PdfEditor.Apply(path, null, edits, output);
            using var file = PdfFile.Open(output);
            var text = file.Text(0);
            Check(text.Contains("Città: Perugia") && text.Contains("àèìòù €"), "testo con accenti ed euro: " + text.Replace("\r\n", " | "));
            Check(text.Contains("Una frase lunga") && text.Contains("\n"), "va a capo nella larghezza del riquadro");
            using var bmp = file.Render(0, 595);
            Save(bmp, "testo-nuovo");
        }

        private static void CheckReplaceText(string work)
        {
            var path = MakeSample(work, "da-correggere.pdf", 2);
            using (var file = PdfFile.Open(path))
            {
                var blocks = file.TextBlocks(0);
                var block = blocks.FirstOrDefault(b => b.Text == "Pagina 1");
                Check(block != null, "blocco \"Pagina 1\" trovato: " + string.Join(" | ", blocks.Select(b => b.Text)));
                if (block == null) return;
                Check(Math.Abs(block.Size - 40) < 2, $"dimensione del carattere 40: {block.Size:0.0}");
                Check(block.Bold, "grassetto riconosciuto");
                Check(Math.Abs(block.Baseline - 200) < 2, $"linea di base a 200: {block.Baseline:0.0}");
                Check(block.Box.X > 55 && block.Box.X < 65, $"inizio del blocco a 60: {block.Box.X:0.0}");

                var edits = new EditSet();
                edits.Items.Add(new TextReplacement(0, block, "Capitolo uno"));
                var output = Path.Combine(work, "corretto.pdf");
                PdfEditor.Apply(path, null, edits, output);
                using var done = PdfFile.Open(output);
                var text = done.Text(0);
                Check(text.Contains("Capitolo uno") && !text.Contains("Pagina 1"), "testo sostituito: " + text.Replace("\r\n", " | "));
                Check(done.Text(1).Contains("Pagina 2"), "l'altra pagina non cambia");
                // Il testo nuovo sta nell'oggetto delle aggiunte: posizione e dimensione si controllano sull'immagine.
                using (var check = done.Render(0, 595))
                {
                    int minX = 9999, minY = 9999, maxY = 0;
                    for (int y = 120; y < 260; y++)
                        for (int x = 0; x < 595; x++)
                            if (IsInk(check.GetPixel(x, y))) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
                    // "Capitolo uno": la C maiuscola arriva a circa 0,72 em sopra la base, la p scende di circa 0,2 em.
                    Check(Math.Abs(minX - 62) <= 4 && Math.Abs(minY - (200 - 0.72 * 40)) <= 4 && Math.Abs(maxY - (200 + 0.21 * 40)) <= 4,
                        $"stessa posizione e dimensione: inchiostro da x {minX}, y {minY}-{maxY}");
                }
                using var bmp = done.Render(0, 595);
                Check(bmp.GetPixel(100, 70).Red > 150 && bmp.GetPixel(100, 70).Green < 140, "il resto della pagina (riquadro rosso) resta");
                Save(bmp, "testo-sostituito");
            }
        }

        private static void CheckHandSignature(string work)
        {
            var path = MakeSample(work, "da-firmare.pdf", 1);
            var drawn = new HandSignature
            {
                Strokes = new List<IReadOnlyList<PagePoint>>
                {
                    new List<PagePoint> { new(0.05, 0.7), new(0.2, 0.2), new(0.35, 0.8), new(0.5, 0.3), new(0.7, 0.75), new(0.95, 0.4) },
                },
                AspectRatio = 3,
            };
            var typed = new HandSignature { TypedName = "Mario Rossi" };
            var edits = new EditSet();
            edits.Items.Add(new SignatureItem(0, new PageRect(60, 600, 180, 60), drawn));
            edits.Items.Add(new SignatureItem(0, new PageRect(300, 600, 200, 60), typed));
            var output = Path.Combine(work, "firmato-a-mano.pdf");
            PdfEditor.Apply(path, null, edits, output);
            using var file = PdfFile.Open(output);
            using var bmp = file.Render(0, 595);
            int Ink(int x0, int y0, int x1, int y1)
            {
                int n = 0;
                for (int y = y0; y < y1; y += 2)
                    for (int x = x0; x < x1; x += 2)
                        if (IsInk(bmp.GetPixel(x, y))) n++;
                return n;
            }
            Check(Ink(60, 600, 240, 660) > 40, "firma disegnata visibile");
            Check(Ink(300, 600, 500, 660) > 40, "firma scritta visibile");
            Check(Ink(60, 680, 500, 760) == 0, "niente fuori dai riquadri");
            Check(file.Text(0).Contains("Mario Rossi"), "il nome scritto è testo vero");
            Save(bmp, "firme-a-mano");
        }

        /// <summary>Un modulo con un campo di testo, una casella e un elenco a discesa.</summary>
        private static string MakeForm(string folder)
        {
            var path = Path.Combine(folder, "modulo.pdf");
            using var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(595);
            page.Height = XUnit.FromPoint(842);
            using (var gfx = XGraphics.FromPdfPage(page))
                gfx.DrawString("Modulo di prova", new XFont("Arial", 20), XBrushes.Black, new XPoint(60, 80));

            PdfDictionary AppearanceStream()
            {
                var stream = new PdfDictionary(doc);
                stream.Elements.SetName("/Type", "/XObject");
                stream.Elements.SetName("/Subtype", "/Form");
                stream.Elements["/BBox"] = new PdfArray(doc, new PdfInteger(0), new PdfInteger(0), new PdfInteger(14), new PdfInteger(14));
                stream.CreateStream(System.Text.Encoding.ASCII.GetBytes("0 0 0 rg 2 2 10 10 re f"));
                doc.Internals.AddObject(stream);
                return stream;
            }

            PdfDictionary Widget(string name, string type, double x1, double y1, double x2, double y2)
            {
                var w = new PdfDictionary(doc);
                w.Elements.SetName("/Type", "/Annot");
                w.Elements.SetName("/Subtype", "/Widget");
                w.Elements.SetName("/FT", type);
                w.Elements["/T"] = new PdfString(name);
                w.Elements["/Rect"] = new PdfArray(doc, new PdfReal(x1), new PdfReal(y1), new PdfReal(x2), new PdfReal(y2));
                w.Elements["/DA"] = new PdfString("/Helv 12 Tf 0 g");
                w.Elements.SetInteger("/F", 4);
                w.Elements["/P"] = page.Reference;
                doc.Internals.AddObject(w);
                return w;
            }

            var nome = Widget("nome", "/Tx", 100, 700, 300, 720);
            nome.Elements["/V"] = new PdfString("");
            var accetto = Widget("accetto", "/Btn", 100, 650, 114, 664);
            var ap = new PdfDictionary(doc);
            var normal = new PdfDictionary(doc);
            normal.Elements["/Si"] = AppearanceStream().Reference;
            normal.Elements["/Off"] = AppearanceStream().Reference;
            ap.Elements["/N"] = normal;
            accetto.Elements["/AP"] = ap;
            accetto.Elements.SetName("/V", "/Off");
            accetto.Elements.SetName("/AS", "/Off");
            var citta = Widget("citta", "/Ch", 100, 600, 300, 620);
            citta.Elements.SetInteger("/Ff", 1 << 17); // elenco a discesa
            citta.Elements["/Opt"] = new PdfArray(doc, new PdfString("Roma"), new PdfString("Perugia"), new PdfString("Torino"));
            // nome gerarchico: "indirizzo" è il genitore di "citta"
            var parent = new PdfDictionary(doc);
            parent.Elements["/T"] = new PdfString("indirizzo");
            parent.Elements["/Kids"] = new PdfArray(doc, citta.Reference!);
            doc.Internals.AddObject(parent);
            citta.Elements["/Parent"] = parent.Reference;

            page.Elements["/Annots"] = new PdfArray(doc, nome.Reference!, accetto.Reference!, citta.Reference!);
            var acro = new PdfDictionary(doc);
            acro.Elements["/Fields"] = new PdfArray(doc, nome.Reference!, accetto.Reference!, parent.Reference!);
            var dr = new PdfDictionary(doc);
            acro.Elements["/DR"] = dr;
            acro.Elements["/DA"] = new PdfString("/Helv 12 Tf 0 g");
            doc.Internals.AddObject(acro);
            doc.Internals.Catalog.Elements["/AcroForm"] = acro.Reference;
            doc.Save(path);
            return path;
        }

        private static void CheckForms(string work)
        {
            var path = MakeForm(work);
            IReadOnlyList<FormField> fields;
            using (var view = PdfFile.Open(path))
                fields = PdfForms.Read(path, null, view);
            Check(fields.Count == 3, "3 campi: " + string.Join(", ", fields.Select(f => $"{f.Name}:{f.Kind}")));
            var nome = fields.FirstOrDefault(f => f.Name == "nome");
            Check(nome != null && nome.Kind == FormFieldKind.Text, "campo di testo \"nome\"");
            if (nome != null)
            {
                var box = nome.Widgets[0].Box;
                Check(Math.Abs(box.X - 100) < 0.5 && Math.Abs(box.Y - 122) < 0.5 && Math.Abs(box.Width - 200) < 0.5, $"riquadro come si vede: {box}");
            }
            Check(fields.Any(f => f.Name == "accetto" && f.Kind == FormFieldKind.CheckBox && f.Value == "false" && f.Widgets[0].OnState == "Si"), "casella \"accetto\" con stato Si");
            Check(fields.Any(f => f.Name == "indirizzo.citta" && f.Kind == FormFieldKind.Choice && f.Options.SequenceEqual(new[] { "Roma", "Perugia", "Torino" })), "elenco con nome gerarchico e opzioni");

            var edits = new EditSet();
            edits.Fields["nome"] = "Lucia Bianchi àè";
            edits.Fields["accetto"] = "true";
            edits.Fields["indirizzo.citta"] = "Perugia";
            var output = Path.Combine(work, "modulo-compilato.pdf");
            PdfEditor.Apply(path, null, edits, output);
            using var done = PdfFile.Open(output);
            var again = PdfForms.Read(output, null, done);
            Check(again.First(f => f.Name == "nome").Value == "Lucia Bianchi àè", "valore di testo salvato");
            Check(again.First(f => f.Name == "accetto").Value == "true", "casella spuntata salvata");
            Check(again.First(f => f.Name == "indirizzo.citta").Value == "Perugia", "scelta salvata");
        }

        private static X509Certificate2 TestCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=Firma di prova DockPDF, O=DockStudio, C=IT", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
            using var cert = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
            // Copia con chiave esportabile, come un certificato da file .pfx.
            return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx, "prova"), "prova", X509KeyStorageFlags.EphemeralKeySet);
        }

        private static void CheckDigitalSignature(string work)
        {
            var path = MakeSample(work, "contratto-da-firmare.pdf", 2);
            using var cert = TestCertificate();
            Check(DigitalSigning.DisplayName(cert) == "Firma di prova DockPDF", "nome del titolare");

            // Invisibile, insieme ad altre modifiche
            var edits = new EditSet();
            edits.Items.Add(new TextItem(0, new PageRect(60, 300, 300, 40), "Letto e approvato", FontFamilyKind.Sans, 14, Argb.Black));
            var invisible = Path.Combine(work, "firmato-invisibile.pdf");
            PdfEditor.Apply(path, null, edits, invisible, new DigitalSignRequest(cert, Reason: "Approvazione", Location: "Perugia"));
            var checks = DigitalSigning.Verify(invisible);
            Check(checks.Count == 1, "una firma nel file");
            if (checks.Count == 1)
            {
                var c = checks[0];
                Check(c.Valid, "firma valida");
                Check(c.CoversWholeFile, "la firma copre tutto il file");
                Check(c.Signer == "Firma di prova DockPDF", "firmatario: " + c.Signer);
                Check(c.SubFilter.Length > 0, "tipo di firma: " + c.SubFilter);
            }
            using (var f = PdfFile.Open(invisible))
                Check(f.Text(0).Contains("Letto e approvato") && f.PageCount == 2, "le modifiche sono nel file firmato");

            // Un byte cambiato dentro l'area firmata: la verifica deve fallire.
            var bytes = File.ReadAllBytes(invisible);
            int at = Array.IndexOf(bytes, (byte)'L', 200);
            var tampered = Path.Combine(work, "firmato-alterato.pdf");
            bytes[Math.Max(at, 300)] ^= 0x01;
            File.WriteAllBytes(tampered, bytes);
            var bad = DigitalSigning.Verify(tampered);
            Check(bad.Count == 1 && !bad[0].Valid, "un file alterato dopo la firma non è più valido");

            // Visibile su una pagina
            var visible = Path.Combine(work, "firmato-visibile.pdf");
            PdfEditor.Apply(path, null, new EditSet(), visible, new DigitalSignRequest(cert, Reason: "Accettazione", Page: 1, Box: new PageRect(300, 700, 220, 70)));
            var vc = DigitalSigning.Verify(visible);
            Check(vc.Count == 1 && vc[0].Valid && vc[0].CoversWholeFile, "firma visibile valida");
            using (var f = PdfFile.Open(visible))
            {
                using var bmp = f.Render(1, 595);
                int ink = 0;
                for (int y = 700; y < 770; y += 2)
                    for (int x = 300; x < 520; x += 2)
                        if (IsInk(bmp.GetPixel(x, y))) ink++;
                int outside = 0;
                for (int y = 600; y < 690; y += 2)
                    for (int x = 300; x < 520; x += 2)
                        if (IsInk(bmp.GetPixel(x, y))) outside++;
                int minX = 9999, minY = 9999, maxX = 0, maxY = 0;
                for (int y = 250; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                        if (IsInk(bmp.GetPixel(x, y))) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                Check(ink > 50 && outside == 0, $"il riquadro della firma è dove deve essere ({ink} punti dentro, {outside} sopra; inchiostro in {minX},{minY}-{maxX},{maxY})");
                Save(bmp, "firma-digitale-visibile");
                using var p0 = f.Render(0, 595);
                Save(p0, "firma-digitale-pagina0");
            }
        }
    }
}
