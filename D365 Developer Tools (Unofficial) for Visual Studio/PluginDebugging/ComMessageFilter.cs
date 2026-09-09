using System;
using System.Runtime.InteropServices;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging
{
    [ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOleMessageFilter
    {
        [PreserveSig]
        int HandleInComingCall(int callType, IntPtr taskCaller, int tickCount, IntPtr interfaceInfo);

        [PreserveSig]
        int RetryRejectedCall(IntPtr taskCallee, int tickCount, int rejectType);

        [PreserveSig]
        int MessageFilter(IntPtr taskCallee, int tickCount, int pendingType);
    }

    /// <summary>
    /// Registers a COM IOleMessageFilter for the duration of the debugger-attach sequence, so a
    /// DTE/Debugger automation call made while Visual Studio's main thread is momentarily busy retries
    /// (SERVERCALL_RETRYLATER) instead of throwing RPC_E_CALL_REJECTED — a real, known risk for exactly
    /// this kind of external, timing-sensitive automation call (see the Plugin Debugging Phase 2 plan).
    /// No precedent exists elsewhere in this codebase for this specific COM interaction; this follows the
    /// standard IOleMessageFilter pattern (Microsoft KB 293215) other VS extensions use for the same kind
    /// of Process2.Attach2 automation.
    /// </summary>
    internal sealed class ComMessageFilter : IOleMessageFilter, IDisposable
    {
        private const int ServerCallIsHandled = 0;
        private const int ServerCallRetryLater = 2;
        private const int PendingMsgWaitDefProcess = 2;

        private readonly IOleMessageFilter _previousFilter;
        private bool _disposed;

        public ComMessageFilter()
        {
            CoRegisterMessageFilter(this, out _previousFilter);
        }

        public int HandleInComingCall(int callType, IntPtr taskCaller, int tickCount, IntPtr interfaceInfo) => ServerCallIsHandled;

        public int RetryRejectedCall(IntPtr taskCallee, int tickCount, int rejectType) =>
            rejectType == ServerCallRetryLater ? 500 : -1; // ask COM to retry shortly rather than fail the call outright

        public int MessageFilter(IntPtr taskCallee, int tickCount, int pendingType) => PendingMsgWaitDefProcess;

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            CoRegisterMessageFilter(_previousFilter, out _);
        }

        [DllImport("Ole32.dll")]
        private static extern int CoRegisterMessageFilter(IOleMessageFilter newFilter, out IOleMessageFilter oldFilter);
    }
}
