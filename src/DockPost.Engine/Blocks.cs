using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace DockPost.Engine
{
    /// <summary>Un pezzo di testo con il suo stile. <see cref="Link"/> è solo http, https o mailto.</summary>
    public sealed record Run(string Text, bool Bold = false, bool Italic = false, bool Underline = false,
        bool Monospace = false, string? Link = null);

    public enum BlockStyle { Paragraph, Heading1, Heading2, Heading3, Preformatted }

    public abstract record Block(int QuoteLevel);

    /// <summary>Paragrafo, titolo, riga di elenco (<see cref="Bullet"/> non nullo) o testo preformattato.</summary>
    public sealed record Paragraph(IReadOnlyList<Run> Runs, BlockStyle Style, int QuoteLevel, int ListLevel = 0, string? Bullet = null)
        : Block(QuoteLevel)
    {
        public string PlainText => string.Concat(Runs.Select(r => r.Text));
    }

    public sealed record RuleBlock(int QuoteLevel) : Block(QuoteLevel);

    /// <summary>
    /// Immagine: <see cref="Data"/> se è dentro il messaggio, <see cref="RemoteUrl"/> se sta su un server (si scarica
    /// solo quando l'utente lo chiede, perché direbbe al mittente che il messaggio è stato aperto).
    /// </summary>
    public sealed record ImageBlock(byte[]? Data, string? RemoteUrl, string Alt, int QuoteLevel) : Block(QuoteLevel);

    /// <summary>
    /// Trasforma l'HTML delle email (da trattare come ostile) e il testo semplice in blocchi da disegnare: niente
    /// script, stili, moduli o contenuti esterni; i link restano solo se sono http, https o mailto.
    /// </summary>
    public static class Blocks
    {
        private const int MaxBlocks = 20_000;
        private const int MaxImageBytes = 8 * 1024 * 1024;

        private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style", "head", "title", "meta", "link", "noscript", "template", "iframe", "frame", "frameset",
            "object", "embed", "applet", "svg", "math", "canvas", "audio", "video", "input", "select", "textarea",
            "button", "base",
        };

        private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
        {
            "p", "div", "section", "article", "header", "footer", "main", "aside", "nav", "address", "center",
            "table", "tbody", "thead", "tfoot", "tr", "td", "th", "caption", "dl", "dt", "dd", "figure", "figcaption",
            "form", "fieldset", "details", "summary",
        };

        public static readonly Regex UrlPattern = new(@"\b(?:https?://|www\.)[^\s<>""']+[^\s<>""'.,;:!?)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Link accettato così com'è, o null se non è http, https o mailto.</summary>
        public static string? SafeLink(string? href)
        {
            if (string.IsNullOrWhiteSpace(href)) return null;
            href = href.Trim();
            if (href.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) href = "https://" + href;
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) return null;
            return uri.Scheme is "http" or "https" or "mailto" ? uri.AbsoluteUri : null;
        }

        // ---- Testo semplice ------------------------------------------------------------------------------

        /// <summary>Testo semplice: paragrafi separati da righe vuote, citazioni con "&gt;", link riconosciuti.</summary>
        public static List<Block> FromText(string text)
        {
            var blocks = new List<Block>();
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var paragraph = new List<string>();
            int paragraphQuote = 0;

            void Flush()
            {
                if (paragraph.Count == 0) return;
                var joined = string.Join("\n", paragraph);
                blocks.Add(new Paragraph(LinkRuns(joined), BlockStyle.Paragraph, paragraphQuote));
                paragraph.Clear();
            }

            foreach (var raw in lines)
            {
                int level = 0;
                var line = raw;
                while (true)
                {
                    var trimmed = line.TrimStart(' ');
                    if (!trimmed.StartsWith('>')) break;
                    level++;
                    line = trimmed[1..];
                    if (line.StartsWith(' ')) line = line[1..];
                }
                if (line.Trim().Length == 0)
                {
                    Flush();
                    paragraphQuote = level;
                    continue;
                }
                if (level != paragraphQuote) { Flush(); paragraphQuote = level; }
                paragraph.Add(line.TrimEnd());
                if (blocks.Count > MaxBlocks) break;
            }
            Flush();
            return blocks;
        }

        /// <summary>Testo con gli indirizzi web trasformati in link.</summary>
        public static List<Run> LinkRuns(string text, bool bold = false, bool italic = false)
        {
            var runs = new List<Run>();
            int last = 0;
            foreach (Match m in UrlPattern.Matches(text))
            {
                if (m.Index > last) runs.Add(new Run(text[last..m.Index], bold, italic));
                var link = SafeLink(m.Value);
                runs.Add(link == null ? new Run(m.Value, bold, italic) : new Run(m.Value, bold, italic, Link: link));
                last = m.Index + m.Length;
            }
            if (last < text.Length) runs.Add(new Run(text[last..], bold, italic));
            return runs;
        }

        // ---- HTML ----------------------------------------------------------------------------------------

        /// <summary>
        /// HTML → blocchi. <paramref name="embedded"/> dà i dati delle immagini allegate nominate con "cid:".
        /// </summary>
        public static List<Block> FromHtml(string html, Func<string, byte[]?>? embedded = null)
        {
            var parser = new HtmlParser(new HtmlParserOptions { IsScripting = false, IsEmbedded = false });
            var document = parser.ParseDocument(html.Length > 4_000_000 ? html[..4_000_000] : html);
            var builder = new Builder(embedded);
            if (document.Body != null) builder.Walk(document.Body, new Style(), 0);
            builder.Flush();
            return builder.Blocks;
        }

        /// <summary>Testo semplice di un HTML: per l'anteprima, la ricerca e le risposte.</summary>
        public static string ToPlainText(IEnumerable<Block> blocks)
        {
            var sb = new StringBuilder();
            foreach (var b in blocks)
            {
                switch (b)
                {
                    case Paragraph t:
                        if (t.Bullet != null) sb.Append(new string(' ', Math.Max(0, t.ListLevel - 1) * 2)).Append(t.Bullet).Append(' ');
                        sb.AppendLine(t.PlainText);
                        sb.AppendLine();
                        break;
                    case RuleBlock:
                        sb.AppendLine("----------");
                        break;
                    case ImageBlock i when i.Alt.Length > 0:
                        sb.AppendLine("[" + i.Alt + "]");
                        break;
                }
            }
            return Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n").Trim();
        }

        /// <summary>Anteprima di una riga: spazi compressi, al massimo <paramref name="length"/> caratteri.</summary>
        public static string Preview(string text, int length = 200)
        {
            var collapsed = Regex.Replace(text, @"\s+", " ").Trim();
            return collapsed.Length <= length ? collapsed : collapsed[..length].TrimEnd() + "…";
        }

        private readonly record struct Style(bool Bold = false, bool Italic = false, bool Underline = false,
            bool Monospace = false, string? Link = null, bool Pre = false);

        private sealed class Builder
        {
            private readonly Func<string, byte[]?>? _embedded;
            private readonly List<Run> _runs = new();
            private BlockStyle _style = BlockStyle.Paragraph;
            private int _quote;
            private int _listLevel;
            private string? _bullet;
            private readonly Stack<int> _listCounters = new();

            public Builder(Func<string, byte[]?>? embedded) => _embedded = embedded;

            public List<Block> Blocks { get; } = new();

            public void Flush()
            {
                // spazi in eccesso all'inizio e alla fine del paragrafo
                while (_runs.Count > 0 && _runs[^1].Text.Trim().Length == 0 && !_runs[^1].Text.Contains('\n') ) _runs.RemoveAt(_runs.Count - 1);
                if (_runs.Count > 0 && Blocks.Count < MaxBlocks)
                {
                    var runs = Merge(_runs);
                    if (runs.Count > 0 && _style != BlockStyle.Preformatted)
                    {
                        runs[0] = runs[0] with { Text = runs[0].Text.TrimStart() };
                        runs[^1] = runs[^1] with { Text = runs[^1].Text.TrimEnd() };
                        runs.RemoveAll(r => r.Text.Length == 0);
                    }
                    if (runs.Count > 0)
                        Blocks.Add(new Paragraph(runs, _style, _quote, _listLevel, _bullet));
                }
                _runs.Clear();
                _style = BlockStyle.Paragraph;
                _bullet = null;
            }

            private static List<Run> Merge(List<Run> runs)
            {
                var merged = new List<Run>();
                foreach (var r in runs)
                {
                    if (merged.Count > 0 && merged[^1] with { Text = "" } == r with { Text = "" })
                        merged[^1] = merged[^1] with { Text = merged[^1].Text + r.Text };
                    else merged.Add(r);
                }
                return merged;
            }

            private void AddText(string text, Style s)
            {
                if (s.Pre)
                {
                    _style = BlockStyle.Preformatted;
                    _runs.Add(new Run(text, s.Bold, s.Italic, s.Underline, true, s.Link));
                    return;
                }
                var collapsed = Regex.Replace(text, @"[\s ]+", m => m.Value.Contains(' ') ? " " : " ");
                if (collapsed.Length == 0) return;
                bool lastEndsSpace = _runs.Count == 0 || _runs[^1].Text.EndsWith(' ') || _runs[^1].Text.EndsWith('\n');
                if (lastEndsSpace && collapsed.StartsWith(' ')) collapsed = collapsed[1..];
                if (collapsed.Length == 0) return;
                if (s.Link == null)
                {
                    foreach (var run in LinkRuns(collapsed, s.Bold, s.Italic))
                        _runs.Add(run with { Underline = s.Underline, Monospace = s.Monospace });
                }
                else _runs.Add(new Run(collapsed, s.Bold, s.Italic, s.Underline, s.Monospace, s.Link));
            }

            public void Walk(INode node, Style style, int depth)
            {
                if (depth > 200 || Blocks.Count >= MaxBlocks) return;
                foreach (var child in node.ChildNodes)
                {
                    switch (child)
                    {
                        case IText text:
                            AddText(text.Data, style);
                            break;
                        case IElement element:
                            Element(element, style, depth);
                            break;
                    }
                }
            }

            private void Element(IElement e, Style style, int depth)
            {
                var name = e.LocalName;
                if (Skipped.Contains(name)) return;
                if (e.GetAttribute("hidden") != null) return;
                var css = (e.GetAttribute("style") ?? "").ToLowerInvariant().Replace(" ", "");
                if (css.Contains("display:none") || css.Contains("visibility:hidden")) return;

                var s = style;
                if (css.Contains("font-weight:bold") || css.Contains("font-weight:700") || css.Contains("font-weight:800") || css.Contains("font-weight:900")) s = s with { Bold = true };
                if (css.Contains("font-style:italic")) s = s with { Italic = true };
                if (css.Contains("text-decoration:underline")) s = s with { Underline = true };

                switch (name)
                {
                    case "br":
                        AddText("\n", s with { Pre = true });
                        if (!style.Pre) _style = BlockStyle.Paragraph;
                        return;
                    case "hr":
                        Flush();
                        Blocks.Add(new RuleBlock(_quote));
                        return;
                    case "img":
                        Image(e);
                        return;
                    case "b" or "strong": Walk(e, s with { Bold = true }, depth + 1); return;
                    case "i" or "em" or "cite" or "var": Walk(e, s with { Italic = true }, depth + 1); return;
                    case "u" or "ins": Walk(e, s with { Underline = true }, depth + 1); return;
                    case "code" or "tt" or "kbd" or "samp": Walk(e, s with { Monospace = true }, depth + 1); return;
                    case "a":
                        Walk(e, s with { Link = SafeLink(e.GetAttribute("href")) ?? s.Link }, depth + 1);
                        return;
                    case "pre":
                        Flush();
                        Walk(e, s with { Pre = true }, depth + 1);
                        Flush();
                        return;
                    case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        Flush();
                        Walk(e, s with { Bold = true }, depth + 1);
                        _style = name switch { "h1" => BlockStyle.Heading1, "h2" => BlockStyle.Heading2, _ => BlockStyle.Heading3 };
                        Flush();
                        return;
                    case "blockquote":
                        Flush();
                        _quote++;
                        Walk(e, s, depth + 1);
                        Flush();
                        _quote--;
                        return;
                    case "ul" or "ol":
                        Flush();
                        _listLevel++;
                        _listCounters.Push(0);
                        Walk(e, s, depth + 1);
                        Flush();
                        _listCounters.Pop();
                        _listLevel--;
                        return;
                    case "li":
                        Flush();
                        if (_listCounters.Count > 0)
                        {
                            int n = _listCounters.Pop() + 1;
                            _listCounters.Push(n);
                            bool ordered = e.ParentElement?.LocalName == "ol";
                            _bullet = ordered ? n + "." : (_listLevel % 2 == 1 ? "•" : "◦");
                        }
                        else _bullet = "•";
                        var bullet = _bullet;
                        Walk(e, s, depth + 1);
                        if (_runs.Count > 0) _bullet = bullet;
                        Flush();
                        return;
                }

                if (BlockElements.Contains(name))
                {
                    Flush();
                    Walk(e, s, depth + 1);
                    Flush();
                    return;
                }
                Walk(e, s, depth + 1);
            }

            private void Image(IElement e)
            {
                var src = (e.GetAttribute("src") ?? "").Trim();
                var alt = (e.GetAttribute("alt") ?? "").Trim();
                // i pixel spia (1×1) non si mostrano nemmeno come segnaposto
                if (int.TryParse(e.GetAttribute("width"), out var w) && w <= 2) return;
                if (int.TryParse(e.GetAttribute("height"), out var h) && h <= 2) return;

                if (src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
                {
                    var data = _embedded?.Invoke(src[4..].Trim('<', '>'));
                    if (data != null && data.Length <= MaxImageBytes) { Flush(); Blocks.Add(new ImageBlock(data, null, alt, _quote)); }
                    else if (alt.Length > 0) AddText("[" + alt + "]", new Style(Italic: true));
                    return;
                }
                if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = src.IndexOf(',');
                    if (comma > 0 && src[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase) && src.Length - comma < MaxImageBytes * 4 / 3)
                    {
                        try
                        {
                            var data = Convert.FromBase64String(src[(comma + 1)..]);
                            Flush();
                            Blocks.Add(new ImageBlock(data, null, alt, _quote));
                        }
                        catch (FormatException) { /* immagine rovinata: si salta */ }
                    }
                    return;
                }
                if (SafeLink(src) is { } remote && remote.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    Flush();
                    Blocks.Add(new ImageBlock(null, remote, alt, _quote));
                }
            }
        }
    }
}
