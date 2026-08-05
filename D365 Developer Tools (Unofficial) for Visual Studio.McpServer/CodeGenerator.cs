using System.Text;
using System.Text.RegularExpressions;

namespace D365DeveloperTools.McpServer;

/// <summary>
/// Generates the same early-bound C# entity classes and option-set enums as the Visual Studio
/// extension's CodeGen/EarlyBoundClassGenerator + EnumGenerator, kept in sync by hand since this
/// process can't reference the extension's .NET Framework assembly directly (see DataverseClient).
/// </summary>
internal static class CodeGenerator
{
    public static readonly HashSet<string> OptionSetTypes = ["Picklist", "State", "Status"];

    public static string ToPascalCase(string s)
    {
        var sanitized = Regex.Replace(s ?? string.Empty, "[^a-zA-Z0-9]+", " ").Trim();
        var pascal = string.Concat(sanitized
            .Split([' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

        return Regex.IsMatch(pascal, @"^\d") ? "_" + pascal : pascal;
    }

    /// <summary>Prefers the friendly display name over the logical name, falling back when the display name is missing.</summary>
    public static string ToPascalCase(string logicalName, string displayName) =>
        ToPascalCase(string.IsNullOrEmpty(displayName) ? logicalName : displayName);

    public static string ToEnumKey(string label)
    {
        var sanitized = Regex.Replace(label ?? string.Empty, "[^a-zA-Z0-9 _]", " ").Trim();
        var pascal = ToPascalCase(string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized);
        return Regex.IsMatch(pascal, @"^\d") ? "_" + pascal : pascal;
    }

    public static string GenerateEnum(string attributeLogicalName, string attributeDisplayName, IReadOnlyList<OptionValue> options)
    {
        var enumName = ToPascalCase(string.IsNullOrEmpty(attributeDisplayName) ? attributeLogicalName : attributeDisplayName);
        var sb = new StringBuilder();

        sb.Append("public enum ").AppendLine(enumName);
        sb.AppendLine("{");
        foreach (var option in options)
        {
            sb.Append("    ").Append(ToEnumKey(option.Label)).Append(" = ").Append(option.Value).AppendLine(",");
        }
        sb.AppendLine("}");

        return sb.ToString();
    }

    public static string GenerateEnumName(string attributeLogicalName, string attributeDisplayName) =>
        ToPascalCase(attributeLogicalName, attributeDisplayName);

    /// <summary>Full, ready-to-paste document: any option-set enums, then the entity class.</summary>
    public static string GenerateClassFile(
        string entityLogicalName,
        string entityDisplayName,
        IReadOnlyList<AttributeDefinition> selectedAttributes,
        IReadOnlyDictionary<string, string> optionSetEnumNames,
        IEnumerable<string> enumBlocks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using Microsoft.Xrm.Sdk;");
        sb.AppendLine("using Microsoft.Xrm.Sdk.Client;");
        sb.AppendLine();

        foreach (var enumBlock in enumBlocks)
        {
            sb.AppendLine(enumBlock.TrimEnd());
            sb.AppendLine();
        }

        var primaryIdAttribute = selectedAttributes.FirstOrDefault(a => a.IsPrimaryId);
        sb.Append(GenerateClass(entityLogicalName, entityDisplayName, selectedAttributes, primaryIdAttribute, optionSetEnumNames));
        return sb.ToString();
    }

    private static string GenerateClass(
        string entityLogicalName,
        string entityDisplayName,
        IReadOnlyList<AttributeDefinition> selectedAttributes,
        AttributeDefinition? primaryIdAttribute,
        IReadOnlyDictionary<string, string> optionSetEnumNames)
    {
        var className = ToPascalCase(entityLogicalName, entityDisplayName);
        var sb = new StringBuilder();

        sb.Append("// Requires a reference to Microsoft.Xrm.Sdk (e.g. the Microsoft.CrmSdk.CoreAssemblies NuGet package).").AppendLine();
        sb.AppendLine("[EntityLogicalName(EntityLogicalName)]");
        sb.Append("public class ").Append(className).AppendLine(" : Entity");
        sb.AppendLine("{");
        sb.Append("    public const string EntityLogicalName = \"").Append(entityLogicalName).AppendLine("\";");

        if (primaryIdAttribute != null)
        {
            sb.Append("    public const string PrimaryIdAttribute = \"").Append(primaryIdAttribute.LogicalName).AppendLine("\";");
        }

        sb.AppendLine();
        sb.Append("    public ").Append(className).AppendLine("() : base(EntityLogicalName) { }");

        if (primaryIdAttribute != null)
        {
            sb.AppendLine();
            sb.AppendLine("    public override Guid Id");
            sb.AppendLine("    {");
            sb.AppendLine("        get => base.Id;");
            sb.AppendLine("        set { base.Id = value; SetAttributeValue(PrimaryIdAttribute, value); }");
            sb.AppendLine("    }");
        }

        foreach (var attribute in selectedAttributes)
        {
            sb.AppendLine();
            AppendProperty(sb, attribute, optionSetEnumNames);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendProperty(StringBuilder sb, AttributeDefinition attribute, IReadOnlyDictionary<string, string> optionSetEnumNames)
    {
        var propertyName = ToPascalCase(attribute.LogicalName, attribute.DisplayName);

        var notes = new List<string>();
        if (attribute.IsPrimaryId) { notes.Add("Primary ID"); }
        if (attribute.IsPrimaryName) { notes.Add("Primary Name"); }
        if (notes.Count > 0)
        {
            sb.Append("    /// <summary>").Append(string.Join(" | ", notes)).AppendLine("</summary>");
        }

        var clrType = MapClrType(attribute.AttributeType);
        if (clrType == null)
        {
            sb.Append("    // unsupported: ").Append(attribute.AttributeType).Append(" (").Append(attribute.LogicalName).AppendLine(")");
            return;
        }

        sb.Append("    [AttributeLogicalName(\"").Append(attribute.LogicalName).AppendLine("\")]");

        if (clrType == "OptionSetValue")
        {
            var enumName = optionSetEnumNames.TryGetValue(attribute.LogicalName, out var name) ? name : null;
            if (enumName == null)
            {
                sb.Append("    public OptionSetValue ").AppendLine(propertyName);
                sb.AppendLine("    {");
                sb.Append("        get => GetAttributeValue<OptionSetValue>(\"").Append(attribute.LogicalName).AppendLine("\");");
                sb.Append("        set => SetAttributeValue(\"").Append(attribute.LogicalName).AppendLine("\", value);");
                sb.AppendLine("    }");
                return;
            }

            sb.Append("    public ").Append(enumName).Append("? ").AppendLine(propertyName);
            sb.AppendLine("    {");
            sb.AppendLine("        get");
            sb.AppendLine("        {");
            sb.Append("            var v = GetAttributeValue<OptionSetValue>(\"").Append(attribute.LogicalName).AppendLine("\");");
            sb.Append("            return v == null ? (").Append(enumName).Append("?)null : (").Append(enumName).AppendLine(")v.Value;");
            sb.AppendLine("        }");
            sb.Append("        set => SetAttributeValue(\"").Append(attribute.LogicalName).Append("\", value.HasValue ? new OptionSetValue((int)value.Value) : null);").AppendLine();
            sb.AppendLine("    }");
            return;
        }

        sb.Append("    public ").Append(clrType).Append(' ').AppendLine(propertyName);
        sb.AppendLine("    {");
        sb.Append("        get => GetAttributeValue<").Append(clrType).Append(">(\"").Append(attribute.LogicalName).AppendLine("\");");
        sb.Append("        set => SetAttributeValue(\"").Append(attribute.LogicalName).AppendLine("\", value);");
        sb.AppendLine("    }");
    }

    /// <summary>Returns null for attribute types not supported in early-bound generation.</summary>
    private static string? MapClrType(string attributeType) => attributeType switch
    {
        "String" or "Memo" or "EntityName" => "string",
        "Uniqueidentifier" => "Guid?",
        "Integer" => "int?",
        "BigInt" => "long?",
        "Double" => "double?",
        "Decimal" => "decimal?",
        "Money" => "Money",
        "Boolean" => "bool?",
        "DateTime" => "DateTime?",
        "Picklist" or "State" or "Status" => "OptionSetValue",
        "Lookup" or "Customer" or "Owner" => "EntityReference",
        _ => null,
    };
}
