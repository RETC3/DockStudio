using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace DockPDF.Engine
{
    /// <summary>
    /// I moduli PDF compilabili (AcroForm): lettura dei campi con i loro riquadri sulle pagine e scrittura dei valori.
    /// Si lavora sui dizionari del PDF, così vanno bene anche i moduli fatti in modo poco ordinato.
    /// </summary>
    public static class PdfForms
    {
        private const int FlagReadOnly = 1;
        private const int FlagMultiline = 1 << 12;
        private const int FlagRadio = 1 << 15;
        private const int FlagPushButton = 1 << 16;

        /// <summary>Un campo trovato nel PDF: il suo dizionario e quelli dei riquadri (widget), con la pagina.</summary>
        private sealed class Entry
        {
            public required string Name { get; init; }
            public required PdfDictionary Field { get; init; }
            public List<(int Page, PdfDictionary Widget)> Widgets { get; } = new();
        }

        /// <summary>I campi del modulo, nell'ordine delle pagine. Vuoto se il PDF non ha un modulo.</summary>
        public static IReadOnlyList<FormField> Read(string path, string? password, PdfFile view)
        {
            using var document = OpenForRead(path, password);
            var result = new List<FormField>();
            foreach (var entry in Collect(document))
            {
                var field = entry.Field;
                string type = Inherited(field, "/FT") is PdfName ft ? ft.Value : "";
                int flags = Inherited(field, "/Ff") is PdfInteger ff ? ff.Value : 0;
                var kind = type switch
                {
                    "/Tx" => FormFieldKind.Text,
                    "/Ch" => FormFieldKind.Choice,
                    "/Btn" when (flags & FlagPushButton) != 0 => FormFieldKind.Other,
                    "/Btn" when (flags & FlagRadio) != 0 => FormFieldKind.Radio,
                    "/Btn" => FormFieldKind.CheckBox,
                    _ => FormFieldKind.Other,
                };
                if (kind == FormFieldKind.Other) continue;
                string value = Inherited(field, "/V") switch
                {
                    PdfString s => s.Value,
                    PdfName n => kind == FormFieldKind.CheckBox ? (n.Value != "/Off" ? "true" : "false") : n.Value.TrimStart('/'),
                    PdfArray a when a.Elements.Count > 0 && a.Elements[0] is PdfString s0 => s0.Value,
                    _ => kind == FormFieldKind.CheckBox ? "false" : "",
                };
                var options = new List<string>();
                if (kind == FormFieldKind.Choice && Inherited(field, "/Opt") is PdfArray opt)
                    foreach (var o in opt.Elements)
                        options.Add(Resolve(o) switch
                        {
                            PdfString s => s.Value,
                            PdfArray pair when pair.Elements.Count > 1 && Resolve(pair.Elements[1]) is PdfString d => d.Value,
                            _ => "",
                        });
                var widgets = new List<FieldWidget>();
                foreach (var (page, widget) in entry.Widgets)
                {
                    if (widget.Elements.GetArray("/Rect") is not { Elements.Count: 4 } rect) continue;
                    double x1 = Number(rect.Elements[0]), y1 = Number(rect.Elements[1]), x2 = Number(rect.Elements[2]), y2 = Number(rect.Elements[3]);
                    var box = view.Geometry(page).ToView(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
                    widgets.Add(new FieldWidget(page, box, OnState(widget)));
                }
                if (widgets.Count == 0) continue;
                result.Add(new FormField(entry.Name, kind, value, options, widgets, (flags & FlagReadOnly) != 0, (flags & FlagMultiline) != 0));
            }
            return result;
        }

        /// <summary>Scrive i valori nei campi (per nome completo) e chiede ai lettori di ridisegnarli.</summary>
        public static void Fill(PdfDocument document, IReadOnlyDictionary<string, string> values)
        {
            var entries = Collect(document).ToDictionary(e => e.Name, StringComparer.Ordinal);
            foreach (var (name, value) in values)
            {
                if (!entries.TryGetValue(name, out var entry)) continue;
                var field = entry.Field;
                string type = Inherited(field, "/FT") is PdfName ft ? ft.Value : "";
                int flags = Inherited(field, "/Ff") is PdfInteger ff ? ff.Value : 0;
                if (type == "/Btn")
                {
                    string state;
                    if ((flags & FlagRadio) != 0) state = string.IsNullOrEmpty(value) ? "Off" : value;
                    else state = value == "true" ? entry.Widgets.Select(w => OnState(w.Widget)).FirstOrDefault(s => s != null) ?? "Yes" : "Off";
                    field.Elements.SetName("/V", "/" + state);
                    foreach (var (_, widget) in entry.Widgets)
                        widget.Elements.SetName("/AS", OnState(widget) == state ? "/" + state : "/Off");
                }
                else
                {
                    field.Elements["/V"] = new PdfString(value, IsAscii(value) ? PdfStringEncoding.PDFDocEncoding : PdfStringEncoding.Unicode);
                    // PDFsharp sa ridisegnare l'aspetto dei campi di testo; se non ci riesce, l'aspetto vecchio (che
                    // mostrerebbe ancora il valore di prima) si toglie e i lettori lo rifanno (NeedAppearances).
                    bool redrawn = false;
                    if (type == "/Tx")
                    {
                        try
                        {
                            if (document.AcroForm?.Fields[name] is PdfTextField text)
                            {
                                text.Text = value;
                                redrawn = true;
                            }
                        }
                        catch (Exception)
                        {
                            redrawn = false;
                        }
                    }
                    if (!redrawn)
                        foreach (var (_, widget) in entry.Widgets) widget.Elements.Remove("/AP");
                }
            }
            var acro = document.Internals.Catalog.Elements.GetDictionary("/AcroForm");
            acro?.Elements.SetBoolean("/NeedAppearances", true);
        }

        // ---- Lettura dei dizionari ------------------------------------------------------------------------

        private static PdfDocument OpenForRead(string path, string? password)
        {
            using var file = PdfFile.Open(path, password);
            if (!file.IsEncrypted) return PdfReader.Open(new MemoryStream(File.ReadAllBytes(path)), PdfDocumentOpenMode.Modify);
            var clear = new MemoryStream();
            file.SaveWithoutSecurity(clear);
            clear.Position = 0;
            return PdfReader.Open(clear, PdfDocumentOpenMode.Modify);
        }

        /// <summary>Tutti i campi con riquadri, cercati dalle annotazioni delle pagine.</summary>
        private static List<Entry> Collect(PdfDocument document)
        {
            var byName = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var order = new List<Entry>();
            for (int p = 0; p < document.PageCount; p++)
            {
                if (Resolve(document.Pages[p].Elements["/Annots"]) is not PdfArray annots) continue;
                foreach (var item in annots.Elements)
                {
                    if (Resolve(item) is not PdfDictionary widget) continue;
                    if (widget.Elements.GetName("/Subtype") != "/Widget") continue;
                    // Il campo è il widget stesso (se ha un nome) o il suo genitore.
                    var field = widget.Elements.ContainsKey("/T") ? widget : Resolve(widget.Elements["/Parent"]) as PdfDictionary;
                    if (field == null) continue;
                    var name = FullName(field);
                    if (name.Length == 0) continue;
                    if (!byName.TryGetValue(name, out var entry))
                    {
                        entry = new Entry { Name = name, Field = field };
                        byName[name] = entry;
                        order.Add(entry);
                    }
                    entry.Widgets.Add((p, widget));
                }
            }
            return order;
        }

        private static string FullName(PdfDictionary field)
        {
            var parts = new List<string>();
            for (var d = field; d != null && parts.Count < 32; d = Resolve(d.Elements["/Parent"]) as PdfDictionary)
                if (Resolve(d.Elements["/T"]) is PdfString t && t.Value.Length > 0) parts.Insert(0, t.Value);
            return string.Join(".", parts);
        }

        /// <summary>Un valore del campo o dei suoi genitori (FT, Ff, V e Opt si ereditano).</summary>
        private static PdfItem? Inherited(PdfDictionary field, string key)
        {
            for (int depth = 0; field != null && depth < 32; depth++)
            {
                if (field.Elements.ContainsKey(key)) return Resolve(field.Elements[key]);
                field = (Resolve(field.Elements["/Parent"]) as PdfDictionary)!;
            }
            return null;
        }

        /// <summary>Nome dello stato "acceso" di una casella o di un pulsante d'opzione (diverso da Off).</summary>
        private static string? OnState(PdfDictionary widget)
        {
            if (Resolve(widget.Elements["/AP"]) is not PdfDictionary ap) return null;
            if (Resolve(ap.Elements["/N"]) is not PdfDictionary normal) return null;
            return normal.Elements.Keys.Select(k => k.TrimStart('/')).FirstOrDefault(k => k != "Off");
        }

        private static PdfItem? Resolve(PdfItem? item) => item is PdfReference r ? r.Value : item;

        private static double Number(PdfItem item) => Resolve(item) switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            PdfLongInteger l => l.Value,
            _ => 0,
        };

        private static bool IsAscii(string s) => s.All(c => c < 128);
    }
}
