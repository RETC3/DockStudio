using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using MimeKit;

namespace DockPost.Engine
{
    public enum DraftKind { New, Reply, ReplyAll, Forward }

    /// <summary>Un messaggio in scrittura.</summary>
    public sealed class Draft
    {
        public string AccountId { get; set; } = "";
        public string To { get; set; } = "";
        public string Cc { get; set; } = "";
        public string Bcc { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public List<string> Attachments { get; } = new();

        /// <summary>Allegati del messaggio originale (inoltro): nome e contenuto.</summary>
        public List<(string Name, byte[] Data, string ContentType)> CarriedAttachments { get; } = new();

        public string? InReplyTo { get; set; }
        public List<string> References { get; } = new();

        /// <summary>Messaggio a cui si risponde (per segnarlo come risposto dopo l'invio): cartella e UID.</summary>
        public (long Folder, uint Uid)? Answers { get; set; }
    }

    /// <summary>Indirizzi scritti dall'utente non validi.</summary>
    public sealed class AddressException : Exception
    {
        public AddressException(string address) : base(address) => Address = address;
        public string Address { get; }
    }

    /// <summary>Crea risposte e inoltri e trasforma una bozza in un messaggio MIME pronto da inviare.</summary>
    public static class Composer
    {
        /// <summary>"a@b.it, Anna <anna@c.it>; d@e.it" → indirizzi; lancia <see cref="AddressException"/> se uno non va.</summary>
        public static List<MailboxAddress> ParseAddresses(string text)
        {
            var list = new List<MailboxAddress>();
            foreach (var piece in SplitAddresses(text))
            {
                if (!MailboxAddress.TryParse(ParserOptions.Default, piece, out var address) || !IsPlausible(address.Address))
                    throw new AddressException(piece);
                list.Add(address);
            }
            return list;
        }

        private static IEnumerable<string> SplitAddresses(string text)
        {
            var current = new StringBuilder();
            bool quoted = false, angle = false;
            foreach (var ch in text ?? "")
            {
                if (ch == '"') quoted = !quoted;
                if (ch == '<') angle = true;
                if (ch == '>') angle = false;
                if ((ch == ',' || ch == ';' || ch == '\n') && !quoted && !angle)
                {
                    if (current.ToString().Trim().Length > 0) yield return current.ToString().Trim();
                    current.Clear();
                    continue;
                }
                current.Append(ch);
            }
            if (current.ToString().Trim().Length > 0) yield return current.ToString().Trim();
        }

        private static bool IsPlausible(string address) => Providers.DomainOf(address) != null;

        public static string ToText(IEnumerable<InternetAddress> addresses) =>
            string.Join(", ", addresses.OfType<MailboxAddress>().Select(a =>
                string.IsNullOrWhiteSpace(a.Name) ? a.Address : $"{a.Name} <{a.Address}>"));

        /// <summary>Oggetto con un solo "Re:" o "I:" davanti (anche se l'originale aveva "RE:", "R:", "Fwd:"...).</summary>
        public static string PrefixSubject(string subject, string prefix)
        {
            var s = (subject ?? "").Trim();
            while (true)
            {
                var lower = s.ToLowerInvariant();
                var found = new[] { "re:", "r:", "aw:", "fw:", "fwd:", "i:", "inoltra:" }.FirstOrDefault(p => lower.StartsWith(p));
                if (found == null) break;
                s = s[found.Length..].TrimStart();
            }
            return prefix + " " + s;
        }

