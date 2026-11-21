using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using DockSpace.Engine;
using DockStudio.Core.Localization;

namespace DockSpace.Views
{
    /// <summary>
    /// Mappa a riquadri di una cartella: ogni file è un rettangolo colorato per famiglia, le cartelle
    /// racchiudono il loro contenuto. Clic: seleziona; doppio clic su una cartella: ci entra.
    /// </summary>
    public sealed class TreemapView : Control
    {
        private const double HeaderHeight = 16;
        private const double Padding = 2;

        private FsNode? _folder;
        private FsNode? _selected;
        private TreemapTile? _hover;
        private List<TreemapTile> _tiles = new();
        private Size _layoutSize;
        private bool _dirty = true;

        public TreemapView()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        /// <summary>Clic su un riquadro.</summary>
        public event Action<FsNode>? NodeClicked;

        /// <summary>Doppio clic su un riquadro.</summary>
        public event Action<FsNode>? NodeActivated;

        /// <summary>Clic destro su un riquadro (per il menu).</summary>
        public event Action<FsNode, Point>? NodeContextRequested;

        public FsNode? Folder
        {
            get => _folder;
            set
            {
                _folder = value;
                Invalidate();
            }
        }

        public FsNode? Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                InvalidateVisual();
            }
        }

        /// <summary>Da chiamare quando l'albero cambia (per esempio dopo un'eliminazione).</summary>
        public void Invalidate()
        {
            _dirty = true;
            _hover = null;
            InvalidateVisual();
        }

        public IReadOnlyList<TreemapTile> Tiles
        {
            get
            {
                EnsureLayout();
                return _tiles;
            }
        }

        private void EnsureLayout()
        {
            var size = Bounds.Size;
            if (!_dirty && size == _layoutSize) return;
            _layoutSize = size;
            _dirty = false;
            _tiles = _folder == null || size.Width < 4 || size.Height < 4
                ? new List<TreemapTile>()
                : Treemap.Layout(_folder, new TreemapRect(0, 0, size.Width, size.Height), headerHeight: HeaderHeight, padding: Padding);
        }

        public override void Render(DrawingContext context)
        {
            bool dark = ActualThemeVariant == ThemeVariant.Dark;
            var background = new SolidColorBrush(dark ? Color.Parse("#1B1C20") : Color.Parse("#F6F7F9"));
            context.FillRectangle(background, new Rect(Bounds.Size));
            EnsureLayout();
            if (_tiles.Count == 0)
            {
                if (_folder != null && _folder.Size == 0)
                    DrawCentered(context, Text.T("map.empty"), dark);
                return;
            }

            var folderBorder = new Pen(new SolidColorBrush(dark ? Color.Parse("#80000000") : Color.Parse("#B0FFFFFF")), 1);
            var fileBorder = new Pen(new SolidColorBrush(dark ? Color.Parse("#1B1C20") : Color.Parse("#FFFFFF")), 0.6);
            var headerText = new SolidColorBrush(dark ? Color.Parse("#ECEDEF") : Color.Parse("#1D1F24"));
            var typeface = new Typeface(FontFamily.Default);
            var culture = Languages.Culture;

            foreach (var tile in _tiles)
            {
                var r = ToRect(tile.Rect);
                if (tile.Node.IsDirectory)
                {
                    // Le cartelle sono grigie, più scure (o più chiare nel tema scuro) quanto più sono profonde.
                    byte shade = (byte)Math.Clamp(dark ? 58 + tile.Depth * 10 : 214 - tile.Depth * 14, 0, 255);
                    context.DrawRectangle(new SolidColorBrush(Color.FromRgb(shade, shade, (byte)Math.Min(255, shade + 4))), folderBorder, r);
                    var inner = Treemap.Inner(tile.Rect, HeaderHeight, Padding);
                    if (inner.Y > tile.Rect.Y + Padding + 1)
                    {
                        var label = new FormattedText($"{tile.Node.Name}  {Sizes.Format(tile.Node.Size, culture)}", culture,
                            FlowDirection.LeftToRight, typeface, 11, headerText)
                        { MaxTextWidth = Math.Max(1, r.Width - 6), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                        context.DrawText(label, new Point(r.X + 4, r.Y + 2));
                    }
                }
                else
                {
                    var color = Palette.For(FileTypes.CategoryOf(tile.Node));
                    context.DrawRectangle(new SolidColorBrush(color), fileBorder, r);
                    if (r.Width > 70 && r.Height > 22)
                    {
                        var label = new FormattedText(tile.Node.Name, culture, FlowDirection.LeftToRight, typeface, 11, Brushes.White)
                        { MaxTextWidth = r.Width - 6, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                        context.DrawText(label, new Point(r.X + 3, r.Y + 3));
                    }
                }
            }

            if (_hover is { } hover)
                context.DrawRectangle(null, new Pen(new SolidColorBrush(dark ? Colors.White : Colors.Black), 1.5), ToRect(hover.Rect).Deflate(0.75));

            if (_selected != null)
            {
                foreach (var tile in _tiles)
                {
                    if (!ReferenceEquals(tile.Node, _selected)) continue;
                    var accent = this.TryFindResource("SystemAccentColor", ActualThemeVariant, out var res) && res is Color c ? c : Colors.DodgerBlue;
                    context.DrawRectangle(null, new Pen(new SolidColorBrush(accent), 3), ToRect(tile.Rect).Deflate(1.5));
                    break;
                }
            }
        }

        private void DrawCentered(DrawingContext context, string text, bool dark)
        {
            var ft = new FormattedText(text, Languages.Culture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 13,
                new SolidColorBrush(dark ? Color.Parse("#A3A7AF") : Color.Parse("#6B6F78")));
            context.DrawText(ft, new Point((Bounds.Width - ft.Width) / 2, (Bounds.Height - ft.Height) / 2));
        }

        private static Rect ToRect(TreemapRect r) => new(r.X, r.Y, r.Width, r.Height);

        private TreemapTile? Hit(Point p)
        {
            EnsureLayout();
            return Treemap.HitTest(_tiles, p.X, p.Y);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var hit = Hit(e.GetPosition(this));
            if (Equals(hit, _hover)) return;
            _hover = hit;
            ToolTip.SetTip(this, hit is { } t ? $"{t.Node.FullPath}\n{Sizes.Format(t.Node.Size, Languages.Culture)}" : null);
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hover = null;
            ToolTip.SetTip(this, null);
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            if (Hit(point.Position) is not { } tile) return;
            if (point.Properties.IsRightButtonPressed)
            {
                NodeClicked?.Invoke(tile.Node);
                NodeContextRequested?.Invoke(tile.Node, point.Position);
            }
            else if (e.ClickCount >= 2) NodeActivated?.Invoke(tile.Node);
            else NodeClicked?.Invoke(tile.Node);
            e.Handled = true;
        }
    }
}
