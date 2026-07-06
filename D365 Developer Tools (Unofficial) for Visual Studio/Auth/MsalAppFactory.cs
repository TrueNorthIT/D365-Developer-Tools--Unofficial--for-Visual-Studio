using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Desktop;
using Microsoft.Identity.Client.Extensions.Msal;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Auth
{
    /// <summary>
    /// Owns the single MSAL public client application used for all interactive ("user account")
    /// connections, plus its persistent, DPAPI-protected token cache. One MSAL client-id, scoped
    /// per-call to the right tenant/account, rather than one PublicClientApplication per connection.
    ///
    /// Uses Microsoft's own published "XRM Tooling" public client application — the same one
    /// XrmToolBox and most community Dataverse tools use — rather than a dedicated app registration
    /// for this extension. It already has Dataverse's standard interactive-user consent pre-approved,
    /// so signing in just uses the user's own Microsoft/Entra credentials with no extra app-registration
    /// setup step. Its redirect URI is a custom "app://" scheme (not "http://localhost"), which requires
    /// MSAL's embedded WebView rather than the system-browser flow — see UserAuthProvider.
    /// </summary>
    internal static class MsalAppFactory
    {
        // Microsoft's published multitenant "XRM Tooling" client app — see
        // https://learn.microsoft.com/power-apps/developer/data-platform/xrm-tooling/use-connection-strings-xrm-tooling-connect
        public const string ClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";
        public const string RedirectUri = "app://58145b91-0c36-4500-8554-080854f2ac97";

        private static readonly string CacheDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D365DeveloperTools", "msalcache");

        private const string CacheFileName = "msal_user_cache.bin";

        private static readonly SemaphoreSlim InitLock = new SemaphoreSlim(1, 1);
        private static IPublicClientApplication _pca;
        private static MsalCacheHelper _cacheHelper;

        public static async Task<IPublicClientApplication> GetPublicClientAsync()
        {
            if (_pca != null) { return _pca; }

            await InitLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_pca != null) { return _pca; }

                var pca = PublicClientApplicationBuilder
                    .Create(ClientId)
                    .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
                    .WithRedirectUri(RedirectUri)
                    .WithWindowsEmbeddedBrowserSupport()
                    .Build();

                Directory.CreateDirectory(CacheDirectory);
                // On Windows this cache is protected with DPAPI automatically; no extra Linux/Mac config needed.
                var storageProperties = new StorageCreationPropertiesBuilder(CacheFileName, CacheDirectory)
                    .Build();

                _cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties).ConfigureAwait(false);
                _cacheHelper.RegisterCache(pca.UserTokenCache);

                _pca = pca;
                return _pca;
            }
            finally
            {
                InitLock.Release();
            }
        }
    }
}
