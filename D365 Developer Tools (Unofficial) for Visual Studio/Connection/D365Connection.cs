namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection
{
    /// <summary>An established, live connection (in-memory only; see StoredConnection for the persisted subset).</summary>
    internal sealed class D365Connection
    {
        public string EnvironmentUrl { get; set; }
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public AuthMode AuthMode { get; set; }
        public string AccountId { get; set; }
        public WhoAmIResponse WhoAmI { get; set; }

        public StoredConnection ToStored() => new StoredConnection
        {
            EnvironmentUrl = EnvironmentUrl,
            TenantId = TenantId,
            ClientId = ClientId,
            AuthMode = AuthMode,
            AccountId = AccountId,
        };
    }
}
