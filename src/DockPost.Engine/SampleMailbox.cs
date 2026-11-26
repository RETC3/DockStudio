using System;
using System.IO;
using System.Linq;
using MimeKit;

namespace DockPost.Engine
{
    /// <summary>
    /// Una casella di prova (account, cartelle, messaggi con testo e allegati) per i controlli automatici, le
    /// immagini del sito e le prove a mano senza un server. Nessun indirizzo esiste davvero (dominio example.com).
    /// </summary>
    public static class SampleMailbox
    {
        public static Account Fill(MailStore store, bool italian = true)
        {
            var account = new Account
            {
                Id = "sample",
                DisplayName = italian ? "Giulia Bianchi" : "Julia White",
                Email = "giulia@example.com",
                UserName = "giulia@example.com",
                Imap = new("imap.example.com", 993, Security.Tls),
                Smtp = new("smtp.example.com", 465, Security.Tls),
            };
            var inbox = store.UpsertFolder(account.Id, "INBOX", "Inbox", FolderKind.Inbox, '/');
            store.UpsertFolder(account.Id, "Drafts", "Drafts", FolderKind.Drafts, '/');
            var sent = store.UpsertFolder(account.Id, "Sent", "Sent", FolderKind.Sent, '/');
            store.UpsertFolder(account.Id, "Archive", "Archive", FolderKind.Archive, '/');
            store.UpsertFolder(account.Id, "Trash", "Trash", FolderKind.Trash, '/');
            var work = store.UpsertFolder(account.Id, "Lavoro", italian ? "Lavoro" : "Work", FolderKind.Other, '/');
            store.UpsertFolder(account.Id, "Lavoro/Clienti", italian ? "Clienti" : "Clients", FolderKind.Other, '/');

            var now = new DateTimeOffset(2026, 11, 26, 10, 30, 0, TimeSpan.FromHours(1));
            var items = italian
                ? new[]
                {
                    ("Marco Rossi", "marco@example.com", "Preventivo per il nuovo sito", "Ciao Giulia, ti mando il preventivo aggiornato con le tre opzioni di cui abbiamo parlato. Fammi sapere entro venerdì.", 0.1, false, false, true),
                    ("Anna Verdi", "anna@example.com", "Riunione di giovedì spostata alle 15", "La riunione con il cliente è spostata alle 15:00 nella sala grande. Porta i disegni stampati, per favore.", 2.0, false, true, false),
                    ("Banca Esempio", "notifiche@example.com", "Il tuo estratto conto di novembre", "L'estratto conto di novembre è disponibile nell'area riservata.", 26.0, true, false, false),
                    ("Luca Neri", "luca@example.com", "Foto della gita", "Eccole! Le più belle sono la terza e la settima. A presto!", 30.0, true, false, true),
                    ("Sara Galli", "sara@example.com", "Re: Bozza del contratto", "Ho segnato in giallo le modifiche al punto 4. Il resto per me va bene.", 50.0, true, true, false),
                    ("Newsletter Viaggi", "news@example.com", "Dieci mete per l'inverno", "Dalle Dolomiti alla Lapponia: le offerte della settimana.", 75.0, true, false, false),
                    ("Paolo Conti", "paolo@example.com", "Cena di sabato", "Confermo per sabato alle 20. Porto io il dolce.", 98.0, true, false, false),
                }
                : new[]
                {
                    ("Mark Reed", "mark@example.com", "Quote for the new website", "Hi Julia, here is the updated quote with the three options we discussed. Let me know by Friday.", 0.1, false, false, true),
                    ("Anna Green", "anna@example.com", "Thursday's meeting moved to 3 pm", "The client meeting is moved to 3:00 pm in the large room. Please bring the printed drawings.", 2.0, false, true, false),
                    ("Example Bank", "alerts@example.com", "Your November statement", "Your November statement is available in your account.", 26.0, true, false, false),
                    ("Luke Black", "luke@example.com", "Trip photos", "Here they are! The best ones are the third and the seventh. See you soon!", 30.0, true, false, true),
                    ("Sarah Hill", "sarah@example.com", "Re: Draft contract", "I highlighted the changes to section 4 in yellow. The rest is fine with me.", 50.0, true, true, false),
                    ("Travel Newsletter", "news@example.com", "Ten winter destinations", "From the Dolomites to Lapland: this week's deals.", 75.0, true, false, false),
                    ("Paul Hunt", "paul@example.com", "Saturday dinner", "Confirmed for Saturday at 8 pm. I'll bring dessert.", 98.0, true, false, false),
                };

            uint uid = 1;
            foreach (var (name, address, subject, text, hoursAgo, seen, flagged, attachment) in items.Reverse())
            {
                var date = now.AddHours(-hoursAgo);
                var header = new MessageHeader(uid, $"<{uid}@example.com>", null, subject, name, address, account.Title, "", date,
                    seen, flagged, false, 4000, attachment, Blocks.Preview(text));
                store.AddMessages(inbox, new[] { header });
                var row = store.Messages(inbox).First(r => r.Uid == uid);
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(name, address));
                message.To.Add(new MailboxAddress(account.DisplayName, account.Email));
                message.Subject = subject;
                message.Date = date;
                message.MessageId = $"{uid}@example.com";
                var body = new BodyBuilder
                {
                    TextBody = text,
                    HtmlBody = uid == items.Length
                        ? $"<html><head><style>p{{color:red}}</style><script>alert(1)</script></head><body><p>{text}</p>" +
                          "<p><b>" + (italian ? "Riepilogo" : "Summary") + "</b></p><ul><li>" +
                          (italian ? "Opzione A: sito vetrina" : "Option A: brochure site") + "</li><li>" +
                          (italian ? "Opzione B: con negozio online" : "Option B: with online shop") + "</li></ul>" +
                          "<blockquote>" + (italian ? "Il messaggio precedente, citato." : "The previous message, quoted.") + "</blockquote>" +
                          "<p><a href=\"https://example.com/preventivo\">example.com/preventivo</a></p>" +
                          "<img src=\"https://tracker.example.com/open.gif\" width=\"1\" height=\"1\">" +
                          "<img src=\"https://example.com/logo.png\" alt=\"Logo\"></body></html>"
                        : null,
                };
                if (attachment) body.Attachments.Add(italian ? "preventivo.pdf" : "quote.pdf", new byte[12_345], new ContentType("application", "pdf"));
                message.Body = body.ToMessageBody();
                using var ms = new MemoryStream();
                message.WriteTo(ms);
                store.SaveBody(row.Id, ms.ToArray(), text);
                uid++;
            }
            store.SetLastUid(inbox, uid - 1);

            store.AddMessages(sent, new[]
            {
                new MessageHeader(1, "<s1@example.com>", null, italian ? "Re: Cena di sabato" : "Re: Saturday dinner", account.DisplayName,
                    account.Email, "paolo@example.com", "", now.AddHours(-97), true, false, false, 2000, false,
                    italian ? "Perfetto, a sabato!" : "Great, see you Saturday!"),
            });
            store.AddMessages(work, new[]
            {
                new MessageHeader(1, "<w1@example.com>", null, italian ? "Piano del trimestre" : "Quarterly plan", "Anna Verdi",
                    "anna@example.com", account.Email, "", now.AddDays(-12), true, false, false, 9000, false,
                    italian ? "In allegato il piano con le scadenze." : "Attached is the plan with the deadlines."),
            });
            return account;
        }
    }
}
