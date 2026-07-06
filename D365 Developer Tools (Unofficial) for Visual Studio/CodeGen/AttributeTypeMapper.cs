namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.CodeGen
{
    internal enum ClrTypeKind
    {
        String,
        Guid,
        Int,
        Long,
        Double,
        Decimal,
        Money,
        Bool,
        DateTime,
        OptionSetEnum,
        EntityReference,
        Unsupported,
    }

    /// <summary>
    /// Maps a Dataverse AttributeType to the early-bound CLR/SDK property type used by
    /// EarlyBoundClassGenerator. Direct analog of interfaceGenerator.ts's mapType, but targeting real
    /// Microsoft.Xrm.Sdk types instead of TypeScript structural types.
    ///
    /// Deliberately does NOT emit the Web-API-JSON-only "_x_value" / formatted-value annotation shape
    /// that interfaceGenerator.ts produces for lookups — early-bound SDK classes store an actual
    /// EntityReference in the attribute bag, so that's the correct property type here instead.
    /// </summary>
    internal static class AttributeTypeMapper
    {
        public static ClrTypeKind GetKind(string attributeType)
        {
            switch (attributeType)
            {
                case "String":
                case "Memo":
                case "EntityName":
                    return ClrTypeKind.String;
                case "Uniqueidentifier":
                    return ClrTypeKind.Guid;
                case "Integer":
                    return ClrTypeKind.Int;
                case "BigInt":
                    return ClrTypeKind.Long;
                case "Double":
                    return ClrTypeKind.Double;
                case "Decimal":
                    return ClrTypeKind.Decimal;
                case "Money":
                    return ClrTypeKind.Money;
                case "Boolean":
                    return ClrTypeKind.Bool;
                case "DateTime":
                    return ClrTypeKind.DateTime;
                case "Picklist":
                case "State":
                case "Status":
                    return ClrTypeKind.OptionSetEnum;
                case "Lookup":
                case "Customer":
                case "Owner":
                    return ClrTypeKind.EntityReference;
                default:
                    return ClrTypeKind.Unsupported;
            }
        }

        /// <summary>The CLR type name for a property, excluding option-set enums (caller supplies the enum name).</summary>
        public static string GetClrTypeName(ClrTypeKind kind)
        {
            switch (kind)
            {
                case ClrTypeKind.String: return "string";
                case ClrTypeKind.Guid: return "Guid?";
                case ClrTypeKind.Int: return "int?";
                case ClrTypeKind.Long: return "long?";
                case ClrTypeKind.Double: return "double?";
                case ClrTypeKind.Decimal: return "decimal?";
                case ClrTypeKind.Money: return "Money";
                case ClrTypeKind.Bool: return "bool?";
                case ClrTypeKind.DateTime: return "DateTime?";
                case ClrTypeKind.EntityReference: return "EntityReference";
                default: return "object";
            }
        }
    }
}
