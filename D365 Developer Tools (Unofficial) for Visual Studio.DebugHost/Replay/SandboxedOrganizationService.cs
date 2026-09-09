using System;
using System.Collections.Generic;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace D365DeveloperTools.DebugHost.Replay
{
    /// <summary>
    /// The IOrganizationService a replayed plugin actually talks to. Reads (Retrieve/RetrieveMultiple,
    /// and read-shaped generic Execute calls per WriteRequestClassifier) proxy live to the real
    /// environment via the wrapped ServiceClient; writes no-op and record rather than throw — a
    /// synthetic-but-plausible response (a new Guid for Create, an empty/discarded response otherwise)
    /// so the plugin's own execution continues past the write instead of aborting the very debugging
    /// session the developer is trying to observe (see the Plugin Debugging Phase 2 plan).
    /// </summary>
    internal sealed class SandboxedOrganizationService : IOrganizationService
    {
        private readonly ServiceClient _liveConnection;

        public List<SandboxedWriteRecord> SandboxedWrites { get; } = new List<SandboxedWriteRecord>();

        public SandboxedOrganizationService(ServiceClient liveConnection)
        {
            _liveConnection = liveConnection ?? throw new ArgumentNullException(nameof(liveConnection));
        }

        public Guid Create(Entity entity)
        {
            Record("Create", entity?.LogicalName);
            return Guid.NewGuid();
        }

        public void Update(Entity entity)
        {
            Record("Update", entity?.LogicalName);
        }

        public void Delete(string entityName, Guid id)
        {
            Record("Delete", entityName);
        }

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            Record("Associate", entityName);
        }

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            Record("Disassociate", entityName);
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            return _liveConnection.Retrieve(entityName, id, columnSet);
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            return _liveConnection.RetrieveMultiple(query);
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is ExecuteMultipleRequest executeMultiple)
            {
                return ExecuteMultipleInternal(executeMultiple);
            }

            var requestTypeName = request.GetType().Name;
            if (WriteRequestClassifier.IsWrite(requestTypeName))
            {
                Record(requestTypeName, request.RequestName);
                return new OrganizationResponse { ResponseName = request.RequestName };
            }

            return _liveConnection.Execute(request);
        }

        /// <summary>
        /// ExecuteMultipleRequest is a special case (per WriteRequestClassifier's own doc comment, it is
        /// never asked to classify this type directly) — recurse into each inner request through the same
        /// Execute dispatch above (which also correctly handles a nested ExecuteMultipleRequest, however
        /// unlikely, without duplicating the classify/dispatch logic here) so each is individually
        /// proxied live or sandboxed on its own merits.
        /// </summary>
        private ExecuteMultipleResponse ExecuteMultipleInternal(ExecuteMultipleRequest executeMultiple)
        {
            var settings = executeMultiple.Settings ?? new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true };
            var innerRequests = executeMultiple.Requests ?? new OrganizationRequestCollection();
            var responseItems = new ExecuteMultipleResponseItemCollection();
            var isFaulted = false;

            for (var i = 0; i < innerRequests.Count; i++)
            {
                var item = new ExecuteMultipleResponseItem { RequestIndex = i };
                var shouldStop = false;

                try
                {
                    var innerResponse = Execute(innerRequests[i]);
                    if (settings.ReturnResponses) { item.Response = innerResponse; }
                }
                catch (Exception ex)
                {
                    // Only a live-proxied read can actually throw here (sandboxed writes never throw) —
                    // still honor ContinueOnError/ReturnResponses faithfully either way.
                    isFaulted = true;
                    item.Fault = new OrganizationServiceFault { Message = ex.Message };
                    shouldStop = !settings.ContinueOnError;
                }

                responseItems.Add(item);
                if (shouldStop) { break; }
            }

            var response = new ExecuteMultipleResponse();
            response.Results["Responses"] = responseItems;
            response.Results["IsFaulted"] = isFaulted;
            return response;
        }

        private void Record(string operation, string entityOrRequestName)
        {
            SandboxedWrites.Add(new SandboxedWriteRecord
            {
                Operation = operation,
                EntityOrRequestName = entityOrRequestName,
                TimestampUtc = DateTime.UtcNow.ToString("o"),
            });
        }
    }
}
