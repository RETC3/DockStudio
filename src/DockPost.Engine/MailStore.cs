using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace DockPost.Engine
{
    /// <summary>Tipo di cartella, per l'icona, l'ordine e le azioni (Elimina sposta nel Cestino...).</summary>
    public enum FolderKind
    {
        Inbox = 0,
        Drafts = 1,
        Sent = 2,
        Archive = 3,
        Junk = 4,
        Trash = 5,
        Other = 9,
    }

    public sealed record FolderInfo(long Id, string AccountId, string Path, string Name, FolderKind Kind,
        int Unread, int Total, uint UidValidity, uint LastUid, char Separator)
    {
        /// <summary>Profondità nell'albero (0 = cartella principale).</summary>
        public int Depth => Separator == '\0' ? 0 : Path.Count(c => c == Separator);
    }

    /// <summary>Intestazione di un messaggio come arriva dal server, prima di salvarla.</summary>
    public sealed record MessageHeader(
        uint Uid,
        string? MessageId,
        string? InReplyTo,
        string Subject,
        string FromName,
        string FromAddress,
        string To,
        string Cc,
        DateTimeOffset Date,
        bool Seen,
        bool Flagged,
        bool Answered,
        long Size,
        bool HasAttachments,
        string Preview);

    /// <summary>Una riga dell'elenco dei messaggi.</summary>
    public sealed record MessageRow(
        long Id,
        long FolderId,
        uint Uid,
        string? MessageId,
        string Subject,
        string FromName,
        string FromAddress,
        string To,
        string Cc,
        DateTimeOffset Date,
        bool Seen,
        bool Flagged,
        bool Answered,
        bool HasAttachments,
        string Preview,
        bool HasBody)
    {
        public string Sender => string.IsNullOrWhiteSpace(FromName) ? FromAddress : FromName;
    }

    /// <summary>
    /// Archivio locale della posta: un database SQLite (cartelle, intestazioni, indice di ricerca) e accanto i
    /// messaggi completi in formato .eml, ognuno in un file chiamato con la sua impronta SHA-256 (scritto una volta,
    /// mai modificato). Così l'elenco si apre subito anche con decine di migliaia di messaggi e la posta già
    /// scaricata si legge senza rete.
    /// </summary>
    public sealed class MailStore
    {
        private readonly string _connection;
        private readonly object _gate = new();

        public MailStore(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(BodiesFolder);
            _connection = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(folder, "store.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = true,
            }.ToString();
            Migrate();
        }

        public string Folder { get; }
        public string BodiesFolder => Path.Combine(Folder, "bodies");

        private SqliteConnection Open()
        {
            var c = new SqliteConnection(_connection);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            cmd.ExecuteNonQuery();
            return c;
        }

        private void Migrate()
        {
            using var c = Open();
            Exec(c, "PRAGMA journal_mode = WAL;");
            Exec(c, """
                CREATE TABLE IF NOT EXISTS folders (
                    id INTEGER PRIMARY KEY,
                    account TEXT NOT NULL,
                    path TEXT NOT NULL,
                    name TEXT NOT NULL,
                    kind INTEGER NOT NULL,
                    separator INTEGER NOT NULL DEFAULT 0,
                    uidvalidity INTEGER NOT NULL DEFAULT 0,
                    last_uid INTEGER NOT NULL DEFAULT 0,
                    UNIQUE(account, path));
                CREATE TABLE IF NOT EXISTS messages (
                    id INTEGER PRIMARY KEY,
                    folder INTEGER NOT NULL REFERENCES folders(id) ON DELETE CASCADE,
                    uid INTEGER NOT NULL,
                    message_id TEXT,
                    in_reply_to TEXT,
                    subject TEXT NOT NULL,
                    from_name TEXT NOT NULL,
                    from_addr TEXT NOT NULL,
                    to_text TEXT NOT NULL,
                    cc_text TEXT NOT NULL,
                    date INTEGER NOT NULL,
                    seen INTEGER NOT NULL,
                    flagged INTEGER NOT NULL,
                    answered INTEGER NOT NULL,
                    size INTEGER NOT NULL,
                    attachments INTEGER NOT NULL,
                    preview TEXT NOT NULL,
                    body TEXT,
                    UNIQUE(folder, uid));
                CREATE INDEX IF NOT EXISTS messages_by_date ON messages(folder, date DESC);
                CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
                    subject, sender, recipients, text, tokenize = 'unicode61 remove_diacritics 2');
                """);
        }

        private static void Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // ---- Cartelle ------------------------------------------------------------------------------------

        public long UpsertFolder(string account, string path, string name, FolderKind kind, char separator)
        {
            lock (_gate)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO folders(account, path, name, kind, separator) VALUES($a, $p, $n, $k, $s)
                    ON CONFLICT(account, path) DO UPDATE SET name = excluded.name, kind = excluded.kind, separator = excluded.separator;
                    SELECT id FROM folders WHERE account = $a AND path = $p;
                    """;
                cmd.Parameters.AddWithValue("$a", account);
                cmd.Parameters.AddWithValue("$p", path);
                cmd.Parameters.AddWithValue("$n", name);
                cmd.Parameters.AddWithValue("$k", (int)kind);
                cmd.Parameters.AddWithValue("$s", (int)separator);
                return (long)cmd.ExecuteScalar()!;
            }
        }

        /// <summary>Toglie le cartelle che sul server non ci sono più (con i loro messaggi).</summary>
        public void RemoveFoldersExcept(string account, IReadOnlyCollection<string> paths)
        {
            lock (_gate)
            {
                using var c = Open();
                using var tx = c.BeginTransaction();
                var existing = new List<(long Id, string Path)>();
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT id, path FROM folders WHERE account = $a";
                    cmd.Parameters.AddWithValue("$a", account);
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) existing.Add((r.GetInt64(0), r.GetString(1)));
                }
                var keep = new HashSet<string>(paths);
                foreach (var (id, path) in existing)
                    if (!keep.Contains(path)) DeleteFolder(c, tx, id);
                tx.Commit();
            }
        }

        private static void DeleteFolder(SqliteConnection c, SqliteTransaction tx, long id)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                DELETE FROM messages_fts WHERE rowid IN (SELECT id FROM messages WHERE folder = $f);
                DELETE FROM messages WHERE folder = $f;
                DELETE FROM folders WHERE id = $f;
                """;
            cmd.Parameters.AddWithValue("$f", id);
            cmd.ExecuteNonQuery();
        }

        public void RemoveAccount(string account)
        {
            lock (_gate)
            {
                using var c = Open();
                using var tx = c.BeginTransaction();
                var ids = new List<long>();
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT id FROM folders WHERE account = $a";
                    cmd.Parameters.AddWithValue("$a", account);
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) ids.Add(r.GetInt64(0));
                }
                foreach (var id in ids) DeleteFolder(c, tx, id);
                tx.Commit();
            }
            SweepBodies();
        }

        public IReadOnlyList<FolderInfo> Folders(string account)
        {
            lock (_gate)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                    SELECT f.id, f.account, f.path, f.name, f.kind, f.separator, f.uidvalidity, f.last_uid,
                           (SELECT COUNT(*) FROM messages m WHERE m.folder = f.id AND m.seen = 0),
                           (SELECT COUNT(*) FROM messages m WHERE m.folder = f.id)
                    FROM folders f WHERE f.account = $a
                    ORDER BY CASE WHEN f.kind = 9 THEN 1 ELSE 0 END, f.kind, f.path COLLATE NOCASE
                    """;
                cmd.Parameters.AddWithValue("$a", account);
                using var r = cmd.ExecuteReader();
                var list = new List<FolderInfo>();
                while (r.Read())
                    list.Add(new FolderInfo(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), (FolderKind)r.GetInt32(4),
                        r.GetInt32(8), r.GetInt32(9), (uint)r.GetInt64(6), (uint)r.GetInt64(7), (char)r.GetInt32(5)));
                return list;
            }
        }

        public FolderInfo? FolderById(long id)
        {
            string? account;
            lock (_gate)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT account FROM folders WHERE id = $f";
                cmd.Parameters.AddWithValue("$f", id);
                account = cmd.ExecuteScalar() as string;
            }
            return account == null ? null : Folders(account).FirstOrDefault(f => f.Id == id);
        }

        /// <summary>Il server ha cambiato UIDVALIDITY: i numeri dei messaggi non valgono più, si riparte da zero.</summary>
        public void ResetFolder(long folder, uint uidValidity)
        {
            lock (_gate)
            {
                using var c = Open();
                using var tx = c.BeginTransaction();
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    DELETE FROM messages_fts WHERE rowid IN (SELECT id FROM messages WHERE folder = $f);
                    DELETE FROM messages WHERE folder = $f;
                    UPDATE folders SET uidvalidity = $v, last_uid = 0 WHERE id = $f;
                    """;
                cmd.Parameters.AddWithValue("$f", folder);
                cmd.Parameters.AddWithValue("$v", (long)uidValidity);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
        }

        public void SetLastUid(long folder, uint lastUid)
        {
            lock (_gate)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE folders SET last_uid = MAX(last_uid, $u) WHERE id = $f";
                cmd.Parameters.AddWithValue("$f", folder);
                cmd.Parameters.AddWithValue("$u", (long)lastUid);
                cmd.ExecuteNonQuery();
            }
        }

        // ---- Messaggi ------------------------------------------------------------------------------------

        /// <summary>Aggiunge le intestazioni nuove (quelle già presenti con lo stesso UID restano come sono).</summary>
        public int AddMessages(long folder, IEnumerable<MessageHeader> headers)
        {
            int added = 0;
            lock (_gate)
            {
                using var c = Open();
                using var tx = c.BeginTransaction();
                using var insert = c.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT OR IGNORE INTO messages(folder, uid, message_id, in_reply_to, subject, from_name, from_addr, to_text,
                        cc_text, date, seen, flagged, answered, size, attachments, preview)
                    VALUES($f, $u, $mid, $irt, $s, $fn, $fa, $to, $cc, $d, $seen, $fl, $an, $sz, $at, $pv);
                    SELECT changes(), last_insert_rowid();
                    """;
                var p = new Dictionary<string, SqliteParameter>();
                foreach (var name in new[] { "$f", "$u", "$mid", "$irt", "$s", "$fn", "$fa", "$to", "$cc", "$d", "$seen", "$fl", "$an", "$sz", "$at", "$pv" })
                    p[name] = insert.Parameters.Add(name, SqliteType.Text);
                using var fts = c.CreateCommand();
                fts.Transaction = tx;
                fts.CommandText = "INSERT INTO messages_fts(rowid, subject, sender, recipients, text) VALUES($id, $s, $snd, $rcp, $t)";
                var fid = fts.Parameters.Add("$id", SqliteType.Integer);
                var fs = fts.Parameters.Add("$s", SqliteType.Text);
                var fsnd = fts.Parameters.Add("$snd", SqliteType.Text);
                var frcp = fts.Parameters.Add("$rcp", SqliteType.Text);
                var ft = fts.Parameters.Add("$t", SqliteType.Text);

                foreach (var h in headers)
                {
                    p["$f"].Value = folder;
                    p["$u"].Value = (long)h.Uid;
                    p["$mid"].Value = (object?)h.MessageId ?? DBNull.Value;
                    p["$irt"].Value = (object?)h.InReplyTo ?? DBNull.Value;
                    p["$s"].Value = h.Subject;
                    p["$fn"].Value = h.FromName;
                    p["$fa"].Value = h.FromAddress;
                    p["$to"].Value = h.To;
                    p["$cc"].Value = h.Cc;
                    p["$d"].Value = h.Date.ToUnixTimeMilliseconds();
                    p["$seen"].Value = h.Seen ? 1 : 0;
                    p["$fl"].Value = h.Flagged ? 1 : 0;
                    p["$an"].Value = h.Answered ? 1 : 0;
                    p["$sz"].Value = h.Size;
                    p["$at"].Value = h.HasAttachments ? 1 : 0;
                    p["$pv"].Value = h.Preview;
                    using var r = insert.ExecuteReader();
                    r.Read();
                    if (r.GetInt64(0) == 0) continue;
                    long id = r.GetInt64(1);
                    r.Close();
                    fid.Value = id;
                    fs.Value = h.Subject;
                    fsnd.Value = h.FromName + " " + h.FromAddress;
                    frcp.Value = h.To + " " + h.Cc;
                    ft.Value = h.Preview;
                    fts.ExecuteNonQuery();
                    added++;
                }
                tx.Commit();
            }
            return added;
        }

        /// <summary>UID già presenti nella cartella.</summary>
        public HashSet<uint> Uids(long folder)
        {
            lock (_gate)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT uid FROM messages WHERE folder = $f";
                cmd.Parameters.AddWithValue("$f", folder);
                using var r = cmd.ExecuteReader();
                var set = new HashSet<uint>();
                while (r.Read()) set.Add((uint)r.GetInt64(0));
                return set;
            }
        }

        /// <summary>Toglie i messaggi che sul server non ci sono più. Restituisce quanti.</summary>
        public int RemoveUids(long folder, IEnumerable<uint> uids)
        {
            int removed = 0;
            lock (_gate)
            {
                using var c = Open();
                using var tx = c.BeginTransaction();
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    DELETE FROM messages_fts WHERE rowid IN (SELECT id FROM messages WHERE folder = $f AND uid = $u);
                    DELETE FROM messages WHERE folder = $f AND uid = $u;
                    """;
                var f = cmd.Parameters.AddWithValue("$f", folder);
                var u = cmd.Parameters.Add("$u", SqliteType.Integer);
                foreach (var uid in uids)
                {
                    u.Value = (long)uid;
                    removed += cmd.ExecuteNonQuery() > 0 ? 1 : 0;
                }
                tx.Commit();
            }
            return removed;
        }

        public void SetFlags(long folder, uint uid, bool? seen = null, bool? flagged = null, bool? answered = null)
        {
            lock (_gate)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                    UPDATE messages SET seen = COALESCE($s, seen), flagged = COALESCE($fl, flagged), answered = COALESCE($a, answered)
                    WHERE folder = $f AND uid = $u
                    """;
                cmd.Parameters.AddWithValue("$f", folder);
                cmd.Parameters.AddWithValue("$u", (long)uid);
                cmd.Parameters.AddWithValue("$s", seen.HasValue ? (seen.Value ? 1 : 0) : DBNull.Value);
                cmd.Parameters.AddWithValue("$fl", flagged.HasValue ? (flagged.Value ? 1 : 0) : DBNull.Value);
                cmd.Parameters.AddWithValue("$a", answered.HasValue ? (answered.Value ? 1 : 0) : DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Stati letto/contrassegnato/risposto come li ha il server; restituisce quanti sono cambiati.</summary>
        public int ApplyFlags(long folder, IEnumerable<(uint Uid, bool Seen, bool Flagged, bool Answered)> flags)
        {
            int changed = 0;
            lock (_gate)
            {
                using var c = Open();
                using var tx = c.BeginTransaction();
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    UPDATE messages SET seen = $s, flagged = $fl, answered = $a
                    WHERE folder = $f AND uid = $u AND (seen <> $s OR flagged <> $fl OR answered <> $a)
                    """;
                cmd.Parameters.AddWithValue("$f", folder);
                var u = cmd.Parameters.Add("$u", SqliteType.Integer);
                var s = cmd.Parameters.Add("$s", SqliteType.Integer);
                var fl = cmd.Parameters.Add("$fl", SqliteType.Integer);
                var a = cmd.Parameters.Add("$a", SqliteType.Integer);
                foreach (var f in flags)
                {
                    u.Value = (long)f.Uid;
                    s.Value = f.Seen ? 1 : 0;
                    fl.Value = f.Flagged ? 1 : 0;
                    a.Value = f.Answered ? 1 : 0;
                    changed += cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            return changed;
        }

        private const string RowColumns = """
            m.id, m.folder, m.uid, m.message_id, m.subject, m.from_name, m.from_addr, m.to_text, m.cc_text, m.date,
            m.seen, m.flagged, m.answered, m.attachments, m.preview, m.body
            """;

        private static MessageRow ReadRow(SqliteDataReader r) => new(
            r.GetInt64(0), r.GetInt64(1), (uint)r.GetInt64(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4),
            r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8),
            DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(9)), r.GetInt64(10) != 0, r.GetInt64(11) != 0,
            r.GetInt64(12) != 0, r.GetInt64(13) != 0, r.GetString(14), !r.IsDBNull(15));

        /// <summary>Messaggi di una cartella (o di tutte, con <paramref name="folder"/> null), dal più recente.</summary>
        public IReadOnlyList<MessageRow> Messages(long? folder, SearchQuery? query = null, int limit = 5000, string? account = null)
        {
            lock (_gate)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                var where = new List<string>();
                if (folder.HasValue)
                {
                    where.Add("m.folder = $folder");
                    cmd.Parameters.AddWithValue("$folder", folder.Value);
                }
                else if (account != null)
                {
                    where.Add("m.folder IN (SELECT id FROM folders WHERE account = $account)");
                    cmd.Parameters.AddWithValue("$account", account);
                }
                query?.AppendSql(where, cmd.Parameters);
                cmd.CommandText = $"SELECT {RowColumns} FROM messages m"
                                  + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
                                  + " ORDER BY m.date DESC LIMIT $limit";
                cmd.Parameters.AddWithValue("$limit", limit);
                using var r = cmd.ExecuteReader();
                var list = new List<MessageRow>();
                while (r.Read()) list.Add(ReadRow(r));
                return list;
            }
        }

        public MessageRow? Message(long id)
        {
            lock (_gate)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT {RowColumns} FROM messages m WHERE m.id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                using var r = cmd.ExecuteReader();
                return r.Read() ? ReadRow(r) : null;
            }
        }

        // ---- Contenuti -----------------------------------------------------------------------------------

        /// <summary>Percorso del messaggio completo (.eml) se è già stato scaricato.</summary>
        public string? BodyPath(long message)
        {
            string? hash;
            lock (_gate)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT body FROM messages WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", message);
                hash = cmd.ExecuteScalar() as string;
            }
            if (hash == null) return null;
            var path = Path.Combine(BodiesFolder, hash + ".eml");
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Salva il messaggio completo con il nome della sua impronta e aggiunge il testo all'indice di ricerca.
        /// Restituisce il percorso del file.
        /// </summary>
        public string SaveBody(long message, byte[] eml, string searchableText)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(eml));
            var path = Path.Combine(BodiesFolder, hash + ".eml");
            if (!File.Exists(path))
            {
                var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
                File.WriteAllBytes(temp, eml);
                try { File.Move(temp, path, overwrite: false); }
                catch (IOException) { File.Delete(temp); /* scritto nel frattempo da un altro: stesso contenuto */ }
            }
            lock (_gate)
            {
                using var c = Open();
                using var tx = c.BeginTransaction();
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    UPDATE messages SET body = $h WHERE id = $id;
                    UPDATE messages_fts SET text = $t WHERE rowid = $id;
                    """;
                cmd.Parameters.AddWithValue("$h", hash);
                cmd.Parameters.AddWithValue("$id", message);
                cmd.Parameters.AddWithValue("$t", searchableText.Length > 200_000 ? searchableText[..200_000] : searchableText);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            return path;
        }

        /// <summary>Cancella i file dei contenuti che nessun messaggio nomina più.</summary>
        public int SweepBodies()
        {
            HashSet<string> used;
            lock (_gate)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT body FROM messages WHERE body IS NOT NULL";
                using var r = cmd.ExecuteReader();
                used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (r.Read()) used.Add(r.GetString(0));
            }
            int removed = 0;
            foreach (var file in Directory.EnumerateFiles(BodiesFolder, "*.eml"))
            {
                if (used.Contains(Path.GetFileNameWithoutExtension(file))) continue;
                try { File.Delete(file); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return removed;
        }

        /// <summary>Indirizzi con cui si è scambiata posta, per suggerire i destinatari mentre si scrive.</summary>
        public IReadOnlyList<(string Name, string Address)> KnownAddresses(string prefix, int limit = 8)
        {
            lock (_gate)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                    SELECT from_name, from_addr, COUNT(*) AS n FROM messages
                    WHERE from_addr LIKE $p ESCAPE '\' OR from_name LIKE $p ESCAPE '\' OR from_name LIKE $w ESCAPE '\'
                    GROUP BY lower(from_addr) ORDER BY n DESC LIMIT $l
                    """;
                var escaped = prefix.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                cmd.Parameters.AddWithValue("$p", escaped + "%");
                cmd.Parameters.AddWithValue("$w", "% " + escaped + "%");
                cmd.Parameters.AddWithValue("$l", limit);
                using var r = cmd.ExecuteReader();
                var list = new List<(string, string)>();
                while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
                return list;
            }
        }
    }
}
