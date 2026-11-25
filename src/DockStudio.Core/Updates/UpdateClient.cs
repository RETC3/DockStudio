using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DockStudio.Core.Localization;

namespace DockStudio.Core.Updates
{
    /// <summary>Una release più recente di quella in uso.</summary>
    public sealed record UpdateInfo(Version Version, string PageUrl, string Notes,
        string? SetupUrl, string? SetupName, long SetupSize, string? SetupSha256 = null);

    /// <summary>
    /// Aggiornamenti di un'app: legge l'ultima release dal sito (latest.json) o, se il sito non risponde,
    /// dalla riserva su Cloudflare Pages e infine da GitHub; scarica l'installer e ne controlla dimensione e
    /// impronta SHA-256. Come avviare l'installer dipende dal sistema (vedi DockStudio.Core.Windows).
    /// </summary>
    public sealed class UpdateClient
    {
        private readonly AppIdentity _app;
        private readonly Assembly _versionSource;
        private readonly Regex _setupName;

        /// <param name="app">L'app da aggiornare.</param>
        /// <param name="versionSource">L'assembly dell'app, da cui si legge la versione in uso.</param>
        public UpdateClient(AppIdentity app, Assembly versionSource)
        {
            _app = app;
            _versionSource = versionSource;
            _setupName = new Regex(app.SetupAssetPattern, RegexOptions.IgnoreCase);
        }

        public AppIdentity App => _app;

        /// <summary>
        /// File JSON locale al posto del feed (stesso formato di latest.json e dell'API di GitHub), per provare
        /// gli aggiornamenti: variabile PREFISSO_UPDATE_FEED.
        /// </summary>
        public string? FeedOverride => _app.Env("UPDATE_FEED");

        private string? GitHubDownloadPrefix => _app.GitHubRepo == null ? null : $"https://github.com/{_app.GitHubRepo}/releases/download/";

        /// <summary>Fonti dell'ultima release, in ordine.</summary>
        public IEnumerable<string> Feeds
        {
            get
            {
                if (FeedOverride != null) { yield return FeedOverride; yield break; }
                yield return _app.SiteUrl + "/latest.json";
                if (_app.PagesUrl != null) yield return _app.PagesUrl + "/latest.json";
                if (_app.GitHubRepo != null) yield return $"https://api.github.com/repos/{_app.GitHubRepo}/releases/latest";
            }
        }

        public string ReleasesPage => _app.SiteUrl + "/#scarica";

        public Version CurrentVersion
        {
            get
            {
                var info = _versionSource.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (info != null && Version.TryParse(info.Split('+', '-')[0], out var v))
                    return Normalize(v);
                return Normalize(_versionSource.GetName().Version ?? new Version(0, 0, 0));
            }
        }

        private HttpClient CreateClient(TimeSpan timeout)
        {
            var http = new HttpClient { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(_app.UserAgent);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return http;
        }

        /// <summary>Restituisce la release più recente se è più nuova di quella in uso, altrimenti null.</summary>
        public Task<UpdateInfo?> CheckAsync(CancellationToken ct = default) => CheckAsync(CurrentVersion, ct);

        /// <summary>
        /// Come <see cref="CheckAsync(CancellationToken)"/>, ma rispetto a una versione data: serve a chi gestisce
        /// un'altra app (il launcher DockStudio). Con la versione 0.0.0 restituisce sempre l'ultima uscita.
        /// </summary>
        public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
        {
            Exception? last = null;
            foreach (var url in Feeds)
            {
                try
                {
                    return Parse(await ReadFeedAsync(url, ct), Normalize(current));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex; // si prova la fonte successiva
                }
            }
            throw last ?? new InvalidOperationException(CoreText.Strings["updates.noSource"]);
        }

        private async Task<string> ReadFeedAsync(string url, CancellationToken ct)
        {
            var feed = new Uri(url);
            if (feed.IsFile)
                return await File.ReadAllTextAsync(feed.LocalPath, ct);

            using var http = CreateClient(TimeSpan.FromSeconds(15));
            using var resp = await http.GetAsync(feed, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException(CoreText.Strings.Format("updates.noReleases", _app.Name));
            if ((int)resp.StatusCode == 403 || (int)resp.StatusCode == 429)
                throw new InvalidOperationException(CoreText.Strings["updates.tooManyRequests"]);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        /// <summary>Interpreta latest.json o la risposta di /releases/latest di GitHub (stesso formato).</summary>
        public UpdateInfo? Parse(string json, Version current)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;

            string tag = Str(root, "tag_name") ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
            version = Normalize(version);
            if (version <= current) return null;

            string? setupUrl = null, setupName = null, sha = null;
            long setupSize = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = Str(a, "name") ?? "";
                    if (!_setupName.IsMatch(name)) continue;
                    setupName = name;
                    setupUrl = Str(a, "browser_download_url");
                    setupSize = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                    sha = Str(a, "sha256");
                    if (sha != null && !Regex.IsMatch(sha, "^[0-9a-fA-F]{64}$")) sha = null;
                    break;
                }
            }

            // Si apre solo una pagina del sito o del repository, non un indirizzo qualsiasi.
            var page = Str(root, "html_url");
            if (page == null || !IsOwnPage(page))
                page = ReleasesPage;

            return new UpdateInfo(version, page, (Str(root, "body") ?? "").Trim(), setupUrl, setupName, setupSize, sha?.ToLowerInvariant());
        }

