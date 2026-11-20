using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DockStudio.Core;
using DockStudio.Core.Updates;
using ScreenLove.Services;

namespace ScreenLove
{
    /// <summary>Impostazioni utente, salvate in %APPDATA%\ScreenLove\settings.json.</summary>
    public sealed class AppSettings : IUsageStatsSettings
    {
        public string CapturesFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenLove");

        public bool CopyToClipboard { get; set; } = true;
        public bool OpenEditorAfterCapture { get; set; } = false;
        public bool ShowWorkspaceAfterCapture { get; set; } = true;
        public bool IncludeCursor { get; set; } = false;

        /// <summary>Schermo intero con più monitor: chiedi quale catturare (altrimenti tutti).</summary>
        public bool FullScreenChooseMonitor { get; set; } = true;
        public int DelaySeconds { get; set; } = 3;

        /// <summary>Finestra delle catture: pannello a destra con l'anteprima grande della cattura selezionata.</summary>
        public bool WorkspacePreview { get; set; } = false;

        // Registrazione video
        public int RecordFps { get; set; } = 30;
        public bool RecordCursor { get; set; } = true;
        public bool RecordSystemAudio { get; set; } = true;
        public bool RecordMicrophone { get; set; } = false;
        public bool RecordWebcam { get; set; } = false;

        // Editor
        public string WatermarkText { get; set; } = "ScreenLove";

        // Aggiornamenti
        public bool CheckForUpdates { get; set; } = true;
        public DateTime? LastUpdateCheck { get; set; }

        /// <summary>
        /// True quando l'utente ha scelto cosa condividere (installer, finestra al primo avvio o impostazioni).
        /// Finché è false non si invia nulla, nemmeno con le opzioni qui sotto attive.
        /// </summary>
        public bool PrivacyChoiceMade { get; set; } = false;

        /// <summary>Statistiche d'uso anonime insieme al controllo degli aggiornamenti (vedi Services.UsageStats).</summary>
        public bool SendUsageStats { get; set; } = false;

        /// <summary>Segnalazioni automatiche degli errori (vedi Services.ErrorReports).</summary>
        public bool SendErrorReports { get; set; } = false;

        /// <summary>Minuti di utilizzo dall'ultimo invio delle statistiche.</summary>
        public int UsageMinutes { get; set; }

        /// <summary>Codice casuale dell'installazione per le statistiche; null finché non serve.</summary>
        public string? InstallId { get; set; }

        /// <summary>Tasti rapidi: nome dell'azione → combinazione (vedi Services.HotkeyBinding).</summary>
        public Dictionary<string, string> Hotkeys { get; set; } = new();

        public Services.HotkeyBinding GetHotkey(Services.HotkeyAction action) =>
            Services.HotkeyBinding.Parse(Hotkeys.TryGetValue(action.ToString(), out var s) ? s : Services.HotkeyBinding.Defaults[action]);

        public void SetHotkey(Services.HotkeyAction action, Services.HotkeyBinding binding) =>
            Hotkeys[action.ToString()] = binding.ToString();

        public AppSettings Clone() =>
            JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this)) ?? new AppSettings();

        public void CopyFrom(AppSettings other)
        {
            foreach (var p in typeof(AppSettings).GetProperties())
                if (p.CanWrite) p.SetValue(this, p.GetValue(other));
            Hotkeys = new Dictionary<string, string>(other.Hotkeys);
        }

        private static readonly JsonSettingsStore<AppSettings> Store = new(Updater.Identity);

        public static AppSettings Load() => Store.Load();

        public void Save() => Store.Save(this);
    }
}
