using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace DockSpace.Engine.Ntfs
{
    /// <summary>
    /// Scansione veloce di un intero disco NTFS: invece di aprire le cartelle una per una legge direttamente la
    /// tabella dei file del disco (Master File Table), che contiene nome, cartella, dimensione e data di ogni
    /// file. Richiede che l'app sia avviata come amministratore.
    /// <para>
    /// Differenze con la scansione classica: un file con più collegamenti fisici (hard link) si conta una volta
    /// sola, nella sua prima posizione; si vedono anche i file di sistema del disco ($MFT, $LogFile...).
    /// I flussi di dati alternativi non si contano, come nella scansione classica.
    /// </para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class NtfsScanner
    {
        private const int RootRecord = 5;
        private const uint AttrStandardInformation = 0x10;
        private const uint AttrAttributeList = 0x20;
        private const uint AttrFileName = 0x30;
        private const uint AttrData = 0x80;
        private const uint AttrEnd = 0xFFFFFFFF;
        private const byte NamespaceDos = 2;

        /// <summary>True se <paramref name="path"/> è la radice di un disco NTFS (es. "C:\").</summary>
        public static bool IsNtfsDriveRoot(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                var root = Path.GetPathRoot(full);
                if (root == null || root.Length != 3 || !string.Equals(full.TrimEnd('\\') + "\\", root, StringComparison.OrdinalIgnoreCase))
                    return false;
                return string.Equals(new DriveInfo(root).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Legge tutto il disco <paramref name="driveRoot"/> (es. "C:\"). Lancia
        /// <see cref="UnauthorizedAccessException"/> se l'app non è avviata come amministratore e
        /// <see cref="NotSupportedException"/> se il disco non è NTFS o la sua tabella non si riesce a leggere
        /// (in quel caso si usa la scansione classica).
        /// </summary>
        public static FsNode Scan(string driveRoot, ScanProgress? progress = null, CancellationToken cancel = default)
        {
            progress ??= new ScanProgress();
            var root = Path.GetPathRoot(Path.GetFullPath(driveRoot)) ?? throw new ArgumentException(driveRoot);
            if (!IsNtfsDriveRoot(root)) throw new NotSupportedException("Il disco non è NTFS: " + root);
            progress.CurrentPath = root;

            using var volume = OpenVolume(root);
            var table = ReadTable(volume, progress, cancel);
            return BuildTree(root, table, progress, cancel);
        }

        // ---- Lettura del disco --------------------------------------------------------------------------

        private sealed class Table
        {
            public Table(int count)
            {
                Parent = new int[count];
                Name = new string?[count];
                NameNamespace = new byte[count];
                Size = new long[count];
                LastWrite = new long[count];
                Flags = new byte[count];
                Array.Fill(Parent, -1);
            }

            public readonly int[] Parent;
            public readonly string?[] Name;
            public readonly byte[] NameNamespace;
            public readonly long[] Size;
            public readonly long[] LastWrite;

            /// <summary>Bit 0: record in uso; bit 1: cartella; bit 2: ha un nome.</summary>
            public readonly byte[] Flags;
        }

        private readonly record struct Run(long Lcn, long Clusters);

        private sealed class Geometry
        {
            public int BytesPerSector;
            public int ClusterSize;
            public int RecordSize;
            public long MftOffset;
        }

        private static Table ReadTable(SafeFileHandle volume, ScanProgress progress, CancellationToken cancel)
        {
            var geo = ReadBootSector(volume);

            // Il record 0 descrive la tabella stessa: da lì si sa dove sta sul disco e quanto è grande.
            var first = new byte[Align(geo.RecordSize, geo.BytesPerSector)];
            ReadExactly(volume, first, geo.MftOffset);
            if (!ApplyFixups(first.AsSpan(0, geo.RecordSize), geo.BytesPerSector))
                throw new NotSupportedException("Tabella NTFS non leggibile (record 0).");
            var (runs, mftBytes) = MftRuns(first.AsSpan(0, geo.RecordSize));
            if (runs.Count == 0 || mftBytes <= 0)
                throw new NotSupportedException("Tabella NTFS non leggibile (posizione).");

            long countLong = mftBytes / geo.RecordSize;
            if (countLong > int.MaxValue) throw new NotSupportedException("Tabella NTFS troppo grande.");
            int count = (int)countLong;
            var table = new Table(count);

            // Si legge a blocchi grandi; un record può stare a cavallo di due blocchi o di due frammenti.
            const int ChunkSize = 4 * 1024 * 1024;
            var chunk = new byte[ChunkSize];
            var record = new byte[geo.RecordSize];
            int recordFill = 0;
            int index = 0;

            foreach (var run in runs)
            {
                long runOffset = run.Lcn * geo.ClusterSize;
                long runBytes = run.Clusters * geo.ClusterSize;
                for (long pos = 0; pos < runBytes && index < count; pos += ChunkSize)
                {
                    cancel.ThrowIfCancellationRequested();
                    int len = (int)Math.Min(ChunkSize, runBytes - pos);
                    if (run.Lcn < 0) Array.Clear(chunk, 0, len); // frammento vuoto (non dovrebbe esistere)
                    else ReadExactly(volume, chunk.AsSpan(0, len), runOffset + pos);

                    int offset = 0;
                    while (offset < len && index < count)
                    {
                        int take = Math.Min(geo.RecordSize - recordFill, len - offset);
                        Buffer.BlockCopy(chunk, offset, record, recordFill, take);
                        recordFill += take;
                        offset += take;
                        if (recordFill < geo.RecordSize) break;
                        ParseRecord(record, index, table, geo.BytesPerSector);
                        recordFill = 0;
                        index++;
                    }
                    progress.SetWork(index, count);
                }
            }
            return table;
        }

        private static Geometry ReadBootSector(SafeFileHandle volume)
        {
            var boot = new byte[4096];
            ReadExactly(volume, boot.AsSpan(0, 512), 0);
            if (Encoding.ASCII.GetString(boot, 3, 8) != "NTFS    ")
                throw new NotSupportedException("Il disco non è NTFS.");

            int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x0B));
            int sectorsPerCluster = boot[0x0D];
            // Valori oltre 128 indicano 2^(256-valore) settori per cluster (dischi con cluster molto grandi).
            if (sectorsPerCluster > 128) sectorsPerCluster = 1 << (256 - sectorsPerCluster);
            int clusterSize = bytesPerSector * sectorsPerCluster;
            long mftCluster = BinaryPrimitives.ReadInt64LittleEndian(boot.AsSpan(0x30));
            int clustersPerRecord = unchecked((sbyte)boot[0x40]);
            int recordSize = clustersPerRecord > 0 ? clustersPerRecord * clusterSize : 1 << -clustersPerRecord;

            if (bytesPerSector < 512 || clusterSize <= 0 || recordSize < 512 || recordSize > 65536 || mftCluster <= 0)
                throw new NotSupportedException("Settore di avvio NTFS non valido.");

            return new Geometry
            {
                BytesPerSector = bytesPerSector,
                ClusterSize = clusterSize,
                RecordSize = recordSize,
                MftOffset = mftCluster * clusterSize,
            };
        }

        /// <summary>Frammenti dell'attributo $DATA del record 0 (la tabella) e la sua dimensione.</summary>
        private static (List<Run> Runs, long Bytes) MftRuns(ReadOnlySpan<byte> record)
        {
            int attr = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x14));
            while (attr + 16 <= record.Length)
            {
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(attr));
                if (type == AttrEnd) break;
                int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(attr + 4));
                if (length <= 0 || attr + length > record.Length) break;
                if (type == AttrAttributeList)
                    throw new NotSupportedException("Tabella NTFS troppo frammentata.");
                if (type == AttrData && record[attr + 8] != 0 && record[attr + 9] == 0)
                {
                    var a = record.Slice(attr, length);
                    long dataSize = BinaryPrimitives.ReadInt64LittleEndian(a.Slice(0x30));
                    int runsOffset = BinaryPrimitives.ReadUInt16LittleEndian(a.Slice(0x20));
                    return (DecodeRuns(a.Slice(runsOffset)), dataSize);
                }
                attr += length;
            }
            return (new List<Run>(), 0);
        }

        private static List<Run> DecodeRuns(ReadOnlySpan<byte> data)
        {
            var runs = new List<Run>();
            long lcn = 0;
            int p = 0;
            while (p < data.Length && data[p] != 0)
            {
                int lengthBytes = data[p] & 0x0F;
                int offsetBytes = data[p] >> 4;
                p++;
                if (lengthBytes == 0 || lengthBytes > 8 || offsetBytes > 8 || p + lengthBytes + offsetBytes > data.Length) break;
                long clusters = 0;
                for (int i = 0; i < lengthBytes; i++) clusters |= (long)data[p + i] << (8 * i);
                p += lengthBytes;
                if (offsetBytes == 0)
                {
                    runs.Add(new Run(-1, clusters)); // frammento vuoto (file sparso)
                    continue;
                }
                long delta = 0;
                for (int i = 0; i < offsetBytes; i++) delta |= (long)data[p + i] << (8 * i);
                // Estensione del segno: lo spostamento è relativo al frammento precedente e può essere negativo.
                if ((data[p + offsetBytes - 1] & 0x80) != 0 && offsetBytes < 8)
                    delta |= -1L << (8 * offsetBytes);
                p += offsetBytes;
                lcn += delta;
                runs.Add(new Run(lcn, clusters));
            }
            return runs;
        }

        /// <summary>
        /// Ripristina gli ultimi due byte di ogni settore del record (protezione NTFS contro le scritture a metà).
        /// False se il record è danneggiato o non è un record di file.
        /// </summary>
        private static bool ApplyFixups(Span<byte> record, int bytesPerSector)
        {
            if (record.Length < 48 || record[0] != (byte)'F' || record[1] != (byte)'I' || record[2] != (byte)'L' || record[3] != (byte)'E')
                return false;
            int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(4));
            int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(6));
            if (usaCount < 2 || usaOffset + usaCount * 2 > record.Length) return false;
            // La protezione lavora a passi di 512 byte, qualunque sia la dimensione dei settori.
            const int Stride = 512;
            if ((usaCount - 1) * Stride > record.Length) return false;
            ushort usn = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(usaOffset));
            for (int i = 1; i < usaCount; i++)
            {
                int end = i * Stride - 2;
                if (BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(end)) != usn) return false;
                record[end] = record[usaOffset + i * 2];
                record[end + 1] = record[usaOffset + i * 2 + 1];
            }
            return true;
        }

        private static void ParseRecord(byte[] buffer, int index, Table table, int bytesPerSector)
        {
            var record = buffer.AsSpan();
            if (!ApplyFixups(record, bytesPerSector)) return;
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x16));
            if ((flags & 0x01) == 0) return; // record libero (file cancellato)

            // I record di estensione aggiungono attributi a quello base (file con molti nomi o molto frammentati).
            long baseRef = BinaryPrimitives.ReadInt64LittleEndian(record.Slice(0x20)) & 0x0000FFFFFFFFFFFF;
            int target = baseRef != 0 ? (int)baseRef : index;
            if ((uint)target >= (uint)table.Parent.Length) return;

            if (baseRef == 0)
            {
                table.Flags[target] |= 0x01;
                if ((flags & 0x02) != 0) table.Flags[target] |= 0x02;
            }

            int attr = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x14));
            int used = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x18)), (uint)record.Length);
            while (attr + 16 <= used)
            {
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(attr));
                if (type == AttrEnd) break;
                int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(attr + 4));
                if (length < 16 || attr + length > used) break;
                var a = record.Slice(attr, length);
                bool nonResident = a[8] != 0;
                int nameLength = a[9];

                switch (type)
                {
                    case AttrStandardInformation when !nonResident:
                    {
                        int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(a.Slice(0x14));
                        if (valueOffset + 0x10 <= a.Length)
                            table.LastWrite[target] = BinaryPrimitives.ReadInt64LittleEndian(a.Slice(valueOffset + 0x08));
                        break;
                    }
                    case AttrFileName when !nonResident:
                    {
                        int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(a.Slice(0x14));
                        if (valueOffset + 0x42 > a.Length) break;
                        var v = a.Slice(valueOffset);
                        int nameChars = v[0x40];
                        byte ns = v[0x41];
                        if (0x42 + nameChars * 2 > v.Length) break;
                        // Si tiene il nome lungo; il nome breve 8.3 (DOS) solo se non c'è altro.
                        bool better = table.Name[target] == null || (table.NameNamespace[target] == NamespaceDos && ns != NamespaceDos);
                        if (!better) break;
                        long parent = BinaryPrimitives.ReadInt64LittleEndian(v) & 0x0000FFFFFFFFFFFF;
                        table.Parent[target] = parent > int.MaxValue ? -1 : (int)parent;
                        table.Name[target] = MemoryMarshal.Cast<byte, char>(v.Slice(0x42, nameChars * 2)).ToString();
                        table.NameNamespace[target] = ns;
                        table.Flags[target] |= 0x04;
                        break;
                    }
                    case AttrData when nameLength == 0:
                    {
                        if (!nonResident)
                        {
                            table.Size[target] = BinaryPrimitives.ReadUInt32LittleEndian(a.Slice(0x10));
                        }
                        else if (BinaryPrimitives.ReadInt64LittleEndian(a.Slice(0x10)) == 0)
                        {
                            // Solo il primo frammento (VCN iniziale 0) riporta la dimensione reale del file.
                            table.Size[target] = BinaryPrimitives.ReadInt64LittleEndian(a.Slice(0x30));
                        }
                        break;
                    }
                }
                attr += length;
            }
        }

        // ---- Costruzione dell'albero --------------------------------------------------------------------

        private static FsNode BuildTree(string rootPath, Table t, ScanProgress progress, CancellationToken cancel)
        {
            int count = t.Parent.Length;
            var nodes = new FsNode?[count];
            var root = new FsNode(rootPath, isDirectory: true, parent: null);
            if (RootRecord < count)
            {
                nodes[RootRecord] = root;
                root.LastWriteUtc = ToUtc(t.LastWrite[RootRecord]);
            }

            long files = 0, folders = 0, bytes = 0;
            for (int i = 0; i < count; i++)
            {
                if (i == RootRecord || (t.Flags[i] & 0x05) != 0x05) continue;
                bool isDir = (t.Flags[i] & 0x02) != 0;
                nodes[i] = new FsNode(t.Name[i]!, isDir, parent: null)
                {
                    Size = isDir ? 0 : t.Size[i],
                    LastWriteUtc = ToUtc(t.LastWrite[i]),
                };
                if (isDir) folders++;
                else { files++; bytes += t.Size[i]; }
            }
            progress.AddFiles(files, bytes);
            progress.AddFolders(folders);

            for (int i = 0; i < count; i++)
            {
                if ((i & 0xFFFF) == 0) cancel.ThrowIfCancellationRequested();
                var node = nodes[i];
                if (node == null || i == RootRecord) continue;
                int p = t.Parent[i];
                var parent = (uint)p < (uint)count ? nodes[p] : null;
                // Elementi senza una cartella valida (resti di file cancellati): si ignorano.
                if (parent == null || !parent.IsDirectory || p == i) continue;
                node.Parent = parent;
                parent.AddChild(node);
            }

            // Una catena di cartelle che non arriva alla radice (tabella incoerente) resta fuori dall'albero:
            // FsTree.Complete parte dalla radice e la ignora.
            FsTree.Complete(root);
            return root;
        }

        private static DateTime ToUtc(long fileTime)
        {
            try { return fileTime > 0 ? DateTime.FromFileTimeUtc(fileTime) : DateTime.MinValue; }
            catch (ArgumentOutOfRangeException) { return DateTime.MinValue; }
        }

        // ---- Accesso al volume --------------------------------------------------------------------------

        private static SafeFileHandle OpenVolume(string driveRoot)
        {
            var device = @"\\.\" + driveRoot.TrimEnd('\\');
            var handle = CreateFileW(device, GenericRead, FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                if (error == 5) throw new UnauthorizedAccessException("Per la scansione veloce serve avviare DockSpace come amministratore.");
                throw new Win32Exception(error);
            }
            return handle;
        }

        private static void ReadExactly(SafeFileHandle volume, Span<byte> buffer, long offset)
        {
            int done = 0;
            while (done < buffer.Length)
            {
                int n = RandomAccess.Read(volume, buffer.Slice(done), offset + done);
                if (n <= 0) throw new EndOfStreamException("Lettura del disco interrotta.");
                done += n;
            }
        }

        private static int Align(int value, int to) => (value + to - 1) / to * to;

        private const uint GenericRead = 0x80000000;
        private const uint FileShareRead = 1, FileShareWrite = 2, FileShareDelete = 4;
        private const uint OpenExisting = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);
    }
}
