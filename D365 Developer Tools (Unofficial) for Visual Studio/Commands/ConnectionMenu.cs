using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// Shared "click for connection options" menu — ports statusBar.ts's showD365Menu. Reused by both
    /// the Entity Explorer tool window header and the "D365: Connect / Manage Connection…" command.
    ///
    /// Picked actions are awaited in-line (not fire-and-forget): this is invoked from a
    /// VisualStudio.Extensibility Command's ExecuteCommandAsync, whose execution scope the command
    /// host may tear down as soon as that Task completes — a detached fire-and-forget continuation
    /// started here was being silently abandoned before showing its own follow-up UI (e.g. the
    /// environment-URL prompt after picking "Connect to Environment…").
    /// </summary>
    internal static class ConnectionMenu
    {
        public static async Task ShowAsync(ConnectionManager connectionManager, IUserPrompts prompts)
        {
            if (connectionManager.IsRestoring)
            {
                prompts.ShowInfo("D365: Still restoring previous connection…");
                return;
            }

            var conn = connectionManager.Connection;
            var items = new List<PickItem<Func<Task>>>();

            if (conn != null)
            {
                items.Add(new PickItem<Func<Task>>("Disconnect", conn.EnvironmentUrl, () =>
                {
                    connectionManager.Disconnect();
                    return Task.CompletedTask;
                }));

                if (conn.AuthMode == AuthMode.User)
                {
                    items.Add(new PickItem<Func<Task>>(
                        "Switch Account…", "Sign in with a different Microsoft account",
                        () => connectionManager.SwitchAccountAsync()));
                }

                items.Add(new PickItem<Func<Task>>(
                    "Connect to Different Environment…", null,
                    () => connectionManager.ConnectAsync()));
            }
            else
            {
                items.Add(new PickItem<Func<Task>>(
                    "Connect to Environment…", null,
                    () => connectionManager.ConnectAsync()));
            }

            bool IsCurrent(StoredConnection r) =>
                conn != null && conn.EnvironmentUrl == r.EnvironmentUrl && conn.AuthMode == r.AuthMode && conn.ClientId == r.ClientId;

            foreach (var recent in connectionManager.GetRecentEnvironments().Where(r => !IsCurrent(r)))
            {
                var captured = recent;
                items.Add(new PickItem<Func<Task>>(
                    FriendlyName(captured.EnvironmentUrl),
                    captured.EnvironmentUrl,
                    () => connectionManager.ConnectToStoredAsync(captured),
                    detail: captured.AuthMode == AuthMode.User ? "User account" : $"Client credentials · {captured.ClientId}"));
            }

            var pick = await prompts.PickOneAsync(
                "D365 Developer Tools",
                conn != null ? $"Connected to {conn.EnvironmentUrl}" : "Not connected to a D365 environment",
                items).ConfigureAwait(true);

            if (pick?.Value != null)
            {
                await pick.Value().ConfigureAwait(true);
            }
        }

        public static string FriendlyName(string environmentUrl)
        {
            try { return new Uri(environmentUrl).Host.Split('.')[0]; }
            catch { return environmentUrl; }
        }
    }
}
