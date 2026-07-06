using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.CodeGen;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Completion
{
    /// <summary>
    /// Ports registerInsertInterfaceCommand from d365CodeActionProvider.ts: prompts for an entity
    /// (and optionally which fields), fetches option sets as needed, and inserts the generated
    /// early-bound C# class (plus any enums) at the position the "d365" completion word occupied.
    /// </summary>
    internal static class D365CompletionInsertionService
    {
        public static async Task GenerateAndInsertAsync(ITextBuffer buffer, ITrackingSpan applicableSpan, bool selectFields)
        {
            var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
            var connectionManager = package.ConnectionManager;
            var client = package.DataverseClient;
            var prompts = package.UserPrompts;

            if (!connectionManager.IsConnected)
            {
                prompts.ShowError("D365: Connect to an environment before generating a class.");
                return;
            }

            // Clear the "d365" word immediately, matching the VS Code completion path's instant
            // clear-then-fill-in-later behavior, and keep a tracking point so later edits (from the
            // dialogs the user interacts with in the meantime) don't invalidate the insert position.
            var clearSpan = applicableSpan.GetSpan(buffer.CurrentSnapshot);
            ITrackingPoint insertPoint;
            using (var edit = buffer.CreateEdit())
            {
                edit.Replace(clearSpan, string.Empty);
                var snapshot = edit.Apply();
                insertPoint = snapshot.CreateTrackingPoint(clearSpan.Start.Position, PointTrackingMode.Positive);
            }

            List<EntityDefinition> entities;
            try
            {
                entities = await prompts.RunWithProgressAsync("D365: Loading entities…", () => client.GetEntitiesAsync()).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to load entities: {ex.Message}");
                return;
            }

            var entityItems = entities
                .Select(e => new PickItem<EntityDefinition>(e.DisplayName, e.LogicalName, e, detail: e.IsCustom ? "Custom entity" : null))
                .ToList();
            var entityPick = await prompts.PickOneAsync("D365: Select entity", "Type to filter…", entityItems).ConfigureAwait(true);
            if (entityPick == null) { return; }

            var entity = entityPick.Value;

            List<AttributeDefinition> allAttributes;
            try
            {
                allAttributes = await prompts.RunWithProgressAsync(
                    $"D365: Loading '{entity.LogicalName}'…",
                    () => client.GetAttributesAsync(entity.LogicalName)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Entity '{entity.LogicalName}' failed to load: {ex.Message}");
                return;
            }

            var selectedAttributes = allAttributes;
            if (selectFields)
            {
                var attributeItems = allAttributes
                    .Select(a => new PickItem<AttributeDefinition>(
                        a.DisplayName,
                        a.LogicalName,
                        a,
                        detail: string.Join("  ·  ", new[] { a.AttributeType, a.IsPrimaryId ? "Primary ID" : null, a.IsPrimaryName ? "Primary Name" : null }.Where(s => s != null)),
                        @checked: a.IsPrimaryId || a.IsPrimaryName))
                    .ToList();

                var picked = await prompts.PickManyAsync($"D365: Select fields — {entity.LogicalName}", "Choose fields to include…", attributeItems).ConfigureAwait(true);
                if (picked == null || picked.Count == 0) { return; }
                selectedAttributes = picked.Select(p => p.Value).ToList();
            }

            var optionSetAttrs = selectedAttributes.Where(a => OptionSetCasts.OptionSetTypes.Contains(a.AttributeType)).ToList();
            var enumBlocks = new List<string>();
            var enumNames = new Dictionary<string, string>();

            if (optionSetAttrs.Count > 0)
            {
                try
                {
                    await prompts.RunWithProgressAsync("D365: Loading option sets…", async () =>
                    {
                        foreach (var attr in optionSetAttrs)
                        {
                            var options = await client.GetAttributeOptionsAsync(entity.LogicalName, attr.LogicalName, attr.AttributeType).ConfigureAwait(true);
                            enumNames[attr.LogicalName] = EnumGenerator.GetEnumName(attr.LogicalName, attr.DisplayName);
                            enumBlocks.Add(EnumGenerator.GenerateEnum(attr.LogicalName, attr.DisplayName, options));
                        }
                    }).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    prompts.ShowError($"D365: Failed to load option sets: {ex.Message}");
                    return;
                }
            }

            var primaryId = allAttributes.FirstOrDefault(a => a.IsPrimaryId);
            var sb = new StringBuilder();
            foreach (var enumBlock in enumBlocks)
            {
                sb.AppendLine(enumBlock.TrimEnd());
                sb.AppendLine();
            }
            sb.Append(EarlyBoundClassGenerator.Generate(entity.LogicalName, entity.DisplayName, selectedAttributes, primaryId, enumNames));

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var currentSnapshot = buffer.CurrentSnapshot;
            var position = insertPoint.GetPosition(currentSnapshot);
            buffer.Insert(position, sb.ToString());
        }
    }
}
