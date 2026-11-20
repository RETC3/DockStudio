using System;
using System.Collections.Generic;
using System.IO;
using DockStudio.Core.Suite;

namespace DockStudio.Core.Licensing
{
    /// <summary>
    /// Trova e verifica la licenza della suite. Il file si cerca prima nella cartella dell'utente, poi in
    /// quella del computer (installazione aziendale); vince la prima licenza valida per l'app.
    /// Nessun account e nessuna connessione: tutto si verifica con la chiave pubblica.
    /// </summary>
    public sealed class LicenseManager
    {
        private readonly string _appName;
        private readonly string _publicKeyPem;
        private readonly Func<DateTime> _today;

        public LicenseManager(string appName, string? publicKeyPem = null, Func<DateTime>? today = null)
        {
            _appName = appName;
            _publicKeyPem = publicKeyPem ?? LicenseKeys.PublicKeyPem;
            _today = today ?? (() => DateTime.Today);
        }

        public static IEnumerable<string> Locations
        {
            get
            {
                yield return Path.Combine(SuitePaths.UserFolder, LicenseFile.FileName);
                if (SuitePaths.Override == null)
                    yield return Path.Combine(SuitePaths.MachineFolder, LicenseFile.FileName);
            }
        }

        /// <summary>La licenza in uso. Se ci sono solo licenze non valide, restituisce l'esito della prima.</summary>
        public LicenseCheck Check()
        {
            LicenseCheck? first = null;
            foreach (var path in Locations)
            {
                if (!File.Exists(path)) continue;
                LicenseCheck result;
                try { result = Evaluate(File.ReadAllText(path), path); }
                catch { result = new LicenseCheck(LicenseStatus.Invalid, null, path); }
                if (result.Status == LicenseStatus.Valid) return result;
                first ??= result;
            }
            return first ?? new LicenseCheck(LicenseStatus.None, null, null);
        }

        public LicenseCheck Evaluate(string text, string? path = null)
        {
            var info = LicenseFile.Verify(text, _publicKeyPem);
            if (info == null) return new LicenseCheck(LicenseStatus.Invalid, null, path);
            if (!info.Covers(_appName)) return new LicenseCheck(LicenseStatus.OtherApp, info, path);
            if (info.IsExpired(_today())) return new LicenseCheck(LicenseStatus.Expired, info, path);
            return new LicenseCheck(LicenseStatus.Valid, info, path);
        }

        /// <summary>
        /// Installa una licenza per l'utente corrente (per esempio scelta dall'utente con "Inserisci licenza").
        /// La verifica prima: un file non valido non viene copiato.
        /// </summary>
        public LicenseCheck Install(string text)
        {
            var target = Path.Combine(SuitePaths.UserFolder, LicenseFile.FileName);
            var check = Evaluate(text, target);
            if (check.Status is LicenseStatus.Valid or LicenseStatus.Expired)
            {
                Directory.CreateDirectory(SuitePaths.UserFolder);
                File.WriteAllText(target, text);
            }
            return check;
        }
    }
}
