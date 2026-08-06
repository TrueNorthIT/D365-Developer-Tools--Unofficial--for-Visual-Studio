using System;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Auth
{
    /// <summary>
    /// App-only authentication using the Azure AD client credentials flow. Used when the extension
    /// authenticates as a service/daemon rather than on behalf of a signed-in user.
    /// </summary>
    internal sealed class ClientCredentialsProvider : IAuthProvider
    {
        private readonly IConfidentialClientApplication _app;
        private readonly string[] _scopes;

        public ClientCredentialsProvider(string environmentUrl, string tenantId, string clientId, string clientSecret)
        {
            _scopes = new[] { $"{environmentUrl.TrimEnd('/')}/.default" };

            _app = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .Build();
        }

        public async Task<string> GetAccessTokenAsync()
        {
            // MSAL caches silently in-memory; the first call acquires a token, later calls reuse it
            // until it's close to expiry.
            var result = await _app.AcquireTokenForClient(_scopes).ExecuteAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(result?.AccessToken))
            {
                throw new InvalidOperationException("Failed to acquire an access token from Azure AD.");
            }

            return result.AccessToken;
        }

        public void Dispose() { }
    }
}
