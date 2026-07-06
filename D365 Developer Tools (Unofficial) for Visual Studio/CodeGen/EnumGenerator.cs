using System.Collections.Generic;
using System.Text;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.CodeGen
{
    /// <summary>
    /// Ports interfaceGenerator.ts's generateEnum to a real (non-flags) public enum. Generation is
    /// one-entity-at-a-time (mirroring the "Generate C# Class"/"Generate Enum" context-menu UX), so
    /// cross-entity enum-name collisions are a known, accepted v1 limitation rather than something
    /// this handles.
    /// </summary>
    internal static class EnumGenerator
    {
        public static string GetEnumName(string attributeLogicalName, string attributeDisplayName) =>
            NameUtilities.ToPascalCase(string.IsNullOrEmpty(attributeDisplayName) ? attributeLogicalName : attributeDisplayName);

        public static string GenerateEnum(string attributeLogicalName, string attributeDisplayName, IReadOnlyList<OptionValue> options)
        {
            var enumName = GetEnumName(attributeLogicalName, attributeDisplayName);
            var sb = new StringBuilder();

            sb.Append("public enum ").Append(enumName).AppendLine();
            sb.AppendLine("{");
            foreach (var option in options)
            {
                sb.Append("    ").Append(NameUtilities.ToEnumKey(option.Label)).Append(" = ").Append(option.Value).AppendLine(",");
            }
            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
