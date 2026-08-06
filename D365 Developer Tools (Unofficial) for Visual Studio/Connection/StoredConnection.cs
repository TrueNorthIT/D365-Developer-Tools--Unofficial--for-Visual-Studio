namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection
{
    /// <summary>The subset of a connection that's persisted to disk (no tokens, no secrets).</summary>
    internal sealed class StoredConnection
    {
        public string EnvironmentUrl { get; set; }
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public AuthMode AuthMode { get; set; }

        /// <summary>MSAL HomeAccountId, only set for AuthMode.User connections. Pins silent restore to the exact signed-in account.</summary>
        public string AccountId { get; set; }

        public bool IsSameEnvironmentAs(StoredConnection other)
        {
            return other != null
                && EnvironmentUrl == other.EnvironmentUrl
                && AuthMode == other.AuthMode
                && ClientId == other.ClientId;
        }
    }
}
