using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace DockPost.Engine
{
    /// <summary>Serve di nuovo l'accesso dell'utente (password cambiata, accesso revocato, token scaduto).</summary>
    public sealed class SignInRequiredException : Exception
    {
        public SignInRequiredException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Accesso con l'account Microsoft (Outlook.com, Hotmail, Live, Microsoft 365) per IMAP e SMTP con OAuth 2.0.
    /// L'accesso avviene nel browser predefinito: DockPost non vede mai la password. I token stanno in una cache
    /// che l'app cifra (su Windows con DPAPI) tramite <see cref="LoadCache"/> e <see cref="SaveCache"/>.
    /// Serve l'ID di un'app registrata in Microsoft Entra (client pubblico, account aziendali e personali,
    /// URI di reindirizzamento http://localhost): costante <see cref="DefaultClientId"/> o variabile
    /// d'ambiente DOCKPOST_MS_CLIENT_ID.
    /// </summary>
    public sealed class MicrosoftSignIn
    {
        /// <summary>ID dell'app DockPost registrata in Microsoft Entra. Vuoto finché non c'è la registrazione.</summary>
        public const string DefaultClientId = "";

        public static readonly string[] Scopes =
        {
            "https://outlook.office.com/IMAP.AccessAsUser.All",
            "https://outlook.office.com/SMTP.Send",
            "offline_access",
        };

        private readonly IPublicClientApplication? _app;

        public MicrosoftSignIn(Func<byte[]?> loadCache, Action<byte[]> saveCache, string? clientId = null)
        {
            LoadCache = loadCache;
            SaveCache = saveCache;
            ClientId = clientId ?? Environment.GetEnvironmentVariable("DOCKPOST_MS_CLIENT_ID") ?? DefaultClientId;
            if (!IsConfigured) return;
            _app = PublicClientApplicationBuilder.Create(ClientId)
                .WithAuthority(AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
                .WithRedirectUri("http://localhost")
                .WithClientName("DockPost")
                .Build();
            _app.UserTokenCache.SetBeforeAccess(args =>
            {
                var data = LoadCache();
                if (data != null) args.TokenCache.DeserializeMsalV3(data);
            });
            _app.UserTokenCache.SetAfterAccess(args =>
            {
                if (args.HasStateChanged) SaveCache(args.TokenCache.SerializeMsalV3());
            });
        }

        public string ClientId { get; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
        public Func<byte[]?> LoadCache { get; }
        public Action<byte[]> SaveCache { get; }

        /// <summary>Apre il browser per l'accesso; restituisce l'indirizzo con cui l'utente è entrato.</summary>
        public async Task<string> SignInAsync(string? loginHint, CancellationToken cancel)
        {
            var app = _app ?? throw new InvalidOperationException("Microsoft sign-in is not configured (client ID).");
            var builder = app.AcquireTokenInteractive(Scopes)
                .WithUseEmbeddedWebView(false)
                .WithPrompt(Prompt.SelectAccount);
            if (!string.IsNullOrWhiteSpace(loginHint)) builder = builder.WithLoginHint(loginHint);
            var result = await builder.ExecuteAsync(cancel);
            return result.Account.Username;
        }

        /// <summary>Token per IMAP/SMTP senza chiedere nulla; <see cref="SignInRequiredException"/> se serve rientrare.</summary>
        public async Task<string> AccessTokenAsync(string email, CancellationToken cancel)
        {
            var app = _app ?? throw new SignInRequiredException("Microsoft sign-in is not configured.");
            var accounts = await app.GetAccountsAsync();
            var account = accounts.FirstOrDefault(a => string.Equals(a.Username, email, StringComparison.OrdinalIgnoreCase));
            if (account == null) throw new SignInRequiredException("No saved sign-in for " + email);
            try
            {
                var result = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancel);
                return result.AccessToken;
            }
            catch (MsalUiRequiredException ex)
            {
                throw new SignInRequiredException(ex.Message, ex);
            }
        }

        public async Task SignOutAsync(string email)
        {
            if (_app == null) return;
            foreach (var a in (await _app.GetAccountsAsync()).Where(a => string.Equals(a.Username, email, StringComparison.OrdinalIgnoreCase)))
                await _app.RemoveAsync(a);
        }
    }
}
