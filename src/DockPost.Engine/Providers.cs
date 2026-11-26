using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DockPost.Engine
{
    /// <summary>Impostazioni trovate per un indirizzo: server, accesso e un'eventuale avvertenza per l'utente.</summary>
    /// <param name="Source">Da dove arrivano: "known", "autoconfig", "ispdb", "mx", "guess".</param>
    /// <param name="NoteKey">Chiave del testo da mostrare (es. password per le app di Gmail), o null.</param>
    public sealed record ProviderGuess(
        string? ProviderId,
        string Name,
        ServerSettings Imap,
        ServerSettings Smtp,
        AuthKind Auth,
        string UserName,
        bool ServerSavesSent,
        string Source,
        string? NoteKey = null);

    /// <summary>
    /// Riconosce il provider dall'indirizzo, in quest'ordine: elenco interno dei provider più usati; file di
    /// configurazione automatica pubblicato dal dominio; banca dati di Thunderbird (ISPDB); chi riceve la posta
    /// del dominio (record MX, per i domini propri su Google, Microsoft 365...); infine un tentativo con i nomi
    /// usuali (imap.dominio, smtp.dominio). Alle ricerche in rete va solo il dominio, mai l'indirizzo intero.
    /// </summary>
    public static class Providers
    {
        private sealed record Known(string Id, string Name, string[] Domains, ServerSettings Imap, ServerSettings Smtp,
            AuthKind Auth, bool SavesSent, string? NoteKey);

        private static readonly Known[] Table =
        {
            new("gmail", "Gmail", new[] { "gmail.com", "googlemail.com" },
                new("imap.gmail.com", 993, Security.Tls), new("smtp.gmail.com", 465, Security.Tls),
                AuthKind.Password, true, "provider.note.gmail"),
            new("microsoft", "Outlook.com", new[] { "outlook.com", "outlook.it", "hotmail.com", "hotmail.it", "live.com", "live.it", "msn.com" },
                new("outlook.office365.com", 993, Security.Tls), new("smtp-mail.outlook.com", 587, Security.StartTls),
                AuthKind.Microsoft, true, null),
            new("yahoo", "Yahoo Mail", new[] { "yahoo.com", "yahoo.it", "ymail.com", "rocketmail.com" },
                new("imap.mail.yahoo.com", 993, Security.Tls), new("smtp.mail.yahoo.com", 465, Security.Tls),
                AuthKind.Password, false, "provider.note.appPassword"),
            new("icloud", "iCloud Mail", new[] { "icloud.com", "me.com", "mac.com" },
                new("imap.mail.me.com", 993, Security.Tls), new("smtp.mail.me.com", 587, Security.StartTls),
                AuthKind.Password, false, "provider.note.appPassword"),
            new("libero", "Libero Mail", new[] { "libero.it", "inwind.it", "iol.it", "blu.it", "giallo.it" },
                new("imapmail.libero.it", 993, Security.Tls), new("smtp.libero.it", 465, Security.Tls),
                AuthKind.Password, false, null),
            new("virgilio", "Virgilio Mail", new[] { "virgilio.it" },
                new("in.virgilio.it", 993, Security.Tls), new("out.virgilio.it", 465, Security.Tls),
                AuthKind.Password, false, null),
        };

        /// <summary>Dominio dell'indirizzo in minuscolo, o null se l'indirizzo non è valido.</summary>
        public static string? DomainOf(string email)
        {
            email = email.Trim();
            int at = email.LastIndexOf('@');
            if (at <= 0 || at == email.Length - 1 || email.Contains(' ')) return null;
            var domain = email[(at + 1)..].ToLowerInvariant();
            return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') ? domain : null;
        }

        /// <summary>Solo l'elenco interno, senza rete.</summary>
        public static ProviderGuess? FromKnown(string email)
        {
            var domain = DomainOf(email);
            if (domain == null) return null;
            var known = Table.FirstOrDefault(k => k.Domains.Contains(domain));
            return known == null ? null : ToGuess(known, email.Trim(), "known");
        }

        private static ProviderGuess ToGuess(Known k, string email, string source) =>
            new(k.Id, k.Name, k.Imap, k.Smtp, k.Auth, email, k.SavesSent, source, k.NoteKey);

        /// <summary>Microsoft 365 di un'azienda (dominio proprio con la posta su Exchange Online).</summary>
        private static ProviderGuess Microsoft365(string email, string source) =>
            new("microsoft365", "Microsoft 365", new("outlook.office365.com", 993, Security.Tls),
                new("smtp.office365.com", 587, Security.StartTls), AuthKind.Microsoft, email, true, source);

        private static ProviderGuess GoogleWorkspace(string email, string source) =>
            new("gmail", "Google Workspace", new("imap.gmail.com", 993, Security.Tls),
                new("smtp.gmail.com", 465, Security.Tls), AuthKind.Password, email, true, source, "provider.note.gmail");

        /// <summary>Cerca le impostazioni; non lancia eccezioni per problemi di rete (passa al tentativo dopo).</summary>
        public static async Task<ProviderGuess?> DetectAsync(string email, HttpClient http, CancellationToken cancel = default)
        {
            email = email.Trim();
            var domain = DomainOf(email);
            if (domain == null) return null;
            if (FromKnown(email) is { } known) return known;

            foreach (var url in new[]
                     {
                         $"https://autoconfig.{domain}/mail/config-v1.1.xml",
                         $"https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml",
                     })
            {
                if (await TryAutoconfig(url, email, "autoconfig", http, cancel) is { } found) return found;
            }
            if (await TryAutoconfig($"https://autoconfig.thunderbird.net/v1.1/{domain}", email, "ispdb", http, cancel) is { } db)
                return db;

            // Domini propri: chi riceve la posta? (DNS su HTTPS, solo il nome del dominio)
            var mx = await LookupMx(domain, http, cancel);
            if (mx.Count > 0)
            {
                var first = mx[0];
                if (first.EndsWith(".mail.protection.outlook.com") || first.EndsWith(".outlook.com"))
                    return Microsoft365(email, "mx");
                if (first.EndsWith("google.com") || first.EndsWith("googlemail.com"))
                    return GoogleWorkspace(email, "mx");
                // il provider del server di posta potrebbe essere nella banca dati (es. aruba.it, register.it)
                var mxDomain = BaseDomain(first);
                if (mxDomain != null && mxDomain != domain &&
                    await TryAutoconfig($"https://autoconfig.thunderbird.net/v1.1/{mxDomain}", email, "mx", http, cancel) is { } byMx)
                    return byMx;
            }

            return new ProviderGuess(null, domain, new($"imap.{domain}", 993, Security.Tls),
                new($"smtp.{domain}", 465, Security.Tls), AuthKind.Password, email, false, "guess", "provider.note.guess");
        }

        /// <summary>"mx1.mail.aruba.it" → "aruba.it" (gli ultimi due pezzi del nome).</summary>
        public static string? BaseDomain(string host)
        {
            var parts = host.TrimEnd('.').Split('.');
            return parts.Length >= 2 ? parts[^2] + "." + parts[^1] : null;
        }

        private static async Task<ProviderGuess?> TryAutoconfig(string url, string email, string source, HttpClient http, CancellationToken cancel)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(6));
                using var response = await http.GetAsync(url, timeout.Token);
                if (!response.IsSuccessStatusCode) return null;
                var xml = await response.Content.ReadAsStringAsync(timeout.Token);
                if (xml.Length > 256 * 1024) return null;
                return ParseAutoconfig(xml, email, source);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or InvalidOperationException)
            {
                if (cancel.IsCancellationRequested) throw;
                return null;
            }
        }

        /// <summary>
        /// Legge un file di configurazione automatica (formato "clientConfig" 1.1 di Thunderbird): il primo server
        /// IMAP e il primo SMTP, con i segnaposto del nome utente sostituiti.
        /// </summary>
        public static ProviderGuess? ParseAutoconfig(string xml, string email, string source)
        {
            XDocument doc;
            try { doc = XDocument.Parse(xml, LoadOptions.None); }
            catch (System.Xml.XmlException) { return null; }

            var provider = doc.Root?.Element("emailProvider");
            if (provider == null) return null;
            var imap = provider.Elements("incomingServer").FirstOrDefault(e => (string?)e.Attribute("type") == "imap");
            var smtp = provider.Elements("outgoingServer").FirstOrDefault(e => (string?)e.Attribute("type") == "smtp");
            if (imap == null || smtp == null) return null;

            ServerSettings? Server(XElement e)
            {
                var host = ((string?)e.Element("hostname"))?.Trim();
                if (string.IsNullOrEmpty(host) || !int.TryParse((string?)e.Element("port"), out var port) || port is <= 0 or > 65535)
                    return null;
                var security = ((string?)e.Element("socketType"))?.Trim().ToUpperInvariant() switch
                {
                    "SSL" => Security.Tls,
                    "STARTTLS" => Security.StartTls,
                    _ => Security.None,
                };
                return new ServerSettings(Substitute(host, email), port, security);
            }

            var imapServer = Server(imap);
            var smtpServer = Server(smtp);
            // Senza protezione la password viaggerebbe in chiaro: non lo si propone.
            if (imapServer == null || smtpServer == null || imapServer.Security == Security.None || smtpServer.Security == Security.None)
                return null;

            var user = Substitute(((string?)imap.Element("username"))?.Trim() ?? "%EMAILADDRESS%", email);
            var name = ((string?)provider.Element("displayShortName"))?.Trim()
                       ?? ((string?)provider.Element("displayName"))?.Trim()
                       ?? DomainOf(email) ?? "";
            var auth = imap.Elements("authentication").Any(a => a.Value.Trim() == "OAuth2") &&
                       imapServer.Host.EndsWith("office365.com", StringComparison.OrdinalIgnoreCase)
                ? AuthKind.Microsoft
                : AuthKind.Password;
            return new ProviderGuess(null, name, imapServer, smtpServer, auth, user, false, source);
        }

        private static string Substitute(string text, string email)
        {
            int at = email.IndexOf('@');
            var local = at > 0 ? email[..at] : email;
            var domain = at > 0 ? email[(at + 1)..] : "";
            return text.Replace("%EMAILADDRESS%", email).Replace("%EMAILLOCALPART%", local).Replace("%EMAILDOMAIN%", domain);
        }

        /// <summary>Server di posta del dominio, dal più preferito, con DNS su HTTPS (Cloudflare).</summary>
        private static async Task<List<string>> LookupMx(string domain, HttpClient http, CancellationToken cancel)
        {
            var result = new List<(int Priority, string Host)>();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://cloudflare-dns.com/dns-query?name={Uri.EscapeDataString(domain)}&type=MX");
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await http.SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode) return new();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                if (!json.RootElement.TryGetProperty("Answer", out var answers)) return new();
                foreach (var a in answers.EnumerateArray())
                {
                    if (!a.TryGetProperty("type", out var t) || t.GetInt32() != 15) continue;
                    var data = a.GetProperty("data").GetString() ?? "";
                    var parts = data.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && int.TryParse(parts[0], out var prio))
                        result.Add((prio, parts[1].TrimEnd('.').ToLowerInvariant()));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                if (cancel.IsCancellationRequested) throw;
            }
            return result.OrderBy(r => r.Priority).Select(r => r.Host).ToList();
        }
    }
}
