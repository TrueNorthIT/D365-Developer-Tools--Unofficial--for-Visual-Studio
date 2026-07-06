namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared
{
    /// <summary>One row in a QuickPickDialog list — the WPF analog of a vscode.QuickPickItem.</summary>
    internal sealed class PickItem<T>
    {
        public string Label { get; set; }
        public string Description { get; set; }
        public string Detail { get; set; }
        public bool Checked { get; set; }
        public T Value { get; set; }

        public PickItem() { }

        public PickItem(string label, string description, T value, string detail = null, bool @checked = false)
        {
            Label = label;
            Description = description;
            Detail = detail;
            Checked = @checked;
            Value = value;
        }
    }
}
