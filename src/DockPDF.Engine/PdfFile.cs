using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DockStudio.Imaging;
using SkiaSharp;

namespace DockPDF.Engine
{
    /// <summary>Il PDF è protetto e la password manca o è sbagliata.</summary>
    public sealed class PdfPasswordException : Exception
    {
        public PdfPasswordException(string file, bool wrongPassword)
            : base(wrongPassword ? $"Password errata: {file}" : $"Serve la password: {file}")
        {
            File = file;
            WrongPassword = wrongPassword;
        }

        public string File { get; }

        /// <summary>True se era stata data una password, ma non è quella giusta.</summary>
        public bool WrongPassword { get; }
    }

    /// <summary>Il file non è un PDF o è rovinato.</summary>
    public sealed class PdfFormatException : Exception
    {
        public PdfFormatException(string file) : base($"PDF non leggibile: {file}") => File = file;

        public string File { get; }
    }

    /// <summary>
    /// Un PDF aperto con PDFium per guardarlo: numero e dimensioni delle pagine, immagini delle pagine, testo.
    /// Il file si legge tutto in memoria, così resta libero (si può sovrascrivere mentre è aperto).
    /// </summary>
    public sealed unsafe partial class PdfFile : IDisposable
    {
        private readonly IntPtr _data;
        private IntPtr _document;

        // Ambiente dei moduli di PDFium: struttura FPDF_FORMFILLINFO (versione 1, senza richiami) e handle.
        private IntPtr _formInfo;
        private IntPtr _form;

        private PdfFile(string name, IntPtr data, IntPtr document, bool encrypted)
        {
            Name = name;
            _data = data;
            _document = document;
            IsEncrypted = encrypted;
            lock (Pdfium.Sync)
            {
                PageCount = Pdfium.FPDF_GetPageCount(document);
                // Senza l'ambiente dei moduli PDFium non disegna i campi (valori compilati, firme visibili).
                const int size = 64 * 8;
                _formInfo = Marshal.AllocHGlobal(size);
                new Span<byte>((void*)_formInfo, size).Clear();
                *(int*)_formInfo = 1;
                _form = Pdfium.FPDFDOC_InitFormFillEnvironment(document, (void*)_formInfo);
            }
        }

        /// <summary>Nome del file (per i messaggi).</summary>
        public string Name { get; }

        public int PageCount { get; }

        /// <summary>True se il PDF è protetto da password (anche solo per i permessi).</summary>
        public bool IsEncrypted { get; }

        public static PdfFile Open(string path, string? password = null) =>
            Open(File.ReadAllBytes(path), Path.GetFileName(path), password);

        public static PdfFile Open(byte[] bytes, string name, string? password = null)
        {
            Pdfium.EnsureLoaded();
            IntPtr data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
            try
            {
                Marshal.Copy(bytes, 0, data, bytes.Length);
                byte[]? pwd = password == null ? null : Encoding.UTF8.GetBytes(password + "\0");
                IntPtr document;
                uint error;
                lock (Pdfium.Sync)
                {
                    fixed (byte* p = pwd)
                        document = Pdfium.FPDF_LoadMemDocument64((void*)data, (nuint)bytes.Length, p);
                    error = document == IntPtr.Zero ? Pdfium.FPDF_GetLastError() : 0;
                }
                if (document == IntPtr.Zero)
                {
                    if (error == Pdfium.ErrPassword) throw new PdfPasswordException(name, !string.IsNullOrEmpty(password));
                    throw new PdfFormatException(name);
                }
                bool encrypted;
                lock (Pdfium.Sync) encrypted = Pdfium.FPDF_GetSecurityHandlerRevision(document) >= 0;
                return new PdfFile(name, data, document, encrypted);
            }
            catch
            {
                Marshal.FreeHGlobal(data);
                throw;
            }
        }

        /// <summary>True se per aprire il file serve una password.</summary>
        public static bool NeedsPassword(string path)
        {
            try
            {
                using var _ = Open(path);
                return false;
            }
            catch (PdfPasswordException)
            {
                return true;
            }
        }

        /// <summary>Dimensioni della pagina in punti (1/72 di pollice), già girate secondo la rotazione della pagina.</summary>
        public (double Width, double Height) PageSize(int index)
        {
            lock (Pdfium.Sync)
            {
                var page = LoadPage(index);
                try { return (Pdfium.FPDF_GetPageWidthF(page), Pdfium.FPDF_GetPageHeightF(page)); }
                finally { Pdfium.FPDF_ClosePage(page); }
            }
        }

