using System;
using System.Runtime.InteropServices;
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
