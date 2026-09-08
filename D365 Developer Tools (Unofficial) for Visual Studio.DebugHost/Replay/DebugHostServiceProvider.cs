using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace D365DeveloperTools.DebugHost.Replay
{
    /// <summary>Records every Trace call instead of writing to the platform's own trace log — surfaced back to the extension as ReplayResult.TraceMessages once the host process exits.</summary>
    internal sealed class RecordingTracingService : ITracingService
    {
        public List<string> Messages { get; } = new List<string>();

        public void Trace(string format, params object[] args)
        {
            try
            {
                Messages.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            }
            catch (FormatException)
            {
                // A plugin passing literal braces with no args is sloppy but real — don't let a bad
                // trace call crash the very replay session the developer is trying to observe.
                Messages.Add(format);
            }
        }
    }

    /// <summary>The real platform hands out a fresh IOrganizationService per requested user id — replay has only one sandboxed connection, so it's returned regardless of which user id is asked for (single-user, single-connection replay is in scope for v1 — see the Plugin Debugging Phase 2 plan).</summary>
    internal sealed class SandboxedOrganizationServiceFactory : IOrganizationServiceFactory
    {
        private readonly IOrganizationService _service;

        public SandboxedOrganizationServiceFactory(IOrganizationService service)
        {
            _service = service;
        }

        public IOrganizationService CreateOrganizationService(Guid? userId) => _service;
    }

    /// <summary>Posting to a real Service Bus/webhook endpoint during a debug replay should never actually happen — sandboxed the same way a write is: no-op rather than throw, so execution continues past it.</summary>
    internal sealed class NoOpServiceEndpointNotificationService : IServiceEndpointNotificationService
    {
        public string Execute(EntityReference serviceEndpoint, IExecutionContext context) => string.Empty;
    }

    /// <summary>
    /// Resolves the handful of services a plugin's IServiceProvider is expected to provide (see the
    /// project template's own Plugin1.cs and Microsoft.Xrm.Sdk.IExecutionContext's own service set) —
    /// anything else throws, matching the real platform's own behavior for an unsupported service type.
    /// </summary>
    internal sealed class DebugHostServiceProvider : IServiceProvider
    {
        private readonly RemoteExecutionContext _context;
        private readonly SandboxedOrganizationService _organizationService;
        private readonly RecordingTracingService _tracingService;

        public DebugHostServiceProvider(RemoteExecutionContext context, SandboxedOrganizationService organizationService, RecordingTracingService tracingService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
            _tracingService = tracingService ?? throw new ArgumentNullException(nameof(tracingService));
        }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(ITracingService)) { return _tracingService; }
            if (serviceType == typeof(IPluginExecutionContext) || serviceType == typeof(IExecutionContext)) { return _context; }
            if (serviceType == typeof(IOrganizationServiceFactory)) { return new SandboxedOrganizationServiceFactory(_organizationService); }
            if (serviceType == typeof(IServiceEndpointNotificationService)) { return new NoOpServiceEndpointNotificationService(); }

            throw new InvalidOperationException($"No replay-time implementation is registered for service type '{serviceType.FullName}'.");
        }
    }
}
