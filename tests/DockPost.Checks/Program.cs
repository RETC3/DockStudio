using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using DockPost;
using DockPost.Engine;
using DockPost.Mail;
using DockPost.Views;
using DockStudio.Core.Localization;
using DockStudio.UI;
using MimeKit;

namespace DockPost.Checks
{
    public sealed class TestApp : Application
    {
        public override void Initialize() => DockTheme.Apply(this, AppInfo.Accent);
    }

    internal static class Program
    {
        private static int _failures;
        private static string _images = "";

        private static int Main(string[] args)
        {
            _images = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "dockpost-checks"));
            Directory.CreateDirectory(_images);
            Section("Testi", CheckTexts);
            Section("Riconoscimento del provider", CheckProviders);
            Section("Lettura sicura dell'HTML", CheckHtml);
            Section("Testo semplice", CheckPlainText);
            Section("Messaggio con allegati", CheckContent);
            Section("Risposte e inoltri", CheckCompose);
            Section("Archivio locale", CheckStore);
            Section("Ricerca", CheckSearch);
            Section("Tipi di cartella", CheckFolderKinds);
            if (OperatingSystem.IsWindows()) Section("Gestore credenziali di Windows", CheckCredentials);
            Section("Finestre", CheckWindows);
            // Copertina del sito (site/public/apps/dockpost/copertina.png): DOCKPOST_COVER=<file png>.
            if (Environment.GetEnvironmentVariable("DOCKPOST_COVER") is { Length: > 0 } cover)
                Section("Copertina del sito", () => MakeCover(cover));

