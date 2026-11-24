using System;
using System.Collections.Generic;

namespace DockPDF.Engine
{
    // Tutte le coordinate delle modifiche sono "come si vede la pagina": punti (1/72 di pollice), origine in alto a
    // sinistra, pagina già girata secondo la sua rotazione. È lo stesso spazio delle immagini di PdfFile.Render.

    /// <summary>Un punto sulla pagina, come la si vede.</summary>
    public readonly record struct PagePoint(double X, double Y);

    /// <summary>Un rettangolo sulla pagina, come la si vede.</summary>
    public readonly record struct PageRect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;

        public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;

        public static PageRect FromCorners(double x1, double y1, double x2, double y2) =>
            new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    /// <summary>Colore ARGB (0xAARRGGBB).</summary>
    public readonly record struct Argb(uint Value)
    {
        public byte A => (byte)(Value >> 24);
        public byte R => (byte)(Value >> 16);
        public byte G => (byte)(Value >> 8);
        public byte B => (byte)Value;

        public static Argb FromRgb(byte r, byte g, byte b, byte a = 255) => new((uint)(a << 24 | r << 16 | g << 8 | b));

        public static readonly Argb Black = new(0xFF000000);
        public static readonly Argb White = new(0xFFFFFFFF);
        public static readonly Argb Transparent = new(0);
    }

    /// <summary>Famiglie di caratteri disponibili per scrivere sui PDF (vedi <see cref="PdfFonts"/>).</summary>
    public enum FontFamilyKind
    {
        Sans,
        Serif,
        Mono,

        /// <summary>Corsivo a mano, per le firme scritte.</summary>
        Script,
    }

    /// <summary>Una modifica da applicare a una pagina.</summary>
    public abstract record EditItem(int Page);

    /// <summary>Testo nuovo. Il riquadro dà la posizione (in alto a sinistra) e la larghezza per andare a capo.</summary>
    public sealed record TextItem(int Page, PageRect Box, string Text, FontFamilyKind Family, double Size, Argb Color,
        bool Bold = false, bool Italic = false) : EditItem(Page);

    /// <summary>Rettangolo: bordo, riempimento o entrambi. Evidenziatore e bianchetto sono rettangoli.</summary>
    public sealed record ShapeItem(int Page, PageRect Box, Argb Stroke, Argb Fill, double StrokeWidth, bool Ellipse = false,
        bool Multiply = false) : EditItem(Page)
    {
        /// <summary>Evidenziatore: giallo trasparente che si "moltiplica" con il testo sotto (resta leggibile).</summary>
        public static ShapeItem Highlight(int page, PageRect box, Argb? color = null) =>
            new(page, box, Argb.Transparent, color ?? new Argb(0x80FFE14D), 0, Multiply: true);

        /// <summary>Bianchetto: copre quello che c'è sotto con un rettangolo pieno (di solito bianco).</summary>
        public static ShapeItem Whiteout(int page, PageRect box, Argb? color = null) =>
            new(page, box, Argb.Transparent, color ?? Argb.White, 0);
    }

    /// <summary>Linea o freccia.</summary>
    public sealed record LineItem(int Page, PagePoint From, PagePoint To, Argb Color, double Width, bool Arrow = false) : EditItem(Page);

    /// <summary>Tratti a mano libera (penna).</summary>
    public sealed record InkItem(int Page, IReadOnlyList<IReadOnlyList<PagePoint>> Strokes, Argb Color, double Width) : EditItem(Page);

    /// <summary>Immagine (PNG o JPEG) nel riquadro, senza deformarla.</summary>
    public sealed record ImageItem(int Page, PageRect Box, byte[] Data) : EditItem(Page);

    /// <summary>Firma disegnata, scritta o da immagine, nel riquadro.</summary>
    public sealed record SignatureItem(int Page, PageRect Box, HandSignature Signature) : EditItem(Page);

    /// <summary>
    /// Testo già nel PDF da cambiare: il blocco (<see cref="PdfTextBlock"/>) viene tolto e al suo posto si scrive il
    /// testo nuovo con un carattere simile.
    /// </summary>
    public sealed record TextReplacement(int Page, PdfTextBlock Block, string NewText) : EditItem(Page);

    /// <summary>
    /// Testo invisibile ma selezionabile e cercabile, allargato o stretto fino a occupare il riquadro: è il testo
    /// riconosciuto dall'OCR, messo sopra la parola nella scansione.
    /// </summary>
    public sealed record HiddenTextItem(int Page, PageRect Box, string Text) : EditItem(Page);

    /// <summary>
    /// Una firma fatta a mano: tratti (coordinate tra 0 e 1 rispetto a un riquadro <see cref="AspectRatio"/>:1),
    /// oppure un nome scritto in corsivo, oppure un'immagine PNG con lo sfondo trasparente.
    /// </summary>
    public sealed record HandSignature
    {
        public IReadOnlyList<IReadOnlyList<PagePoint>>? Strokes { get; init; }
        public string? TypedName { get; init; }
        public byte[]? Image { get; init; }
        public Argb Color { get; init; } = new(0xFF1B2A6B);

        /// <summary>Larghezza / altezza naturale della firma.</summary>
        public double AspectRatio { get; init; } = 3;

        /// <summary>Spessore del tratto rispetto all'altezza del riquadro.</summary>
        public double StrokeRatio { get; init; } = 0.035;
    }

    /// <summary>
    /// Un pezzo di testo già presente nella pagina (di solito una riga): da dove viene, dove si trova e com'è scritto.
    /// </summary>
    public sealed record PdfTextBlock(int Page, IReadOnlyList<int> Objects, PageRect Box, string Text, double Size,
        FontFamilyKind Family, bool Bold, bool Italic, Argb Color, double Baseline);

    /// <summary>Tipi di campo dei moduli PDF.</summary>
    public enum FormFieldKind
    {
        Text,
        CheckBox,
        Choice,
        Radio,
        Other,
    }

    /// <summary>Un riquadro di un campo su una pagina.</summary>
    public sealed record FieldWidget(int Page, PageRect Box, string? OnState);

    /// <summary>Un campo di un modulo PDF compilabile.</summary>
    public sealed record FormField(string Name, FormFieldKind Kind, string Value, IReadOnlyList<string> Options,
        IReadOnlyList<FieldWidget> Widgets, bool ReadOnly, bool Multiline);

    /// <summary>Tutte le modifiche di un salvataggio.</summary>
    public sealed class EditSet
    {
        public List<EditItem> Items { get; } = new();

        /// <summary>Valori dei campi del modulo per nome completo; per le caselle "true"/"false", per i pulsanti d'opzione lo stato scelto.</summary>
        public Dictionary<string, string> Fields { get; } = new(StringComparer.Ordinal);

        public bool IsEmpty => Items.Count == 0 && Fields.Count == 0;
    }
}
