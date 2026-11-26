using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace DockPost.Engine
{
    public enum MailErrorKind
    {
        /// <summary>Server irraggiungibile, connessione caduta, nessuna rete.</summary>
        Network,
        /// <summary>Nome utente o password non accettati.</summary>
        Authentication,
        /// <summary>Il certificato del server non è valido.</summary>
        Certificate,
        /// <summary>Serve rientrare con l'account Microsoft.</summary>
        SignIn,
        /// <summary>Il server ha rifiutato il comando.</summary>
        Server,
    }

    /// <summary>Errore spiegabile all'utente: il tipo sceglie il testo, il messaggio originale resta per i dettagli.</summary>
    public sealed class MailException : Exception
    {
        public MailException(MailErrorKind kind, string server, Exception inner) : base(inner.Message, inner)
        {
            Kind = kind;
            Server = server;
        }

        public MailErrorKind Kind { get; }
        public string Server { get; }

        public static MailException Wrap(Exception ex, string server) => ex switch
        {
            MailException m => m,
            SignInRequiredException => new(MailErrorKind.SignIn, server, ex),
            MailKit.Security.AuthenticationException => new(MailErrorKind.Authentication, server, ex),
            SslHandshakeException => new(MailErrorKind.Certificate, server, ex),
            System.Security.Authentication.AuthenticationException => new(MailErrorKind.Certificate, server, ex),
            ImapCommandException or SmtpCommandException => new(MailErrorKind.Server, server, ex),
            _ => new(MailErrorKind.Network, server, ex),
        };
    }

    public sealed record SyncProgress(string AccountId, string Folder, int Done, int Total);

    /// <summary>
    /// Connessione di un account: sincronizza le cartelle IMAP nell'archivio locale, scarica i messaggi aperti,
    /// porta sul server letto/contrassegnato/spostato/eliminato e invia con SMTP. Una sola operazione IMAP alla volta
    /// per account; la connessione resta aperta e si riapre se cade.
    /// </summary>
    public sealed class MailClient : IAsyncDisposable
    {
        private const int Batch = 250;
        private const int FirstSyncLimit = 5000;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        private readonly ISecretStore _secrets;
        private readonly MicrosoftSignIn? _microsoft;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private ImapClient? _imap;

        public MailClient(Account account, MailStore store, ISecretStore secrets, MicrosoftSignIn? microsoft)
        {
            Account = account;
            Store = store;
            _secrets = secrets;
            _microsoft = microsoft;
        }

        public Account Account { get; }
        public MailStore Store { get; }

        private static SecureSocketOptions Options(Security s) => s switch
        {
            Security.Tls => SecureSocketOptions.SslOnConnect,
            Security.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None,
        };

        private async Task<SaslMechanism?> OAuth(CancellationToken cancel)
        {
            if (Account.Auth != AuthKind.Microsoft) return null;
            if (_microsoft == null) throw new SignInRequiredException("Microsoft sign-in unavailable");
            var token = await _microsoft.AccessTokenAsync(Account.UserName, cancel);
            return new SaslMechanismOAuth2(Account.UserName, token);
        }

        private string Password() =>
            _secrets.Read(Account.SecretKey) ?? throw new MailKit.Security.AuthenticationException("No saved password");

        private async Task<ImapClient> Imap(CancellationToken cancel)
        {
            if (_imap is { IsConnected: true, IsAuthenticated: true }) return _imap;
            if (_imap != null) { try { _imap.Dispose(); } catch { } }
            var client = new ImapClient { Timeout = (int)Timeout.TotalMilliseconds };
            try
            {
                await client.ConnectAsync(Account.Imap.Host, Account.Imap.Port, Options(Account.Imap.Security), cancel);
                if (await OAuth(cancel) is { } sasl) await client.AuthenticateAsync(sasl, cancel);
                else await client.AuthenticateAsync(Account.UserName, Password(), cancel);
            }
            catch
            {
                client.Dispose();
                throw;
            }
            _imap = client;
            return client;
        }

        /// <summary>Esegue un'operazione IMAP; se la connessione era caduta la riprova una volta su una nuova.</summary>
        private async Task<T> WithImap<T>(Func<ImapClient, Task<T>> action, CancellationToken cancel)
        {
            await _gate.WaitAsync(cancel);
            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        return await action(await Imap(cancel));
                    }
                    catch (Exception ex) when (attempt == 0 && ex is IOException or SocketException or ImapProtocolException or ServiceNotConnectedException)
                    {
                        try { _imap?.Dispose(); } catch { }
                        _imap = null;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { throw MailException.Wrap(ex, Account.Imap.Host); }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { throw MailException.Wrap(ex, Account.Imap.Host); }
            finally { _gate.Release(); }
        }

        /// <summary>Prova IMAP e SMTP con le impostazioni dell'account (prima di salvarlo).</summary>
        public async Task TestAsync(CancellationToken cancel)
        {
            await WithImap(async c => { await c.Inbox.StatusAsync(StatusItems.Count, cancel); return 0; }, cancel);
            try
            {
                using var smtp = await Smtp(cancel);
                await smtp.DisconnectAsync(true, cancel);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { throw MailException.Wrap(ex, Account.Smtp.Host); }
        }

        // ---- Cartelle e sincronizzazione ------------------------------------------------------------------

        public static FolderKind KindOf(IMailFolder folder, ImapClient client)
        {
            if (folder == client.Inbox || folder.FullName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) return FolderKind.Inbox;
            var a = folder.Attributes;
            if (a.HasFlag(FolderAttributes.Sent)) return FolderKind.Sent;
            if (a.HasFlag(FolderAttributes.Drafts)) return FolderKind.Drafts;
            if (a.HasFlag(FolderAttributes.Trash)) return FolderKind.Trash;
            if (a.HasFlag(FolderAttributes.Junk)) return FolderKind.Junk;
            if (a.HasFlag(FolderAttributes.Archive)) return FolderKind.Archive;
            return KindFromName(folder.Name);
        }

        /// <summary>Tipo dal nome, per i server che non dicono a cosa serve la cartella.</summary>
        public static FolderKind KindFromName(string name) => name.Trim().ToLowerInvariant() switch
        {
            "sent" or "sent items" or "sent messages" or "sent mail" or "posta inviata" or "inviata" or "inviati" or "elementi inviati" => FolderKind.Sent,
            "drafts" or "draft" or "bozze" => FolderKind.Drafts,
            "trash" or "deleted" or "deleted items" or "deleted messages" or "cestino" or "posta eliminata" or "elementi eliminati" => FolderKind.Trash,
            "junk" or "spam" or "junk e-mail" or "junk email" or "posta indesiderata" => FolderKind.Junk,
            "archive" or "archives" or "archivio" => FolderKind.Archive,
            _ => FolderKind.Other,
        };

        /// <summary>Elenco delle cartelle sincronizzabili, con la Posta in arrivo per prima.</summary>
        private async Task<List<IMailFolder>> ListFolders(ImapClient client, CancellationToken cancel)
        {
            var ns = client.PersonalNamespaces.Count > 0 ? client.PersonalNamespaces[0] : new FolderNamespace('/', "");
            var all = await client.GetFoldersAsync(ns, StatusItems.None, subscribedOnly: false, cancel);
            var list = all.Where(f => !f.Attributes.HasFlag(FolderAttributes.NoSelect) &&
                                      !f.Attributes.HasFlag(FolderAttributes.NonExistent) &&
                                      // "Tutti i messaggi" di Gmail ripete ogni messaggio delle altre cartelle
                                      !f.Attributes.HasFlag(FolderAttributes.All)).ToList();
            if (!list.Contains(client.Inbox)) list.Insert(0, client.Inbox);
            return list.OrderBy(f => f == client.Inbox ? 0 : 1).ToList();
        }

        /// <summary>
        /// Porta l'archivio locale allo stato del server: cartelle, messaggi nuovi (la prima volta solo gli ultimi
        /// <see cref="Account.DaysToKeep"/> giorni), letti/contrassegnati e messaggi spariti.
        /// </summary>
        public Task<int> SyncAsync(IProgress<SyncProgress>? progress, CancellationToken cancel) =>
            WithImap(async client =>
            {
                var folders = await ListFolders(client, cancel);
                var paths = new List<string>();
                var ids = new List<(IMailFolder Folder, long Id)>();
                foreach (var f in folders)
                {
                    var kind = KindOf(f, client);
                    var name = kind == FolderKind.Inbox ? "Inbox" : f.Name;
                    ids.Add((f, Store.UpsertFolder(Account.Id, f.FullName, name, kind, f.DirectorySeparator)));
                    paths.Add(f.FullName);
                }
                Store.RemoveFoldersExcept(Account.Id, paths);

                int added = 0;
                for (int i = 0; i < ids.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress?.Report(new SyncProgress(Account.Id, ids[i].Folder.Name, i, ids.Count));
                    try { added += await SyncFolder(ids[i].Folder, ids[i].Id, cancel); }
                    catch (ImapCommandException) { /* una cartella che non si apre non ferma le altre */ }
                    catch (FolderNotFoundException) { }
                }
                progress?.Report(new SyncProgress(Account.Id, "", ids.Count, ids.Count));
                return added;
            }, cancel);

        private async Task<int> SyncFolder(IMailFolder folder, long id, CancellationToken cancel)
        {
            await folder.OpenAsync(FolderAccess.ReadOnly, cancel);
            try
            {
                var info = Store.FolderById(id)!;
                if (info.UidValidity != folder.UidValidity)
                {
                    Store.ResetFolder(id, folder.UidValidity);
                    info = Store.FolderById(id)!;
                }
                if (folder.Count == 0)
                {
                    var gone = Store.Uids(id);
                    if (gone.Count > 0) Store.RemoveUids(id, gone);
                    return 0;
                }

                // messaggi nuovi
                IList<UniqueId> fresh;
                if (info.LastUid == 0)
                {
                    var query = Account.DaysToKeep > 0
                        ? MailKit.Search.SearchQuery.DeliveredAfter(DateTime.Today.AddDays(-Account.DaysToKeep))
                        : MailKit.Search.SearchQuery.All;
                    fresh = (await folder.SearchAsync(query, cancel)).ToList();
                    if (fresh.Count > FirstSyncLimit) fresh = fresh.Skip(fresh.Count - FirstSyncLimit).ToList();
                }
                else
                {
                    var range = new UniqueIdRange(new UniqueId(info.LastUid + 1), UniqueId.MaxValue);
                    fresh = (await folder.SearchAsync(range, MailKit.Search.SearchQuery.All, cancel)).Where(u => u.Id > info.LastUid).ToList();
                }

                int added = 0;
                // dal più recente, così la posta nuova arriva subito
                foreach (var chunk in fresh.Reverse().Chunk(Batch))
                {
                    cancel.ThrowIfCancellationRequested();
                    var summaries = await folder.FetchAsync(chunk.ToList(),
                        MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                        MessageSummaryItems.Size | MessageSummaryItems.BodyStructure | MessageSummaryItems.PreviewText |
                        MessageSummaryItems.InternalDate, cancel);
                    added += Store.AddMessages(id, summaries.Select(ToHeader));
                }
                if (fresh.Count > 0) Store.SetLastUid(id, fresh.Max(u => u.Id));

                // letti/contrassegnati e messaggi spariti, per quelli che abbiamo
                var known = Store.Uids(id);
                if (known.Count > 0)
                {
                    var present = new HashSet<uint>();
                    var flags = new List<(uint, bool, bool, bool)>();
                    var knownList = known.Select(u => new UniqueId(folder.UidValidity, u)).ToList();
                    foreach (var chunk in knownList.Chunk(2000))
                    {
                        var states = await folder.FetchAsync(chunk.ToList(), MessageSummaryItems.UniqueId | MessageSummaryItems.Flags, cancel);
                        foreach (var s in states)
                        {
                            present.Add(s.UniqueId.Id);
                            var f = s.Flags ?? MessageFlags.None;
                            flags.Add((s.UniqueId.Id, f.HasFlag(MessageFlags.Seen), f.HasFlag(MessageFlags.Flagged), f.HasFlag(MessageFlags.Answered)));
                        }
                    }
                    Store.ApplyFlags(id, flags);
                    var gone = known.Where(u => !present.Contains(u)).ToList();
                    if (gone.Count > 0) Store.RemoveUids(id, gone);
                }
                return added;
            }
            finally
            {
                if (folder.IsOpen) await folder.CloseAsync(false, cancel);
            }
        }

        private static string Addresses(InternetAddressList? list) =>
            list == null ? "" : Composer.ToText(list);

        public static MessageHeader ToHeader(IMessageSummary s)
        {
            var env = s.Envelope;
            var from = env?.From.Mailboxes.FirstOrDefault();
            var flags = s.Flags ?? MessageFlags.None;
            var date = env?.Date ?? s.InternalDate ?? DateTimeOffset.Now;
            bool attachments = s.Attachments?.Any() == true;
            return new MessageHeader(
                s.UniqueId.Id,
                env?.MessageId,
                env?.InReplyTo,
                env?.Subject ?? "",
                from?.Name ?? "",
                from?.Address ?? "",
                Addresses(env?.To),
                Addresses(env?.Cc),
                date,
                flags.HasFlag(MessageFlags.Seen),
                flags.HasFlag(MessageFlags.Flagged),
                flags.HasFlag(MessageFlags.Answered),
                s.Size ?? 0,
                attachments,
                Blocks.Preview(s.PreviewText ?? ""));
        }

        // ---- Azioni su un messaggio -------------------------------------------------------------------------

        private async Task<IMailFolder> OpenFolder(ImapClient client, long folderId, FolderAccess access, CancellationToken cancel)
        {
            var info = Store.FolderById(folderId) ?? throw new FolderNotFoundException("?");
            var folder = await client.GetFolderAsync(info.Path, cancel);
            if (!folder.IsOpen || folder.Access < access) await folder.OpenAsync(access, cancel);
            return folder;
        }

        /// <summary>Scarica il messaggio completo (se non c'è già) e restituisce il percorso del file .eml.</summary>
        public async Task<string> BodyAsync(MessageRow row, CancellationToken cancel)
        {
            if (Store.BodyPath(row.Id) is { } existing) return existing;
            var bytes = await WithImap(async client =>
            {
                var folder = await OpenFolder(client, row.FolderId, FolderAccess.ReadOnly, cancel);
                using var message = await folder.GetMessageAsync(new UniqueId(row.Uid), cancel);
                using var ms = new MemoryStream();
                await message.WriteToAsync(ms, cancel);
                return ms.ToArray();
            }, cancel);
            string text;
            try
            {
                using var ms = new MemoryStream(bytes);
                text = MessageContent.From(await MimeMessage.LoadAsync(ms, cancel)).PlainText;
            }
            catch (FormatException) { text = ""; }
            return Store.SaveBody(row.Id, bytes, text);
        }

        public async Task SetFlagsAsync(MessageRow row, bool? seen, bool? flagged, CancellationToken cancel)
        {
            Store.SetFlags(row.FolderId, row.Uid, seen, flagged);
            await WithImap(async client =>
            {
                var folder = await OpenFolder(client, row.FolderId, FolderAccess.ReadWrite, cancel);
                var uid = new UniqueId(row.Uid);
                if (seen == true) await folder.AddFlagsAsync(uid, MessageFlags.Seen, true, cancel);
                if (seen == false) await folder.RemoveFlagsAsync(uid, MessageFlags.Seen, true, cancel);
                if (flagged == true) await folder.AddFlagsAsync(uid, MessageFlags.Flagged, true, cancel);
                if (flagged == false) await folder.RemoveFlagsAsync(uid, MessageFlags.Flagged, true, cancel);
                return 0;
            }, cancel);
        }

        public async Task MarkAnsweredAsync(long folderId, uint uid, CancellationToken cancel)
        {
            Store.SetFlags(folderId, uid, answered: true);
            await WithImap(async client =>
            {
                var folder = await OpenFolder(client, folderId, FolderAccess.ReadWrite, cancel);
                await folder.AddFlagsAsync(new UniqueId(uid), MessageFlags.Answered, true, cancel);
                return 0;
            }, cancel);
        }

        /// <summary>Sposta nella cartella indicata (sul server e poi nell'archivio locale, alla prossima sincronizzazione).</summary>
        public async Task MoveAsync(MessageRow row, long targetFolderId, CancellationToken cancel)
        {
            await WithImap(async client =>
            {
                var target = Store.FolderById(targetFolderId) ?? throw new FolderNotFoundException("?");
                var folder = await OpenFolder(client, row.FolderId, FolderAccess.ReadWrite, cancel);
                var destination = await client.GetFolderAsync(target.Path, cancel);
                await folder.MoveToAsync(new UniqueId(row.Uid), destination, cancel);
                return 0;
            }, cancel);
            Store.RemoveUids(row.FolderId, new[] { row.Uid });
        }

        /// <summary>
        /// Elimina: dal Cestino (o se il Cestino non c'è) per sempre, altrimenti spostando nel Cestino.
        /// Restituisce true se il messaggio è sparito per sempre.
        /// </summary>
        public async Task<bool> DeleteAsync(MessageRow row, CancellationToken cancel)
        {
            var folders = Store.Folders(Account.Id);
            var trash = folders.FirstOrDefault(f => f.Kind == FolderKind.Trash);
            var current = folders.FirstOrDefault(f => f.Id == row.FolderId);
            if (trash != null && current?.Kind != FolderKind.Trash)
            {
                await MoveAsync(row, trash.Id, cancel);
                return false;
            }
            await WithImap(async client =>
            {
                var folder = await OpenFolder(client, row.FolderId, FolderAccess.ReadWrite, cancel);
                var uid = new UniqueId(row.Uid);
                await folder.AddFlagsAsync(uid, MessageFlags.Deleted, true, cancel);
                if (client.Capabilities.HasFlag(ImapCapabilities.UidPlus)) await folder.ExpungeAsync(new[] { uid }, cancel);
                else await folder.ExpungeAsync(cancel);
                return 0;
            }, cancel);
            Store.RemoveUids(row.FolderId, new[] { row.Uid });
            return true;
        }

        // ---- Invio ---------------------------------------------------------------------------------------

        private async Task<SmtpClient> Smtp(CancellationToken cancel)
        {
            var smtp = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
            try
            {
                await smtp.ConnectAsync(Account.Smtp.Host, Account.Smtp.Port, Options(Account.Smtp.Security), cancel);
                if (await OAuth(cancel) is { } sasl) await smtp.AuthenticateAsync(sasl, cancel);
                else await smtp.AuthenticateAsync(Account.UserName, Password(), cancel);
                return smtp;
            }
            catch
            {
                smtp.Dispose();
                throw;
            }
        }

        /// <summary>Invia e, se il server non lo fa da sé, mette una copia nella cartella Inviati.</summary>
        public async Task SendAsync(MimeMessage message, CancellationToken cancel)
        {
            try
            {
                using var smtp = await Smtp(cancel);
                await smtp.SendAsync(message, cancel);
                await smtp.DisconnectAsync(true, cancel);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { throw MailException.Wrap(ex, Account.Smtp.Host); }

            if (Account.ServerSavesSent) return;
            var sent = Store.Folders(Account.Id).FirstOrDefault(f => f.Kind == FolderKind.Sent);
            if (sent == null) return;
            try
            {
                await WithImap(async client =>
                {
                    var folder = await client.GetFolderAsync(sent.Path, cancel);
                    await folder.AppendAsync(new AppendRequest(message, MessageFlags.Seen), cancel);
                    return 0;
                }, cancel);
            }
            catch (MailException)
            {
                // il messaggio è partito: una copia mancante in Inviati non è un errore da mostrare come "non inviato"
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_imap != null)
                {
                    try { if (_imap.IsConnected) await _imap.DisconnectAsync(true); } catch { }
                    _imap.Dispose();
                    _imap = null;
                }
            }
            finally { _gate.Release(); }
        }
    }
}
