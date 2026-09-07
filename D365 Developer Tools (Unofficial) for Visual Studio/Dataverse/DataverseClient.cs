using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
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
            var url = ApiUrl("EntityDefinitions", "$select=MetadataId,LogicalName,SchemaName,DisplayName,IsCustomEntity,IconVectorName");
            var raw = await FetchPagedAsync<EntityDefinitionDto>(url).ConfigureAwait(false);

            return raw
                .Select(e => new EntityDefinition
                {
                    MetadataId = e.MetadataId,
                    LogicalName = e.LogicalName,
                    SchemaName = e.SchemaName,
                    DisplayName = string.IsNullOrEmpty(e.DisplayName.ExtractLabel()) ? e.SchemaName : e.DisplayName.ExtractLabel(),
                    IsCustom = e.IsCustomEntity,
                    IconVectorName = string.IsNullOrEmpty(e.IconVectorName) ? null : e.IconVectorName,
                })
                .OrderBy(e => e.LogicalName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Batch-fetches raw SVG bytes for the given (distinct) icon web resource names, keyed by name.
        /// Best-effort: a failed batch or a missing/non-SVG resource is simply absent from the result —
        /// icons are cosmetic, so callers should never let a lookup failure block anything else.
        /// </summary>
        public async Task<Dictionary<string, byte[]>> GetIconSvgContentAsync(IReadOnlyCollection<string> webResourceNames)
        {
            const int batchSize = 15; // keeps the $filter well under Dataverse's URL length limits
            var names = webResourceNames.Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            var result = new Dictionary<string, byte[]>();

            for (var i = 0; i < names.Count; i += batchSize)
            {
                var batch = names.Skip(i).Take(batchSize).ToList();
                var filter = string.Join(" or ", batch.Select(n => $"name eq '{EscapeODataLiteral(n)}'"));
                var url = ApiUrl("webresourceset", "$select=name,content", $"$filter={filter}");

                List<WebResourceContentDto> raw;
                try
                {
                    raw = await FetchPagedAsync<WebResourceContentDto>(url).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                foreach (var item in raw)
                {
                    if (string.IsNullOrEmpty(item.Content)) { continue; }
                    try { result[item.Name] = Convert.FromBase64String(item.Content); }
                    catch (FormatException) { /* not actually base64 — skip */ }
                }
            }

            return result;
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
        public Task<HashSet<string>> GetSolutionEntityIdsAsync(string solutionId) =>
            GetSolutionComponentObjectIdsAsync(solutionId, componentType: 1); // Entity

        /// <summary>Returns the PluginAssemblyIds of plugin assemblies contained in the given solution.</summary>
        public Task<HashSet<string>> GetSolutionPluginAssemblyIdsAsync(string solutionId) =>
            GetSolutionComponentObjectIdsAsync(solutionId, componentType: 91); // Plugin Assembly

        private async Task<HashSet<string>> GetSolutionComponentObjectIdsAsync(string solutionId, int componentType)
        {
            var url = ApiUrl(
                "solutioncomponents",
                "$select=objectid",
                $"$filter=_solutionid_value eq '{solutionId}' and componenttype eq {componentType}");

            var raw = await FetchPagedAsync<SolutionComponentDto>(url).ConfigureAwait(false);
            return new HashSet<string>(raw.Select(c => c.ObjectId));
        }

        // ── Plugin assemblies / types / steps / images ──────────────────────────

        public async Task<List<PluginAssemblyDefinition>> GetPluginAssembliesAsync()
        {
            var url = ApiUrl(
                "pluginassemblies",
                "$select=pluginassemblyid,name,version,isolationmode,sourcetype,description,_packageid_value");

            var raw = await FetchPagedAsync<PluginAssemblyDto>(url).ConfigureAwait(false);

            var packageIds = raw.Where(a => a.PackageIdValue != null).Select(a => a.PackageIdValue).Distinct().ToList();
            var packages = packageIds.Count > 0
                ? await GetPluginPackagesByIdAsync(packageIds).ConfigureAwait(false)
                : new Dictionary<string, PluginPackageDto>();

            return raw
                .Select(a =>
                {
                    PluginPackageDto package = null;
                    if (a.PackageIdValue != null) { packages.TryGetValue(a.PackageIdValue, out package); }

                    return new PluginAssemblyDefinition
                    {
                        PluginAssemblyId = a.PluginAssemblyId,
                        Name = a.Name,
                        Version = a.Version,
                        IsolationMode = PluginOptionLabels.IsolationMode(a.IsolationMode),
                        SourceType = PluginOptionLabels.SourceType(a.SourceType),
                        PackageName = package?.Name,
                        PackageVersion = package?.Version,
                        Description = a.Description,
                    };
                })
                .OrderBy(a => a.Name, StringComparer.Ordinal)
                .ToList();
        }

        private async Task<Dictionary<string, PluginPackageDto>> GetPluginPackagesByIdAsync(List<string> packageIds)
        {
            var filter = string.Join(" or ", packageIds.Select(id => $"pluginpackageid eq '{id}'"));
            var url = ApiUrl("pluginpackages", "$select=pluginpackageid,name,version", $"$filter={filter}");

            var raw = await FetchPagedAsync<PluginPackageDto>(url).ConfigureAwait(false);
            return raw.ToDictionary(p => p.PluginPackageId, p => p);
        }

        public async Task<List<PluginTypeDefinition>> GetPluginTypesAsync(string pluginAssemblyId)
        {
            var url = ApiUrl(
                "plugintypes",
                "$select=plugintypeid,name,friendlyname,typename,isworkflowactivity",
                $"$filter=_pluginassemblyid_value eq '{pluginAssemblyId}'");

            var raw = await FetchPagedAsync<PluginTypeDto>(url).ConfigureAwait(false);

            return raw
                .Select(t => new PluginTypeDefinition
                {
                    PluginTypeId = t.PluginTypeId,
                    Name = t.Name,
                    FriendlyName = t.FriendlyName,
                    TypeName = t.TypeName,
                    IsWorkflowActivity = t.IsWorkflowActivity,
                })
                .OrderBy(t => t.TypeName, StringComparer.Ordinal)
                .ToList();
        }

        public Task<List<SdkMessageStepDefinition>> GetSdkMessageStepsAsync(string pluginTypeId) =>
            GetSdkMessageStepsByFilterAsync($"_plugintypeid_value eq '{pluginTypeId}'", "$orderby=stage,rank");

        public async Task<SdkMessageStepDefinition> GetSdkMessageStepAsync(string stepId)
        {
            var steps = await GetSdkMessageStepsByFilterAsync($"sdkmessageprocessingstepid eq '{stepId}'").ConfigureAwait(false);
            return steps.FirstOrDefault();
        }

        private async Task<List<SdkMessageStepDefinition>> GetSdkMessageStepsByFilterAsync(string filter, string orderBy = null)
        {
            var queryParts = new List<string>
            {
                "$select=sdkmessageprocessingstepid,name,stage,mode,rank,statecode,filteringattributes,description,configuration," +
                    "_sdkmessageid_value,_sdkmessagefilterid_value,_impersonatinguserid_value,_sdkmessageprocessingstepsecureconfigid_value",
                $"$filter={filter}",
            };
            if (orderBy != null) { queryParts.Add(orderBy); }

            var url = ApiUrl("sdkmessageprocessingsteps", queryParts.ToArray());
            var raw = await FetchPagedAsync<SdkMessageStepDto>(url).ConfigureAwait(false);

            var messageIds = raw.Where(s => s.SdkMessageIdValue != null).Select(s => s.SdkMessageIdValue).Distinct().ToList();
            var messageNames = messageIds.Count > 0
                ? await GetSdkMessageNamesByIdAsync(messageIds).ConfigureAwait(false)
                : new Dictionary<string, string>();

            var filterIds = raw.Where(s => s.SdkMessageFilterIdValue != null).Select(s => s.SdkMessageFilterIdValue).Distinct().ToList();
            var filterEntities = filterIds.Count > 0
                ? await GetSdkMessageFilterEntitiesByIdAsync(filterIds).ConfigureAwait(false)
                : new Dictionary<string, string>();

            return raw
                .Select(s => new SdkMessageStepDefinition
                {
                    StepId = s.SdkMessageProcessingStepId,
                    Name = s.Name,
                    SdkMessageId = s.SdkMessageIdValue,
                    MessageName = s.SdkMessageIdValue != null && messageNames.TryGetValue(s.SdkMessageIdValue, out var messageName) ? messageName : null,
                    SdkMessageFilterId = s.SdkMessageFilterIdValue,
                    PrimaryEntity = s.SdkMessageFilterIdValue != null && filterEntities.TryGetValue(s.SdkMessageFilterIdValue, out var entity) ? entity : null,
                    StageValue = s.Stage,
                    Stage = PluginOptionLabels.Stage(s.Stage),
                    ModeValue = s.Mode,
                    Mode = PluginOptionLabels.Mode(s.Mode),
                    Rank = s.Rank,
                    IsEnabled = s.StateCode == 0,
                    FilteringAttributes = s.FilteringAttributes,
                    Description = s.Description,
                    UnsecureConfiguration = s.Configuration,
                    ImpersonatingUserId = s.ImpersonatingUserIdValue,
                    SecureConfigId = s.SecureConfigIdValue,
                })
                .ToList();
        }

        private async Task<Dictionary<string, string>> GetSdkMessageNamesByIdAsync(List<string> sdkMessageIds)
        {
            var filter = string.Join(" or ", sdkMessageIds.Select(id => $"sdkmessageid eq '{id}'"));
            var url = ApiUrl("sdkmessages", "$select=sdkmessageid,name", $"$filter={filter}");

            var raw = await FetchPagedAsync<SdkMessageDto>(url).ConfigureAwait(false);
            return raw.ToDictionary(m => m.SdkMessageId, m => m.Name);
        }

        private async Task<Dictionary<string, string>> GetSdkMessageFilterEntitiesByIdAsync(List<string> sdkMessageFilterIds)
        {
            var filter = string.Join(" or ", sdkMessageFilterIds.Select(id => $"sdkmessagefilterid eq '{id}'"));
            var url = ApiUrl("sdkmessagefilters", "$select=sdkmessagefilterid,primaryobjecttypecode", $"$filter={filter}");

            var raw = await FetchPagedAsync<SdkMessageFilterDto>(url).ConfigureAwait(false);
            return raw.ToDictionary(f => f.SdkMessageFilterId, f => f.PrimaryObjectTypeCode);
        }

        public async Task<List<SdkMessageStepImageDefinition>> GetSdkMessageStepImagesAsync(string stepId)
        {
            var url = ApiUrl(
                "sdkmessageprocessingstepimages",
                "$select=sdkmessageprocessingstepimageid,name,entityalias,imagetype,attributes",
                $"$filter=_sdkmessageprocessingstepid_value eq '{stepId}'");

            var raw = await FetchPagedAsync<SdkMessageStepImageDto>(url).ConfigureAwait(false);

            return raw
                .Select(i => new SdkMessageStepImageDefinition
                {
                    ImageId = i.SdkMessageProcessingStepImageId,
                    Name = i.Name,
                    EntityAlias = i.EntityAlias,
                    ImageTypeValue = i.ImageType,
                    ImageType = PluginOptionLabels.ImageType(i.ImageType),
                    Attributes = i.Attributes,
                })
                .ToList();
        }

        public Task<string> CreateSdkMessageStepImageAsync(string stepId, ImageRegistrationDetails details) =>
            CreateRecordAsync("sdkmessageprocessingstepimages", new Dictionary<string, object>
            {
                ["name"] = details.Name,
                ["entityalias"] = details.EntityAlias,
                ["imagetype"] = details.ImageType,
                ["attributes"] = details.Attributes,
                ["messagepropertyname"] = details.MessagePropertyName,
                ["sdkmessageprocessingstepid@odata.bind"] = $"/sdkmessageprocessingsteps({stepId})",
            }, null);

        public Task UpdateSdkMessageStepImageAsync(string imageId, ImageRegistrationDetails details) =>
            UpdateRecordAsync("sdkmessageprocessingstepimages", imageId, new Dictionary<string, object>
            {
                ["name"] = details.Name,
                ["entityalias"] = details.EntityAlias,
                ["imagetype"] = details.ImageType,
                ["attributes"] = details.Attributes,
                ["messagepropertyname"] = details.MessagePropertyName,
            });

        // ── Publishing plugin assemblies / packages ─────────────────────────────

        public async Task<PluginRecordRef> FindPluginAssemblyByNameAsync(string name)
        {
            var url = ApiUrl("pluginassemblies", "$select=pluginassemblyid,version", $"$filter=name eq '{EscapeODataLiteral(name)}'");
            var raw = await FetchPagedAsync<PluginAssemblyLookupDto>(url).ConfigureAwait(false);
            var match = raw.FirstOrDefault();
            return match == null ? null : new PluginRecordRef { Id = match.PluginAssemblyId, Version = match.Version };
        }

        public async Task<PluginRecordRef> FindPluginPackageByNameAsync(string name)
        {
            var url = ApiUrl("pluginpackages", "$select=pluginpackageid,version", $"$filter=name eq '{EscapeODataLiteral(name)}'");
            var raw = await FetchPagedAsync<PluginPackageLookupDto>(url).ConfigureAwait(false);
            var match = raw.FirstOrDefault();
            return match == null ? null : new PluginRecordRef { Id = match.PluginPackageId, Version = match.Version };
        }

        /// <summary>Creates a new PluginAssembly record. Isolation mode defaults to Sandbox, matching Dataverse's own default for new registrations.</summary>
        public Task<string> CreatePluginAssemblyAsync(string name, string contentBase64, string version, string solutionUniqueName) =>
            CreateRecordAsync("pluginassemblies", new
            {
                name,
                content = contentBase64,
                version,
                isolationmode = 2, // Sandbox
                sourcetype = 0, // Database
            }, solutionUniqueName);

        public Task UpdatePluginAssemblyContentAsync(string pluginAssemblyId, string contentBase64, string version) =>
            UpdateRecordAsync("pluginassemblies", pluginAssemblyId, new { content = contentBase64, version });

        /// <summary>Description is the only assembly field the Plugin Registration Tool itself lets you edit after registration.</summary>
        public Task UpdatePluginAssemblyDescriptionAsync(string pluginAssemblyId, string description) =>
            UpdateRecordAsync("pluginassemblies", pluginAssemblyId, new { description });

        public Task<string> CreatePluginPackageAsync(string name, string contentBase64, string version, string solutionUniqueName) =>
            CreateRecordAsync("pluginpackages", new
            {
                name,
                content = contentBase64,
                version,
            }, solutionUniqueName);

        public Task UpdatePluginPackageContentAsync(string pluginPackageId, string contentBase64, string version) =>
            UpdateRecordAsync("pluginpackages", pluginPackageId, new { content = contentBase64, version });

        public async Task<HashSet<string>> GetExistingPluginTypeNamesAsync(string pluginAssemblyId)
        {
            var url = ApiUrl("plugintypes", "$select=typename", $"$filter=_pluginassemblyid_value eq '{pluginAssemblyId}'");
            var raw = await FetchPagedAsync<PluginTypeNameDto>(url).ConfigureAwait(false);
            return new HashSet<string>(raw.Select(t => t.TypeName), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// workflowActivityGroupName should be non-null (and set to something meaningful, e.g. "{assembly} ({version})",
        /// matching the Plugin Registration Tool's own default) for custom workflow activities — Dataverse won't surface
        /// the activity in the classic process designer if it's left null. isworkflowactivity itself is read-only:
        /// Dataverse determines it server-side from reflecting the assembly, so there's nothing to set for that part.
        /// </summary>
        public Task<string> CreatePluginTypeAsync(string pluginAssemblyId, string typeName, string friendlyName, string solutionUniqueName, string workflowActivityGroupName = null)
        {
            var body = new Dictionary<string, object>
            {
                ["typename"] = typeName,
                ["friendlyname"] = friendlyName,
                ["name"] = typeName,
                ["pluginassemblyid@odata.bind"] = $"/pluginassemblies({pluginAssemblyId})",
            };

            if (!string.IsNullOrEmpty(workflowActivityGroupName))
            {
                body["workflowactivitygroupname"] = workflowActivityGroupName;
            }

            return CreateRecordAsync("plugintypes", body, solutionUniqueName);
        }

        /// <summary>Finds PluginType records matching a fully-qualified type name — may return more than one if the same type name exists in more than one assembly.</summary>
        public async Task<List<PluginTypeMatch>> FindPluginTypesByTypeNameAsync(string typeName)
        {
            var url = ApiUrl(
                "plugintypes",
                "$select=plugintypeid,typename,friendlyname,_pluginassemblyid_value",
                $"$filter=typename eq '{EscapeODataLiteral(typeName)}'");

            var raw = await FetchPagedAsync<PluginTypeMatchDto>(url).ConfigureAwait(false);
            if (raw.Count == 0) { return new List<PluginTypeMatch>(); }

            var assemblyIds = raw.Select(t => t.PluginAssemblyIdValue).Distinct().ToList();
            var filter = string.Join(" or ", assemblyIds.Select(id => $"pluginassemblyid eq '{id}'"));
            var assemblyUrl = ApiUrl("pluginassemblies", "$select=pluginassemblyid,name", $"$filter={filter}");
            var assemblies = (await FetchPagedAsync<PluginAssemblyNameDto>(assemblyUrl).ConfigureAwait(false))
                .ToDictionary(a => a.PluginAssemblyId, a => a.Name);

            return raw
                .Select(t => new PluginTypeMatch
                {
                    PluginTypeId = t.PluginTypeId,
                    PluginAssemblyId = t.PluginAssemblyIdValue,
                    TypeName = t.TypeName,
                    FriendlyName = t.FriendlyName,
                    AssemblyName = assemblies.TryGetValue(t.PluginAssemblyIdValue, out var name) ? name : t.PluginAssemblyIdValue,
                })
                .ToList();
        }

        // ── Unregistering plugin assemblies / types / steps / images ───────────

        /// <summary>Fails if any SdkMessageProcessingSteps still depend on this assembly's types — the same referential-integrity rule the Plugin Registration Tool relies on.</summary>
        public Task DeletePluginAssemblyAsync(string pluginAssemblyId) => DeleteRecordAsync("pluginassemblies", pluginAssemblyId);

        /// <summary>Fails if any SdkMessageProcessingSteps still reference this type.</summary>
        public Task DeletePluginTypeAsync(string pluginTypeId) => DeleteRecordAsync("plugintypes", pluginTypeId);

        public Task DeleteSdkMessageStepAsync(string stepId) => DeleteRecordAsync("sdkmessageprocessingsteps", stepId);

        public Task DeleteSdkMessageStepImageAsync(string imageId) => DeleteRecordAsync("sdkmessageprocessingstepimages", imageId);

        private async Task DeleteRecordAsync(string entitySetName, string id)
        {
            using (await SendAsync(RecordUrl(entitySetName, id), HttpMethod.Delete, null, null).ConfigureAwait(false)) { }
        }

        // ── Custom APIs ──────────────────────────────────────────────────────

        public async Task<List<CustomApiDefinition>> GetCustomApisAsync()
        {
            var url = ApiUrl(
                "customapis",
                "$select=customapiid,uniquename,name,displayname,description,bindingtype,boundentitylogicalname," +
                    "allowedcustomprocessingsteptype,isfunction,isprivate,executeprivilegename,_plugintypeid_value",
                "$orderby=name");

            var raw = await FetchPagedAsync<CustomApiDto>(url).ConfigureAwait(false);

            return raw
                .Select(a => new CustomApiDefinition
                {
                    CustomApiId = a.CustomApiId,
                    UniqueName = a.UniqueName,
                    Name = a.Name,
                    DisplayName = a.DisplayName,
                    Description = a.Description,
                    BindingTypeValue = a.BindingType,
                    BindingType = CustomApiOptionLabels.BindingType(a.BindingType),
                    BoundEntityLogicalName = a.BoundEntityLogicalName,
                    AllowedCustomProcessingStepTypeValue = a.AllowedCustomProcessingStepType,
                    AllowedCustomProcessingStepType = CustomApiOptionLabels.ProcessingStepType(a.AllowedCustomProcessingStepType),
                    IsFunction = a.IsFunction,
                    IsPrivate = a.IsPrivate,
                    ExecutePrivilegeName = a.ExecutePrivilegeName,
                    PluginTypeId = a.PluginTypeIdValue,
                })
                .ToList();
        }

        public Task<string> CreateCustomApiAsync(CustomApiRegistrationDetails details)
        {
            var body = BuildCustomApiBody(details, includeImmutableFields: true);
            return CreateRecordAsync("customapis", body, details.SolutionUniqueName);
        }

        /// <summary>UniqueName/BindingType/BoundEntityLogicalName/AllowedCustomProcessingStepType/IsFunction are omitted here even if changed — Dataverse rejects updates to them, matching the Plugin Registration Tool's own restriction.</summary>
        public Task UpdateCustomApiAsync(string customApiId, CustomApiRegistrationDetails details)
        {
            var body = BuildCustomApiBody(details, includeImmutableFields: false);
            return UpdateRecordAsync("customapis", customApiId, body, details.SolutionUniqueName);
        }

        public Task DeleteCustomApiAsync(string customApiId) => DeleteRecordAsync("customapis", customApiId);

        private static Dictionary<string, object> BuildCustomApiBody(CustomApiRegistrationDetails details, bool includeImmutableFields)
        {
            var body = new Dictionary<string, object>
            {
                ["name"] = details.Name,
                ["displayname"] = details.DisplayName,
                ["description"] = details.Description,
                ["isprivate"] = details.IsPrivate,
                ["executeprivilegename"] = details.ExecutePrivilegeName,
            };

            if (includeImmutableFields)
            {
                body["uniquename"] = details.UniqueName;
                body["bindingtype"] = details.BindingType;
                body["allowedcustomprocessingsteptype"] = details.AllowedCustomProcessingStepType;
                body["isfunction"] = details.IsFunction;

                if (!string.IsNullOrEmpty(details.BoundEntityLogicalName))
                {
                    body["boundentitylogicalname"] = details.BoundEntityLogicalName;
                }
            }

            if (!string.IsNullOrEmpty(details.PluginTypeId))
            {
                body["PluginTypeId@odata.bind"] = $"/plugintypes({details.PluginTypeId})";
            }

            return body;
        }

        public async Task<List<CustomApiParameterDefinition>> GetCustomApiRequestParametersAsync(string customApiId)
        {
            var url = ApiUrl(
                "customapirequestparameters",
                "$select=customapirequestparameterid,uniquename,name,displayname,description,type,logicalentityname,isoptional",
                $"$filter=_customapiid_value eq '{customApiId}'",
                "$orderby=name");

            var raw = await FetchPagedAsync<CustomApiRequestParameterDto>(url).ConfigureAwait(false);
            return raw.Select(p => new CustomApiParameterDefinition
            {
                Id = p.CustomApiRequestParameterId,
                UniqueName = p.UniqueName,
                Name = p.Name,
                DisplayName = p.DisplayName,
                Description = p.Description,
                TypeValue = p.Type,
                Type = CustomApiOptionLabels.ParameterType(p.Type),
                LogicalEntityName = p.LogicalEntityName,
                IsRequestParameter = true,
                IsOptional = p.IsOptional,
            }).ToList();
        }

        public async Task<List<CustomApiParameterDefinition>> GetCustomApiResponsePropertiesAsync(string customApiId)
        {
            var url = ApiUrl(
                "customapiresponseproperties",
                "$select=customapiresponsepropertyid,uniquename,name,displayname,description,type,logicalentityname",
                $"$filter=_customapiid_value eq '{customApiId}'",
                "$orderby=name");

            var raw = await FetchPagedAsync<CustomApiResponsePropertyDto>(url).ConfigureAwait(false);
            return raw.Select(p => new CustomApiParameterDefinition
            {
                Id = p.CustomApiResponsePropertyId,
                UniqueName = p.UniqueName,
                Name = p.Name,
                DisplayName = p.DisplayName,
                Description = p.Description,
                TypeValue = p.Type,
                Type = CustomApiOptionLabels.ParameterType(p.Type),
                LogicalEntityName = p.LogicalEntityName,
                IsRequestParameter = false,
            }).ToList();
        }

        public Task<string> CreateCustomApiRequestParameterAsync(string customApiId, CustomApiParameterRegistrationDetails details) =>
            CreateRecordAsync("customapirequestparameters", BuildCustomApiParameterBody(details, includeImmutableFields: true, isRequestParameter: true, customApiId), null);

        public Task UpdateCustomApiRequestParameterAsync(string parameterId, CustomApiParameterRegistrationDetails details) =>
            UpdateRecordAsync("customapirequestparameters", parameterId, BuildCustomApiParameterBody(details, includeImmutableFields: false, isRequestParameter: true, null));

        public Task DeleteCustomApiRequestParameterAsync(string parameterId) => DeleteRecordAsync("customapirequestparameters", parameterId);

        public Task<string> CreateCustomApiResponsePropertyAsync(string customApiId, CustomApiParameterRegistrationDetails details) =>
            CreateRecordAsync("customapiresponseproperties", BuildCustomApiParameterBody(details, includeImmutableFields: true, isRequestParameter: false, customApiId), null);

        public Task UpdateCustomApiResponsePropertyAsync(string propertyId, CustomApiParameterRegistrationDetails details) =>
            UpdateRecordAsync("customapiresponseproperties", propertyId, BuildCustomApiParameterBody(details, includeImmutableFields: false, isRequestParameter: false, null));

        public Task DeleteCustomApiResponsePropertyAsync(string propertyId) => DeleteRecordAsync("customapiresponseproperties", propertyId);

        /// <summary>Shared by both request-parameter and response-property create/update — identical body shape except customapiresponseproperty has no isoptional field at all, so it must never be sent for those.</summary>
        private static Dictionary<string, object> BuildCustomApiParameterBody(CustomApiParameterRegistrationDetails details, bool includeImmutableFields, bool isRequestParameter, string customApiId)
        {
            var body = new Dictionary<string, object>
            {
                ["name"] = details.Name,
                ["displayname"] = details.DisplayName,
                ["description"] = details.Description,
            };

            if (includeImmutableFields)
            {
                body["uniquename"] = details.UniqueName;
                body["type"] = details.Type;

                if (isRequestParameter)
                {
                    body["isoptional"] = details.IsOptional;
                }

                if (!string.IsNullOrEmpty(details.LogicalEntityName))
                {
                    body["logicalentityname"] = details.LogicalEntityName;
                }

                if (!string.IsNullOrEmpty(customApiId))
                {
                    body["CustomAPIId@odata.bind"] = $"/customapis({customApiId})";
                }
            }

            return body;
        }

        /// <summary>Returns the solutions (from the same set GetSolutionsAsync returns) that contain the given plugin assembly.</summary>
        public Task<List<DataverseSolution>> GetSolutionsContainingPluginAssemblyAsync(string pluginAssemblyId) =>
            GetSolutionsContainingComponentAsync(pluginAssemblyId, componentType: 91); // Plugin Assembly

        /// <summary>Returns the solutions (from the same set GetSolutionsAsync returns) that contain the given SDK message processing step.</summary>
        public Task<List<DataverseSolution>> GetSolutionsContainingStepAsync(string stepId) =>
            GetSolutionsContainingComponentAsync(stepId, componentType: 92); // SDK Message Processing Step

        private async Task<List<DataverseSolution>> GetSolutionsContainingComponentAsync(string objectId, int componentType)
        {
            var url = ApiUrl(
                "solutioncomponents",
                "$select=_solutionid_value",
                $"$filter=objectid eq '{objectId}' and componenttype eq {componentType}");

            var raw = await FetchPagedAsync<SolutionComponentSolutionDto>(url).ConfigureAwait(false);
            var solutionIds = new HashSet<string>(raw.Select(c => c.SolutionIdValue));
            if (solutionIds.Count == 0) { return new List<DataverseSolution>(); }

            var allSolutions = await GetSolutionsAsync().ConfigureAwait(false);
            return allSolutions.Where(s => solutionIds.Contains(s.SolutionId)).ToList();
        }

        /// <summary>Enabled users, for the step editor's "Run in User's Context" (impersonation) picker.</summary>
        public async Task<List<SystemUserOption>> GetUsersAsync()
        {
            var url = ApiUrl("systemusers", "$select=systemuserid,fullname", "$filter=isdisabled eq false", "$orderby=fullname");
            var raw = await FetchPagedAsync<SystemUserDto>(url).ConfigureAwait(false);
            return raw.Select(u => new SystemUserOption { UserId = u.SystemUserId, FullName = u.FullName }).ToList();
        }

        public async Task<List<SdkMessageOption>> GetSdkMessagesAsync()
        {
            var url = ApiUrl("sdkmessages", "$select=sdkmessageid,name", "$orderby=name");
            var raw = await FetchPagedAsync<SdkMessageDto>(url).ConfigureAwait(false);
            return raw.Select(m => new SdkMessageOption { SdkMessageId = m.SdkMessageId, Name = m.Name }).ToList();
        }

        /// <summary>The entities a given message can be filtered to. Empty if the message supports registering without an entity filter.</summary>
        public async Task<List<SdkMessageFilterOption>> GetSdkMessageFiltersAsync(string sdkMessageId)
        {
            var url = ApiUrl(
                "sdkmessagefilters",
                "$select=sdkmessagefilterid,primaryobjecttypecode",
                $"$filter=_sdkmessageid_value eq '{sdkMessageId}'",
                "$orderby=primaryobjecttypecode");

            var raw = await FetchPagedAsync<SdkMessageFilterDto>(url).ConfigureAwait(false);
            return raw
                .Where(f => !string.IsNullOrEmpty(f.PrimaryObjectTypeCode))
                .Select(f => new SdkMessageFilterOption { SdkMessageFilterId = f.SdkMessageFilterId, EntityLogicalName = f.PrimaryObjectTypeCode })
                .ToList();
        }

        public async Task<string> CreateSdkMessageStepAsync(string pluginTypeId, StepRegistrationDetails details)
        {
            var body = new Dictionary<string, object>
            {
                ["name"] = details.Name,
                ["stage"] = details.Stage,
                ["mode"] = details.Mode,
                ["rank"] = details.Rank,
                ["description"] = details.Description,
                ["configuration"] = details.UnsecureConfiguration,
                ["plugintypeid@odata.bind"] = $"/plugintypes({pluginTypeId})",
                ["sdkmessageid@odata.bind"] = $"/sdkmessages({details.SdkMessageId})",
            };

            if (!string.IsNullOrEmpty(details.SdkMessageFilterId))
            {
                body["sdkmessagefilterid@odata.bind"] = $"/sdkmessagefilters({details.SdkMessageFilterId})";
            }

            if (!string.IsNullOrEmpty(details.FilteringAttributes))
            {
                body["filteringattributes"] = details.FilteringAttributes;
            }

            if (!string.IsNullOrEmpty(details.ImpersonatingUserId))
            {
                body["impersonatinguserid@odata.bind"] = $"/systemusers({details.ImpersonatingUserId})";
            }

            if (!string.IsNullOrEmpty(details.SecureConfiguration))
            {
                var secureConfigId = await CreateSdkMessageStepSecureConfigAsync(details.SecureConfiguration).ConfigureAwait(false);
                body["sdkmessageprocessingstepsecureconfigid@odata.bind"] = $"/sdkmessageprocessingstepsecureconfigs({secureConfigId})";
            }

            return await CreateRecordAsync("sdkmessageprocessingsteps", body, details.SolutionUniqueName).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates an existing SDK message processing step. Message/entity/stage/mode/rank/filtering
        /// attributes are all editable, matching the Plugin Registration Tool's own "Update Step" dialog.
        /// Clearing the entity filter or impersonating user (switching back to "all entities" / "calling
        /// user") needs a separate $ref delete — Web API PATCH can't null out a single-valued navigation
        /// property by binding it to nothing. A blank SecureConfiguration is treated as "leave it
        /// unchanged", not "clear it" — see StepRegistrationDetails.SecureConfiguration.
        /// </summary>
        public async Task UpdateSdkMessageStepAsync(string stepId, StepRegistrationDetails details)
        {
            var body = new Dictionary<string, object>
            {
                ["name"] = details.Name,
                ["stage"] = details.Stage,
                ["mode"] = details.Mode,
                ["rank"] = details.Rank,
                ["filteringattributes"] = details.FilteringAttributes,
                ["description"] = details.Description,
                ["configuration"] = details.UnsecureConfiguration,
                ["sdkmessageid@odata.bind"] = $"/sdkmessages({details.SdkMessageId})",
            };

            if (!string.IsNullOrEmpty(details.SdkMessageFilterId))
            {
                body["sdkmessagefilterid@odata.bind"] = $"/sdkmessagefilters({details.SdkMessageFilterId})";
            }

            if (!string.IsNullOrEmpty(details.ImpersonatingUserId))
            {
                body["impersonatinguserid@odata.bind"] = $"/systemusers({details.ImpersonatingUserId})";
            }

            if (!string.IsNullOrEmpty(details.SecureConfiguration))
            {
                if (string.IsNullOrEmpty(details.ExistingSecureConfigId))
                {
                    var secureConfigId = await CreateSdkMessageStepSecureConfigAsync(details.SecureConfiguration).ConfigureAwait(false);
                    body["sdkmessageprocessingstepsecureconfigid@odata.bind"] = $"/sdkmessageprocessingstepsecureconfigs({secureConfigId})";
                }
                else
                {
                    await UpdateSdkMessageStepSecureConfigAsync(details.ExistingSecureConfigId, details.SecureConfiguration).ConfigureAwait(false);
                }
            }

            await UpdateRecordAsync("sdkmessageprocessingsteps", stepId, body, details.SolutionUniqueName).ConfigureAwait(false);

            if (string.IsNullOrEmpty(details.SdkMessageFilterId))
            {
                await ClearSdkMessageStepFilterAsync(stepId).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(details.ImpersonatingUserId))
            {
                await ClearSdkMessageStepImpersonationAsync(stepId).ConfigureAwait(false);
            }
        }

        /// <summary>Activates or deactivates a registered step. Enabled = statecode 0/statuscode 1, Disabled = statecode 1/statuscode 2.</summary>
        public Task SetSdkMessageStepEnabledAsync(string stepId, bool enabled) =>
            UpdateRecordAsync("sdkmessageprocessingsteps", stepId, new
            {
                statecode = enabled ? 0 : 1,
                statuscode = enabled ? 1 : 2,
            });

        private Task<string> CreateSdkMessageStepSecureConfigAsync(string secureConfigValue) =>
            CreateRecordAsync("sdkmessageprocessingstepsecureconfigs", new { secureconfig = secureConfigValue }, null);

        private Task UpdateSdkMessageStepSecureConfigAsync(string secureConfigId, string secureConfigValue) =>
            UpdateRecordAsync("sdkmessageprocessingstepsecureconfigs", secureConfigId, new { secureconfig = secureConfigValue });

        private async Task ClearSdkMessageStepFilterAsync(string stepId)
        {
            var url = $"{RecordUrl("sdkmessageprocessingsteps", stepId)}/sdkmessagefilterid/$ref";
            using (await SendAsync(url, HttpMethod.Delete, null, null, HttpStatusCode.NotFound).ConfigureAwait(false)) { }
        }

        private async Task ClearSdkMessageStepImpersonationAsync(string stepId)
        {
            var url = $"{RecordUrl("sdkmessageprocessingsteps", stepId)}/impersonatinguserid/$ref";
            using (await SendAsync(url, HttpMethod.Delete, null, null, HttpStatusCode.NotFound).ConfigureAwait(false)) { }
        }

        // ── Plugin trace logs ────────────────────────────────────────────────

        /// <summary>
        /// Queries plugintracelogs for one bounded, newest-first page. Deliberately does NOT use
        /// FetchPagedAsync&lt;T&gt; — that follows every @odata.nextLink unconditionally, which is fine
        /// for bounded metadata sets but would hang the UI against a busy org's trace log table.
        /// filter.Top caps the page size instead; PluginTraceLogPage.NextLink is retained for a future
        /// "Load more" affordance, not wired into the v1 UI.
        /// </summary>
        public async Task<PluginTraceLogPage> GetPluginTraceLogsAsync(PluginTraceLogFilter filter)
        {
            var filters = new List<string>();

            // plugintracelog.typename is confirmed (against a live environment) to hold the *assembly-
            // qualified* type name ("Namespace.Class, AssemblyName, Version=..., Culture=..., PublicKeyToken=...")
            // — not the bare class name plugintype.typename holds (what Plugin Explorer surfaces). An
            // eq comparison against the bare name therefore never matches; startswith against the bare
            // name plus a trailing ", " anchors it as an exact type-name prefix rather than a fuzzy
            // substring match. Dataverse's Web API supports startswith/contains/endswith in $filter
            // (unlike tolower/toupper/etc., which it doesn't), so this doesn't need a client-side fallback.
            if (!string.IsNullOrEmpty(filter.TypeName)) { filters.Add($"startswith(typename,'{EscapeODataLiteral(filter.TypeName)}, ')"); }
            if (!string.IsNullOrEmpty(filter.PrimaryEntity)) { filters.Add($"primaryentity eq '{EscapeODataLiteral(filter.PrimaryEntity)}'"); }
            if (!string.IsNullOrEmpty(filter.MessageName)) { filters.Add($"messagename eq '{EscapeODataLiteral(filter.MessageName)}'"); }
            if (!string.IsNullOrEmpty(filter.CorrelationId)) { filters.Add($"correlationid eq '{filter.CorrelationId}'"); }
            if (filter.From.HasValue) { filters.Add($"createdon ge {filter.From.Value:yyyy-MM-ddTHH:mm:ssZ}"); }
            if (filter.To.HasValue) { filters.Add($"createdon le {filter.To.Value:yyyy-MM-ddTHH:mm:ssZ}"); }
            if (filter.ExceptionsOnly) { filters.Add("exceptiondetails ne null"); }

            var queryParts = new List<string>
            {
                "$select=plugintracelogid,typename,messagename,primaryentity,performanceexecutionduration," +
                    "exceptiondetails,messageblock,createdon,correlationid,depth,mode,operationtype," +
                    "persistencekey",
                "$orderby=createdon desc",
                $"$top={filter.Top}",
            };
            if (filters.Count > 0) { queryParts.Add($"$filter={string.Join(" and ", filters)}"); }

            var url = ApiUrl("plugintracelogs", queryParts.ToArray());

            // A direct RequestAsync (not FetchPagedAsync) — see the method summary above.
            var page = await RequestAsync<ODataResponse<PluginTraceLogDto>>(url).ConfigureAwait(false);

            return new PluginTraceLogPage
            {
                Items = (page?.Value ?? new List<PluginTraceLogDto>()).Select(MapTraceLog).ToList(),
                NextLink = page?.NextLink,
            };
        }

        private static PluginTraceLogEntry MapTraceLog(PluginTraceLogDto t) => new PluginTraceLogEntry
        {
            TraceLogId = t.PluginTraceLogId,
            TypeName = t.TypeName,
            MessageName = t.MessageName,
            PrimaryEntity = t.PrimaryEntity,
            PerformanceExecutionDuration = t.PerformanceExecutionDuration,
            ExceptionDetails = t.ExceptionDetails,
            MessageBlock = t.MessageBlock,
            CreatedOn = t.CreatedOn,
            CorrelationId = t.CorrelationId,
            Depth = t.Depth,
            ModeValue = t.Mode,
            Mode = PluginOptionLabels.Mode(t.Mode),
            OperationTypeValue = t.OperationType,
            OperationType = PluginOptionLabels.OperationType(t.OperationType),
            PersistenceKey = t.PersistenceKey,
            HasProfilingData = !string.IsNullOrEmpty(t.PersistenceKey),
        };

        /// <summary>Reads the org's tracing level so the UI can warn "tracing is off" instead of showing a confusing empty list.</summary>
        public async Task<PluginTraceLogSettingsInfo> GetPluginTraceLogSettingAsync()
        {
            var orgId = _connectionManager.Connection.WhoAmI.OrganizationId;
            var url = $"{RecordUrl("organizations", orgId)}?$select=organizationid,plugintracelogsetting";
            var org = await RequestAsync<OrganizationTraceSettingDto>(url).ConfigureAwait(false);

            return org == null
                ? null
                : new PluginTraceLogSettingsInfo { OrganizationId = org.OrganizationId, Setting = (PluginTraceLogSetting)org.PluginTraceLogSetting };
        }

        /// <summary>Changes the org-wide tracing level — the same write the Plugin Registration Tool's "Settings" dialog performs.</summary>
        public Task SetPluginTraceLogSettingAsync(string organizationId, PluginTraceLogSetting setting) =>
            UpdateRecordAsync("organizations", organizationId, new { plugintracelogsetting = (int)setting });

        // ── Internals ────────────────────────────────────────────────────────

        private string ApiUrl(string resource, params string[] queryParts)
        {
            var baseUrl = _connectionManager.Connection.EnvironmentUrl.TrimEnd('/');
            var query = queryParts.Length > 0 ? "?" + string.Join("&", queryParts) : string.Empty;
            return $"{baseUrl}/api/data/v9.2/{resource}{query}";
        }

        private string RecordUrl(string entitySetName, string id) => $"{ApiUrl(entitySetName)}({id})";

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
            using (var response = await SendAsync(url, method ?? HttpMethod.Get, body, null).ConfigureAwait(false))
            {
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return string.IsNullOrEmpty(text) ? default : JsonConvert.DeserializeObject<T>(text);
            }
        }

        /// <summary>Creates a record and returns its new ID, parsed from the OData-EntityId response header (Dataverse returns 204 No Content on create).</summary>
        private async Task<string> CreateRecordAsync(string entitySetName, object body, string solutionUniqueName)
        {
            var headers = string.IsNullOrEmpty(solutionUniqueName)
                ? null
                : new Dictionary<string, string> { ["MSCRM.SolutionUniqueName"] = solutionUniqueName };

            using (var response = await SendAsync(ApiUrl(entitySetName), HttpMethod.Post, body, headers).ConfigureAwait(false))
            {
                if (!response.Headers.TryGetValues("OData-EntityId", out var values))
                {
                    throw new InvalidOperationException("Dataverse did not return an OData-EntityId header for the created record.");
                }

                var match = Regex.Match(values.First(), @"\(([0-9a-fA-F-]{36})\)");
                if (!match.Success)
                {
                    throw new InvalidOperationException($"Could not parse the created record's ID from: {values.First()}");
                }

                return match.Groups[1].Value;
            }
        }

        private async Task UpdateRecordAsync(string entitySetName, string id, object body, string solutionUniqueName = null)
        {
            var headers = string.IsNullOrEmpty(solutionUniqueName)
                ? null
                : new Dictionary<string, string> { ["MSCRM.SolutionUniqueName"] = solutionUniqueName };

            using (await SendAsync(RecordUrl(entitySetName, id), new HttpMethod("PATCH"), body, headers).ConfigureAwait(false))
            {
                // Dataverse returns 204 No Content on a successful update; nothing further to read.
            }
        }

        private async Task<HttpResponseMessage> SendAsync(string url, HttpMethod method, object body, IDictionary<string, string> extraHeaders, HttpStatusCode? alsoAcceptable = null)
        {
            var token = await _connectionManager.GetAccessTokenAsync().ConfigureAwait(false);

            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Add("Accept", "application/json");

            if (extraHeaders != null)
            {
                foreach (var header in extraHeaders) { request.Headers.Add(header.Key, header.Value); }
            }

            if (body != null)
            {
                request.Content = new StringContent(JsonConvert.SerializeObject(body), System.Text.Encoding.UTF8, "application/json");
            }

            var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && response.StatusCode != alsoAcceptable)
            {
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                response.Dispose();
                throw new InvalidOperationException($"Dataverse API error {(int)response.StatusCode}: {(string.IsNullOrEmpty(text) ? response.ReasonPhrase : text)}");
            }

            return response;
        }

        private static string EscapeODataLiteral(string value) => value?.Replace("'", "''");
    }
}
