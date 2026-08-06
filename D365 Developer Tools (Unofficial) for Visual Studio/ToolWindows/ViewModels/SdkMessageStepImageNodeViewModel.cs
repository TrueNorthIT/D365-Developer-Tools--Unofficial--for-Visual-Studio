using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels
{
    /// <summary>Leaf row under a step: a registered pre/post image.</summary>
    internal sealed class SdkMessageStepImageNodeViewModel
    {
        public SdkMessageStepImageDefinition Image { get; }
        public SdkMessageStepNodeViewModel Owner { get; set; }

        public string Name => Image.Name;
        public string EntityAlias => Image.EntityAlias;
        public string ImageType => Image.ImageType;
        public string Attributes => Image.Attributes;

        /// <summary>Unused; present only so the shared TreeViewItem.IsExpanded style binding doesn't produce binding errors on leaf rows.</summary>
        public bool IsExpanded { get; set; }

        public SdkMessageStepImageNodeViewModel(SdkMessageStepImageDefinition image)
        {
            Image = image;
        }
    }
}
