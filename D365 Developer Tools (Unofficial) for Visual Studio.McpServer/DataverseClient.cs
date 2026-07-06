using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace D365DeveloperTools.McpServer;

internal sealed record EntityDefinition(string MetadataId, string LogicalName, string SchemaName, string DisplayName, bool IsCustom);

internal sealed record AttributeDefinition(string LogicalName, string SchemaName, string DisplayName, string AttributeType, bool IsPrimaryId, bool IsPrimaryName);

internal sealed record OptionValue(int Value, string Label);

internal sealed record DataverseSolution(string SolutionId, string UniqueName, string FriendlyName);

/// <summary>
/// Minimal standalone Dataverse Web API v9.2 client for the MCP server process. Deliberately
/// self-contained rather than shared with the main extension assembly: this process targets a modern
/// .NET runtime so it can host the MCP SDK, while the extension itself targets .NET Framework 4.7.2
/// for Visual Studio SDK compatibility, so the two can't share a compiled assembly.
/// </summary>
internal sealed class DataverseClient
{
    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Dictionary<string, string> OptionSetCasts = new()
    {
        ["Picklist"] = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata",
        ["State"] = "Microsoft.Dynamics.CRM.StateAttributeMetadata",
        ["Status"] = "Microsoft.Dynamics.CRM.StatusAttributeMetadata",
    };

    private readonly BridgeClient _bridge;

    public DataverseClient(BridgeClient bridge) => _bridge = bridge;

