using System.ComponentModel;
using ModelContextProtocol.Server;

namespace D365DeveloperTools.McpServer;

[McpServerToolType]
internal sealed class D365Tools(DataverseClient client)
{
    [McpServerTool(Name = "list_entities"), Description("List entities (tables) in the Dataverse environment, optionally filtered to a solution.")]
    public async Task<string> ListEntitiesAsync(
        [Description("Solution unique name or friendly name (optional)")] string? solution = null,
        CancellationToken cancellationToken = default)
    {
        var entities = await client.GetEntitiesAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(solution))
        {
            var solutions = await client.GetSolutionsAsync(cancellationToken).ConfigureAwait(false);
            var match = solutions.FirstOrDefault(s =>
                string.Equals(s.UniqueName, solution, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.FriendlyName, solution, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                var names = string.Join(", ", solutions.Select(s => $"{s.FriendlyName} ({s.UniqueName})"));
                return $"No solution found matching \"{solution}\".\nAvailable: {names}";
            }

            var entityIds = await client.GetSolutionEntityIdsAsync(match.SolutionId, cancellationToken).ConfigureAwait(false);
            entities = entities.Where(e => entityIds.Contains(e.MetadataId)).ToList();
        }

        return string.Join('\n', entities.Select(e =>
            $"{e.LogicalName}  ({e.DisplayName}){(e.IsCustom ? "  [custom]" : string.Empty)}"));
    }

    [McpServerTool(Name = "get_entity_attributes"), Description("Get all attributes (fields) for a Dataverse entity, with their types and flags.")]
    public async Task<string> GetEntityAttributesAsync(
        [Description("Entity logical name, e.g. \"lead\", \"contact\", \"account\"")] string entityLogicalName,
        CancellationToken cancellationToken)
    {
        var attributes = await client.GetAttributesAsync(entityLogicalName, cancellationToken).ConfigureAwait(false);
        return string.Join('\n', attributes.Select(a =>
        {
            var flags = string.Join("  |  ", new[]
            {
                a.AttributeType,
                a.IsPrimaryId ? "Primary ID" : null,
                a.IsPrimaryName ? "Primary Name" : null,
            }.Where(f => f != null));
            return $"{a.LogicalName}  ({a.DisplayName})  —  {flags}";
        }));
    }

    [McpServerTool(Name = "get_option_values"), Description("Get the option set values for a Picklist, State, or Status attribute.")]
    public async Task<string> GetOptionValuesAsync(
        [Description("Entity logical name")] string entityLogicalName,
        [Description("Attribute logical name")] string attributeLogicalName,
        [Description("Attribute type: Picklist, State, or Status")] string attributeType,
        CancellationToken cancellationToken)
    {
        var options = await client.GetOptionValuesAsync(entityLogicalName, attributeLogicalName, attributeType, cancellationToken).ConfigureAwait(false);
        return string.Join('\n', options.Select(o => $"{o.Value}  —  {o.Label}"));
    }

    [McpServerTool(Name = "generate_class"), Description("Generate an early-bound C# class (Microsoft.Xrm.Sdk.Entity subclass) for a Dataverse entity. Option set fields automatically get companion enums.")]
    public async Task<string> GenerateClassAsync(
        [Description("Entity logical name")] string entityLogicalName,
        [Description("Field logical names to include. Omit to include all fields.")] string[]? fields = null,
        CancellationToken cancellationToken = default)
    {
        var entityDisplayName = await client.GetEntityDisplayNameAsync(entityLogicalName, cancellationToken).ConfigureAwait(false);
        var attributes = await client.GetAttributesAsync(entityLogicalName, cancellationToken).ConfigureAwait(false);

        if (fields is { Length: > 0 })
        {
            var wanted = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
            attributes = attributes.Where(a => wanted.Contains(a.LogicalName)).ToList();
        }

        var enumBlocks = new List<string>();
        var enumNames = new Dictionary<string, string>();

        foreach (var attribute in attributes.Where(a => CodeGenerator.OptionSetTypes.Contains(a.AttributeType)))
        {
            try
            {
                var options = await client.GetOptionValuesAsync(entityLogicalName, attribute.LogicalName, attribute.AttributeType, cancellationToken).ConfigureAwait(false);
                var block = CodeGenerator.GenerateEnum(attribute.LogicalName, attribute.DisplayName, options);
                enumBlocks.Add(block);
                enumNames[attribute.LogicalName] = CodeGenerator.GenerateEnumName(attribute.LogicalName, attribute.DisplayName);
            }
            catch
            {
                // Falls back to a raw OptionSetValue property for this field.
            }
        }

        return CodeGenerator.GenerateClassFile(entityLogicalName, entityDisplayName, attributes, enumNames, enumBlocks);
    }

    [McpServerTool(Name = "generate_enum"), Description("Generate a C# enum for a Picklist, State, or Status attribute.")]
    public async Task<string> GenerateEnumAsync(
        [Description("Entity logical name")] string entityLogicalName,
        [Description("Attribute logical name")] string attributeLogicalName,
        [Description("Attribute type: Picklist, State, or Status")] string attributeType,
        [Description("Display name for the enum (falls back to logical name)")] string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        var options = await client.GetOptionValuesAsync(entityLogicalName, attributeLogicalName, attributeType, cancellationToken).ConfigureAwait(false);
        return CodeGenerator.GenerateEnum(attributeLogicalName, displayName ?? attributeLogicalName, options);
    }
}