            Console.WriteLine();
            Console.WriteLine($"Immagini: {_images}");
            Console.WriteLine(_failures == 0 ? "Tutti i controlli superati." : $"{_failures} controlli NON superati.");
            return _failures == 0 ? 0 : 1;
        }

        private static void Section(string name, Action body)
        {
            Console.WriteLine();
            Console.WriteLine($"== {name}");
            try { body(); }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERRORE  eccezione: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                _failures++;
            }
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "OK    " : "ERRORE")}  {what}");
            if (!ok) _failures++;
        }

        private static string TempFolder(string name)
        {
            var path = Path.Combine(Path.GetTempPath(), $"dockpost-{name}-{Guid.NewGuid():N}"[..40]);
            Directory.CreateDirectory(path);
            return path;
        }

        // ---- Motore --------------------------------------------------------------------------------------

        private static void CheckTexts()
        {
            var problems = Text.Strings.Problems();
            Check(problems.Count == 0, "italiano e inglese hanno le stesse chiavi e gli stessi segnaposto"
                                       + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));
            foreach (var key in new[] { "provider.note.gmail", "provider.note.appPassword", "provider.note.guess" })
                Check(Text.Strings.Get(key, "en") != key, $"testo {key}");
        }

        private static void CheckProviders()
        {
            var gmail = Providers.FromKnown("Mario.Rossi@Gmail.com");
            Check(gmail is { ProviderId: "gmail", Imap.Host: "imap.gmail.com", Imap.Port: 993, ServerSavesSent: true, NoteKey: "provider.note.gmail" },
                "Gmail riconosciuto (con l'avvertenza della password per le app)");
            var outlook = Providers.FromKnown("anna@hotmail.it");
            Check(outlook is { Auth: AuthKind.Microsoft, Smtp.Security: Security.StartTls }, "Hotmail → accesso Microsoft, SMTP con STARTTLS");
            Check(Providers.FromKnown("x@libero.it")?.Imap.Host == "imapmail.libero.it", "Libero");
            Check(Providers.FromKnown("x@azienda-sconosciuta.it") == null, "dominio sconosciuto: niente dall'elenco interno");
            Check(Providers.DomainOf("senza-chiocciola") == null && Providers.DomainOf("a@b") == null && Providers.DomainOf("a b@c.it") == null,
                "indirizzi non validi rifiutati");
            Check(Providers.BaseDomain("mx1.mail.aruba.it") == "aruba.it", "dominio base di un server MX");

            const string xml = """
                <clientConfig version="1.1"><emailProvider id="esempio.it">
                  <displayName>Posta Esempio</displayName><displayShortName>Esempio</displayShortName>
                  <incomingServer type="pop3"><hostname>pop.esempio.it</hostname><port>995</port><socketType>SSL</socketType></incomingServer>
                  <incomingServer type="imap"><hostname>imap.esempio.it</hostname><port>993</port><socketType>SSL</socketType><username>%EMAILLOCALPART%</username></incomingServer>
                  <outgoingServer type="smtp"><hostname>smtp.%EMAILDOMAIN%</hostname><port>587</port><socketType>STARTTLS</socketType><username>%EMAILADDRESS%</username></outgoingServer>
                </emailProvider></clientConfig>
                """;
            var parsed = Providers.ParseAutoconfig(xml, "luca@esempio.it", "autoconfig");
            Check(parsed is { Name: "Esempio", Imap.Host: "imap.esempio.it", Smtp.Host: "smtp.esempio.it", Smtp.Port: 587, UserName: "luca" },
                "configurazione automatica letta (IMAP al posto di POP, segnaposto sostituiti)");
            var plain = xml.Replace("<socketType>SSL</socketType><username>%EMAILLOCALPART%", "<socketType>plain</socketType><username>%EMAILLOCALPART%");
            Check(Providers.ParseAutoconfig(plain, "luca@esempio.it", "autoconfig") == null, "server senza protezione rifiutato");
            Check(Providers.ParseAutoconfig("<non-xml", "a@b.it", "x") == null, "XML rovinato: nessun errore, nessun risultato");
        }

        private static void CheckHtml()
        {
            const string html = """
                <html><head><title>T</title><style>body{color:red}</style><script>alert('x')</script></head>
                <body onload="steal()">
                  <h1>Benvenuto</h1>
                  <p>Ciao <b>Anna</b>, <i>ecco</i> il <a href="https://example.com/doc">documento</a>
                     e un <a href="javascript:alert(1)">link cattivo</a>.</p>
                  <div style="display:none">nascosto</div>
                  <ul><li>uno</li><li>due<ol><li>a</li></ol></li></ul>
                  <blockquote><p>citato</p><blockquote>citato due volte</blockquote></blockquote>
                  <img src="https://tracker.example.com/p.gif" width="1" height="1">
                  <img src="https://example.com/foto.jpg" alt="Foto">
                  <img src="cid:logo@x" alt="Logo">
                  <form><input value="password"><button>Invia</button></form>
                  <iframe src="https://example.com"></iframe>
                  <hr><pre>  codice   spazi</pre>
                </body></html>
                """;
            var embedded = new byte[] { 1, 2, 3 };
            var blocks = Blocks.FromHtml(html, cid => cid == "logo@x" ? embedded : null);
            var text = Blocks.ToPlainText(blocks);
            Check(!text.Contains("alert") && !text.Contains("color:red") && !text.Contains("nascosto"), "script, stili e testo nascosto esclusi");
            Check(!text.Contains("password") && !text.Contains("Invia"), "moduli esclusi");
            var paragraphs = blocks.OfType<Paragraph>().ToList();
            Check(paragraphs.FirstOrDefault()?.Style == BlockStyle.Heading1, "titolo riconosciuto");
            var greeting = paragraphs.First(p => p.PlainText.StartsWith("Ciao"));
            Check(greeting.Runs.Any(r => r.Bold && r.Text == "Anna") && greeting.Runs.Any(r => r.Italic && r.Text == "ecco"), "grassetto e corsivo");
            Check(greeting.Runs.Any(r => r.Link == "https://example.com/doc"), "link https mantenuto");
            Check(greeting.Runs.All(r => r.Link == null || !r.Link.StartsWith("javascript")), "link javascript: tolto");
            Check(paragraphs.Any(p => p.Bullet == "•" && p.PlainText == "uno") && paragraphs.Any(p => p.Bullet == "1." && p.ListLevel == 2),
                "elenchi puntati e numerati annidati");
            Check(paragraphs.Any(p => p.QuoteLevel == 1 && p.PlainText == "citato") && paragraphs.Any(p => p.QuoteLevel == 2),
                "citazioni su due livelli");
            var images = blocks.OfType<ImageBlock>().ToList();
            Check(images.Count(i => i.RemoteUrl != null) == 1, "immagine remota segnata (il pixel spia 1×1 no)");
            Check(images.Any(i => i.Data == embedded), "immagine interna (cid:) presa dal messaggio");
            Check(blocks.OfType<RuleBlock>().Any(), "linea orizzontale");
            Check(paragraphs.Any(p => p.Style == BlockStyle.Preformatted && p.PlainText.Contains("  codice   spazi")), "testo preformattato con gli spazi");
            Check(Blocks.SafeLink("file:///C:/Windows") == null && Blocks.SafeLink("mailto:a@b.it") != null, "solo http, https e mailto");

            // HTML enorme o annidato all'eccesso: nessun errore
            var deep = string.Concat(Enumerable.Repeat("<div>", 5000)) + "fondo" + string.Concat(Enumerable.Repeat("</div>", 5000));
            Blocks.FromHtml(deep);
            Check(true, "HTML annidato 5000 volte letto senza errori");
        }

        private static void CheckPlainText()
        {
            var blocks = Blocks.FromText("Ciao,\nriga due\n\n> citato\n>> due volte\n\nvedi https://example.com/a.");
            var p = blocks.OfType<Paragraph>().ToList();
            Check(p.Count == 4, $"quattro paragrafi ({p.Count})");
            Check(p[0].PlainText == "Ciao,\nriga due", "a capo dentro il paragrafo");
            Check(p[1].QuoteLevel == 1 && p[2].QuoteLevel == 2, "livelli di citazione");
            Check(p[3].Runs.Any(r => r.Link == "https://example.com/a"), "indirizzo web riconosciuto (senza il punto finale)");
            Check(Blocks.Preview("  uno\n\n due   tre ", 7) == "uno due…", "anteprima compressa e tagliata");
        }

        private static MimeMessage SampleMessage()
        {
            var m = new MimeMessage();
            m.From.Add(new MailboxAddress("Anna Verdi", "anna@example.com"));
            m.To.Add(new MailboxAddress("Giulia", "giulia@example.com"));
            m.To.Add(new MailboxAddress("Marco", "marco@example.com"));
            m.Cc.Add(new MailboxAddress("Sara", "sara@example.com"));
            m.Subject = "RE: R: Preventivo";
            m.MessageId = "abc@example.com";
            m.Date = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.FromHours(1));
            var body = new BodyBuilder { TextBody = "Testo del messaggio.\nSeconda riga.", HtmlBody = "<p>Testo del messaggio.</p>" };
            body.Attachments.Add("../../etc/con:fig?.txt", new byte[] { 65, 66, 67 }, new ContentType("text", "plain"));
            body.Attachments.Add("programma.exe", new byte[10], new ContentType("application", "octet-stream"));
            m.Body = body.ToMessageBody();
            return m;
        }

        private static void CheckContent()
        {
            var content = MessageContent.From(SampleMessage());
            Check(content.Attachments.Count == 2, "due allegati");
            var name = content.Attachments[0].FileName;
            Check(!name.Contains('/') && !name.Contains('\\') && !name.Contains(':') && !name.Contains('?'), $"nome dell'allegato reso sicuro: {name}");
            using var ms = new MemoryStream();
            content.WriteAttachment(0, ms);
            Check(ms.ToArray().SequenceEqual(new byte[] { 65, 66, 67 }), "contenuto dell'allegato decodificato");
            Check(Shell.IsDangerous("programma.exe") && Shell.IsDangerous("x.JS") && !Shell.IsDangerous("foto.jpg"), "file eseguibili riconosciuti");
            Check(content.PlainText.StartsWith("Testo del messaggio."), "testo semplice per ricerca e risposte");
        }

        private static void CheckCompose()
        {
            var me = new Account { Id = "me", DisplayName = "Giulia", Email = "giulia@example.com" };
            var content = MessageContent.From(SampleMessage());
            var reply = Composer.Respond(DraftKind.Reply, content, me);
            Check(reply.Subject == "Re: Preventivo", $"oggetto della risposta: {reply.Subject}");
            Check(reply.To == "Anna Verdi <anna@example.com>" && reply.Cc == "", "risposta al solo mittente");
            Check(reply.InReplyTo == "abc@example.com" && reply.References.Contains("abc@example.com"), "risposta collegata alla conversazione");
            Check(reply.Body.Contains("> Testo del messaggio.") && reply.Body.Contains("> Seconda riga."), "originale citato");
            var all = Composer.Respond(DraftKind.ReplyAll, content, me);
            Check(all.Cc.Contains("marco@example.com") && all.Cc.Contains("sara@example.com") && !all.Cc.Contains("giulia@example.com"),
                "rispondi a tutti: gli altri in Cc, senza sé stessi");
            var forward = Composer.Respond(DraftKind.Forward, content, me, forwardPrefix: "I:");
            Check(forward.Subject == "I: Preventivo" && forward.To == "" && forward.CarriedAttachments.Count == 2, "inoltro con gli allegati");

            var draft = new Draft { AccountId = "me", To = "anna@example.com; \"Rossi, Marco\" <marco@example.com>", Subject = "Prova", Body = "Ciao <script>\n> citato\nhttps://example.com" };
            var message = Composer.Build(draft, me);
            Check(message.To.Mailboxes.Count() == 2, "due destinatari (anche con la virgola nel nome)");
            Check(message.HtmlBody!.Contains("&lt;script&gt;") && !message.HtmlBody.Contains("<script>"), "testo protetto nell'HTML");
            Check(message.HtmlBody.Contains("<blockquote") && message.HtmlBody.Contains("href=\"https://example.com/\""), "citazione e link nell'HTML");
            Check(message.TextBody?.Replace("\r\n", "\n").TrimEnd() == draft.Body, "parte di testo semplice");
            bool threw = false;
            try { Composer.ParseAddresses("anna@example.com, non-un-indirizzo"); }
            catch (AddressException ex) { threw = ex.Address == "non-un-indirizzo"; }
            Check(threw, "indirizzo non valido segnalato");
        }

        private static void CheckStore()
        {
            var folder = TempFolder("store");
            try
            {
                var store = new MailStore(folder);
                var account = SampleMailbox.Fill(store);
                var folders = store.Folders(account.Id);
                Check(folders.Count == 7 && folders[0].Kind == FolderKind.Inbox, "cartelle con la Posta in arrivo per prima");
                Check(folders.Single(f => f.Path == "Lavoro/Clienti").Depth == 1, "sottocartella");
                var inbox = folders[0];
                Check(inbox.Unread == 2 && inbox.Total == 7, $"7 messaggi, 2 da leggere ({inbox.Total}, {inbox.Unread})");
                var rows = store.Messages(inbox.Id);
                Check(rows.Count == 7 && rows[0].Date > rows[^1].Date, "dal più recente");
                Check(rows.All(r => r.HasBody), "contenuti salvati");
                var first = rows[0];
                store.SetFlags(inbox.Id, first.Uid, seen: true, flagged: true);
                var again = store.Message(first.Id)!;
                Check(again.Seen && again.Flagged, "letto e contrassegnato");
                store.ApplyFlags(inbox.Id, new[] { (first.Uid, false, false, true) });
                again = store.Message(first.Id)!;
                Check(!again.Seen && !again.Flagged && again.Answered, "stati dal server applicati");
                var before = Directory.GetFiles(store.BodiesFolder).Length;
                store.RemoveUids(inbox.Id, new[] { first.Uid });
                Check(store.Message(first.Id) == null, "messaggio sparito dal server tolto");
                Check(store.SweepBodies() == 1 && Directory.GetFiles(store.BodiesFolder).Length == before - 1, "contenuto non più usato cancellato");
                store.ResetFolder(inbox.Id, 999);
                Check(store.Messages(inbox.Id).Count == 0 && store.FolderById(inbox.Id)!.UidValidity == 999, "UIDVALIDITY cambiato: cartella svuotata");
                Check(store.KnownAddresses("ann").Any(a => a.Address == "anna@example.com"), "indirizzi conosciuti per i suggerimenti");
                store.RemoveFoldersExcept(account.Id, new[] { "INBOX", "Sent" });
                Check(store.Folders(account.Id).Count == 2, "cartelle sparite dal server tolte");
                store.RemoveAccount(account.Id);
                Check(store.Folders(account.Id).Count == 0, "account rimosso con la sua posta");

                // un secondo archivio sulla stessa cartella legge quello che c'è
                var reopened = new MailStore(folder);
                Check(reopened.Folders(account.Id).Count == 0, "archivio riaperto");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(folder, true); } catch { }
            }
        }

        private static void CheckSearch()
        {
            var q = SearchQuery.Parse("budget da:anna ha:allegato -bozza dopo:2026-10-01 è:nonletto \"due parole\" is:flagged");
            Check(q.Words.SequenceEqual(new[] { "budget", "due parole" }), "parole libere (anche tra virgolette)");
            Check(q.From.Single() == "anna" && q.HasAttachment == true && q.Unread == true && q.Flagged == true, "filtri");
            Check(q.ExcludedWords.Single() == "bozza" && q.After == new DateTime(2026, 10, 1), "esclusione e data");
            Check(SearchQuery.Parse("  ").IsEmpty, "ricerca vuota");

            var folder = TempFolder("search");
            try
            {
                var store = new MailStore(folder);
                var account = SampleMailbox.Fill(store);
                int Count(string text) => store.Messages(null, SearchQuery.Parse(text), account: account.Id).Count;
                Check(Count("preventivo") == 1, "parola nell'oggetto");
                Check(Count("dolomiti") == 1, "parola nel testo, senza badare alle maiuscole");
                Check(Count("lapponia viaggi") == 1, "più parole insieme");
                Check(Count("da:anna") == 2, "da: (Posta in arrivo e Lavoro)");
                Check(Count("ha:allegato") == 2, "ha:allegato");
                Check(Count("è:nonletto") == 2 && Count("is:unread") == 2, "da leggere (italiano e inglese)");
                Check(Count("è:contrassegnato") == 2, "contrassegnati");
                Check(Count("preven") == 1, "inizio di parola");
                Check(Count("riunione -giovedì") == 0 && Count("riunione") == 1, "parola esclusa");
                Check(Count("\" OR 1=1 --") >= 0, "caratteri strani senza errori");
                Check(Count("prima:2026-11-23") == 3, "prima di una data");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(folder, true); } catch { }
            }
        }

        private static void CheckFolderKinds()
        {
            Check(MailClient.KindFromName("Posta inviata") == FolderKind.Sent && MailClient.KindFromName("Sent Items") == FolderKind.Sent, "Inviata");
            Check(MailClient.KindFromName("Cestino") == FolderKind.Trash && MailClient.KindFromName("Deleted Items") == FolderKind.Trash, "Cestino");
            Check(MailClient.KindFromName("Spam") == FolderKind.Junk && MailClient.KindFromName("Bozze") == FolderKind.Drafts, "Spam e Bozze");
            Check(MailClient.KindFromName("Fatture") == FolderKind.Other, "cartella dell'utente");
        }

        private static void CheckCredentials()
        {
            if (!OperatingSystem.IsWindows()) return;
            var store = new DockPost.Platform.WindowsCredentialStore();
            const string key = "DockPost/controlli-automatici";
            store.Write(key, "pàssword con spazi ✓");
            Check(store.Read(key) == "pàssword con spazi ✓", "password scritta e riletta");
            store.Delete(key);
            Check(store.Read(key) == null, "password cancellata");
        }

        // ---- Finestre ------------------------------------------------------------------------------------

        private static void CheckWindows()
        {
            AppBuilder.Configure<TestApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            foreach (var language in new[] { "it", "en" })
                foreach (var dark in new[] { false, true })
                {
                    Languages.Set(language);
                    DockTheme.SetAppearance(Application.Current!, dark ? Appearance.Dark : Appearance.Light);
                    var suffix = $"{language}-{(dark ? "scuro" : "chiaro")}";

                    // senza account: benvenuto
                    var emptyFolder = TempFolder("empty");
                    var empty = new MailSession(emptyFolder, new MemorySecretStore(), null, offline: true);
                    var welcome = new MainWindow(empty, new StartOptions(false, Automatic: false)) { Width = 1280, Height = 800 };
                    welcome.Show();
                    Pump();
                    Save(welcome, "benvenuto-" + suffix);
                    welcome.Close();

                    // casella di prova: il messaggio HTML più recente aperto
                    var session = App.CreateSession(new StartOptions(true, Automatic: false));
                    var window = new MainWindow(session, new StartOptions(true, Automatic: false)) { Width = 1280, Height = 800 };
                    window.Show();
                    Pump();
                    Check(window.MessageCount == 7, $"7 messaggi nell'elenco ({window.MessageCount})");
                    window.SelectMessage(0);
                    Pump(40);
                    Save(window, "posta-" + suffix);

                    window.Search(language == "it" ? "da:anna" : "from:anna");
                    Pump();
                    Check(window.MessageCount == 2, $"ricerca da:anna → 2 ({window.MessageCount})");
                    Save(window, "ricerca-" + suffix);
                    window.Close();

                    var account = new AccountWindow(session);
                    account.Show();
                    Pump();
                    Save(account, "account-" + suffix);
                    account.Close();

                    var original = MessageContent.From(SampleMessage());
                    var draft = Composer.Respond(DraftKind.Reply, original, session.Accounts.Items[0], Text.T("compose.replyPrefix"),
                        Text.T("compose.forwardPrefix"), Text.T("compose.quoteHeader"), Text.T("compose.forwardHeader"), Languages.Culture);
                    var compose = new ComposeWindow(session, draft);
                    compose.Show();
                    Pump();
                    Check(compose.BodyText.Contains("> Testo del messaggio."), "risposta con l'originale citato");
                    Save(compose, "risposta-" + suffix);
                    compose.Close();
                }

            // righe riciclate: il modello delle righe riceve per un attimo l'elemento null (errore del 2026-11-25 in DockSpace)
            var s = App.CreateSession(new StartOptions(true, Automatic: false));
            var main = new MainWindow(s, new StartOptions(true, Automatic: false));
            bool ok;
            try { ok = main.MessageTemplate?.Build(null) != null; }
            catch (Exception) { ok = false; }
            Check(ok, "modello delle righe dei messaggi con elemento null");
        }

        private static void MakeCover(string file)
        {
            Languages.Set("it");
            DockTheme.SetAppearance(Application.Current!, Appearance.Light);
            var session = App.CreateSession(new StartOptions(true, Automatic: false));
            var window = new MainWindow(session, new StartOptions(true, Automatic: false)) { Width = 1200, Height = 760 };
            window.Show();
            Pump();
            window.SelectMessage(0);
            Pump(40);
            window.CaptureRenderedFrame()?.Save(Path.GetFullPath(file), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Check(File.Exists(file), "copertina " + file);
            window.Close();
        }

        private static void Pump(int rounds = 10)
        {
            for (int i = 0; i < rounds; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(15);
            }
            Dispatcher.UIThread.RunJobs();
        }

        private static void Save(Avalonia.Controls.Window window, string name)
        {
            var frame = window.CaptureRenderedFrame();
            var file = Path.Combine(_images, name + ".png");
            frame?.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Check(frame != null && File.Exists(file), $"immagine {name}.png");
        }
    }
}