    public async Task<List<EntityDefinition>> GetEntitiesAsync(CancellationToken cancellationToken)
    {
        var raw = await FetchPagedAsync<RawEntity>(
            "EntityDefinitions?$select=MetadataId,LogicalName,SchemaName,DisplayName,IsCustomEntity",
            cancellationToken).ConfigureAwait(false);

        return raw
            .Select(e => new EntityDefinition(
                e.MetadataId,
                e.LogicalName,
                e.SchemaName,
                string.IsNullOrEmpty(ExtractLabel(e.DisplayName)) ? e.SchemaName : ExtractLabel(e.DisplayName),
                e.IsCustomEntity))
            .OrderBy(e => e.LogicalName, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<List<AttributeDefinition>> GetAttributesAsync(string entityLogicalName, CancellationToken cancellationToken)
    {
        var raw = await FetchPagedAsync<RawAttribute>(
            $"EntityDefinitions(LogicalName='{entityLogicalName}')/Attributes" +
            "?$select=LogicalName,SchemaName,DisplayName,AttributeType,IsPrimaryId,IsPrimaryName",
            cancellationToken).ConfigureAwait(false);

        return raw
            .Select(a => new AttributeDefinition(
                a.LogicalName,
                a.SchemaName,
                string.IsNullOrEmpty(ExtractLabel(a.DisplayName)) ? a.SchemaName : ExtractLabel(a.DisplayName),
                a.AttributeType,
                a.IsPrimaryId,
                a.IsPrimaryName))
            .OrderBy(a => a.LogicalName, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<List<OptionValue>> GetOptionValuesAsync(string entityLogicalName, string attributeLogicalName, string attributeType, CancellationToken cancellationToken)
    {
        if (!OptionSetCasts.TryGetValue(attributeType, out var cast))
        {
            throw new InvalidOperationException($"{attributeType} is not a supported option-set type.");
        }

        var data = await RequestAsync<RawOptionSetData>(
            $"EntityDefinitions(LogicalName='{entityLogicalName}')/Attributes(LogicalName='{attributeLogicalName}')/{cast}?$expand=OptionSet,GlobalOptionSet",
            cancellationToken).ConfigureAwait(false);

        var options = data?.OptionSet?.Options ?? data?.GlobalOptionSet?.Options ?? [];
        return options
            .Select(o => new OptionValue(o.Value, string.IsNullOrEmpty(ExtractLabel(o.Label)) ? o.Value.ToString() : ExtractLabel(o.Label)))
            .ToList();
    }

    public async Task<List<DataverseSolution>> GetSolutionsAsync(CancellationToken cancellationToken)
    {
        var raw = await FetchPagedAsync<RawSolution>(
            "solutions?$select=solutionid,uniquename,friendlyname&$filter=isvisible eq true",
            cancellationToken).ConfigureAwait(false);

        return raw.Select(s => new DataverseSolution(s.SolutionId, s.UniqueName, s.FriendlyName)).ToList();
    }

    public async Task<HashSet<string>> GetSolutionEntityIdsAsync(string solutionId, CancellationToken cancellationToken)
    {
        // componenttype 1 = Entity
        var raw = await FetchPagedAsync<RawSolutionComponent>(
            $"solutioncomponents?$select=objectid&$filter=_solutionid_value eq '{solutionId}' and componenttype eq 1",
            cancellationToken).ConfigureAwait(false);

        return raw.Select(c => c.ObjectId).ToHashSet();
    }

    // ── Internals ────────────────────────────────────────────────────────

    private async Task<List<T>> FetchPagedAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        var credentials = await _bridge.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
        string? url = $"{credentials.EnvironmentUrl.TrimEnd('/')}/api/data/v9.2/{relativeUrl}";

        while (url != null)
        {
            var page = await RequestAsync<ODataPage<T>>(url, cancellationToken, isAbsolute: true).ConfigureAwait(false);
            if (page?.Value != null) { results.AddRange(page.Value); }
            url = page?.NextLink;
        }

        return results;
    }

    private async Task<T?> RequestAsync<T>(string url, CancellationToken cancellationToken, bool isAbsolute = false)
    {
        var credentials = await _bridge.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
        var absoluteUrl = isAbsolute ? url : $"{credentials.EnvironmentUrl.TrimEnd('/')}/api/data/v9.2/{url}";

        using var request = new HttpRequestMessage(HttpMethod.Get, absoluteUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.Token);
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Dataverse {(int)response.StatusCode}: {(string.IsNullOrEmpty(body) ? response.ReasonPhrase : body)}");
        }

        return string.IsNullOrEmpty(body) ? default : JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static string ExtractLabel(RawLabel? label)
    {
        if (label == null) { return string.Empty; }
        return label.UserLocalizedLabel?.Label
            ?? label.LocalizedLabels?.FirstOrDefault(l => l.LanguageCode == 1033)?.Label
            ?? label.LocalizedLabels?.FirstOrDefault()?.Label
            ?? string.Empty;
    }

    // ── Raw Dataverse response shapes ───────────────────────────────────

    private sealed class ODataPage<T>
    {
        public List<T>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    private sealed class RawLocalizedLabel
    {
        public string Label { get; set; } = string.Empty;
        public int LanguageCode { get; set; }
    }

    private sealed class RawLabel
    {
        public RawLocalizedLabel? UserLocalizedLabel { get; set; }
        public List<RawLocalizedLabel>? LocalizedLabels { get; set; }
    }

    private sealed class RawEntity
    {
        public string MetadataId { get; set; } = string.Empty;
        public string LogicalName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public RawLabel? DisplayName { get; set; }
        public bool IsCustomEntity { get; set; }
    }

    private sealed class RawAttribute
    {
        public string LogicalName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public RawLabel? DisplayName { get; set; }
        public string AttributeType { get; set; } = string.Empty;
        public bool IsPrimaryId { get; set; }
        public bool IsPrimaryName { get; set; }
    }

    private sealed class RawSolution
    {
        public string SolutionId { get; set; } = string.Empty;
        public string UniqueName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
    }

    private sealed class RawSolutionComponent
    {
        public string ObjectId { get; set; } = string.Empty;
    }

    private sealed class RawOptionItem
    {
        public int Value { get; set; }
        public RawLabel? Label { get; set; }
    }

    private sealed class RawOptionSet
    {
        public List<RawOptionItem>? Options { get; set; }
    }

    private sealed class RawOptionSetData
    {
        public RawOptionSet? OptionSet { get; set; }
        public RawOptionSet? GlobalOptionSet { get; set; }
    }
}
