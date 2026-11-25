using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DockStudio.Core.Updates;

namespace DockStudio.Launcher
{
    /// <summary>Un'installazione o un aggiornamento in corso (o appena fallito) per una scheda.</summary>
    public sealed record Job(InstallPhase Phase, double Value, string? Error = null)
    {
        public bool Running => Error == null;
    }

    /// <summary>
    /// Lo stato del launcher: le app del catalogo, quelle installate, gli aggiornamenti trovati e le installazioni
    /// in corso. La finestra e il menu dell'area di notifica si ridisegnano a ogni <see cref="Changed"/>
    /// (sempre sul thread dell'interfaccia).
    /// </summary>
    public sealed class Library
    {
        private readonly Dictionary<string, UpdateInfo> _updates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Job> _jobs = new(StringComparer.OrdinalIgnoreCase);

        public Library(IReadOnlyList<CatalogApp>? apps = null)
        {
            Apps = apps ?? Catalog.Local();
            Installed = Installs.Find(Apps);
        }

        public IReadOnlyList<CatalogApp> Apps { get; private set; }
        public IReadOnlyDictionary<string, InstalledApp> Installed { get; private set; }
        public IReadOnlyDictionary<string, UpdateInfo> Updates => _updates;
        public IReadOnlyDictionary<string, Job> Jobs => _jobs;

        /// <summary>Nuova versione del launcher stesso, se c'è.</summary>
        public UpdateInfo? LauncherUpdate { get; private set; }

        /// <summary>Qualcosa è cambiato: elenco, app installate, aggiornamenti o avanzamento.</summary>
        public event Action? Changed;

        /// <summary>Solo l'avanzamento di un'installazione è cambiato (per non ridisegnare tutto).</summary>
        public event Action<string>? Progress;

        public CatalogApp? App(string slug) => Apps.FirstOrDefault(a => string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));

        /// <summary>Aggiornamenti delle app installate (senza quelli in corso).</summary>
        public int PendingUpdates => _updates.Keys.Count(k => Installed.ContainsKey(k) && !_jobs.ContainsKey(k));

        /// <summary>Rilegge le app installate (per esempio dopo un'installazione o al ritorno della finestra).</summary>
        public void RefreshInstalled()
        {
            Installed = Installs.Find(Apps);
            foreach (var slug in _updates.Keys.ToList())
                if (!Installed.TryGetValue(slug, out var i) || i.Version >= _updates[slug].Version) _updates.Remove(slug);
            Raise();
        }

        /// <summary>Prende l'elenco aggiornato dal sito (nuove app, cambi di stato).</summary>
        public async Task RefreshCatalogAsync(CancellationToken ct = default)
        {
            if (await Catalog.RefreshAsync(ct) is not { } apps) return;
            Apps = apps;
            RefreshInstalled();
        }

        /// <summary>
        /// Cerca le nuove versioni di tutte le app installate e del launcher. Restituisce quante app (launcher
        /// compreso) hanno un aggiornamento; gli errori di rete si ignorano app per app.
        /// </summary>
        public async Task<int> CheckUpdatesAsync(CancellationToken ct = default)
        {
            var checks = Installed.Select(async pair =>
            {
                var app = App(pair.Key);
                if (app == null) return;
                try
                {
                    var update = await app.Updates.CheckAsync(pair.Value.Version, ct);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (update != null) _updates[pair.Key] = update;
                        else _updates.Remove(pair.Key);
                    });
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // sito irraggiungibile per questa app: si riprova la volta dopo
                }
            }).ToList();
            try { LauncherUpdate = await AppInfo.Updates.CheckAsync(ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { /* idem */ }
            await Task.WhenAll(checks);
            await Dispatcher.UIThread.InvokeAsync(Raise);
            return PendingUpdates + (LauncherUpdate != null ? 1 : 0);
        }

        /// <summary>Installa l'app (o la aggiorna all'ultima versione), con l'avanzamento nella sua scheda.</summary>
        public async Task<bool> InstallAsync(string slug, CancellationToken ct = default)
        {
            if (App(slug) is not { } app || (_jobs.TryGetValue(slug, out var running) && running.Running)) return false;
            Installed.TryGetValue(slug, out var installed);
            _updates.TryGetValue(slug, out var known);
            _jobs[slug] = new Job(InstallPhase.Checking, 0);
            Raise();
            var progress = new Progress<(InstallPhase Phase, double Value)>(p =>
            {
                if (!_jobs.TryGetValue(slug, out var j) || !j.Running) return;
                bool phaseChanged = j.Phase != p.Phase;
                _jobs[slug] = new Job(p.Phase, p.Value);
                if (phaseChanged) Raise();
                else Progress?.Invoke(slug);
            });
            try
            {
                await Installs.InstallAsync(app, installed, progress, ct, known);
                _jobs.Remove(slug);
                _updates.Remove(slug);
                RefreshInstalled();
                return true;
            }
            catch (OperationCanceledException)
            {
                _jobs.Remove(slug);
                Raise();
                return false;
            }
            catch (Exception ex)
            {
                _ = AppInfo.Errors.SendAsync(LauncherApp.Settings, ex);
                _jobs[slug] = new Job(InstallPhase.Checking, 0, ex.Message);
                Raise();
                return false;
            }
        }

        /// <summary>Aggiorna una dopo l'altra tutte le app con una nuova versione.</summary>
        public async Task UpdateAllAsync()
        {
            foreach (var slug in _updates.Keys.Where(k => Installed.ContainsKey(k)).ToList())
                await InstallAsync(slug);
        }

        /// <summary>Toglie il messaggio di errore di una scheda.</summary>
        public void Dismiss(string slug)
        {
            if (_jobs.TryGetValue(slug, out var j) && !j.Running) _jobs.Remove(slug);
            Raise();
        }

        private void Raise() => Changed?.Invoke();

        /// <summary>Solo per i controlli automatici: uno stato preciso senza toccare il computer né la rete.</summary>
        internal void SetForChecks(IReadOnlyDictionary<string, InstalledApp> installed, IDictionary<string, UpdateInfo> updates, IDictionary<string, Job> jobs)
        {
            Installed = installed;
            _updates.Clear();
            foreach (var u in updates) _updates[u.Key] = u.Value;
            _jobs.Clear();
            foreach (var j in jobs) _jobs[j.Key] = j.Value;
            Raise();
        }
    }
}
