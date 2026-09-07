using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Mcp;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio
{
    /// <summary>
    /// Package entry point — the analog of extension.ts's activate(). Wires up the connection manager,
    /// Dataverse client, and tool window, then kicks off a silent connection restore (mirroring
    /// tryRestoreConnection() being called from activate()).
    ///
    /// Commands are contributed separately via the VisualStudio.Extensibility model (see Commands\),
    /// not classic VSCT — this package exposes its services through <see cref="Instance"/> so those
    /// commands can reach them.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuids.D365DeveloperToolsPackageString)]
    [ProvideToolWindow(typeof(EntityExplorerToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids80.SolutionExplorer)]
    [ProvideToolWindow(typeof(PluginExplorerToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids80.SolutionExplorer)]
    // Float (not Tabbed alongside Solution Explorer, unlike the other two tool windows) — a trace log
    // grid benefits from its own detached window rather than competing for sidebar space. Only sets the
    // *first-ever* placement; VS remembers wherever the user docks/moves/resizes it after that.
    [ProvideToolWindow(typeof(PluginDebuggingToolWindow), Style = VsDockStyle.Float, Width = 700, Height = 500)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class D365DeveloperToolsPackage : AsyncPackage
    {
        private SolutionContext _solutionContext;
        private readonly TaskCompletionSource<bool> _initialized = new TaskCompletionSource<bool>();

        /// <summary>
        /// Set synchronously in the constructor (before InitializeAsync's async body runs), so
        /// commands can detect "package exists but isn't ready yet" versus "package hasn't loaded
        /// at all" and force a load in the latter case.
        /// </summary>
        internal static D365DeveloperToolsPackage Instance { get; private set; }

        internal ConnectionManager ConnectionManager { get; private set; }
        internal DataverseClient DataverseClient { get; private set; }
        internal IUserPrompts UserPrompts { get; private set; }

        /// <summary>
        /// A singleton (unlike PluginExplorerViewModel, which PluginExplorerToolWindow builds fresh
        /// per Initialize()) — ViewStepTraceLogsCommand needs to push a step filter into this ViewModel
        /// whether or not the Plugin Debugging tool window is already open.
        /// </summary>
        internal PluginDebuggingViewModel PluginDebuggingViewModel { get; private set; }

        private McpBridge _mcpBridge;

        public D365DeveloperToolsPackage()
        {
            Instance = this;
        }

        /// <summary>
        /// Ensures the package is loaded and fully initialized, forcing a load via the async-safe
        /// IVsShell7.LoadPackageAsync if a VisualStudio.Extensibility command runs before this
        /// package's own [ProvideAutoLoad] background load has happened. Commands must await this
        /// before touching ConnectionManager/DataverseClient/UserPrompts.
        ///
        /// Deliberately avoids the legacy ServiceProvider.GlobalProvider + IVsShell.LoadPackage
        /// combination — that pairing can throw "Cannot access a closed registry key" when invoked
        /// from a VisualStudio.Extensibility command's execution context.
        /// </summary>
        internal static async Task<D365DeveloperToolsPackage> GetReadyInstanceAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // AsyncServiceProvider.GetServiceAsync and IVsShell7.LoadPackageAsync both return the
            // COM-flavored IVsTask (not System.Threading.Tasks.Task), so no ConfigureAwait here.
            if (Instance == null &&
                await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsShell)) is IVsShell7 shell)
            {
                await shell.LoadPackageAsync(new Guid(PackageGuids.D365DeveloperToolsPackageString));
            }

            if (Instance == null)
            {
                throw new InvalidOperationException("D365 Developer Tools package failed to load.");
            }

            await Instance._initialized.Task.ConfigureAwait(true);
            return Instance;
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress).ConfigureAwait(true);
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            try
            {
                ActivityLog.LogInformation("D365DeveloperTools", "InitializeAsync: starting");

                var solutionService = await GetServiceAsync(typeof(SVsSolution)).ConfigureAwait(true) as IVsSolution;
                _solutionContext = new SolutionContext(solutionService);
                _solutionContext.SolutionOpened += (_, __) =>
                    ConnectionManager.TryRestoreConnectionAsync().FileAndForget("D365DeveloperTools/RestoreConnectionOnSolutionOpen");

                UserPrompts = new WpfUserPrompts();
                ConnectionManager = new ConnectionManager(UserPrompts, _solutionContext);
                DataverseClient = new DataverseClient(ConnectionManager);
                PluginDebuggingViewModel = new PluginDebuggingViewModel(ConnectionManager, DataverseClient, UserPrompts);

                _mcpBridge = new McpBridge(ConnectionManager, UserPrompts);
                ConnectionManager.ConnectionChanged += (_, connection) =>
                {
                    if (connection != null) { _mcpBridge.Start(); }
                    else { _mcpBridge.Stop(); }
                };

                // Covers the case where the package finishes loading after a solution is already open.
                ConnectionManager.TryRestoreConnectionAsync().FileAndForget("D365DeveloperTools/RestoreConnectionOnActivate");

                if (await GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) is OleMenuCommandService commandService)
                {
                    var publishCommandId = new CommandID(PackageGuids.ProjectContextMenuCmdSet, PkgCmdIDList.cmdidPublishToDataverse);
                    commandService.AddCommand(new OleMenuCommand(OnPublishToDataverse, publishCommandId));

                    var changeDeploymentModelCommandId = new CommandID(PackageGuids.ProjectContextMenuCmdSet, PkgCmdIDList.cmdidChangeDeploymentModel);
                    commandService.AddCommand(new OleMenuCommand(OnChangeDeploymentModel, changeDeploymentModelCommandId));

                    var addStepCommandId = new CommandID(PackageGuids.ProjectContextMenuCmdSet, PkgCmdIDList.cmdidAddStepToPlugin);
                    var addStepCommand = new OleMenuCommand(OnAddStepToPlugin, addStepCommandId);
                    addStepCommand.BeforeQueryStatus += OnAddStepToPluginBeforeQueryStatus;
                    commandService.AddCommand(addStepCommand);
                }

                ActivityLog.LogInformation("D365DeveloperTools", "InitializeAsync: completed successfully");
                _initialized.TrySetResult(true);
            }
            catch (Exception ex)
            {
                ActivityLog.LogError("D365DeveloperTools", "InitializeAsync FAILED: " + ex);
                _initialized.TrySetException(ex);
                throw;
            }
        }

        /// <summary>Public wrapper around the protected Package.GetService, for ViewModels that need EnvDTE (e.g. Plugin Explorer's "Publish project..." button).</summary>
        internal EnvDTE.DTE GetDte()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        }

        /// <summary>Public wrapper around the protected AsyncPackage.ShowToolWindowAsync, for the Extensibility-model commands.</summary>
        internal async Task ShowEntityExplorerToolWindowAsync()
        {
            var window = await ShowToolWindowAsync(typeof(EntityExplorerToolWindow), 0, create: true, cancellationToken: DisposalToken).ConfigureAwait(true);
            if (window?.Frame == null)
            {
                throw new NotSupportedException("Cannot create the D365 Entity Explorer tool window.");
            }
        }

        /// <summary>Public wrapper around the protected AsyncPackage.ShowToolWindowAsync, for the Extensibility-model commands.</summary>
        internal async Task ShowPluginExplorerToolWindowAsync()
        {
            var window = await ShowToolWindowAsync(typeof(PluginExplorerToolWindow), 0, create: true, cancellationToken: DisposalToken).ConfigureAwait(true);
            if (window?.Frame == null)
            {
                throw new NotSupportedException("Cannot create the D365 Plugin Explorer tool window.");
            }
        }

        /// <summary>Public wrapper around the protected AsyncPackage.ShowToolWindowAsync, for the Extensibility-model commands and ViewStepTraceLogsCommand.</summary>
        /// <summary>
        /// Set once the first time this successfully floats the Plugin Debugging window (see below) —
        /// guards against re-forcing it to float on every later Show, once the user has redocked it themselves.
        /// </summary>
        private bool _pluginDebuggingWindowPositioned;

        internal async Task ShowPluginDebuggingToolWindowAsync()
        {
            var window = await ShowToolWindowAsync(typeof(PluginDebuggingToolWindow), 0, create: true, cancellationToken: DisposalToken).ConfigureAwait(true);
            if (window?.Frame == null)
            {
                throw new NotSupportedException("Cannot create the D365 Plugin Debugging tool window.");
            }

            // [ProvideToolWindow(Style = VsDockStyle.Float)] on this window only reliably applies when
            // it's shown from a "neutral" context (e.g. the Tools menu) — showing it for the first time
            // from inside Plugin Explorer's own step context menu (ViewStepTraceLogsCommand) instead
            // docks it tabbed alongside whatever tool window is currently active, ignoring the
            // attribute. Forcing it to float explicitly here, once, makes the first-ever placement
            // consistent regardless of which entry point creates it first.
            if (!_pluginDebuggingWindowPositioned && window.Frame is IVsWindowFrame frame)
            {
                _pluginDebuggingWindowPositioned = true;
                var relativeId = Guid.Empty;
                frame.SetFramePos(VSSETFRAMEPOS.SFP_fFloat, ref relativeId, 0, 0, 700, 500);
            }
        }

        /// <summary>Handles the "D365: Publish to Dataverse..." Solution Explorer project context menu command.</summary>
        private void OnPublishToDataverse(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(GetService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)) { return; }

            var selectedItems = dte.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0) { return; }

            var project = selectedItems.Item(1).Project;
            if (project == null) { return; }

            Commands.PublishToDataverseCommand.ExecuteAsync(dte, project)
                .FileAndForget("D365DeveloperTools/PublishToDataverse");
        }

        /// <summary>Handles the "D365: Change Deployment Model..." Solution Explorer project context menu command.</summary>
        private void OnChangeDeploymentModel(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(GetService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)) { return; }

            var selectedItems = dte.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0) { return; }

            var project = selectedItems.Item(1).Project;
            if (project == null) { return; }

            Commands.ChangeDeploymentModelCommand.ExecuteAsync(dte, project)
                .FileAndForget("D365DeveloperTools/ChangeDeploymentModel");
        }

        /// <summary>Only shows "D365: Add Step..." for a single selected .cs file whose text looks like it declares an IPlugin implementation.</summary>
        private void OnAddStepToPluginBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var command = (OleMenuCommand)sender;
            command.Visible = false;
            command.Enabled = false;

            var filePath = TryGetSelectedCSharpFilePath();
            if (filePath == null) { return; }

            if (!PluginPublishing.PluginTypeNameExtractor.FileMightContainPlugin(filePath)) { return; }

            command.Visible = true;
            command.Enabled = true;
        }

        /// <summary>Handles the "D365: Add Step..." .cs file context menu command.</summary>
        private void OnAddStepToPlugin(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var filePath = TryGetSelectedCSharpFilePath();
            if (filePath == null) { return; }

            Commands.AddStepToPluginCommand.ExecuteAsync(filePath)
                .FileAndForget("D365DeveloperTools/AddStepToPlugin");
        }

        private string TryGetSelectedCSharpFilePath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(GetService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)) { return null; }

            var selectedItems = dte.SelectedItems;
            if (selectedItems == null || selectedItems.Count != 1) { return null; }

            var projectItem = selectedItems.Item(1).ProjectItem;
            if (projectItem == null || projectItem.FileCount == 0) { return null; }

            string filePath;
            try
            {
                filePath = projectItem.FileNames[1];
            }
            catch
            {
                return null;
            }

            return !string.IsNullOrEmpty(filePath) && string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase)
                ? filePath
                : null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _mcpBridge?.Dispose();
                PluginDebuggingViewModel?.StopAutoRefresh();
                ConnectionManager?.Dispose();
                if (_solutionContext != null && ThreadHelper.CheckAccess())
                {
                    _solutionContext.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
