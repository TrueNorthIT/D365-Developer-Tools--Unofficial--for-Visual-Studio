using System.Windows.Controls;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows
{
    public partial class PluginDebuggingControl : UserControl
    {
        internal PluginDebuggingViewModel ViewModel => (PluginDebuggingViewModel)DataContext;

        internal PluginDebuggingControl(PluginDebuggingViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