        /// <summary>
        /// Disegna la pagina su fondo bianco, larga <paramref name="width"/> pixel (l'altezza segue le proporzioni).
        /// <paramref name="rotation"/>: rotazione in più, in gradi multipli di 90 (per le anteprime).
        /// </summary>
        public SKBitmap Render(int index, int width, int rotation = 0, bool forPrinting = false)
        {
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
            int quarter = (((rotation / 90) % 4) + 4) % 4;
            lock (Pdfium.Sync)
            {
                var page = LoadPage(index);
                try
                {
                    double w = Pdfium.FPDF_GetPageWidthF(page), h = Pdfium.FPDF_GetPageHeightF(page);
                    if (quarter % 2 == 1) (w, h) = (h, w);
                    int height = Math.Max(1, (int)Math.Round(width * h / w));
                    var bitmap = Pixels.Create(width, height);
                    var target = Pdfium.FPDFBitmap_CreateEx(width, height, Pdfium.FPDFBitmap_BGRA, (void*)bitmap.GetPixels(), bitmap.RowBytes);
                    try
                    {
                        Pdfium.FPDFBitmap_FillRect(target, 0, 0, width, height, 0xFFFFFFFF);
                        int flags = Pdfium.FPDF_ANNOT | (forPrinting ? Pdfium.FPDF_PRINTING : 0);
                        Pdfium.FPDF_RenderPageBitmap(target, page, 0, 0, width, height, quarter, flags);
                        if (_form != IntPtr.Zero)
                        {
                            Pdfium.FORM_OnAfterLoadPage(page, _form);
                            Pdfium.FPDF_FFLDraw(_form, target, page, 0, 0, width, height, quarter, flags);
                            Pdfium.FORM_OnBeforeClosePage(page, _form);
                        }
                    }
                    finally
                    {
                        Pdfium.FPDFBitmap_Destroy(target);
                    }
                    bitmap.NotifyPixelsChanged();
                    return bitmap;
                }
                finally
                {
                    Pdfium.FPDF_ClosePage(page);
                }
            }
        }

        /// <summary>Disegna la pagina alla risoluzione indicata (punti per pollice).</summary>
        public SKBitmap RenderAtDpi(int index, int dpi)
        {
            var (w, _) = PageSize(index);
            return Render(index, Math.Max(1, (int)Math.Round(w * dpi / 72.0)), forPrinting: true);
        }

        /// <summary>Il testo della pagina, se il PDF ne contiene (le scansioni senza OCR non ne hanno).</summary>
        public string Text(int index)
        {
            lock (Pdfium.Sync)
            {
                var page = LoadPage(index);
                var text = Pdfium.FPDFText_LoadPage(page);
                try
                {
                    if (text == IntPtr.Zero) return "";
                    int count = Pdfium.FPDFText_CountChars(text);
                    if (count <= 0) return "";
                    var buffer = new ushort[count + 1];
                    fixed (ushort* b = buffer)
                    {
                        int written = Pdfium.FPDFText_GetText(text, 0, count, b);
                        return new string((char*)b, 0, Math.Max(0, written - 1));
                    }
                }
                finally
                {
                    if (text != IntPtr.Zero) Pdfium.FPDFText_ClosePage(text);
                    Pdfium.FPDF_ClosePage(page);
                }
            }
        }

        /// <summary>True se nella pagina c'è testo vero (non solo immagini).</summary>
        public bool HasText(int index) => Text(index).Trim().Length > 0;

        /// <summary>Scrive una copia del PDF senza password né limiti (serve averlo aperto con la password giusta).</summary>
        public void SaveWithoutSecurity(Stream output)
        {
            bool ok;
            lock (Pdfium.Sync) ok = Pdfium.SaveAsCopy(_document, output, Pdfium.FPDF_REMOVE_SECURITY);
            if (!ok) throw new IOException($"Salvataggio non riuscito: {Name}");
        }

        private IntPtr LoadPage(int index)
        {
            ObjectDisposedException.ThrowIf(_document == IntPtr.Zero, this);
            if ((uint)index >= (uint)PageCount) throw new ArgumentOutOfRangeException(nameof(index));
            var page = Pdfium.FPDF_LoadPage(_document, index);
            if (page == IntPtr.Zero) throw new PdfFormatException(Name);
            return page;
        }

        public void Dispose()
        {
            if (_document == IntPtr.Zero) return;
            Close();
            GC.SuppressFinalize(this);
        }

        ~PdfFile()
        {
            if (_document != IntPtr.Zero) Close();
        }

        private void Close()
        {
            lock (Pdfium.Sync)
            {
                if (_form != IntPtr.Zero) Pdfium.FPDFDOC_ExitFormFillEnvironment(_form);
                Pdfium.FPDF_CloseDocument(_document);
            }
            _form = IntPtr.Zero;
            _document = IntPtr.Zero;
            if (_formInfo != IntPtr.Zero) Marshal.FreeHGlobal(_formInfo);
            _formInfo = IntPtr.Zero;
            Marshal.FreeHGlobal(_data);
        }
    }
}
