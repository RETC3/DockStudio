using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using DockStudio.Core;

namespace DockStudio.Core.Windows
{
    /// <summary>
    /// Una sola copia dell'app per utente. Se si riavvia l'exe (per esempio dall'icona sul desktop) con
    /// l'app già attiva (per esempio nell'area di notifica), la nuova copia avvisa quella in esecuzione, che si mostra,
    /// e poi si chiude.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly string _showEventName;
        private EventWaitHandle? _showEvent;
        private RegisteredWaitHandle? _wait;

        private SingleInstance(Mutex mutex, string showEventName)
        {
            _mutex = mutex;
            _showEventName = showEventName;
        }

        // I nomi restano quelli delle versioni già uscite: così una copia nuova riconosce anche una vecchia.
        private static string MutexName(AppIdentity app) => app.Name + ".SingleInstance";
        private static string ShowEventName(AppIdentity app) => app.Name + ".ShowWorkspace";

        /// <summary>La copia principale, oppure null se un'altra copia è già in esecuzione.</summary>
        public static SingleInstance? TryAcquire(AppIdentity app)
        {
            var mutex = new Mutex(true, MutexName(app), out bool created);
            if (created)
                return new SingleInstance(mutex, ShowEventName(app));
            mutex.Dispose();
            return null;
        }

        /// <summary>Copia principale: <paramref name="onShowRequested"/> viene chiamato (su un thread del pool) a ogni richiesta.</summary>
        public void ListenForShowRequests(Action onShowRequested)
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _showEventName);
            _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        /// <summary>
        /// Copia principale: come <see cref="ListenForShowRequests(Action)"/>, ma riceve anche gli argomenti con cui
        /// è stata avviata la seconda copia (per esempio i file da aprire: doppio clic su un PDF con l'app già aperta).
        /// Ogni richiesta arriva una volta; più richieste ravvicinate possono arrivare insieme, nell'ordine.
        /// </summary>
        public void ListenForShowRequests(Action<string[]> onShowRequested, AppIdentity app)
        {
            var folder = RequestsFolder(app);
            ListenForShowRequests(() =>
            {
                var requests = TakeRequests(folder);
                if (requests.Count == 0) onShowRequested(Array.Empty<string>());
                foreach (var args in requests) onShowRequested(args);
            });
        }

        /// <summary>Seconda copia: passa i suoi argomenti a quella in esecuzione e le chiede di mostrarsi.</summary>
        public static bool RequestShow(AppIdentity app, string[] args)
        {
            if (args.Length > 0)
            {
                try
                {
                    var folder = RequestsFolder(app);
                    Directory.CreateDirectory(folder);
                    // Un file per richiesta: con più file aperti insieme Windows avvia più copie in parallelo.
                    var name = $"{DateTime.UtcNow.Ticks:D20}-{Guid.NewGuid():N}";
                    File.WriteAllText(Path.Combine(folder, name + ".tmp"), JsonSerializer.Serialize(args));
                    File.Move(Path.Combine(folder, name + ".tmp"), Path.Combine(folder, name + ".json"));
                }
                catch (Exception)
                {
                    // senza argomenti l'app si mostra comunque
                }
            }
            return RequestShow(app);
        }

        private static string RequestsFolder(AppIdentity app) => Path.Combine(Path.GetTempPath(), app.Name + ".Requests");

        private static System.Collections.Generic.List<string[]> TakeRequests(string folder)
        {
            var list = new System.Collections.Generic.List<string[]>();
            try
            {
                if (!Directory.Exists(folder)) return list;
                foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                {
                    try
                    {
                        // Le richieste vecchie (app chiusa prima di leggerle) non si eseguono più.
                        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromMinutes(2)
                            && JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) is { } args)
                            list.Add(args);
                    }
                    catch (Exception) { /* richiesta illeggibile */ }
                    try { File.Delete(file); } catch (Exception) { }
                }
            }
            catch (Exception) { }
            return list;
        }

        /// <summary>Seconda copia: chiede a quella in esecuzione di mostrarsi. False se non risponde.</summary>
        public static bool RequestShow(AppIdentity app)
        {
            // La copia in esecuzione può portare la sua finestra in primo piano: l'utente ha appena aperto l'exe.
            AllowSetForegroundWindow(ASFW_ANY);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                // L'evento può mancare per un attimo se l'altra copia si sta ancora avviando.
                if (EventWaitHandle.TryOpenExisting(ShowEventName(app), out var ev))
                {
                    using (ev)
                        ev.Set();
                    return true;
                }
                Thread.Sleep(250);
            }
            return false;
        }

        public void Dispose()
        {
            _wait?.Unregister(null);
            _showEvent?.Dispose();
            _mutex.Dispose();
        }

        private const int ASFW_ANY = -1;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }
}
