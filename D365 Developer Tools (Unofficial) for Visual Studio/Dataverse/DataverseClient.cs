using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse.Dto;
using Newtonsoft.Json;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse
{
    /// <summary>Ports dataverseClient.ts — Dataverse Web API v9.2 access for metadata browsing/codegen.</summary>
    internal sealed class DataverseClient
    {
        private static readonly HttpClient Http = new HttpClient();

        private readonly ConnectionManager _connectionManager;

        public DataverseClient(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public async Task<List<EntityDefinition>> GetEntitiesAsync()
        {
            var url = ApiUrl("EntityDefinitions", "$select=MetadataId,LogicalName,SchemaName,DisplayName,IsCustomEntity");
            var raw = await FetchPagedAsync<EntityDefinitionDto>(url).ConfigureAwait(false);

            return raw
                .Select(e => new EntityDefinition
                {
                    MetadataId = e.MetadataId,
                    LogicalName = e.LogicalName,
                    SchemaName = e.SchemaName,
                    DisplayName = string.IsNullOrEmpty(e.DisplayName.ExtractLabel()) ? e.SchemaName : e.DisplayName.ExtractLabel(),
                    IsCustom = e.IsCustomEntity,
                })
                .OrderBy(e => e.LogicalName, StringComparer.Ordinal)
                .ToList();
        }

        public async Task<List<AttributeDefinition>> GetAttributesAsync(string entityLogicalName)
        {
            var url = ApiUrl(
                $"EntityDefinitions(LogicalName='{entityLogicalName}')/Attributes",
                "$select=LogicalName,SchemaName,DisplayName,AttributeType,IsPrimaryId,IsPrimaryName");

            var raw = await FetchPagedAsync<AttributeDefinitionDto>(url).ConfigureAwait(false);

            return raw
                .Select(a => new AttributeDefinition
                {
                    LogicalName = a.LogicalName,
                    SchemaName = a.SchemaName,
                    DisplayName = string.IsNullOrEmpty(a.DisplayName.ExtractLabel()) ? a.SchemaName : a.DisplayName.ExtractLabel(),
                    AttributeType = a.AttributeType,
                    IsPrimaryId = a.IsPrimaryId,
                    IsPrimaryName = a.IsPrimaryName,
                })
                .OrderBy(a => a.LogicalName, StringComparer.Ordinal)
                .ToList();
        }

        public async Task<List<DataverseSolution>> GetSolutionsAsync()
        {
            // isvisible filters out internal/system solutions like "Default Solution"
            var url = ApiUrl(
                "solutions",
                "$select=solutionid,uniquename,friendlyname",
                "$filter=isvisible eq true",
                "$orderby=friendlyname");

            var raw = await FetchPagedAsync<SolutionDto>(url).ConfigureAwait(false);

            return raw.Select(s => new DataverseSolution
            {
                SolutionId = s.SolutionId,
                UniqueName = s.UniqueName,
                FriendlyName = s.FriendlyName,
            }).ToList();
        }

        public async Task<List<OptionValue>> GetAttributeOptionsAsync(string entityLogicalName, string attributeLogicalName, string attributeType)
        {
            var cast = OptionSetCasts.GetCast(attributeType);
            if (cast == null) { throw new InvalidOperationException($"{attributeType} is not an option-set attribute type"); }

            var baseUrl = _connectionManager.Connection.EnvironmentUrl.TrimEnd('/');
            var url = $"{baseUrl}/api/data/v9.2/EntityDefinitions(LogicalName='{entityLogicalName}')/Attributes(LogicalName='{attributeLogicalName}')/{cast}?$expand=OptionSet,GlobalOptionSet";

            var data = await RequestAsync<AttributeOptionsDto>(url).ConfigureAwait(false);
            var optionSet = data?.OptionSet ?? data?.GlobalOptionSet;

            return (optionSet?.Options ?? new List<OptionDto>())
                .Select(o => new OptionValue
                {
                    Value = o.Value,
                    Label = string.IsNullOrEmpty(o.Label.ExtractLabel()) ? o.Value.ToString() : o.Label.ExtractLabel(),
                })
                .ToList();
        }

        /// <summary>Returns the MetadataIds of entities contained in the given solution.</summary>
        public async Task<HashSet<string>> GetSolutionEntityIdsAsync(string solutionId)
        {
            // componenttype 1 = Entity
            var url = ApiUrl(
                "solutioncomponents",
                "$select=objectid",
                $"$filter=_solutionid_value eq '{solutionId}' and componenttype eq 1");

            var raw = await FetchPagedAsync<SolutionComponentDto>(url).ConfigureAwait(false);
            return new HashSet<string>(raw.Select(c => c.ObjectId));
        }

        // ── Internals ────────────────────────────────────────────────────────

        private string ApiUrl(string resource, params string[] queryParts)
        {
            var baseUrl = _connectionManager.Connection.EnvironmentUrl.TrimEnd('/');
            var query = queryParts.Length > 0 ? "?" + string.Join("&", queryParts) : string.Empty;
            return $"{baseUrl}/api/data/v9.2/{resource}{query}";
        }

        private async Task<List<T>> FetchPagedAsync<T>(string initialUrl)
        {
            var results = new List<T>();
            string url = initialUrl;

            while (url != null)
            {
                var page = await RequestAsync<ODataResponse<T>>(url).ConfigureAwait(false);
                if (page?.Value != null) { results.AddRange(page.Value); }
                url = page?.NextLink;
            }

            return results;
        }

        private async Task<T> RequestAsync<T>(string url, HttpMethod method = null, object body = null)
        {
            var token = await _connectionManager.GetAccessTokenAsync().ConfigureAwait(false);

            using (var request = new HttpRequestMessage(method ?? HttpMethod.Get, url))
            {
                request.Headers.Add("Authorization", $"Bearer {token}");
                request.Headers.Add("OData-MaxVersion", "4.0");
                request.Headers.Add("OData-Version", "4.0");
                request.Headers.Add("Accept", "application/json");

                if (body != null)
                {
                    request.Content = new StringContent(JsonConvert.SerializeObject(body), System.Text.Encoding.UTF8, "application/json");
                }

                using (var response = await Http.SendAsync(request).ConfigureAwait(false))
                {
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"Dataverse API error {(int)response.StatusCode}: {(string.IsNullOrEmpty(text) ? response.ReasonPhrase : text)}");
                    }

                    return string.IsNullOrEmpty(text) ? default : JsonConvert.DeserializeObject<T>(text);
                }
            }
        }
    }
}
