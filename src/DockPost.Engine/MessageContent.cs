using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MimeKit;

namespace DockPost.Engine
{
    /// <summary>Un allegato: nome sicuro da mostrare e da usare come nome di file, dimensione e tipo.</summary>
    public sealed record AttachmentInfo(int Index, string FileName, long Size, string ContentType);

    /// <summary>
    /// Un messaggio aperto: intestazioni, blocchi del testo, allegati. Le immagini remote non sono scaricate:
    /// <see cref="RemoteImages"/> dice quante sono, per offrire "Mostra le immagini".
    /// </summary>
    public sealed class MessageContent
    {
        private readonly List<MimeEntity> _attachments;

        private MessageContent(MimeMessage message, List<Block> blocks, List<MimeEntity> attachments, string plainText)
        {
            Message = message;
            Blocks = blocks;
            _attachments = attachments;
            PlainText = plainText;
            Attachments = attachments.Select((a, i) => new AttachmentInfo(i, SafeFileName(a, i), EstimateSize(a),
                a.ContentType.MimeType)).ToList();
        }

        public MimeMessage Message { get; }
        public IReadOnlyList<Block> Blocks { get; }
        public IReadOnlyList<AttachmentInfo> Attachments { get; }

        /// <summary>Testo semplice (per la ricerca, l'anteprima e le citazioni nelle risposte).</summary>
        public string PlainText { get; }

        public int RemoteImages => Blocks.OfType<ImageBlock>().Count(b => b.RemoteUrl != null);

        public static MessageContent Load(string emlPath)
        {
            using var stream = File.OpenRead(emlPath);
            return From(MimeMessage.Load(stream));
        }

        public static MessageContent From(MimeMessage message)
        {
            var embedded = new Dictionary<string, MimePart>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in message.BodyParts.OfType<MimePart>())
                if (!string.IsNullOrEmpty(part.ContentId)) embedded.TryAdd(part.ContentId.Trim('<', '>'), part);

            var usedCids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            byte[]? Embedded(string cid)
            {
                if (!embedded.TryGetValue(cid, out var part) || part.Content == null) return null;
                usedCids.Add(cid);
                using var ms = new MemoryStream();
                part.Content.DecodeTo(ms);
                return ms.ToArray();
            }

            List<Block> blocks;
            string plain;
            var html = message.HtmlBody;
            var text = message.TextBody;
            if (!string.IsNullOrWhiteSpace(html))
            {
                blocks = Engine.Blocks.FromHtml(html, Embedded);
                plain = !string.IsNullOrWhiteSpace(text) ? text : Engine.Blocks.ToPlainText(blocks);
            }
            else
            {
                plain = text ?? "";
                blocks = Engine.Blocks.FromText(plain);
            }

            var attachments = new List<MimeEntity>();
            foreach (var entity in message.Attachments)
                attachments.Add(entity);
            // immagini dentro il messaggio che il testo non mostra: sono allegati come gli altri
            foreach (var (cid, part) in embedded)
                if (!usedCids.Contains(cid) && !attachments.Contains(part) && !part.ContentType.IsMimeType("text", "*"))
                    attachments.Add(part);

            return new MessageContent(message, blocks, attachments, plain);
        }

        /// <summary>Nome di file senza percorsi e caratteri vietati; un nome di riserva se manca.</summary>
        private static string SafeFileName(MimeEntity entity, int index)
        {
            var name = entity switch
            {
                MimePart p => p.FileName,
                MessagePart m => (m.Message?.Subject ?? "messaggio") + ".eml",
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(name))
                name = $"allegato-{index + 1}" + (MimeTypes.TryGetExtension(entity.ContentType.MimeType, out var ext) ? ext : "");
            name = Path.GetFileName(name.Replace('\\', '/'));
            foreach (var bad in Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }))
                name = name.Replace(bad, '_');
            name = name.Trim().TrimEnd('.');
            if (name.Length > 150) name = name[..100] + Path.GetExtension(name);
            return name.Length == 0 ? $"allegato-{index + 1}" : name;
        }

        private static long EstimateSize(MimeEntity entity)
        {
            if (entity is MimePart { Content: { } content })
            {
                long encoded = content.Stream?.CanSeek == true ? content.Stream.Length : 0;
                return content.Encoding == ContentEncoding.Base64 ? encoded * 3 / 4 : encoded;
            }
            return 0;
        }

        /// <summary>Scrive l'allegato decodificato nel flusso.</summary>
        public void WriteAttachment(int index, Stream destination)
        {
            switch (_attachments[index])
            {
                case MimePart { Content: { } content }:
                    content.DecodeTo(destination);
                    break;
                case MessagePart { Message: { } inner }:
                    inner.WriteTo(destination);
                    break;
            }
        }
    }
}
