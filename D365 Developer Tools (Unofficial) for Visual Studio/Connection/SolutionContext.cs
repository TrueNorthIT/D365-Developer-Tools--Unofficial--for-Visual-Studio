using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection
{
    /// <summary>
    /// Tracks the currently loaded solution's path, which stands in for VS Code's "workspace" as the
    /// scope for a persisted connection. When no solution is loaded, connections are session-only.
    /// </summary>
    internal sealed class SolutionContext : IVsSolutionEvents, IDisposable
    {
        private readonly IVsSolution _solution;
        private uint _cookie;

        public string CurrentSolutionPath { get; private set; }

        public event EventHandler SolutionOpened;
        public event EventHandler SolutionClosing;

        public SolutionContext(IVsSolution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
            ThreadHelper.ThrowIfNotOnUIThread();

            _solution.AdviseSolutionEvents(this, out _cookie);
            CurrentSolutionPath = TryGetSolutionPath();
        }

        private string TryGetSolutionPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_solution.GetSolutionInfo(out _, out var solutionFile, out _) == VSConstants.S_OK &&
                !string.IsNullOrEmpty(solutionFile))
            {
                return solutionFile;
            }
            return null;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            CurrentSolutionPath = TryGetSolutionPath();
            SolutionOpened?.Invoke(this, EventArgs.Empty);
            return VSConstants.S_OK;
        }

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            SolutionClosing?.Invoke(this, EventArgs.Empty);
            return VSConstants.S_OK;
        }

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            CurrentSolutionPath = null;
            return VSConstants.S_OK;
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_cookie != 0)
            {
                _solution.UnadviseSolutionEvents(_cookie);
                _cookie = 0;
            }
        }

        // ── Unused IVsSolutionEvents members ─────────────────────────────────
        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) => VSConstants.S_OK;
        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded) => VSConstants.S_OK;
        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) => VSConstants.S_OK;
        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) => VSConstants.S_OK;
        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) => VSConstants.S_OK;
        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;
        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;
    }
}
