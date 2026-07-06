using System.Collections.Generic;
using System.Linq;
using System.Text;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.CodeGen
{
    /// <summary>
    /// Generates a CrmSvcUtil-style, early-bound Microsoft.Xrm.Sdk.Entity subclass for a Dataverse
    /// entity. This is the C#/early-bound counterpart of interfaceGenerator.ts's generateInterface.
    /// </summary>
    internal static class EarlyBoundClassGenerator
    {
        /// <summary>Full, ready-to-open document: usings, any option-set enums, then the entity class.</summary>
        public static string GenerateFile(
            string entityLogicalName,
            string entityDisplayName,
            IReadOnlyList<AttributeDefinition> selectedAttributes,
            AttributeDefinition primaryIdAttribute,
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

            sb.Append(Generate(entityLogicalName, entityDisplayName, selectedAttributes, primaryIdAttribute, optionSetEnumNames));
            return sb.ToString();
        }

        public static string Generate(
            string entityLogicalName,
            string entityDisplayName,
            IReadOnlyList<AttributeDefinition> selectedAttributes,
            AttributeDefinition primaryIdAttribute,
            IReadOnlyDictionary<string, string> optionSetEnumNames)
        {
            var className = NameUtilities.ToPascalCase(entityLogicalName);
            var sb = new StringBuilder();

            sb.Append("// ").Append(entityDisplayName).Append(" (").Append(entityLogicalName).AppendLine(")");
            sb.AppendLine("// Requires a reference to Microsoft.Xrm.Sdk (e.g. the Microsoft.CrmSdk.CoreAssemblies NuGet package).");
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
            var kind = AttributeTypeMapper.GetKind(attribute.AttributeType);
            var propertyName = NameUtilities.ToPascalCase(attribute.LogicalName);

            var notes = new List<string>();
            if (attribute.IsPrimaryId) { notes.Add("Primary ID"); }
            if (attribute.IsPrimaryName) { notes.Add("Primary Name"); }
            if (notes.Count > 0)
            {
                sb.Append("    /// <summary>").Append(string.Join(" | ", notes)).AppendLine("</summary>");
            }

            if (kind == ClrTypeKind.Unsupported)
            {
                sb.Append("    // unsupported in v1: ").Append(attribute.AttributeType).Append(" (").Append(attribute.LogicalName).AppendLine(")");
                return;
            }

            sb.Append("    [AttributeLogicalName(\"").Append(attribute.LogicalName).AppendLine("\")]");

            if (kind == ClrTypeKind.OptionSetEnum)
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

            var clrType = AttributeTypeMapper.GetClrTypeName(kind);
            sb.Append("    public ").Append(clrType).Append(" ").AppendLine(propertyName);
            sb.AppendLine("    {");
            sb.Append("        get => GetAttributeValue<").Append(clrType).Append(">(\"").Append(attribute.LogicalName).AppendLine("\");");
            sb.Append("        set => SetAttributeValue(\"").Append(attribute.LogicalName).AppendLine("\", value);");
            sb.AppendLine("    }");
        }
    }
}
