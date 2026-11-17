using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using Windows.Media.Capture.Frames;

namespace ScreenLove.Services
{
    /// <summary>Un dispositivo scelto nelle impostazioni. Id null = quello predefinito di Windows.</summary>
    public sealed record MediaDevice(string? Id, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>Elenco di microfoni, uscite audio e webcam, e ricerca del dispositivo scelto.</summary>
    public static class MediaDevices
    {
        public const string DefaultName = "Predefinito di Windows";

        /// <summary>Uscite audio (altoparlanti, cuffie…), per registrare l'audio di sistema.</summary>
        public static List<MediaDevice> Outputs() => Endpoints(DataFlow.Render);

        /// <summary>Microfoni.</summary>
        public static List<MediaDevice> Microphones() => Endpoints(DataFlow.Capture);

        private static List<MediaDevice> Endpoints(DataFlow flow)
        {
            var list = new List<MediaDevice> { new(null, DefaultName) };
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var d in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                {
                    using (d)
                        list.Add(new MediaDevice(d.ID, d.FriendlyName));
                }
            }
            catch
            {
                // audio di Windows non disponibile: resta solo "predefinito"
            }
            ReleaseComLeftovers();
            return list;
        }

        /// <summary>
        /// NAudio non rilascia subito tutti gli oggetti COM dell'audio di Windows (per esempio l'elenco dei
        /// dispositivi). Lasciati al finalizzatore, in alcune sessioni (es. Desktop remoto) fanno fallire il
        /// disegno delle immagini con "memoria insufficiente": li si rilascia subito. Succede di rado
        /// (apertura delle impostazioni, inizio registrazione), quindi il costo è trascurabile.
        /// </summary>
        private static void ReleaseComLeftovers()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        /// <summary>
        /// Dispositivo audio con l'id scelto, oppure quello predefinito (anche se l'id non esiste più:
        /// in quel caso <paramref name="missing"/> è true).
        /// </summary>
        public static MMDevice OpenAudio(string? id, DataFlow flow, out bool missing)
        {
            try
            {
                return OpenAudioCore(id, flow, out missing);
            }
            finally
            {
                ReleaseComLeftovers(); // dopo la chiusura dell'enumeratore
            }
        }

        private static MMDevice OpenAudioCore(string? id, DataFlow flow, out bool missing)
        {
            missing = false;
            using var enumerator = new MMDeviceEnumerator();
            if (!string.IsNullOrEmpty(id))
            {
                try
                {
                    var device = enumerator.GetDevice(id);
                    if (device.State == DeviceState.Active)
                        return device;
                    device.Dispose();
                }
                catch
                {
                    // scollegato o rimosso
                }
                missing = true;
            }
            return enumerator.GetDefaultAudioEndpoint(flow, flow == DataFlow.Render ? Role.Multimedia : Role.Console);
        }

        /// <summary>Webcam (gruppi di sorgenti con un flusso a colori).</summary>
        public static async Task<List<MediaDevice>> WebcamsAsync()
        {
            var list = new List<MediaDevice> { new(null, DefaultName) };
            try
            {
                foreach (var g in await MediaFrameSourceGroup.FindAllAsync())
                    if (g.SourceInfos.Any(i => i.SourceKind == MediaFrameSourceKind.Color))
                        list.Add(new MediaDevice(g.Id, g.DisplayName));
            }
            catch
            {
                // nessun accesso alle webcam: resta solo "predefinito"
            }
            return list;
        }
    }
}
