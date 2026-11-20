using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DockStudio.Core.Licensing
{
    /// <summary>Tipo di licenza. Senza file di licenza l'app è nella versione gratuita.</summary>
    public enum Edition
    {
        Free,
        Business,
    }

    /// <summary>
    /// Contenuto di una licenza: chi la usa, che tipo è e fino a quando vale. È firmato da DockStudio,
    /// quindi non si può modificare senza invalidarlo. Non contiene dati personali oltre al nome
    /// dell'intestatario (di solito un'azienda).
    /// </summary>
    public sealed record LicenseInfo
    {
        /// <summary>Codice della licenza, per riconoscerla (assistenza, revoca).</summary>
        public string Id { get; init; } = "";

        /// <summary>Intestatario, come appare nelle informazioni dell'app.</summary>
        public string Licensee { get; init; } = "";

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Edition Edition { get; init; } = Edition.Business;

        /// <summary>Numero di postazioni acquistate (informativo: offline non si può contare).</summary>
        public int Seats { get; init; } = 1;

        public DateTime Issued { get; init; }

        /// <summary>Ultimo giorno di validità (compreso), o null per una licenza senza scadenza.</summary>
        public DateTime? Expires { get; init; }

        /// <summary>App coperte (nomi come in AppIdentity.Name), o null per tutte le app della suite.</summary>
        public IReadOnlyList<string>? Apps { get; init; }

        public bool IsExpired(DateTime today) => Expires is { } end && today.Date > end.Date;

        public bool Covers(string appName) =>
            Apps == null || Apps.Count == 0 || Apps.Contains(appName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Esito della lettura di un file di licenza.</summary>
    public enum LicenseStatus
    {
        /// <summary>Nessun file di licenza: versione gratuita.</summary>
        None,
        Valid,
        /// <summary>File illeggibile o firma sbagliata (modificato a mano o non emesso da DockStudio).</summary>
        Invalid,
        Expired,
        /// <summary>Licenza valida, ma per altre app della suite.</summary>
        OtherApp,
    }

    public sealed record LicenseCheck(LicenseStatus Status, LicenseInfo? Info, string? Path)
    {
        public Edition Edition => Status == LicenseStatus.Valid && Info != null ? Info.Edition : Edition.Free;

        /// <summary>Descrizione breve per le informazioni dell'app, nella lingua in uso.</summary>
        public string Description => Status switch
        {
            LicenseStatus.Valid => DockStudio.Core.Localization.CoreText.Strings.Format("license.business", Info!.Licensee),
            LicenseStatus.Expired => DockStudio.Core.Localization.CoreText.Strings.Format("license.expired", Info!.Licensee, Info.Expires),
            LicenseStatus.Invalid => DockStudio.Core.Localization.CoreText.Strings["license.invalid"],
            LicenseStatus.OtherApp => DockStudio.Core.Localization.CoreText.Strings.Format("license.otherApp", Info!.Licensee),
            _ => DockStudio.Core.Localization.CoreText.Strings["license.free"],
        };
    }

    /// <summary>
    /// File di licenza (JSON): il contenuto codificato in base64 e la firma ECDSA P-256 con SHA-256.
    /// La chiave privata resta solo a chi emette le licenze; le app contengono la chiave pubblica.
    /// </summary>
    public static class LicenseFile
    {
        public const string FileName = "dockstudio.license";

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        private sealed record Envelope(int Format, string Payload, string Signature);

        /// <summary>Crea il testo del file di licenza firmandolo con la chiave privata (PEM).</summary>
        public static string Sign(LicenseInfo info, string privateKeyPem)
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(privateKeyPem);
            var payload = JsonSerializer.SerializeToUtf8Bytes(info, Json);
            var signature = key.SignData(payload, HashAlgorithmName.SHA256);
            return JsonSerializer.Serialize(
                new Envelope(1, Convert.ToBase64String(payload), Convert.ToBase64String(signature)),
                new JsonSerializerOptions(Json) { WriteIndented = true });
        }

        /// <summary>Legge e verifica il testo di un file di licenza. Null se è illeggibile o la firma non torna.</summary>
        public static LicenseInfo? Verify(string text, string publicKeyPem)
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<Envelope>(text, Json);
                if (envelope == null || envelope.Format != 1) return null;
                var payload = Convert.FromBase64String(envelope.Payload);
                var signature = Convert.FromBase64String(envelope.Signature);

                using var key = ECDsa.Create();
                key.ImportFromPem(publicKeyPem);
                if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256)) return null;

                var info = JsonSerializer.Deserialize<LicenseInfo>(payload, Json);
                return info is { Id.Length: > 0, Licensee.Length: > 0 } ? info : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Nuova coppia di chiavi (privata, pubblica) in formato PEM.</summary>
        public static (string PrivatePem, string PublicPem) CreateKeyPair()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
        }

        /// <summary>Codice casuale per una nuova licenza, leggibile al telefono (es. DS-7KQ2-M9XF-3TPA).</summary>
        public static string NewId()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // senza 0/O e 1/I
            var bytes = RandomNumberGenerator.GetBytes(12);
            var sb = new StringBuilder("DS");
            for (int i = 0; i < 12; i++)
            {
                if (i % 4 == 0) sb.Append('-');
                sb.Append(alphabet[bytes[i] % alphabet.Length]);
            }
            return sb.ToString();
        }
    }
}
