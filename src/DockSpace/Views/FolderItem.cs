using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DockSpace.Engine;

namespace DockSpace.Views
{
    /// <summary>
    /// Una cartella nell'albero di sinistra. Le sottocartelle si creano solo quando servono (all'apertura del
    /// ramo), così anche un disco con milioni di file si mostra subito.
    /// </summary>
    public sealed class FolderItem
    {
        private readonly Dictionary<FsNode, FolderItem> _index;
        private ObservableCollection<FolderItem>? _children;

        private FolderItem(FsNode node, FolderItem? parent, Dictionary<FsNode, FolderItem> index)
        {
            Node = node;
            Parent = parent;
            _index = index;
            index[node] = this;
        }

        public static FolderItem CreateRoot(FsNode root) => new(root, null, new Dictionary<FsNode, FolderItem>());

        public FsNode Node { get; }
        public FolderItem? Parent { get; }

        /// <summary>Totali cambiati (dopo un'eliminazione): la riga si ridisegna.</summary>
        public event Action? Changed;

        public ObservableCollection<FolderItem> Children
        {
            get
            {
                if (_children != null) return _children;
                _children = new ObservableCollection<FolderItem>();
                foreach (var child in Node.Children)
                    if (child.IsDirectory) _children.Add(new FolderItem(child, this, _index));
                return _children;
            }
        }

        /// <summary>L'elemento dell'albero per una cartella, creando i rami intermedi se serve.</summary>
        public FolderItem? Find(FsNode folder)
        {
            if (_index.TryGetValue(folder, out var item)) return item;
            var path = FsTree.PathFromRoot(folder);
            if (path.Count == 0 || !ReferenceEquals(path[0], Node.Root)) return null;
            var current = this;
            while (current.Parent != null) current = current.Parent;
            for (int i = 1; i < path.Count && current != null; i++)
            {
                _ = current.Children; // crea il livello
                current = _index.TryGetValue(path[i], out var next) ? next : null;
            }
            return current;
        }

        /// <summary>Toglie una sottocartella dall'albero (già tolta dal risultato della scansione).</summary>
        public void RemoveChild(FsNode node)
        {
            if (_children == null) return;
            for (int i = 0; i < _children.Count; i++)
            {
                if (!ReferenceEquals(_children[i].Node, node)) continue;
                _children.RemoveAt(i);
                break;
            }
        }

        /// <summary>Avvisa questa riga e quelle sopra che i totali sono cambiati.</summary>
        public void RaiseChangedUpwards()
        {
            for (var item = this; item != null; item = item.Parent) item.Changed?.Invoke();
        }
    }
}
