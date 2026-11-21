using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DockSpace
{
    /// <summary>Azioni sul sistema: aprire, mostrare nella cartella, spostare nel Cestino, riavviare come amministratore.</summary>
    public static class Shell
    {
        public static void Open(string path)
        {
            if (OperatingSystem.IsMacOS()) Run("open", path);
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        /// <summary>Apre la cartella che contiene l'elemento, con l'elemento selezionato.</summary>
        public static void Reveal(string path)
        {
            if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (OperatingSystem.IsMacOS()) Run("open", "-R", path);
            else Open(Path.GetDirectoryName(path) ?? path);
        }

        /// <summary>Sposta un file o una cartella nel Cestino (recuperabile). Lancia un'eccezione se non riesce.</summary>
        public static void MoveToTrash(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                var op = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    pFrom = path + "\0\0",
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
                };
                int result = SHFileOperationW(ref op);
                if (result != 0) throw new IOException(new Win32Exception(result).Message);
                if (op.fAnyOperationsAborted) throw new OperationCanceledException();
                if (File.Exists(path) || Directory.Exists(path)) throw new IOException("L'elemento è ancora al suo posto.");
            }
            else if (OperatingSystem.IsMacOS())
            {
                var trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
                var target = Path.Combine(trash, Path.GetFileName(path));
                if (File.Exists(target) || Directory.Exists(target))
                    target = Path.Combine(trash, $"{Path.GetFileNameWithoutExtension(path)} {DateTime.Now:HH.mm.ss}{Path.GetExtension(path)}");
                if (Directory.Exists(path)) Directory.Move(path, target);
                else File.Move(path, target);
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
        }

        public static bool IsAdministrator
        {
            get
            {
                if (!OperatingSystem.IsWindows()) return false;
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>
        /// Riavvia DockSpace come amministratore (Windows chiede il permesso) con la scansione veloce di
        /// <paramref name="drive"/>. False se l'utente rifiuta.
        /// </summary>
        public static bool RestartElevated(string drive)
        {
            var exe = Environment.ProcessPath;
            if (exe == null || !OperatingSystem.IsWindows()) return false;
            try
            {
                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    ArgumentList = { "--scan", drive, "--fast" },
                });
                return true;
            }
            catch (Win32Exception)
            {
                return false; // l'utente ha risposto No
            }
        }

        /// <summary>True se DockSpace è stato installato con l'installer (accanto all'exe c'è il disinstallatore).</summary>
        public static bool IsInstalled =>
            OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

        /// <summary>
        /// Avvia l'installer scaricato senza domande: aspetta che DockSpace sia chiuso, sostituisce l'exe e lo
        /// riapre (stessi parametri di DockStudio.Core.Windows.InnoSetup). Chi chiama deve chiudere l'app subito dopo.
        /// </summary>
        public static void LaunchUpdate(string setupPath) =>
            Process.Start(new ProcessStartInfo(setupPath, "/SILENT /NOCANCEL /NORESTART /UPDATE=1") { UseShellExecute = true });

        private static void Run(string file, params string[] args)
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }

        private const uint FO_DELETE = 3;
        private const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);
    }
}
