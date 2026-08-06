using System.Collections.Generic;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse
{
    internal static class OptionSetCasts
    {
        public static readonly HashSet<string> OptionSetTypes = new HashSet<string> { "Picklist", "State", "Status" };

        private static readonly Dictionary<string, string> Casts = new Dictionary<string, string>
        {
            ["Picklist"] = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata",
            ["State"] = "Microsoft.Dynamics.CRM.StateAttributeMetadata",
            ["Status"] = "Microsoft.Dynamics.CRM.StatusAttributeMetadata",
        };

        public static string GetCast(string attributeType) =>
            Casts.TryGetValue(attributeType, out var cast) ? cast : null;
    }
}
