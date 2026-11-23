using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Fonts;

namespace DockPDF.Engine
{
    /// <summary>
    /// I caratteri che DockPDF usa per scrivere sui PDF, presi dal sistema e incorporati nel file: Arial, Times New
    /// Roman e Courier New (ci sono su Windows e su macOS), più un corsivo a mano per le firme (Segoe Script su
    /// Windows). Qualunque altro nome di famiglia diventa il carattere senza grazie.
    /// </summary>
    public sealed class PdfFonts : IFontResolver
    {
        private static readonly object Gate = new();
        private static bool _installed;

        /// <summary>Nomi di famiglia da usare con XFont.</summary>
        public static string FamilyName(FontFamilyKind kind) => kind switch
        {
            FontFamilyKind.Serif => "DockSerif",
            FontFamilyKind.Mono => "DockMono",
            FontFamilyKind.Script => "DockScript",
            _ => "DockSans",
        };

        /// <summary>Installa il risolutore (una volta sola per processo; PDFsharp non permette di cambiarlo dopo).</summary>
        public static void Install()
        {
            lock (Gate)
            {
                if (_installed) return;
                if (GlobalFontSettings.FontResolver is not PdfFonts) GlobalFontSettings.FontResolver = new PdfFonts();
                _installed = true;
            }
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            var kind = familyName switch
            {
                "DockSerif" => FontFamilyKind.Serif,
                "DockMono" => FontFamilyKind.Mono,
                "DockScript" => FontFamilyKind.Script,
                _ => familyName.Contains("Times", StringComparison.OrdinalIgnoreCase) ? FontFamilyKind.Serif
                    : familyName.Contains("Courier", StringComparison.OrdinalIgnoreCase) ? FontFamilyKind.Mono
                    : FontFamilyKind.Sans,
            };
            if (kind == FontFamilyKind.Script) isItalic = false;
            return new FontResolverInfo($"{kind}|{(isBold ? "b" : "")}{(isItalic ? "i" : "")}");
        }

        public byte[]? GetFont(string faceName)
        {
            var parts = faceName.Split('|');
            var kind = Enum.Parse<FontFamilyKind>(parts[0]);
            bool bold = parts[1].Contains('b'), italic = parts[1].Contains('i');
            foreach (var path in Candidates(kind, bold, italic))
                if (File.Exists(path)) return File.ReadAllBytes(path);
            // ultima risorsa: il carattere normale della stessa famiglia, poi Arial
            foreach (var path in Candidates(kind, false, false).Concat(Candidates(FontFamilyKind.Sans, false, false)))
                if (File.Exists(path)) return File.ReadAllBytes(path);
            return null;
        }

        private static IEnumerable<string> Candidates(FontFamilyKind kind, bool bold, bool italic)
        {
            string style = (bold, italic) switch { (true, true) => "bi", (true, false) => "bd", (false, true) => "i", _ => "" };
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrEmpty(windows))
            {
                switch (kind)
                {
                    case FontFamilyKind.Serif:
                        yield return Path.Combine(windows, "times" + (style == "bd" ? "bd" : style) + ".ttf");
                        break;
                    case FontFamilyKind.Mono:
                        yield return Path.Combine(windows, "cour" + style + ".ttf");
                        break;
                    case FontFamilyKind.Script:
                        yield return Path.Combine(windows, bold ? "segoescb.ttf" : "segoesc.ttf");
                        yield return Path.Combine(windows, "segoesc.ttf");
                        yield return Path.Combine(windows, "Inkfree.ttf");
                        yield return Path.Combine(windows, "LHANDW.TTF");
                        break;
                    default:
                        yield return Path.Combine(windows, "arial" + style + ".ttf");
                        break;
                }
            }
            // macOS
            const string mac = "/System/Library/Fonts/Supplemental";
            string macStyle = (bold, italic) switch { (true, true) => " Bold Italic", (true, false) => " Bold", (false, true) => " Italic", _ => "" };
            switch (kind)
            {
                case FontFamilyKind.Serif: yield return Path.Combine(mac, "Times New Roman" + macStyle + ".ttf"); break;
                case FontFamilyKind.Mono: yield return Path.Combine(mac, "Courier New" + macStyle + ".ttf"); break;
                case FontFamilyKind.Script: yield return Path.Combine(mac, "Brush Script.ttf"); break;
                default: yield return Path.Combine(mac, "Arial" + macStyle + ".ttf"); break;
            }
            // Linux (solo per prove)
            yield return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        }
    }
}
