using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Auth;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Persistence;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using Newtonsoft.Json;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection
{
    /// <summary>
    /// Ports connectionManager.ts method-for-method. Connection state is persisted per solution (via
    /// SolutionContext) instead of VS Code's per-workspace state; recent environments are global,
    /// matching the original's behavior of not being workspace-scoped.
    /// </summary>
    internal sealed class ConnectionManager : IDisposable
    {
        private const int MaxRecents = 5;
        private static readonly HttpClient Http = new HttpClient();

        private readonly IUserPrompts _prompts;
        private readonly SolutionContext _solutionContext;

        private IAuthProvider _authProvider;

        public D365Connection Connection { get; private set; }
        public bool IsConnected => Connection != null;
        public bool IsRestoring { get; private set; }

        public event EventHandler<D365Connection> ConnectionChanged;

        public ConnectionManager(IUserPrompts prompts, SolutionContext solutionContext)
        {
            _prompts = prompts;
            _solutionContext = solutionContext;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            if (_authProvider == null) { throw new InvalidOperationException("Not connected to a D365 environment."); }
            return await _authProvider.GetAccessTokenAsync().ConfigureAwait(false);
        }

        // ── Restore saved connection on solution open ───────────────────────

        public async Task TryRestoreConnectionAsync()
        {
            var stored = LoadStoredConnection();
            if (stored == null) { return; }

            IsRestoring = true;
            ConnectionChanged?.Invoke(this, null);

            IAuthProvider authProvider;

            if (stored.AuthMode == AuthMode.ClientCredentials)
            {
                if (string.IsNullOrEmpty(stored.ClientId))
                {
                    IsRestoring = false;
                    ConnectionChanged?.Invoke(this, null);
                    OfferReconnect(stored.EnvironmentUrl);
                    return;
                }

                var secret = SecretStore.TryGet(stored.EnvironmentUrl, stored.ClientId);
                if (secret == null)
                {
                    IsRestoring = false;
                    ConnectionChanged?.Invoke(this, null);
                    OfferReconnect(stored.EnvironmentUrl);
                    return;
                }

                authProvider = new ClientCredentialsProvider(stored.EnvironmentUrl, stored.TenantId, stored.ClientId, secret);
            }
            else
            {
                var userProvider = new UserAuthProvider(stored.EnvironmentUrl, stored.TenantId, stored.AccountId);
                try
                {
                    await userProvider.GetAccessTokenAsync(silent: true).ConfigureAwait(false);
                }
                catch
                {
                    userProvider.Dispose();
                    IsRestoring = false;
                    ConnectionChanged?.Invoke(this, null);
                    OfferReconnect(stored.EnvironmentUrl);
                    return;
                }

                authProvider = userProvider;
            }

            try
            {
                var token = await authProvider.GetAccessTokenAsync().ConfigureAwait(false);
                var whoAmI = await CallWhoAmIAsync(stored.EnvironmentUrl, token).ConfigureAwait(false);

                _authProvider = authProvider;
                Connection = ToLiveConnection(stored, authProvider, whoAmI);
                IsRestoring = false;
                ConnectionChanged?.Invoke(this, Connection);
            }
            catch
            {
                authProvider.Dispose();
                IsRestoring = false;
                ConnectionChanged?.Invoke(this, null);
                OfferReconnect(stored.EnvironmentUrl);
            }
        }

        private void OfferReconnect(string environmentUrl)
        {
            _prompts.ShowInfo($"D365: Previously connected to {environmentUrl}. Use D365 Developer Tools > Connect to reconnect.");
        }

        // ── Interactive connect ──────────────────────────────────────────────

        public async Task ConnectAsync()
        {
            var environmentUrl = await _prompts.PromptTextAsync("D365: Environment URL", "yourorg.crm11.dynamics.com").ConfigureAwait(false);
            if (string.IsNullOrEmpty(environmentUrl)) { return; }

            var authMode = await PickAuthModeAsync().ConfigureAwait(false);
            if (authMode == null) { return; }

            var normalizedUrl = NormalizeUrl(environmentUrl);

            string tenantId;
            try
            {
                tenantId = await _prompts.RunWithProgressAsync("D365: Discovering tenant…",
                    () => TenantDiscovery.DiscoverTenantIdAsync(Http, normalizedUrl)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"Tenant discovery failed: {ex.Message}");
                return;
            }

            string clientId = null;
            if (authMode == AuthMode.ClientCredentials)
            {
                clientId = await _prompts.PromptTextAsync("D365: Azure AD application (client) ID", "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx").ConfigureAwait(false);
                if (string.IsNullOrEmpty(clientId)) { return; }
            }

            await EstablishConnectionAsync(new StoredConnection
            {
                EnvironmentUrl = normalizedUrl,
                TenantId = tenantId,
                ClientId = clientId,
                AuthMode = authMode.Value,
            }).ConfigureAwait(false);
        }

        /// <summary>Reconnects to a previously used environment without re-prompting for URL/tenant/auth mode.</summary>
        public Task ConnectToStoredAsync(StoredConnection stored) => EstablishConnectionAsync(stored);

        /// <summary>For a User-auth connection, forces the Microsoft account picker without changing environment.</summary>
        public async Task SwitchAccountAsync()
        {
            if (Connection == null || !(_authProvider is UserAuthProvider userProvider))
            {
                _prompts.ShowInfo("D365: Switching accounts is only available for user sign-in connections.");
                return;
            }

            var environmentUrl = Connection.EnvironmentUrl;

            await _prompts.RunWithProgressAsync("D365: Switching account…", async () =>
            {
                string token;
                try
                {
                    token = await userProvider.SelectAccountAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _prompts.ShowError($"Sign-in failed: {ex.Message}");
                    return;
                }

                try
                {
                    var whoAmI = await CallWhoAmIAsync(environmentUrl, token).ConfigureAwait(false);
                    Connection.WhoAmI = whoAmI;
                    Connection.AccountId = userProvider.AccountId;
                    SaveStoredConnection(Connection.ToStored());
                    ConnectionChanged?.Invoke(this, Connection);
                    _prompts.ShowInfo("D365: Switched account.");
                }
                catch (Exception ex)
                {
                    _prompts.ShowError($"Connected but WhoAmI failed: {ex.Message}");
                }
            }).ConfigureAwait(false);
        }

        public List<StoredConnection> GetRecentEnvironments() =>
            JsonFileStore.Load<List<StoredConnection>>(RecentsPath) ?? new List<StoredConnection>();

        private void RememberEnvironment(StoredConnection stored)
        {
            var updated = new List<StoredConnection> { stored };
            updated.AddRange(GetRecentEnvironments().Where(r => !r.IsSameEnvironmentAs(stored)));
            JsonFileStore.Save(RecentsPath, updated.Take(MaxRecents).ToList());
        }

        private async Task EstablishConnectionAsync(StoredConnection stored)
        {
            IAuthProvider authProvider;

            if (stored.AuthMode == AuthMode.ClientCredentials)
            {
                if (string.IsNullOrEmpty(stored.ClientId))
                {
                    _prompts.ShowError("D365: This connection is missing its client ID.");
                    return;
                }

                var clientSecret = await GetOrPromptClientSecretAsync(stored.EnvironmentUrl, stored.ClientId).ConfigureAwait(false);
                if (clientSecret == null) { return; }

                authProvider = new ClientCredentialsProvider(stored.EnvironmentUrl, stored.TenantId, stored.ClientId, clientSecret);
            }
            else
            {
                authProvider = new UserAuthProvider(stored.EnvironmentUrl, stored.TenantId, stored.AccountId);
            }

            await _prompts.RunWithProgressAsync("D365: Connecting…", async () =>
            {
                string token;
                try
                {
                    // For user auth, always let the user pick which Microsoft account to use for this
                    // connection — otherwise we'd silently reuse whatever account was last remembered.
                    token = authProvider is UserAuthProvider userProvider
                        ? await userProvider.SelectAccountAsync().ConfigureAwait(false)
                        : await authProvider.GetAccessTokenAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _prompts.ShowError($"Authentication failed: {ex.Message}");
                    authProvider.Dispose();
                    return;
                }

                WhoAmIResponse whoAmI;
                try
                {
                    whoAmI = await CallWhoAmIAsync(stored.EnvironmentUrl, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _prompts.ShowError($"Connected but WhoAmI failed: {ex.Message}");
                    authProvider.Dispose();
                    return;
                }

                _authProvider?.Dispose();
                _authProvider = authProvider;
                Connection = ToLiveConnection(stored, authProvider, whoAmI);
                ConnectionChanged?.Invoke(this, Connection);

                var toStore = Connection.ToStored();
                SaveStoredConnection(toStore);
                RememberEnvironment(toStore);

                _prompts.ShowInfo($"Connected to {stored.EnvironmentUrl}");
            }).ConfigureAwait(false);
        }

        public void Disconnect()
        {
            _authProvider?.Dispose();
            _authProvider = null;
            Connection = null;
            ConnectionChanged?.Invoke(this, null);
            DeleteStoredConnection();
            _prompts.ShowInfo("Disconnected from D365 environment.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private async Task<AuthMode?> PickAuthModeAsync()
        {
            var items = new List<PickItem<AuthMode>>
            {
                new PickItem<AuthMode>("User account", "Sign in with your Microsoft account", AuthMode.User),
                new PickItem<AuthMode>("Client credentials", "App-only using client ID + secret", AuthMode.ClientCredentials),
            };
            var pick = await _prompts.PickOneAsync("D365: Authentication method", "How should the extension authenticate?", items).ConfigureAwait(false);
            return pick == null ? (AuthMode?)null : pick.Value;
        }

        private async Task<string> GetOrPromptClientSecretAsync(string environmentUrl, string clientId)
        {
            var stored = SecretStore.TryGet(environmentUrl, clientId);
            if (stored != null) { return stored; }

            var secret = await _prompts.PromptTextAsync("D365: Azure AD client secret", "Paste your client secret here", isPassword: true).ConfigureAwait(false);
            if (string.IsNullOrEmpty(secret)) { return null; }

            SecretStore.Store(environmentUrl, clientId, secret);
            return secret;
        }

        private static async Task<WhoAmIResponse> CallWhoAmIAsync(string environmentUrl, string token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, $"{environmentUrl.TrimEnd('/')}/api/data/v9.2/WhoAmI"))
            {
                request.Headers.Add("Authorization", $"Bearer {token}");
                request.Headers.Add("OData-MaxVersion", "4.0");
                request.Headers.Add("OData-Version", "4.0");
                request.Headers.Add("Accept", "application/json");

                using (var response = await Http.SendAsync(request).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                    }

                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<WhoAmIResponse>(json);
                }
            }
        }

        private static D365Connection ToLiveConnection(StoredConnection stored, IAuthProvider authProvider, WhoAmIResponse whoAmI) => new D365Connection
        {
            EnvironmentUrl = stored.EnvironmentUrl,
            TenantId = stored.TenantId,
            ClientId = stored.ClientId,
            AuthMode = stored.AuthMode,
            AccountId = (authProvider as UserAuthProvider)?.AccountId ?? stored.AccountId,
            WhoAmI = whoAmI,
        };

        private static string NormalizeUrl(string input)
        {
            var trimmed = input.Trim().TrimEnd('/');
            return Regex.IsMatch(trimmed, "^https?://", RegexOptions.IgnoreCase) ? trimmed : $"https://{trimmed}";
        }

        // ── Per-solution persistence ─────────────────────────────────────────

        private static string RecentsPath => System.IO.Path.Combine(JsonFileStore.RootDirectory, "recent-environments.json");

        private string ConnectionPath
        {
            get
            {
                var solutionPath = _solutionContext?.CurrentSolutionPath;
                if (string.IsNullOrEmpty(solutionPath)) { return null; }
                return System.IO.Path.Combine(JsonFileStore.RootDirectory, "connections", JsonFileStore.HashKey(solutionPath) + ".json");
            }
        }

        private StoredConnection LoadStoredConnection()
        {
            var path = ConnectionPath;
            return path == null ? null : JsonFileStore.Load<StoredConnection>(path);
        }

        private void SaveStoredConnection(StoredConnection stored)
        {
            var path = ConnectionPath;
            if (path != null) { JsonFileStore.Save(path, stored); }
        }

        private void DeleteStoredConnection()
        {
            var path = ConnectionPath;
            if (path != null) { JsonFileStore.Delete(path); }
        }

        public void Dispose()
        {
            _authProvider?.Dispose();
        }
    }
}
