using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;

namespace DockSpace.Engine
{
    /// <summary>
    /// Scansione classica di una cartella o di un disco, su qualsiasi sistema e senza permessi speciali.
    /// Legge le cartelle in parallelo; non segue collegamenti simbolici e giunzioni (per non contare due volte
    /// lo stesso contenuto), ma entra nelle cartelle di OneDrive e degli altri servizi cloud.
    /// </summary>
    public static class FolderScanner
    {
        /// <summary>Fino a questa profondità le sottocartelle si leggono in parallelo.</summary>
        private const int ParallelDepth = 3;

        private readonly record struct Entry(string Name, bool IsDirectory, long Length, DateTime LastWriteUtc, FileAttributes Attributes);

        private static readonly EnumerationOptions Options = new()
        {
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
        };

        /// <summary>Legge tutto il contenuto di <paramref name="path"/>. Annullabile con <paramref name="cancel"/>.</summary>
        public static FsNode Scan(string path, ScanProgress? progress = null, CancellationToken cancel = default)
        {
            progress ??= new ScanProgress();
            var full = Path.GetFullPath(path);
            var info = new DirectoryInfo(full);
            if (!info.Exists) throw new DirectoryNotFoundException(full);

            var root = new FsNode(full, isDirectory: true, parent: null) { LastWriteUtc = SafeLastWrite(info) };
            progress.AddFolder();
            try { ScanDirectory(root, full, 0, progress, cancel); }
            catch (AggregateException) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancel);
            }
            cancel.ThrowIfCancellationRequested();
            FsTree.Complete(root);
            return root;
        }

        private static void ScanDirectory(FsNode dir, string path, int depth, ScanProgress progress, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            progress.CurrentPath = path;

            List<(FsNode Node, string Path)>? subdirs = null;
            try
            {
                var entries = new FileSystemEnumerable<Entry>(path,
                    (ref FileSystemEntry e) => new Entry(e.FileName.ToString(), e.IsDirectory, e.Length, e.LastWriteTimeUtc.UtcDateTime, e.Attributes),
                    Options);
                foreach (var e in entries)
                {
                    if (e.IsDirectory)
                    {
                        var childPath = Path.Join(path, e.Name);
                        if ((e.Attributes & FileAttributes.ReparsePoint) != 0 && IsLink(childPath))
                            continue;
                        var child = new FsNode(e.Name, isDirectory: true, dir) { LastWriteUtc = e.LastWriteUtc };
                        dir.AddChild(child);
                        progress.AddFolder();
                        (subdirs ??= new()).Add((child, childPath));
                    }
                    else
                    {
                        dir.AddChild(new FsNode(e.Name, isDirectory: false, dir) { Size = e.Length, LastWriteUtc = e.LastWriteUtc });
                        progress.AddFile(e.Length);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                // Cartella protetta (es. System Volume Information) o sparita durante la scansione.
                dir.Inaccessible = true;
            }

            if (subdirs == null) return;
            if (depth < ParallelDepth && subdirs.Count > 1)
            {
                Parallel.ForEach(subdirs, new ParallelOptions { CancellationToken = cancel },
                    s => ScanDirectory(s.Node, s.Path, depth + 1, progress, cancel));
            }
            else
            {
                foreach (var s in subdirs)
                    ScanDirectory(s.Node, s.Path, depth + 1, progress, cancel);
            }
        }

        /// <summary>
        /// Collegamento simbolico o giunzione (da saltare). I segnaposto dei servizi cloud sono anch'essi punti di
        /// rianalisi ma non hanno una destinazione: quelli si leggono normalmente.
        /// </summary>
        private static bool IsLink(string path)
        {
            try { return new DirectoryInfo(path).LinkTarget != null; }
            catch { return true; }
        }

        private static DateTime SafeLastWrite(FileSystemInfo info)
        {
            try { return info.LastWriteTimeUtc; }
            catch { return DateTime.MinValue; }
        }
    }
}
