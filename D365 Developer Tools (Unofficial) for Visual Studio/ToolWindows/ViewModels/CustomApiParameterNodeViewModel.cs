using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels
{
    /// <summary>Leaf row under a Custom API: either a request parameter or a response property, distinguished by Kind.</summary>
    internal sealed class CustomApiParameterNodeViewModel
    {
        public CustomApiParameterDefinition Parameter { get; }
        public CustomApiNodeViewModel Owner { get; set; }

        public string Name => Parameter.Name;
        public string Type => Parameter.Type;
        public string Kind => Parameter.IsRequestParameter ? "Request" : "Response";
        public bool IsRequestParameter => Parameter.IsRequestParameter;
        public bool IsOptional => Parameter.IsOptional;

        /// <summary>Unused; present only so the shared TreeViewItem.IsExpanded style binding doesn't produce binding errors on leaf rows.</summary>
        public bool IsExpanded { get; set; }

        public CustomApiParameterNodeViewModel(CustomApiParameterDefinition parameter)
        {
            Parameter = parameter;
        }
    }
}
