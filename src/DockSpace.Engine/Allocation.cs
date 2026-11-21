using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DockSpace.Engine
{
    /// <summary>
    /// Spazio occupato sul disco da un file, per la scansione classica: la dimensione arrotondata ai cluster del
    /// disco; per i file compressi, sparsi o di OneDrive solo online si chiede a Windows lo spazio reale.
    /// </summary>
    internal sealed class Allocation
    {
        // Attributi per cui la dimensione non dice quanto spazio si usa davvero.
        private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
        private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
        private const FileAttributes Special = FileAttributes.Compressed | FileAttributes.SparseFile | FileAttributes.Offline
                                               | RecallOnOpen | RecallOnDataAccess;

        private Allocation(long clusterSize) => ClusterSize = clusterSize;

        public long ClusterSize { get; }

        /// <summary>Per il disco che contiene <paramref name="path"/>. Se non si riesce a saperlo: cluster da 4 KB.</summary>
        public static Allocation For(string path)
        {
            long cluster = 4096;
            if (OperatingSystem.IsWindows())
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (root != null && GetDiskFreeSpaceW(root, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _)
                    && sectorsPerCluster > 0 && bytesPerSector > 0)
                    cluster = (long)sectorsPerCluster * bytesPerSector;
            }
            return new Allocation(cluster);
        }

        public long OnDisk(string directory, string name, long length, FileAttributes attributes)
        {
            if (OperatingSystem.IsWindows() && (attributes & Special) != 0)
            {
                uint low = GetCompressedFileSizeW(Path.Join(directory, name), out uint high);
                if (low != uint.MaxValue || Marshal.GetLastWin32Error() == 0)
                    return Round(((long)high << 32) | low);
            }
            return Round(length);
        }

        public long Round(long bytes) => bytes <= 0 ? 0 : (bytes + ClusterSize - 1) / ClusterSize * ClusterSize;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetCompressedFileSizeW(string fileName, out uint fileSizeHigh);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector,
            out uint freeClusters, out uint totalClusters);
    }
}
