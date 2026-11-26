using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DockPost.Engine;
using MimeKit;

namespace DockPost.Mail
{
    /// <summary>
    /// Tutto quello che l'interfaccia usa della posta: gli account, l'archivio locale, una connessione per account,
    /// la sincronizzazione e le azioni sui messaggi. In modalità prova (<see cref="Offline"/>) lavora solo
    /// sull'archivio locale, senza server.
    /// </summary>
    public sealed class MailSession : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<string, MailClient> _clients = new();
        private int _syncing;

        public MailSession(string dataFolder, ISecretStore secrets, MicrosoftSignIn? microsoft, bool offline = false)
        {
            DataFolder = dataFolder;
            Secrets = secrets;
            Microsoft = microsoft;
            Offline = offline;
            Accounts = new AccountList(Path.Combine(dataFolder, "accounts.json"));
            Store = new MailStore(Path.Combine(dataFolder, "mail"));
        }

        public string DataFolder { get; }
        public ISecretStore Secrets { get; }
        public MicrosoftSignIn? Microsoft { get; }
        public bool Offline { get; }
        public AccountList Accounts { get; }
        public MailStore Store { get; }

        /// <summary>L'archivio è cambiato (posta nuova, letti, spostati): l'interfaccia ricarica. Arriva da qualsiasi thread.</summary>
        public event Action? Changed;

        /// <summary>Un'operazione in background non è riuscita (account, errore).</summary>
        public event Action<Account, Exception>? Failed;

        public event Action<SyncProgress>? Progress;

        public bool IsSyncing => Volatile.Read(ref _syncing) > 0;

        public MailClient Client(Account account) =>
            _clients.GetOrAdd(account.Id, _ => new MailClient(account, Store, Secrets, Microsoft));

        private MailClient? ClientFor(long folderId)
        {
            if (Offline) return null;
            var folder = Store.FolderById(folderId);
            var account = folder == null ? null : Accounts.Find(folder.AccountId);
            return account == null ? null : Client(account);
        }

        public Account? AccountOfFolder(long folderId) =>
            Store.FolderById(folderId) is { } f ? Accounts.Find(f.AccountId) : null;

        // ---- Account -------------------------------------------------------------------------------------

        public void AddAccount(Account account, string? password)
        {
            if (password != null) Secrets.Write(account.SecretKey, password);
            Accounts.Add(account);
            _clients.TryRemove(account.Id, out _);
            Changed?.Invoke();
        }

        public async Task RemoveAccountAsync(Account account)
        {
            if (_clients.TryRemove(account.Id, out var client)) await client.DisposeAsync();
            Secrets.Delete(account.SecretKey);
            if (account.Auth == AuthKind.Microsoft && Microsoft != null)
            {
                try { await Microsoft.SignOutAsync(account.UserName); } catch { /* la cache verrà ripulita al prossimo accesso */ }
            }
            Accounts.Remove(account.Id);
            await Task.Run(() => Store.RemoveAccount(account.Id));
            Changed?.Invoke();
        }

        // ---- Sincronizzazione ----------------------------------------------------------------------------

        /// <summary>Invia/Ricevi per tutti gli account, insieme. Gli errori arrivano con <see cref="Failed"/>.</summary>
        public async Task SyncAllAsync(CancellationToken cancel)
        {
            if (Offline || Interlocked.Increment(ref _syncing) > 1)
            {
                if (!Offline) Interlocked.Decrement(ref _syncing);
                return;
            }
            try
            {
                var progress = new Progress<SyncProgress>(p => Progress?.Invoke(p));
                await Task.WhenAll(Accounts.Items.ToList().Select(async account =>
                {
                    try
                    {
                        int added = await Task.Run(() => Client(account).SyncAsync(new DirectProgress(p => Progress?.Invoke(p)), cancel), cancel);
                        Changed?.Invoke();
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Failed?.Invoke(account, ex);
                    }
                }));
            }
            finally
            {
                Interlocked.Decrement(ref _syncing);
                Changed?.Invoke();
            }
        }

        private sealed class DirectProgress : IProgress<SyncProgress>
        {
            private readonly Action<SyncProgress> _report;
            public DirectProgress(Action<SyncProgress> report) => _report = report;
            public void Report(SyncProgress value) => _report(value);
        }

        // ---- Azioni --------------------------------------------------------------------------------------

        /// <summary>Messaggio completo: dall'archivio se c'è, altrimenti dal server.</summary>
        public async Task<MessageContent?> OpenAsync(MessageRow row, CancellationToken cancel)
        {
            var path = Store.BodyPath(row.Id);
            if (path == null)
            {
                var client = ClientFor(row.FolderId);
                if (client == null) return null;
                path = await client.BodyAsync(row, cancel);
            }
            return await Task.Run(() => MessageContent.Load(path), cancel);
        }

        public async Task SetFlagsAsync(MessageRow row, bool? seen, bool? flagged)
        {
            var client = ClientFor(row.FolderId);
            if (client == null) Store.SetFlags(row.FolderId, row.Uid, seen, flagged);
            else
            {
                try { await client.SetFlagsAsync(row, seen, flagged, CancellationToken.None); }
                catch (Exception ex) when (AccountOfFolder(row.FolderId) is { } a) { Failed?.Invoke(a, ex); }
            }
            Changed?.Invoke();
        }

        /// <summary>Elimina (nel Cestino, o per sempre dal Cestino). Restituisce true se è sparito per sempre.</summary>
        public async Task<bool> DeleteAsync(MessageRow row)
        {
            var client = ClientFor(row.FolderId);
            bool gone;
            if (client == null)
            {
                var account = AccountOfFolder(row.FolderId);
                var trash = account == null ? null : Store.Folders(account.Id).FirstOrDefault(f => f.Kind == FolderKind.Trash);
                Store.RemoveUids(row.FolderId, new[] { row.Uid });
                gone = trash == null || trash.Id == row.FolderId;
            }
            else gone = await client.DeleteAsync(row, CancellationToken.None);
            Changed?.Invoke();
            return gone;
        }

        public async Task MoveAsync(MessageRow row, long targetFolder)
        {
            var client = ClientFor(row.FolderId);
            if (client == null) Store.RemoveUids(row.FolderId, new[] { row.Uid });
            else await client.MoveAsync(row, targetFolder, CancellationToken.None);
            Changed?.Invoke();
        }

        public async Task SendAsync(Draft draft, CancellationToken cancel)
        {
            var account = Accounts.Find(draft.AccountId) ?? throw new InvalidOperationException("account");
            var message = Composer.Build(draft, account);
            if (Offline) return;
            var client = Client(account);
            await client.SendAsync(message, cancel);
            if (draft.Answers is { } answered)
            {
                try { await client.MarkAnsweredAsync(answered.Folder, answered.Uid, cancel); }
                catch (Exception) { /* il messaggio è partito; il segno "risposto" non è essenziale */ }
            }
            Changed?.Invoke();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var c in _clients.Values) await c.DisposeAsync();
            _clients.Clear();
        }
    }
}