        private bool IsOwnPage(string page) =>
            page.StartsWith(_app.SiteUrl + "/", StringComparison.OrdinalIgnoreCase)
            || (_app.PagesUrl != null && page.StartsWith(_app.PagesUrl + "/", StringComparison.OrdinalIgnoreCase))
            || (_app.GitHubRepo != null && page.StartsWith($"https://github.com/{_app.GitHubRepo}/", StringComparison.OrdinalIgnoreCase));

        /// <summary>True se da questo indirizzo si può scaricare ed eseguire un installer.</summary>
        public bool IsTrustedDownload(string url) =>
            url.StartsWith(_app.SiteUrl + "/download/", StringComparison.OrdinalIgnoreCase)
            || (_app.PagesUrl != null && url.StartsWith(_app.PagesUrl + "/download/", StringComparison.OrdinalIgnoreCase))
            || (GitHubDownloadPrefix != null && url.StartsWith(GitHubDownloadPrefix, StringComparison.OrdinalIgnoreCase))
            || (FeedOverride != null && new Uri(url).IsFile);

        /// <summary>Scarica l'installer nella cartella temporanea e ne restituisce il percorso.</summary>
        public async Task<string> DownloadSetupAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
        {
            if (update.SetupUrl == null || update.SetupName == null)
                throw new InvalidOperationException(CoreText.Strings["updates.noInstaller"]);
            if (!IsTrustedDownload(update.SetupUrl))
                throw new InvalidOperationException(CoreText.Strings["updates.untrustedUrl"]);

            var source = new Uri(update.SetupUrl);
            var folder = Path.Combine(Path.GetTempPath(), _app.Name + "-aggiornamento");
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder, "*.exe"))
                try { File.Delete(old); } catch { /* in uso: pazienza */ }
            var target = Path.Combine(folder, Path.GetFileName(update.SetupName));
            var partial = target + ".part";

            try
            {
                await using (var output = File.Create(partial))
                using (var sha = SHA256.Create())
                {
                    Stream input;
                    long total;
                    HttpResponseMessage? resp = null;
                    HttpClient? http = null;
                    if (source.IsFile)
                    {
                        input = File.OpenRead(source.LocalPath);
                        total = input.Length;
                    }
                    else
                    {
                        http = CreateClient(TimeSpan.FromMinutes(30));
                        resp = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, ct);
                        resp.EnsureSuccessStatusCode();
                        total = resp.Content.Headers.ContentLength ?? update.SetupSize;
                        input = await resp.Content.ReadAsStreamAsync(ct);
                    }

                    try
                    {
                        var buffer = new byte[81920];
                        long done = 0;
                        int read;
                        while ((read = await input.ReadAsync(buffer, ct)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), ct);
                            sha.TransformBlock(buffer, 0, read, null, 0);
                            done += read;
                            if (total > 0) progress?.Report((double)done / total);
                        }
                        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                        if (update.SetupSize > 0 && done != update.SetupSize)
                            throw new IOException(CoreText.Strings.Format("updates.incomplete", done, update.SetupSize));
                        if (update.SetupSha256 != null && Convert.ToHexString(sha.Hash!).ToLowerInvariant() != update.SetupSha256)
                            throw new IOException(CoreText.Strings["updates.hashMismatch"]);
                    }
                    finally
                    {
                        await input.DisposeAsync();
                        resp?.Dispose();
                        http?.Dispose();
                    }
                }

                File.Move(partial, target, overwrite: true);
                return target;
            }
            catch
            {
                try { File.Delete(partial); } catch { /* niente */ }
                throw;
            }
        }

        private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        private static bool Bool(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
    }
}
