using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels
{
    internal sealed class AttributeNodeViewModel
    {
        public AttributeDefinition Attribute { get; }
        public EntityNodeViewModel Owner { get; set; }

        public string LogicalName => Attribute.LogicalName;
        public string DisplayName => Attribute.DisplayName;
        public string AttributeType => Attribute.AttributeType;
        public bool IsPrimaryId => Attribute.IsPrimaryId;
        public bool IsPrimaryName => Attribute.IsPrimaryName;
        public bool IsOptionSet => OptionSetCasts.OptionSetTypes.Contains(Attribute.AttributeType);

        public ImageMoniker Icon =>
            IsPrimaryId ? KnownMonikers.Key :
            IsOptionSet ? KnownMonikers.EnumerationPublic :
            KnownMonikers.FieldPublic;

        /// <summary>Unused; present only so the TreeViewItem.IsExpanded style binding (shared with EntityNodeViewModel) doesn't produce binding errors on leaf rows.</summary>
        public bool IsExpanded { get; set; }

        public AttributeNodeViewModel(AttributeDefinition attribute)
        {
            Attribute = attribute;
        }
    }
}
