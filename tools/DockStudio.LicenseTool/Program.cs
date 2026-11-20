using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DockStudio.Core.Licensing;

// Uso:
//   dslicense chiavi <cartella>
//       crea licenze-privata.pem e licenze-pubblica.pem nella cartella (che deve stare FUORI dal repository)
//   dslicense emetti --chiave <privata.pem> --intestatario "Nome Srl" [--postazioni 10]
//                    [--scadenza 2027-12-31] [--app ScreenLove,DockPDF] [--uscita dockstudio.license]
//   dslicense verifica <file> [--pubblica <pubblica.pem>]

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static Dictionary<string, string> Options(IEnumerable<string> args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    string? name = null;
    foreach (var a in args)
    {
        if (a.StartsWith("--")) { name = a[2..]; result[name] = ""; }
        else if (name != null) { result[name] = a; name = null; }
    }
    return result;
}

if (args.Length == 0)
    return Fail("Comandi: chiavi <cartella> | emetti --chiave <pem> --intestatario <nome> ... | verifica <file>");

switch (args[0].ToLowerInvariant())
{
    case "chiavi":
    {
        if (args.Length < 2) return Fail("Indica la cartella dove salvare le chiavi.");
        var folder = Path.GetFullPath(args[1]);
        var privatePath = Path.Combine(folder, "licenze-privata.pem");
        if (File.Exists(privatePath)) return Fail($"Esiste già {privatePath}: non la sovrascrivo.");
        Directory.CreateDirectory(folder);
        var (priv, pub) = LicenseFile.CreateKeyPair();
        File.WriteAllText(privatePath, priv);
        File.WriteAllText(Path.Combine(folder, "licenze-pubblica.pem"), pub);
        Console.WriteLine($"Chiavi create in {folder}.");
        Console.WriteLine("Copia la chiave pubblica in src/DockStudio.Core/Licensing/LicenseKeys.cs.");
        Console.WriteLine("Fai una copia di sicurezza della chiave privata: se si perde, le licenze già vendute restano valide ma non se ne possono emettere altre con la stessa chiave.");
        return 0;
    }

    case "emetti":
    {
        var o = Options(args.Skip(1));
        if (!o.TryGetValue("chiave", out var keyPath) || !o.TryGetValue("intestatario", out var licensee) || licensee.Length == 0)
            return Fail("Servono --chiave <privata.pem> e --intestatario \"Nome\".");
        DateTime? expires = null;
        if (o.TryGetValue("scadenza", out var exp))
            expires = DateTime.ParseExact(exp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var info = new LicenseInfo
        {
            Id = LicenseFile.NewId(),
            Licensee = licensee,
            Edition = Edition.Business,
            Seats = o.TryGetValue("postazioni", out var seats) ? int.Parse(seats, CultureInfo.InvariantCulture) : 1,
            Issued = DateTime.UtcNow.Date,
            Expires = expires,
            Apps = o.TryGetValue("app", out var apps) ? apps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null,
        };
        var output = o.TryGetValue("uscita", out var outPath) && outPath.Length > 0 ? outPath : LicenseFile.FileName;
        File.WriteAllText(output, LicenseFile.Sign(info, File.ReadAllText(keyPath)));
        Console.WriteLine($"Licenza {info.Id} per \"{info.Licensee}\" salvata in {Path.GetFullPath(output)}.");
        return 0;
    }

    case "verifica":
    {
        if (args.Length < 2) return Fail("Indica il file di licenza.");
        var o = Options(args.Skip(2));
        var publicKey = o.TryGetValue("pubblica", out var pubPath) ? File.ReadAllText(pubPath) : LicenseKeys.PublicKeyPem;
        var info = LicenseFile.Verify(File.ReadAllText(args[1]), publicKey);
        if (info == null) return Fail("Licenza NON valida (firma sbagliata o file rovinato).");
        Console.WriteLine($"Valida: {info.Id}, {info.Licensee}, {info.Edition}, {info.Seats} postazioni, " +
                          $"scadenza {(info.Expires?.ToString("yyyy-MM-dd") ?? "nessuna")}, app {(info.Apps == null ? "tutte" : string.Join(", ", info.Apps))}.");
        return 0;
    }

    default:
        return Fail($"Comando sconosciuto: {args[0]}");
}
