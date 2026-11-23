using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.Signatures;

namespace DockPDF.Engine
{
    /// <summary>
    /// Richiesta di firma digitale. <paramref name="Box"/> (come si vede la pagina) rende la firma visibile su
    /// <paramref name="Page"/>; senza riquadro la firma è invisibile (si vede nel pannello delle firme dei lettori).
    /// </summary>
    public sealed record DigitalSignRequest(X509Certificate2 Certificate, string? Reason = null, string? Location = null,
        string? ContactInfo = null, int Page = 0, PageRect? Box = null);

    /// <summary>Esito della verifica di una firma digitale presente nel PDF.</summary>
    public sealed record SignatureCheck(string Signer, DateTime? SigningTime, bool Valid, bool CoversWholeFile, string SubFilter);

    /// <summary>Firma digitale dei PDF con un certificato (Windows: archivio personale, smart card e token; file .pfx/.p12).</summary>
    public static class DigitalSigning
    {
        /// <summary>
        /// I certificati personali con chiave privata adatti a firmare, validi oggi. Le smart card e i token (per
        /// esempio la CNS) compaiono qui quando sono inseriti e il loro driver è installato.
        /// </summary>
        public static IReadOnlyList<X509Certificate2> Certificates()
        {
            var list = new List<X509Certificate2>();
            try
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var now = DateTime.Now;
                foreach (var cert in store.Certificates)
                {
                    if (!cert.HasPrivateKey || cert.NotBefore > now || cert.NotAfter < now) continue;
                    if (IsSigningCertificate(cert)) list.Add(cert);
                }
            }
            catch (CryptographicException)
            {
                // archivio non disponibile (per esempio su macOS senza portachiavi accessibile)
            }
            catch (PlatformNotSupportedException)
            {
            }
            // Prima i certificati di firma "qualificata" (non ripudio), poi gli altri.
            return list.OrderByDescending(HasNonRepudiation).ThenByDescending(c => c.NotAfter).ToList();
        }

        private static bool HasNonRepudiation(X509Certificate2 cert) =>
            cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault() is { } u && (u.KeyUsages & X509KeyUsageFlags.NonRepudiation) != 0;