        /// <summary>Risposta, risposta a tutti o inoltro di un messaggio ricevuto.</summary>
        public static Draft Respond(DraftKind kind, MessageContent original, Account account, string replyPrefix = "Re:",
            string forwardPrefix = "I:", string quoteHeader = "Il {0}, {1} ha scritto:", string forwardHeader = "---------- Messaggio inoltrato ----------",
            IFormatProvider? culture = null)
        {
            culture ??= System.Globalization.CultureInfo.CurrentCulture;
            var m = original.Message;
            var draft = new Draft { AccountId = account.Id };
            var mine = account.Email;
            bool IsMine(MailboxAddress a) => string.Equals(a.Address, mine, StringComparison.OrdinalIgnoreCase);

            if (kind == DraftKind.Forward)
            {
                draft.Subject = PrefixSubject(m.Subject ?? "", forwardPrefix);
                var header = new StringBuilder();
                header.AppendLine(forwardHeader);
                header.AppendLine("From: " + ToText(m.From));
                header.AppendLine("Date: " + m.Date.ToLocalTime().ToString("f", culture));
                header.AppendLine("Subject: " + m.Subject);
                header.AppendLine("To: " + ToText(m.To));
                if (m.Cc.Count > 0) header.AppendLine("Cc: " + ToText(m.Cc));
                draft.Body = "\n\n" + header + "\n" + original.PlainText;
                for (int i = 0; i < original.Attachments.Count; i++)
                {
                    using var ms = new MemoryStream();
                    original.WriteAttachment(i, ms);
                    draft.CarriedAttachments.Add((original.Attachments[i].FileName, ms.ToArray(), original.Attachments[i].ContentType));
                }
                return draft;
            }

            draft.Subject = PrefixSubject(m.Subject ?? "", replyPrefix);
            var replyTo = m.ReplyTo.Mailboxes.Any() ? m.ReplyTo.Mailboxes.ToList() : m.From.Mailboxes.ToList();
            // risposta a un messaggio inviato da sé: si scrive di nuovo ai destinatari
            if (replyTo.All(IsMine) && m.To.Mailboxes.Any()) replyTo = m.To.Mailboxes.ToList();
            draft.To = ToText(replyTo);
            if (kind == DraftKind.ReplyAll)
            {
                var seen = new HashSet<string>(replyTo.Select(a => a.Address), StringComparer.OrdinalIgnoreCase) { mine };
                var cc = m.To.Mailboxes.Concat(m.Cc.Mailboxes).Where(a => seen.Add(a.Address)).ToList();
                draft.Cc = ToText(cc);
            }

            if (!string.IsNullOrEmpty(m.MessageId))
            {
                draft.InReplyTo = m.MessageId;
                draft.References.AddRange(m.References);
                draft.References.Add(m.MessageId);
            }
            var who = m.From.Mailboxes.FirstOrDefault() is { } f ? (string.IsNullOrWhiteSpace(f.Name) ? f.Address : f.Name) : "";
            var quoted = string.Join("\n", original.PlainText.Replace("\r\n", "\n").Split('\n').Select(l => l.StartsWith('>') ? ">" + l : "> " + l));
            draft.Body = "\n\n" + string.Format(culture, quoteHeader, m.Date.ToLocalTime().ToString("f", culture), who) + "\n" + quoted;
            return draft;
        }

        /// <summary>
        /// Bozza → messaggio MIME: testo semplice e HTML con gli stessi contenuti (link cliccabili, citazioni
        /// rientrate), allegati. Lancia <see cref="AddressException"/> per un indirizzo non valido.
        /// </summary>
        public static MimeMessage Build(Draft draft, Account account, DateTimeOffset? now = null)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(account.DisplayName, account.Email));
            message.To.AddRange(ParseAddresses(draft.To));
            message.Cc.AddRange(ParseAddresses(draft.Cc));
            message.Bcc.AddRange(ParseAddresses(draft.Bcc));
            message.Subject = draft.Subject ?? "";
            message.Date = now ?? DateTimeOffset.Now;
            message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(Providers.DomainOf(account.Email) ?? "dockpost.local");
            if (draft.InReplyTo != null) message.InReplyTo = draft.InReplyTo;
            foreach (var r in draft.References) message.References.Add(r);
            message.Headers.Add("User-Agent", "DockPost");

            var body = new BodyBuilder
            {
                TextBody = draft.Body,
                HtmlBody = TextToHtml(draft.Body),
            };
            foreach (var path in draft.Attachments)
                body.Attachments.Add(path);
            foreach (var (name, data, type) in draft.CarriedAttachments)
                body.Attachments.Add(name, data, ContentType.TryParse(type, out var ct) ? ct : new ContentType("application", "octet-stream"));
            message.Body = body.ToMessageBody();
            return message;
        }

        /// <summary>Testo semplice → HTML: paragrafi, a capo, link e citazioni come blockquote.</summary>
        public static string TextToHtml(string text)
        {
            var sb = new StringBuilder("<html><body><div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:11pt\">");
            int level = 0;
            foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                int quote = 0;
                var line = raw;
                while (line.StartsWith('>')) { quote++; line = line[1..]; if (line.StartsWith(' ')) line = line[1..]; }
                while (level < quote) { sb.Append("<blockquote style=\"margin:0 0 0 .8ex;border-left:2px solid #ccc;padding-left:1ex\">"); level++; }
                while (level > quote) { sb.Append("</blockquote>"); level--; }
                foreach (var run in Blocks.LinkRuns(line))
                {
                    var encoded = WebUtility.HtmlEncode(run.Text);
                    sb.Append(run.Link != null ? $"<a href=\"{WebUtility.HtmlEncode(run.Link)}\">{encoded}</a>" : encoded);
                }
                sb.Append("<br>");
            }
            while (level-- > 0) sb.Append("</blockquote>");
            sb.Append("</div></body></html>");
            return sb.ToString();
        }
    }
}
