using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using DockPost.Engine;

namespace DockPost.Platform
{
    /// <summary>
    /// Password nel Gestore credenziali di Windows (credenziali generiche "DockPost/&lt;id account&gt;"): restano
    /// legate all'utente di Windows e non finiscono mai nei file di DockPost.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsCredentialStore : ISecretStore
    {
        private const int CredTypeGeneric = 1;
        private const int CredPersistLocalMachine = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string? Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        public string? Read(string key)
        {
            if (!CredRead(key, CredTypeGeneric, 0, out var ptr)) return null;
            try
            {
                var c = Marshal.PtrToStructure<Credential>(ptr);
                if (c.CredentialBlob == IntPtr.Zero || c.CredentialBlobSize == 0) return "";
                var bytes = new byte[c.CredentialBlobSize];
                Marshal.Copy(c.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            finally { CredFree(ptr); }
        }

        public void Write(string key, string secret)
        {
            var bytes = Encoding.Unicode.GetBytes(secret);
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var c = new Credential
                {
                    Type = CredTypeGeneric,
                    TargetName = key,
                    CredentialBlob = blob,
                    CredentialBlobSize = bytes.Length,
                    Persist = CredPersistLocalMachine,
                    UserName = "DockPost",
                };
                if (!CredWrite(ref c, 0)) throw new IOException("CredWrite " + Marshal.GetLastWin32Error());
            }
            finally
            {
                // la password non resta nella memoria non gestita
                Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
                Marshal.FreeHGlobal(blob);
            }
        }

        public void Delete(string key) => CredDelete(key, CredTypeGeneric, 0);
    }

    /// <summary>Un file cifrato con l'account di Windows (DPAPI): per la cache dei token Microsoft.</summary>
    [SupportedOSPlatform("windows")]
    public sealed class ProtectedFile
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DockPost token cache");

        public ProtectedFile(string path) => Path = path;

        public string Path { get; }

        public byte[]? Load()
        {
            try
            {
                return File.Exists(Path) ? ProtectedData.Unprotect(File.ReadAllBytes(Path), Entropy, DataProtectionScope.CurrentUser) : null;
            }
            catch (Exception ex) when (ex is CryptographicException or IOException)
            {
                return null;
            }
        }

        public void Save(byte[] data)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllBytes(temp, ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser));
            File.Move(temp, Path, overwrite: true);
        }
    }
}