        /// <summary>
        /// Esclude i certificati che non servono a firmare documenti: quelli fatti da sé (per esempio "localhost"
        /// degli strumenti di sviluppo), quelli solo per i siti web e quelli tecnici di Windows.
        /// </summary>
        internal static bool IsSigningCertificate(X509Certificate2 cert)
        {
            var usage = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
            if (usage != null && (usage.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) == 0) return false;
            if (cert.SubjectName.RawData.AsSpan().SequenceEqual(cert.IssuerName.RawData)) return false;
            var issuer = cert.GetNameInfo(X509NameType.SimpleName, true) ?? "";
            if (issuer.StartsWith("MS-Organization-", StringComparison.OrdinalIgnoreCase)) return false;
            if (cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault() is { } eku)
            {
                var oids = eku.EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value).ToHashSet();
                // solo autenticazione dei server (siti web)
                if (oids.Count > 0 && oids.All(o => o == "1.3.6.1.5.5.7.3.1")) return false;
            }
            return true;
        }

        /// <summary>Apre un certificato da file .pfx/.p12 con la sua password.</summary>
        public static X509Certificate2 LoadPfx(string path, string password)
        {
            var cert = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
            if (!cert.HasPrivateKey) throw new CryptographicException("Il file non contiene la chiave privata.");
            return cert;
        }

        /// <summary>Nome leggibile del titolare (CN, altrimenti il soggetto intero).</summary>
        public static string DisplayName(X509Certificate2 cert)
        {
            var name = cert.GetNameInfo(X509NameType.SimpleName, false);
            return string.IsNullOrWhiteSpace(name) ? cert.Subject : name;
        }

        /// <summary>Prepara la firma: PDFsharp la calcola durante <see cref="PdfDocument.Save(string)"/>.</summary>
        internal static void Prepare(PdfDocument document, DigitalSignRequest request, byte[] currentBytes)
        {
            var signer = new PdfSharpDefaultSigner(request.Certificate, PdfMessageDigestType.SHA256, null);
            int pageIndex = Math.Clamp(request.Page, 0, Math.Max(0, document.PageCount - 1));
            XRect rectangle = default;
            IAnnotationAppearanceHandler? appearance = null;
            if (request.Box is { } box)
            {
                // PDFsharp vuole il riquadro nelle coordinate del PDF (origine in basso a sinistra).
                using var file = PdfFile.Open(currentBytes, "documento");
                var geometry = file.Geometry(pageIndex);
                var a = geometry.FromView(box.X, box.Y);
                var b = geometry.FromView(box.Right, box.Bottom);
                rectangle = new XRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
                appearance = new Appearance(DisplayName(request.Certificate), request.Reason, DateTime.Now);
            }
            var options = new DigitalSignatureOptions
            {
                AppName = "DockPDF",
                Reason = request.Reason ?? "",
                Location = request.Location ?? "",
                ContactInfo = request.ContactInfo ?? "",
                PageIndex = pageIndex,
                Rectangle = rectangle,
                AppearanceHandler = appearance,
            };
            DigitalSignatureHandler.ForDocument(document, signer, options);
        }

        /// <summary>Il disegno della firma visibile: nome del titolare, data e motivo.</summary>
        private sealed class Appearance : IAnnotationAppearanceHandler
        {
            private readonly string _name;
            private readonly string? _reason;
            private readonly DateTime _date;

            public Appearance(string name, string? reason, DateTime date)
            {
                _name = name;
                _reason = reason;
                _date = date;
            }

            public void DrawAppearance(XGraphics gfx, XRect position)
            {
                PdfFonts.Install();
                // Il riquadro arriva nelle coordinate della pagina, ma l'aspetto si disegna da (0, 0).
                var rect = new XRect(0, 0, position.Width, position.Height);
                var ink = new XSolidBrush(XColor.FromArgb(255, 27, 42, 107));
                gfx.DrawRectangle(new XPen(XColor.FromArgb(255, 27, 42, 107), 0.8), rect);
                double pad = Math.Min(6, rect.Height * 0.08);
                var inner = new XRect(rect.X + pad, rect.Y + pad, rect.Width - 2 * pad, rect.Height - 2 * pad);
                var lines = new List<(string Text, bool Strong)>
                {
                    ("Firmato digitalmente da", false),
                    (_name, true),
                    (_date.ToString("dd/MM/yyyy HH:mm"), false),
                };
                if (!string.IsNullOrWhiteSpace(_reason)) lines.Add((_reason!, false));
                double size = Math.Max(4, Math.Min(11, inner.Height / (lines.Count * 1.25)));
                double y = inner.Y;
                foreach (var (text, strong) in lines)
                {
                    var font = PdfEditor.Font(FontFamilyKind.Sans, strong ? size * 1.1 : size, bold: strong);
                    gfx.DrawString(text, font, ink, new XRect(inner.X, y, inner.Width, size * 1.3), XStringFormats.TopLeft);
                    y += size * (strong ? 1.4 : 1.25);
                }
            }
        }

        // ---- Verifica -------------------------------------------------------------------------------------

        /// <summary>Verifica crittografica delle firme presenti nel file (integrità e certificato del firmatario).</summary>
        public static IReadOnlyList<SignatureCheck> Verify(string path, string? password = null)
        {
            var bytes = File.ReadAllBytes(path);
            using var file = PdfFile.Open(bytes, Path.GetFileName(path), password);
            var result = new List<SignatureCheck>();
            foreach (var sig in file.Signatures())
            {
                string signer = "?";
                DateTime? time = null;
                bool valid = false, whole = false;
                try
                {
                    var range = sig.ByteRange;
                    using var signed = new MemoryStream();
                    for (int i = 0; i + 1 < range.Length; i += 2) signed.Write(bytes, range[i], range[i + 1]);
                    whole = range.Length >= 4 && range[2] + range[3] == bytes.Length;
                    var cms = new SignedCms(new ContentInfo(signed.ToArray()), detached: true);
                    cms.Decode(TrimZeros(sig.Contents));
                    var info = cms.SignerInfos[0];
                    signer = info.Certificate != null ? DisplayName(info.Certificate) : "?";
                    foreach (var attr in info.SignedAttributes)
                        if (attr.Oid.Value == "1.2.840.113549.1.9.5" && attr.Values.Count > 0)
                            time = new Pkcs9SigningTime(attr.Values[0].RawData).SigningTime;
                    cms.CheckSignature(verifySignatureOnly: true);
                    valid = true;
                }
                catch (CryptographicException)
                {
                    valid = false;
                }
                result.Add(new SignatureCheck(signer, time, valid, whole, sig.SubFilter));
            }
            return result;
        }

        /// <summary>Il contenuto della firma è riempito di zeri fino alla dimensione riservata.</summary>
        private static byte[] TrimZeros(byte[] contents)
        {
            int end = contents.Length;
            while (end > 0 && contents[end - 1] == 0) end--;
            // Il DER può finire con zeri veri: si lascia decidere al decodificatore partendo dalla lunghezza dichiarata.
            if (contents.Length > 4 && contents[0] == 0x30)
            {
                int length = contents[1] < 0x80 ? contents[1] + 2
                    : contents[1] == 0x81 ? contents[2] + 3
                    : contents[1] == 0x82 ? (contents[2] << 8 | contents[3]) + 4
                    : contents[1] == 0x83 ? (contents[2] << 16 | contents[3] << 8 | contents[4]) + 5
                    : end;
                if (length <= contents.Length) end = length;
            }
            return contents[..end];
        }
    }
}
