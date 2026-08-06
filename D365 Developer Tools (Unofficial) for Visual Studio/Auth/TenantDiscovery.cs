using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Auth
{
    /// <summary>
    /// Discovers the Azure AD tenant ID for a Dataverse environment by making an unauthenticated
    /// request and parsing the WWW-Authenticate challenge header, e.g.:
    ///   Bearer authorization_uri="https://login.microsoftonline.com/&lt;tenantId&gt;/oauth2/authorize", ...
    /// </summary>
    internal static class TenantDiscovery
    {
        private static readonly Regex TenantIdPattern = new Regex(
            @"authorization_uri=[""']?https://login\.microsoftonline\.com/([0-9a-fA-F-]{36})/",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task<string> DiscoverTenantIdAsync(HttpClient http, string environmentUrl)
        {
            using (var response = await http.GetAsync($"{environmentUrl.TrimEnd('/')}/api/data/v9.2/").ConfigureAwait(false))
            {
                // A healthy Dataverse endpoint returns 401 for unauthenticated requests.
                if ((int)response.StatusCode != 401)
                {
                    throw new InvalidOperationException(
                        $"Unexpected response from {environmentUrl} (HTTP {(int)response.StatusCode}). " +
                        "Check the environment URL is correct and reachable.");
                }

                if (!response.Headers.TryGetValues("WWW-Authenticate", out var values))
                {
                    throw new InvalidOperationException("No WWW-Authenticate header in response — cannot discover tenant ID.");
                }

                foreach (var headerValue in values)
                {
                    var match = TenantIdPattern.Match(headerValue);
                    if (match.Success) { return match.Groups[1].Value; }
                }

                throw new InvalidOperationException("Could not parse tenant ID from WWW-Authenticate header.");
            }
        }
    }
}
