using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DockSpace.Engine
{
    /// <summary>
    /// Un file o una cartella del risultato di una scansione. Per le cartelle <see cref="Size"/> e i conteggi
    /// sono la somma di tutto il contenuto. La radice ha come nome il percorso completo (es. "C:\").
    /// </summary>
    public sealed class FsNode
    {
        private static readonly IReadOnlyList<FsNode> NoChildren = Array.Empty<FsNode>();

        public FsNode(string name, bool isDirectory, FsNode? parent)
        {
            Name = name;
            IsDirectory = isDirectory;
            Parent = parent;
        }

        public string Name { get; }
        public bool IsDirectory { get; }
        public FsNode? Parent { get; internal set; }

        /// <summary>
        /// Dimensione secondo la misura scelta (<see cref="FsTree.UseMeasure"/>): lo spazio su disco o la
        /// dimensione dei file. Per le cartelle è il totale del contenuto. Ordinamento, quote e mappa usano questa.
        /// </summary>
        public long Size { get; internal set; }

        /// <summary>Dimensione dei file, come la mostra Esplora file in "Dimensione".</summary>
        public long Length { get; internal set; }

        /// <summary>
        /// Spazio occupato davvero sul disco: arrotondato ai cluster, più piccolo per i file compressi o sparsi,
        /// zero per i file di OneDrive solo online e (con la scansione veloce) per i file minuscoli tenuti nella tabella NTFS.
        /// </summary>
        public long OnDisk { get; internal set; }

        /// <summary>File contenuti, a qualsiasi profondità (per un file: 1).</summary>
        public long FileCount { get; internal set; }

        /// <summary>Cartelle contenute, a qualsiasi profondità.</summary>
        public long FolderCount { get; internal set; }

        /// <summary>Ultima modifica (UTC). Per le cartelle, la più recente del contenuto.</summary>
        public DateTime LastWriteUtc { get; internal set; }

        /// <summary>La cartella non si è potuta leggere (permessi): il contenuto manca.</summary>
        public bool Inaccessible { get; internal set; }

        internal List<FsNode>? ChildList;

        /// <summary>Contenuto della cartella, dal più grande al più piccolo (dopo <see cref="FsTree.Complete"/>).</summary>
        public IReadOnlyList<FsNode> Children => ChildList ?? NoChildren;

        internal void AddChild(FsNode child)
        {
            (ChildList ??= new List<FsNode>()).Add(child);
        }

        public bool IsRoot => Parent == null;

        /// <summary>Percorso completo sul disco.</summary>
        public string FullPath
        {
            get
            {
                if (Parent == null) return Name;
                var parts = new Stack<string>();
                for (var n = this; n.Parent != null; n = n.Parent) parts.Push(n.Name);
                var root = Root.Name;
                var sb = new StringBuilder(root);
                foreach (var p in parts)
                {
                    if (sb.Length > 0 && sb[^1] != Path.DirectorySeparatorChar && sb[^1] != Path.AltDirectorySeparatorChar)
                        sb.Append(Path.DirectorySeparatorChar);
                    sb.Append(p);
                }
                return sb.ToString();
            }
        }

        public FsNode Root
        {
            get
            {
                var n = this;
                while (n.Parent != null) n = n.Parent;
                return n;
            }
        }

        /// <summary>Estensione in minuscolo senza punto ("" se non c'è).</summary>
        public string Extension
        {
            get
            {
                if (IsDirectory) return "";
                int dot = Name.LastIndexOf('.');
                return dot <= 0 || dot == Name.Length - 1 ? "" : Name[(dot + 1)..].ToLowerInvariant();
            }
        }

        /// <summary>Quota di questo elemento nella cartella che lo contiene (0–1).</summary>
        public double ShareOfParent => Parent == null || Parent.Size <= 0 ? 1 : (double)Size / Parent.Size;

        /// <summary>True se <paramref name="other"/> è questo elemento o sta al suo interno.</summary>
        public bool Contains(FsNode other)
        {
            for (var n = other; n != null; n = n.Parent)
                if (ReferenceEquals(n, this)) return true;
            return false;
        }

        public override string ToString() => Name;
    }
}
