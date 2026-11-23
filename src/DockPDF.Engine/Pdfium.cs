using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DockPDF.Engine
{
    /// <summary>
    /// Le poche funzioni di PDFium che servono a DockPDF. PDFium non è sicuro tra più thread: ogni chiamata
    /// passa da <see cref="Sync"/>.
    /// </summary>
    internal static unsafe class Pdfium
    {
        private const string Lib = "pdfium";

        public static readonly object Sync = new();

        // Errori di FPDF_GetLastError
        public const uint ErrPassword = 4;
        public const uint ErrFormat = 3;
        public const uint ErrFile = 2;

        // Opzioni di disegno
        public const int FPDF_ANNOT = 0x01;
        public const int FPDF_LCD_TEXT = 0x02;
        public const int FPDF_PRINTING = 0x800;

        public const int FPDFBitmap_BGRA = 4;

        // Opzioni di salvataggio
        public const uint FPDF_REMOVE_SECURITY = 3;

        static Pdfium()
        {
            lock (Sync) FPDF_InitLibrary();
        }

        /// <summary>Si chiama prima di tutto il resto, per inizializzare la libreria una sola volta.</summary>
        public static void EnsureLoaded() => RuntimeHelpers.RunClassConstructor(typeof(Pdfium).TypeHandle);

        [DllImport(Lib)] private static extern void FPDF_InitLibrary();
        [DllImport(Lib)] public static extern uint FPDF_GetLastError();

        [DllImport(Lib)] public static extern IntPtr FPDF_LoadMemDocument64(void* data, nuint size, byte* password);
        [DllImport(Lib)] public static extern void FPDF_CloseDocument(IntPtr document);
        [DllImport(Lib)] public static extern int FPDF_GetPageCount(IntPtr document);
        [DllImport(Lib)] public static extern int FPDF_GetSecurityHandlerRevision(IntPtr document);

        [DllImport(Lib)] public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
        [DllImport(Lib)] public static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Lib)] public static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Lib)] public static extern float FPDF_GetPageHeightF(IntPtr page);

        [DllImport(Lib)] public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, void* firstScan, int stride);
        [DllImport(Lib)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [DllImport(Lib)] public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
        [DllImport(Lib)] public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

        [DllImport(Lib)] public static extern IntPtr FPDFText_LoadPage(IntPtr page);
        [DllImport(Lib)] public static extern void FPDFText_ClosePage(IntPtr textPage);
        [DllImport(Lib)] public static extern int FPDFText_CountChars(IntPtr textPage);
        [DllImport(Lib)] public static extern int FPDFText_GetText(IntPtr textPage, int start, int count, ushort* result);

        [DllImport(Lib)] public static extern int FPDF_SaveAsCopy(IntPtr document, FileWrite* writer, uint flags);

        /// <summary>
        /// FPDF_FILEWRITE di PDFium più, in coda, il riferimento allo stream di destinazione: PDFium passa
        /// indietro il puntatore alla struttura, da cui si ritrova lo stream.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FileWrite
        {
            public int Version;
            public delegate* unmanaged<FileWrite*, void*, CULong, int> WriteBlock;
            public IntPtr Stream;
        }

        [UnmanagedCallersOnly]
        private static int WriteBlock(FileWrite* self, void* data, CULong size)
        {
            try
            {
                var stream = (Stream)GCHandle.FromIntPtr(self->Stream).Target!;
                stream.Write(new ReadOnlySpan<byte>(data, checked((int)size.Value)));
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Salva il documento in <paramref name="output"/> con le opzioni di FPDF_SaveAsCopy.</summary>
        public static bool SaveAsCopy(IntPtr document, Stream output, uint flags)
        {
            var handle = GCHandle.Alloc(output);
            try
            {
                var writer = new FileWrite
                {
                    Version = 1,
                    WriteBlock = &WriteBlock,
                    Stream = GCHandle.ToIntPtr(handle),
                };
                return FPDF_SaveAsCopy(document, &writer, flags) != 0;
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
