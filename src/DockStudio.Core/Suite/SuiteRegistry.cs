using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace DockStudio.Core.Suite
{
    /// <summary>Un'app della suite installata su questo computer.</summary>
    public sealed record SuiteApp
    {
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
        /// <summary>Percorso dell'eseguibile, per aprire l'app da un'altra della suite.</summary>
        public string ExePath { get; init; } = "";
        /// <summary>"windows" o "macos".</summary>
        public string Platform { get; init; } = "";
        public DateTime LastSeen { get; init; }
    }

    /// <summary>
    /// Registro delle app DockStudio dell'utente (apps.json nella cartella della suite). Ogni app si iscrive
    /// all'avvio; le altre lo leggono per mostrare i collegamenti tra le app ("Apri in DockPDF") e per
    /// coordinare gli aggiornamenti. Le voci il cui eseguibile non esiste più vengono ignorate.
    /// </summary>
    public sealed class SuiteRegistry
    {
        public const string FileName = "apps.json";

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        public SuiteRegistry(string? folder = null) => FilePath = Path.Combine(folder ?? SuitePaths.UserFolder, FileName);

        public string FilePath { get; }

        public static string CurrentPlatform =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
            : "linux";

        /// <summary>Le app registrate il cui eseguibile esiste ancora.</summary>
        public IReadOnlyList<SuiteApp> Installed() =>
            Read().Where(a => File.Exists(a.ExePath)).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

        public SuiteApp? Find(string name) =>
            Installed().FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Iscrive (o aggiorna) un'app. Non segnala errori: il registro non deve mai bloccare un'app.</summary>
        public bool Register(string name, Version version, string exePath)
        {
            var entry = new SuiteApp
            {
                Name = name,
                Version = version.ToString(3),
                ExePath = exePath,
                Platform = CurrentPlatform,
                LastSeen = DateTime.UtcNow,
            };
            return Update(apps =>
            {
                apps.RemoveAll(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
                apps.Add(entry);
            });
        }

        /// <summary>Toglie un'app (per esempio dal disinstallatore).</summary>
        public bool Unregister(string name) =>
            Update(apps => apps.RemoveAll(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)));

        private List<SuiteApp> Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<SuiteApp>();
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return Parse(stream);
            }
            catch
            {
                return new List<SuiteApp>();
            }
        }

        private static List<SuiteApp> Parse(Stream stream)
        {
            if (stream.Length == 0) return new List<SuiteApp>();
            try { return JsonSerializer.Deserialize<List<SuiteApp>>(stream, Json) ?? new List<SuiteApp>(); }
            catch (JsonException) { return new List<SuiteApp>(); } // file rovinato: si riparte da zero
        }

        /// <summary>Legge, modifica e riscrive il file tenendolo bloccato, così due app non si sovrascrivono.</summary>
        private bool Update(Action<List<SuiteApp>> change)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    var apps = Parse(stream);
                    change(apps);
                    stream.SetLength(0);
                    stream.Position = 0;
                    JsonSerializer.Serialize(stream, apps, Json);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(50); // un'altra app lo sta scrivendo
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }
    }
}
