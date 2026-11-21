using System;
using System.Collections.Generic;

namespace DockSpace.Engine
{
    /// <summary>Operazioni sull'albero di una scansione: totali, ordinamento, rimozione di un elemento.</summary>
    public static class FsTree
    {
        private static readonly Comparison<FsNode> BySizeDescending = (a, b) =>
        {
            int c = b.Size.CompareTo(a.Size);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        };

        /// <summary>
        /// Calcola i totali delle cartelle (dimensione, file, cartelle, ultima modifica) dal basso verso l'alto
        /// e ordina ogni contenuto dal più grande. Va chiamato una volta, alla fine della scansione.
        /// </summary>
        public static void Complete(FsNode root)
        {
            // Visita in ordine posticipato senza ricorsione: le cartelle possono essere molto profonde.
            var stack = new Stack<(FsNode Node, bool Visited)>();
            stack.Push((root, false));
            while (stack.Count > 0)
            {
                var (node, visited) = stack.Pop();
                if (!node.IsDirectory)
                {
                    node.FileCount = 1;
                    continue;
                }
                if (!visited)
                {
                    stack.Push((node, true));
                    if (node.ChildList != null)
                        foreach (var child in node.ChildList)
                            stack.Push((child, false));
                    continue;
                }

                long size = 0, files = 0, folders = 0;
                var last = node.LastWriteUtc;
                if (node.ChildList != null)
                {
                    foreach (var child in node.ChildList)
                    {
                        size += child.Size;
                        files += child.FileCount;
                        folders += child.FolderCount + (child.IsDirectory ? 1 : 0);
                        if (child.LastWriteUtc > last) last = child.LastWriteUtc;
                    }
                    node.ChildList.Sort(BySizeDescending);
                    node.ChildList.TrimExcess();
                }
                node.Size = size;
                node.FileCount = files;
                node.FolderCount = folders;
                node.LastWriteUtc = last;
            }
        }

        /// <summary>
        /// Toglie un elemento dall'albero (dopo averlo spostato nel Cestino) e aggiorna i totali delle cartelle
        /// che lo contenevano. Non tocca il disco. False se è la radice o non è più nell'albero.
        /// </summary>
        public static bool Remove(FsNode node)
        {
            var parent = node.Parent;
            if (parent?.ChildList == null || !parent.ChildList.Remove(node)) return false;

            long files = node.IsDirectory ? node.FileCount : 1;
            long folders = node.IsDirectory ? node.FolderCount + 1 : 0;
            for (var p = parent; p != null; p = p.Parent)
            {
                p.Size -= node.Size;
                p.FileCount -= files;
                p.FolderCount -= folders;
            }
            node.Parent = null;

            // Le cartelle sopra possono aver cambiato posto tra le sorelle.
            for (var p = parent; p.Parent?.ChildList != null; p = p.Parent)
                p.Parent.ChildList.Sort(BySizeDescending);
            return true;
        }

        /// <summary>Tutti i file sotto <paramref name="root"/> (o il file stesso).</summary>
        public static IEnumerable<FsNode> Files(FsNode root)
        {
            var stack = new Stack<FsNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!node.IsDirectory)
                {
                    yield return node;
                    continue;
                }
                foreach (var child in node.Children)
                    stack.Push(child);
            }
        }

        /// <summary>Il percorso di nodi dalla radice a <paramref name="node"/> compreso.</summary>
        public static IReadOnlyList<FsNode> PathFromRoot(FsNode node)
        {
            var list = new List<FsNode>();
            for (var n = node; n != null; n = n.Parent) list.Add(n);
            list.Reverse();
            return list;
        }
    }
}
