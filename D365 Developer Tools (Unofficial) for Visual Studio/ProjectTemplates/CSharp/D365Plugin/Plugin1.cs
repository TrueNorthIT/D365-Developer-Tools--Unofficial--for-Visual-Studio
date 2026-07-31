using System;
using Microsoft.Xrm.Sdk;

namespace $safeprojectname$
{
    public sealed class Plugin1 : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            tracingService.Trace($"Plugin1 executing for message '{context.MessageName}' on entity '{context.PrimaryEntityName}'.");
        }
    }
}
