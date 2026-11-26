using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace DockPost.Engine
{
    /// <summary>
    /// Ricerca scritta nella casella: parole libere (nel mittente, nei destinatari, nell'oggetto e nel testo) più
    /// filtri come <c>da:anna</c>, <c>a:</c>, <c>oggetto:</c>, <c>ha:allegato</c>, <c>è:nonletto</c>, <c>è:contrassegnato</c>,
    /// <c>dopo:2026-10-01</c>, <c>prima:</c>. Valgono anche i nomi inglesi (from:, to:, subject:, has:attachment,
    /// is:unread, is:flagged, after:, before:). Un "-" davanti esclude la parola. Le virgolette tengono insieme più parole.
    /// </summary>
    public sealed class SearchQuery
    {
        public List<string> Words { get; } = new();
        public List<string> ExcludedWords { get; } = new();
        public List<string> From { get; } = new();
        public List<string> To { get; } = new();
        public List<string> Subject { get; } = new();
        public bool? HasAttachment { get; set; }
        public bool? Unread { get; set; }
        public bool? Flagged { get; set; }
        public DateTime? After { get; set; }
        public DateTime? Before { get; set; }

        public bool IsEmpty =>
            Words.Count == 0 && ExcludedWords.Count == 0 && From.Count == 0 && To.Count == 0 && Subject.Count == 0 &&
            HasAttachment == null && Unread == null && Flagged == null && After == null && Before == null;

        public static SearchQuery Parse(string text)
        {
            var q = new SearchQuery();
            foreach (var token in Tokens(text ?? ""))
            {
                bool negate = token.StartsWith('-') && token.Length > 1;
                var t = negate ? token[1..] : token;
                int colon = t.IndexOf(':');
                if (colon > 0 && colon < t.Length - 1)
                {
                    var key = t[..colon].ToLowerInvariant();
                    var value = t[(colon + 1)..];
                    var v = value.ToLowerInvariant();
                    switch (key)
                    {
                        case "da" or "from": q.From.Add(value); continue;
                        case "a" or "to": q.To.Add(value); continue;
                        case "oggetto" or "subject": q.Subject.Add(value); continue;
                        case "ha" or "has" when v is "allegato" or "allegati" or "attachment" or "attachments":
                            q.HasAttachment = !negate; continue;
                        case "è" or "e" or "is":
                            switch (v)
                            {
                                case "nonletto" or "non-letto" or "unread": q.Unread = !negate; continue;
                                case "letto" or "read": q.Unread = negate; continue;
                                case "contrassegnato" or "flagged" or "starred": q.Flagged = !negate; continue;
                            }
                            break;
                        case "dopo" or "after" when ParseDate(value) is { } after: q.After = after; continue;
                        case "prima" or "before" when ParseDate(value) is { } before: q.Before = before; continue;
                    }
                }
                (negate ? q.ExcludedWords : q.Words).Add(t);
            }
            return q;
        }

        private static DateTime? ParseDate(string s) =>
            DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy", "d.M.yyyy" }, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var d) ? d.Date : null;

        /// <summary>Pezzi separati da spazi; le virgolette tengono insieme (anche dopo "da:").</summary>
        private static IEnumerable<string> Tokens(string text)
        {
            var current = new StringBuilder();
            bool quoted = false;
            foreach (var ch in text)
            {
                if (ch == '"') { quoted = !quoted; continue; }
                if (char.IsWhiteSpace(ch) && !quoted)
                {
                    if (current.Length > 0) yield return current.ToString();
                    current.Clear();
                    continue;
                }
                current.Append(ch);
            }
            if (current.Length > 0) yield return current.ToString();
        }

        /// <summary>Testo per FTS5: ogni parola tra virgolette (niente operatori dall'utente) e come prefisso.</summary>
        internal static string FtsTerm(string word)
        {
            var clean = new string(word.Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '@' or '.' or '-' or '_' or '\'').ToArray()).Trim();
            if (clean.Length == 0) return "";
            return "\"" + clean.Replace("\"", "") + "\"*";
        }

        /// <summary>Aggiunge le condizioni alla query SQL dell'elenco (tabella "m" = messages).</summary>
        public void AppendSql(List<string> where, SqliteParameterCollection p)
        {
            int n = 0;
            string Param(object value)
            {
                var name = "$q" + n++;
                p.AddWithValue(name, value);
                return name;
            }
            static string Like(string s) => "%" + s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

            var fts = Words.Select(FtsTerm).Where(t => t.Length > 0).ToList();
            if (fts.Count > 0)
                where.Add($"m.id IN (SELECT rowid FROM messages_fts WHERE messages_fts MATCH {Param(string.Join(" AND ", fts))})");
            foreach (var w in ExcludedWords.Select(FtsTerm).Where(t => t.Length > 0))
                where.Add($"m.id NOT IN (SELECT rowid FROM messages_fts WHERE messages_fts MATCH {Param(w)})");
            foreach (var f in From)
            {
                var like = Param(Like(f));
                where.Add($"(m.from_addr LIKE {like} ESCAPE '\\' OR m.from_name LIKE {like} ESCAPE '\\')");
            }
            foreach (var t in To)
            {
                var like = Param(Like(t));
                where.Add($"(m.to_text LIKE {like} ESCAPE '\\' OR m.cc_text LIKE {like} ESCAPE '\\')");
            }
            foreach (var s in Subject)
                where.Add($"m.subject LIKE {Param(Like(s))} ESCAPE '\\'");
            if (HasAttachment is { } a) where.Add(a ? "m.attachments = 1" : "m.attachments = 0");
            if (Unread is { } u) where.Add(u ? "m.seen = 0" : "m.seen = 1");
            if (Flagged is { } fl) where.Add(fl ? "m.flagged = 1" : "m.flagged = 0");
            if (After is { } after) where.Add($"m.date >= {Param(new DateTimeOffset(after).ToUnixTimeMilliseconds())}");
            if (Before is { } before) where.Add($"m.date < {Param(new DateTimeOffset(before).ToUnixTimeMilliseconds())}");
        }
    }
}
