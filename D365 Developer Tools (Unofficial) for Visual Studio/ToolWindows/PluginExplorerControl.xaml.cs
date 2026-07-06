using System.Windows.Controls;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows
{
    public partial class PluginExplorerControl : UserControl
    {
        internal PluginExplorerControl(PluginExplorerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
