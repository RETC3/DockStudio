using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace DockPDF
{
    /// <summary>Gli strumenti di DockPDF, nell'ordine della home e del cerchio.</summary>
    public enum PdfTool
    {
        Edit,
        Sign,
        Organize,
        Merge,
        Split,
        Compress,
        ImagesToPdf,
        PdfToImages,
        Protect,
        Unlock,
        Ocr,
    }

    /// <summary>Nome, descrizione e simbolo di ogni strumento.</summary>
    public static class Tools
    {
        public static readonly IReadOnlyList<PdfTool> All = Enum.GetValues<PdfTool>();

        /// <summary>Chiave dei testi: tool.&lt;chiave&gt;.name, .short, .description.</summary>
        public static string Key(PdfTool tool) => tool switch
        {
            PdfTool.ImagesToPdf => "imagesToPdf",
            PdfTool.PdfToImages => "pdfToImages",
            _ => tool.ToString().ToLowerInvariant(),
        };

        public static string Name(PdfTool tool) => Text.T($"tool.{Key(tool)}.name");

        /// <summary>Nome corto per il cerchio (al massimo due righe brevi).</summary>
        public static string ShortName(PdfTool tool) => Text.T($"tool.{Key(tool)}.short");

        public static string Description(PdfTool tool) => Text.T($"tool.{Key(tool)}.description");

        /// <summary>Strumenti non ancora pronti in questa versione di sviluppo.</summary>
        public static bool IsReady(PdfTool tool) => true;

        /// <summary>Lo strumento da un nome scritto sulla riga di comando ("--tool merge").</summary>
        public static PdfTool? Parse(string? name) =>
            All.Cast<PdfTool?>().FirstOrDefault(t => string.Equals(Key(t!.Value), name, StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(t!.Value.ToString(), name, StringComparison.OrdinalIgnoreCase));

        private static readonly Dictionary<PdfTool, Geometry> Icons = new();

        /// <summary>Simbolo dello strumento: linee su una griglia di 24×24, da disegnare a tratto.</summary>
        public static Geometry Icon(PdfTool tool)
        {
            if (Icons.TryGetValue(tool, out var g)) return g;
            g = Geometry.Parse(tool switch
            {
                PdfTool.Edit => "M4,20 V16 L15,5 L19,9 L8,20 Z M13,7 L17,11",
                PdfTool.Sign => "M3,16 C6,7 8,7 8,12 C8,16 11,8 13,11 C14,13 16,12 18,10 M3,20 H21",
                PdfTool.Organize => "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z",
                PdfTool.Merge => "M4,5 L10,12 M4,19 L10,12 M10,12 H20 M17,9 L20,12 L17,15",
                PdfTool.Split => "M4,12 H11 M11,12 L19,5 M11,12 L19,19 M15,5 H19 V9 M15,19 H19 V15",
                PdfTool.Compress => "M12,3 V10 M9,7 L12,10 L15,7 M12,21 V14 M9,17 L12,14 L15,17 M4,12 H20",
                PdfTool.ImagesToPdf => "M3,5 H21 V19 H3 Z M3,16 L8,11 L12,15 L15,12 L21,18 M16,8.5 A1.5,1.5 0 1 1 15.99,8.5",
                PdfTool.PdfToImages => "M13,21 H6 V3 H14 L18,7 V12 M14,3 V7 H18 M14,17 H22 M19,14 L22,17 L19,20",
                PdfTool.Protect => "M6,11 H18 V21 H6 Z M8,11 V8 A4,4 0 0 1 16,8 V11 M12,15 V17",
                PdfTool.Unlock => "M6,11 H18 V21 H6 Z M8,11 V8 A4,4 0 0 1 15.6,6.3 M12,15 V17",
                _ => "M4,8 V4 H8 M16,4 H20 V8 M20,16 V20 H16 M8,20 H4 V16 M8,9 H16 M12,9 V16",
            });
            Icons[tool] = g;
            return g;
        }
    }
}
