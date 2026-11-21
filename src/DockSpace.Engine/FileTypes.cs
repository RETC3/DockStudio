using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DockSpace.Engine
{
    /// <summary>Famiglie di file, per i colori della mappa e il riepilogo per tipo.</summary>
    public enum FileCategory
    {
        Other,
        Image,
        Video,
        Audio,
        Document,
        Archive,
        Code,
        Program,
        System,
    }

    /// <summary>Totale di un'estensione (es. tutti i .mp4) in una cartella.</summary>
    public sealed record ExtensionTotal(string Extension, FileCategory Category, long Count, long Size);

    /// <summary>Totale di una famiglia di file in una cartella.</summary>
    public sealed record CategoryTotal(FileCategory Category, long Count, long Size);

    public static class FileTypes
    {
        private static readonly Dictionary<string, FileCategory> Map = Build();

        private static Dictionary<string, FileCategory> Build()
        {
            var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
            void Add(FileCategory c, string list)
            {
                foreach (var e in list.Split(' ', StringSplitOptions.RemoveEmptyEntries)) map[e] = c;
            }
            Add(FileCategory.Image, "jpg jpeg png gif bmp tif tiff webp heic heif raw cr2 cr3 nef arw dng svg ico psd ai xcf avif jxl");
            Add(FileCategory.Video, "mp4 mkv avi mov wmv flv webm m4v mpg mpeg 3gp ts mts m2ts vob");
            Add(FileCategory.Audio, "mp3 wav flac aac ogg m4a wma opus aiff aif mid midi");
            Add(FileCategory.Document, "pdf doc docx xls xlsx ppt pptx odt ods odp rtf txt md csv epub one msg eml pst ost xps");
            Add(FileCategory.Archive, "zip rar 7z tar gz tgz bz2 xz zst iso img vhd vhdx vmdk cab wim esd");
            Add(FileCategory.Code, "cs js ts jsx tsx py java c cpp h hpp go rs rb php html htm css scss json xml yml yaml sql sh ps1 bat cmd lua kt swift");
            Add(FileCategory.Program, "exe dll msi msix appx sys ocx com jar apk dmg pkg app so dylib");
            Add(FileCategory.System, "tmp log bak old dmp etl evtx cache db sqlite pf mui cat manifest pak bin dat");
            return map;
        }

        public static FileCategory CategoryOf(string extension) =>
            Map.TryGetValue(extension, out var c) ? c : FileCategory.Other;

        public static FileCategory CategoryOf(FsNode node) => node.IsDirectory ? FileCategory.Other : CategoryOf(node.Extension);

        /// <summary>Totali per estensione dei file sotto <paramref name="root"/>, dal più grande.</summary>
        public static IReadOnlyList<ExtensionTotal> ByExtension(FsNode root, CancellationToken cancel = default)
        {
            var totals = new Dictionary<string, (long Count, long Size)>(StringComparer.Ordinal);
            int n = 0;
            foreach (var file in FsTree.Files(root))
            {
                if ((++n & 0xFFFF) == 0) cancel.ThrowIfCancellationRequested();
                var ext = file.Extension;
                totals.TryGetValue(ext, out var t);
                totals[ext] = (t.Count + 1, t.Size + file.Size);
            }
            return totals
                .Select(kv => new ExtensionTotal(kv.Key, CategoryOf(kv.Key), kv.Value.Count, kv.Value.Size))
                .OrderByDescending(e => e.Size)
                .ThenBy(e => e.Extension, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Totali per famiglia a partire dai totali per estensione, dal più grande.</summary>
        public static IReadOnlyList<CategoryTotal> ByCategory(IEnumerable<ExtensionTotal> extensions) =>
            extensions
                .GroupBy(e => e.Category)
                .Select(g => new CategoryTotal(g.Key, g.Sum(e => e.Count), g.Sum(e => e.Size)))
                .OrderByDescending(c => c.Size)
                .ToList();

        /// <summary>I <paramref name="count"/> file più grandi sotto <paramref name="root"/>, dal più grande.</summary>
        public static IReadOnlyList<FsNode> Largest(FsNode root, int count, CancellationToken cancel = default)
        {
            // Coda con priorità che tiene solo i più grandi: memoria fissa anche con milioni di file.
            var heap = new PriorityQueue<FsNode, long>(count + 1);
            int n = 0;
            foreach (var file in FsTree.Files(root))
            {
                if ((++n & 0xFFFF) == 0) cancel.ThrowIfCancellationRequested();
                if (heap.Count < count) heap.Enqueue(file, file.Size);
                else if (file.Size > heap.Peek().Size) heap.EnqueueDequeue(file, file.Size);
            }
            var list = new List<FsNode>(heap.Count);
            while (heap.Count > 0) list.Add(heap.Dequeue());
            list.Reverse();
            return list;
        }
    }
}
