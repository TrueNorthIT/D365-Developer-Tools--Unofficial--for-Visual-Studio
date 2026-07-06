using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows;
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

                // Covers the case where the package finishes loading after a solution is already open.
                ConnectionManager.TryRestoreConnectionAsync().FileAndForget("D365DeveloperTools/RestoreConnectionOnActivate");

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

        /// <summary>Public wrapper around the protected AsyncPackage.ShowToolWindowAsync, for the Extensibility-model commands.</summary>
        internal async Task ShowEntityExplorerToolWindowAsync()
        {
            var window = await ShowToolWindowAsync(typeof(EntityExplorerToolWindow), 0, create: true, cancellationToken: DisposalToken).ConfigureAwait(true);
            if (window?.Frame == null)
            {
                throw new NotSupportedException("Cannot create the D365 Entity Explorer tool window.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
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
