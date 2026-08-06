using System;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using Microsoft.Identity.Client;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Auth
{
    /// <summary>
    /// Interactive ("user account") authentication backed by MSAL.NET. This is the closest available
    /// equivalent to the VS Code extension's use of vscode.authentication's built-in Microsoft
    /// provider: <see cref="GetAccessTokenAsync(bool)"/> with silent=true mirrors VS Code's
    /// { silent: true } restore path (fails rather than prompting), and <see cref="SelectAccountAsync"/>
    /// mirrors { clearSessionPreference: true } (always shows the account picker).
    ///
    /// Unlike VS Code's per-scope session cache, MSAL's token cache is shared across every
    /// tenant/resource a signed-in user has consented to, so the exact account must be pinned via
    /// <see cref="AccountId"/> (MSAL's HomeAccountId) rather than "whatever's cached for this scope".
    /// </summary>
    internal sealed class UserAuthProvider : IAuthProvider
    {
        private readonly string _tenantId;
        private readonly string[] _scopes;

        public string AccountId { get; private set; }

        public UserAuthProvider(string environmentUrl, string tenantId, string accountId = null)
        {
            _tenantId = tenantId;
            _scopes = new[] { $"{environmentUrl.TrimEnd('/')}/.default" };
            AccountId = accountId;
        }

        /// <summary>
        /// silent=true quietly restores a saved connection — throws if no session is ready.
        /// silent=false (default) reuses the pinned account if possible, prompting only if necessary.
        /// Never forces the account picker; use <see cref="SelectAccountAsync"/> for that.
        /// </summary>
        public async Task<string> GetAccessTokenAsync(bool silent = false)
        {
            var pca = await MsalAppFactory.GetPublicClientAsync().ConfigureAwait(false);
            var account = string.IsNullOrEmpty(AccountId)
                ? null
                : await pca.GetAccountAsync(AccountId).ConfigureAwait(false);

            if (account != null)
            {
                try
                {
                    var silentResult = await pca.AcquireTokenSilent(_scopes, account)
                        .WithTenantId(_tenantId)
                        .ExecuteAsync()
                        .ConfigureAwait(false);
                    return silentResult.AccessToken;
                }
                catch (MsalUiRequiredException)
                {
                    if (silent) { throw; }
                }
            }
            else if (silent)
            {
                throw new InvalidOperationException("No cached Microsoft account is available for silent restore.");
            }

            var parentHwnd = await VsShellHelper.GetMainWindowHandleAsync().ConfigureAwait(false);
            var result = await pca.AcquireTokenInteractive(_scopes)
                .WithTenantId(_tenantId)
                // The XRM Tooling client's redirect URI is a custom "app://" scheme, which requires
                // the embedded WebView rather than MSAL's default system-browser + loopback flow.
                .WithUseEmbeddedWebView(true)
                .WithParentActivityOrWindow(parentHwnd)
                .ExecuteAsync()
                .ConfigureAwait(false);

            AccountId = result.Account.HomeAccountId.Identifier;
            return result.AccessToken;
        }

        Task<string> IAuthProvider.GetAccessTokenAsync() => GetAccessTokenAsync(silent: false);

        /// <summary>
        /// Forces the Microsoft account picker, ignoring whichever account is currently pinned.
        /// Intended for explicit user actions (new connection, "Switch Account…") — never for routine
        /// token refreshes, which must stay silent.
        /// </summary>
        public async Task<string> SelectAccountAsync()
        {
            var pca = await MsalAppFactory.GetPublicClientAsync().ConfigureAwait(false);
            var parentHwnd = await VsShellHelper.GetMainWindowHandleAsync().ConfigureAwait(false);
            var result = await pca.AcquireTokenInteractive(_scopes)
                .WithTenantId(_tenantId)
                .WithPrompt(Prompt.SelectAccount)
                .WithUseEmbeddedWebView(true)
                .WithParentActivityOrWindow(parentHwnd)
                .ExecuteAsync()
                .ConfigureAwait(false);

            AccountId = result.Account.HomeAccountId.Identifier;
            return result.AccessToken;
        }

        public void Dispose() { }
    }
}
