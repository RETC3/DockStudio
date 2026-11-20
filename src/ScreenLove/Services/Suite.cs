using System;
using System.Threading.Tasks;
using DockStudio.Core.Licensing;
using DockStudio.Core.Suite;

namespace ScreenLove.Services
{
    /// <summary>ScreenLove dentro la suite DockStudio: iscrizione al registro delle app e licenza.</summary>
    public static class Suite
    {
        public static readonly LicenseManager Licenses = new(Updater.Identity.Name);

        /// <summary>
        /// Iscrive ScreenLove nel registro delle app DockStudio dell'utente, in sottofondo: non rallenta
        /// l'avvio e un errore non arriva mai all'utente.
        /// </summary>
        public static void Register()
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            Task.Run(() => new SuiteRegistry().Register(Updater.Identity.Name, Updater.CurrentVersion, exe));
        }

        /// <summary>La licenza in uso (versione gratuita se non c'è).</summary>
        public static LicenseCheck License => Licenses.Check();
    }
}
