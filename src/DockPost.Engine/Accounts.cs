using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DockPost.Engine
{
    /// <summary>Come si protegge la connessione a un server.</summary>
    public enum Security
    {
        /// <summary>TLS dall'inizio (IMAP 993, SMTP 465).</summary>
        Tls,
        /// <summary>Connessione in chiaro che passa subito a TLS (IMAP 143, SMTP 587).</summary>
        StartTls,
        /// <summary>Nessuna protezione: solo per server di prova sul proprio computer.</summary>
        None,
    }

    /// <summary>Come si accede all'account.</summary>
    public enum AuthKind
    {
        /// <summary>Nome utente e password (per Gmail una password per le app).</summary>
        Password,
        /// <summary>Accesso con l'account Microsoft (OAuth 2.0): Outlook.com, Hotmail, Live, Microsoft 365.</summary>
        Microsoft,
    }

    public sealed record ServerSettings(string Host, int Port, Security Security)
    {
        public override string ToString() => $"{Host}:{Port} ({Security})";
    }

    /// <summary>
    /// Un account di posta. Le password e i token non stanno mai qui: li tiene l'archivio delle credenziali del
    /// sistema (<see cref="ISecretStore"/>), con la chiave <see cref="SecretKey"/>.
    /// </summary>
    public sealed class Account
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Nome mostrato ai destinatari ("Mario Rossi").</summary>
        public string DisplayName { get; set; } = "";

        public string Email { get; set; } = "";

        /// <summary>Nome utente per IMAP e SMTP: quasi sempre l'indirizzo.</summary>
        public string UserName { get; set; } = "";

        public ServerSettings Imap { get; set; } = new("", 993, Security.Tls);
        public ServerSettings Smtp { get; set; } = new("", 465, Security.Tls);
        public AuthKind Auth { get; set; } = AuthKind.Password;

        /// <summary>Provider riconosciuto ("gmail", "microsoft", ...) o null.</summary>
        public string? ProviderId { get; set; }

        /// <summary>
        /// True se il server mette da sé nella cartella Inviati una copia di quello che si invia (Gmail, Microsoft):
        /// allora DockPost non ne carica un'altra.
        /// </summary>
        public bool ServerSavesSent { get; set; }

        /// <summary>Firma aggiunta ai nuovi messaggi, o vuota.</summary>
        public string Signature { get; set; } = "";

        /// <summary>Quanti giorni di posta scaricare la prima volta per ogni cartella (0 = tutta).</summary>
        public int DaysToKeep { get; set; } = 90;

        [JsonIgnore]
        public string SecretKey => "DockPost/" + Id;

        [JsonIgnore]
        public string Title => string.IsNullOrWhiteSpace(DisplayName) ? Email : $"{DisplayName} <{Email}>";
    }

    /// <summary>Elenco degli account salvato in accounts.json (senza segreti).</summary>
    public sealed class AccountList
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly string _path;
        private readonly List<Account> _items = new();

        public AccountList(string path)
        {
            _path = path;
            try
            {
                if (File.Exists(path))
                    _items.AddRange(JsonSerializer.Deserialize<List<Account>>(File.ReadAllText(path), Options) ?? new());
            }
            catch
            {
                // file illeggibile: lo si tiene da parte e si riparte senza account, invece di perderlo
                try { File.Copy(path, path + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: false); } catch { }
            }
        }

        public IReadOnlyList<Account> Items => _items;

        public Account? Find(string id) => _items.FirstOrDefault(a => a.Id == id);

        public void Add(Account account)
        {
            _items.RemoveAll(a => a.Id == account.Id);
            _items.Add(account);
            Save();
        }

        public void Remove(string id)
        {
            _items.RemoveAll(a => a.Id == id);
            Save();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_items, Options));
            File.Move(temp, _path, overwrite: true);
        }
    }

    /// <summary>Dove stanno password e token: il Gestore credenziali di Windows, il Portachiavi del Mac...</summary>
    public interface ISecretStore
    {
        string? Read(string key);
        void Write(string key, string secret);
        void Delete(string key);
    }

    /// <summary>Segreti solo in memoria: per i controlli e quando l'utente non vuole ricordarli.</summary>
    public sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _items = new();
        public string? Read(string key) => _items.TryGetValue(key, out var v) ? v : null;
        public void Write(string key, string secret) => _items[key] = secret;
        public void Delete(string key) => _items.Remove(key);
    }
}
