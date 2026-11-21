using System.Threading;

namespace DockSpace.Engine
{
    /// <summary>Avanzamento di una scansione, aggiornato dai thread del motore e letto dall'interfaccia.</summary>
    public sealed class ScanProgress
    {
        private long _files;
        private long _folders;
        private long _bytes;
        private long _done;
        private long _total;

        public long Files => Interlocked.Read(ref _files);
        public long Folders => Interlocked.Read(ref _folders);
        public long Bytes => Interlocked.Read(ref _bytes);

        /// <summary>Cartella in lettura in questo momento (indicativa).</summary>
        public volatile string? CurrentPath;

        /// <summary>Quota completata (0–1) se si conosce il totale, altrimenti null (lettura NTFS: record letti).</summary>
        public double? Fraction
        {
            get
            {
                long total = Interlocked.Read(ref _total);
                return total <= 0 ? null : (double)Interlocked.Read(ref _done) / total;
            }
        }

        internal void AddFile(long bytes)
        {
            Interlocked.Increment(ref _files);
            Interlocked.Add(ref _bytes, bytes);
        }

        internal void AddFiles(long count, long bytes)
        {
            Interlocked.Add(ref _files, count);
            Interlocked.Add(ref _bytes, bytes);
        }

        internal void AddFolder() => Interlocked.Increment(ref _folders);

        internal void AddFolders(long count) => Interlocked.Add(ref _folders, count);

        internal void SetWork(long done, long total)
        {
            Interlocked.Exchange(ref _total, total);
            Interlocked.Exchange(ref _done, done);
        }
    }
}
