using System;
using System.IO;
using System.Text.Json;

namespace DockStudio.Core
{
    /// <summary>
    /// Impostazioni salvate come JSON nella cartella dei dati dell'app (settings.json). Un file corrotto o
    /// illeggibile non blocca l'app: si riparte dai valori predefiniti; un errore di salvataggio viene ignorato.
    /// </summary>
    public sealed class JsonSettingsStore<T> where T : class, new()
    {
        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

        public JsonSettingsStore(string path) => FilePath = path;

        public JsonSettingsStore(AppIdentity app, string fileName = "settings.json")
            : this(Path.Combine(app.DataFolder, fileName)) { }

        public string FilePath { get; }

        public T Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath)) ?? new T();
            }
            catch
            {
                // file corrotto: si riparte dai valori predefiniti
            }
            return new T();
        }

        public void Save(T settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Indented));
            }
            catch
            {
                // non bloccare l'app per un errore di salvataggio delle impostazioni
            }
        }
    }
}
