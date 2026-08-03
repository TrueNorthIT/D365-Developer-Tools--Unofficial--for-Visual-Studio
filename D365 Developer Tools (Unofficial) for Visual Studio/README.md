# D365 Developer Tools (Unofficial) for Visual Studio

A Visual Studio extension for C# development against Dynamics 365 / Dataverse. Browse entities and
attributes, and generate early-bound C# classes and option-set enums — without leaving the editor.

This is a Visual Studio port of the [D365 Developer Tools (Unofficial)](../../../D365%20VSCode%20Extension)
VS Code extension, adapted for C#/early-bound Dataverse development instead of TypeScript.

## Table of Contents

- [Features](#features)
  - [Entity Explorer](#entity-explorer)
  - [Plugin Explorer](#plugin-explorer)
  - [Publish to Dataverse](#publish-to-dataverse)
  - [Add Step](#add-step)
  - [C# Class Generation](#c-class-generation)
  - [Enum Generation](#enum-generation)
  - [IntelliSense Integration](#intellisense-integration)
  - [Connection Management](#connection-management)
  - [Claude / AI Integration (MCP Server)](#claude--ai-integration-mcp-server)
- [Extensions Menu](#extensions-menu)
- [Requirements](#requirements)

## Features

### Entity Explorer

A tool window (**Tools → D365 Developer Tools → D365: Show Entity Explorer**) that connects to your
Dataverse environment and lets you browse its metadata.

- Lists all entities, searchable by name
- The list for an environment is cached locally, so re-opening it (or reconnecting) shows the entities
  instantly while a fresh copy loads silently in the background
- Filter the list down to a specific solution — your choice is remembered per environment and
  re-applied automatically the next time you connect to it, until you clear the filter
- Expand any entity to see its attributes, types, and whether each field is the primary ID or primary name
- Shows each entity's real Dataverse icon (fetched from its SVG icon web resource and cached locally per
  environment) once it loads, falling back to a generic table icon for entities without one or if the
  icon can't be rendered
- Right-click an entity to generate an early-bound C# class
- Right-click a Picklist, State, or Status field to generate a standalone enum

### Plugin Explorer

A tool window (**Tools → D365 Developer Tools → D365: Show Plugin Explorer**) for browsing registered
plugin assemblies, similar to the Plugin Registration Tool.

- Lists all plugin assemblies, searchable by name, with their version, isolation mode, and — for
  assemblies deployed as part of a NuGet-style plugin package — the package name
- Filter the list down to a specific solution
- Expand an assembly to see its plugin types (and whether each is a workflow activity)
- Expand a plugin type to see its registered steps — message, target entity, stage, and execution mode,
  with disabled steps shown dimmed
- Expand a step to see its registered pre-/post-images
- **Publish project to Dataverse...** — the toolbar button next to the search box builds a project from
  the open solution and publishes it, the same as right-clicking it in Solution Explorer (see
  [Publish to Dataverse](#publish-to-dataverse)); the tree refreshes afterward
- Right-click a plugin type → **Add Step...** to register a new step for it (see [Add Step](#add-step))
- Right-click a step → **Edit Step...** to change its message, target entity, stage, execution mode,
  execution order, filtering attributes, or solution — the same dialog as Add Step, pre-filled with the
  step's current values
- Right-click a step → **Enable**/**Disable** to activate or deactivate it in place, without opening the
  edit dialog

### Publish to Dataverse

Right-click a project in Solution Explorer and choose **D365: Publish to Dataverse...** to build and
deploy it without leaving Visual Studio.

1. Builds the project (Release configuration).
2. Looks for an existing Plugin Assembly or Plugin Package in the connected environment with the same
   name as the built assembly.
   - **Found** — uploads the new build to it. If it's a traditional plugin assembly, the build is also
     scanned for `IPlugin` implementations and any newly added ones are registered as Plugin Types
     (existing ones are left alone, so registered steps are never disturbed).
   - **Not found (first publish)** — the first time you publish a given project, you'll be asked which
     deployment model to use:
     - **Traditional Plugin Assembly** — uploads the .dll to a Plugin Assembly record, then registers a
       Plugin Type for each `IPlugin` implementation it finds.
     - **NuGet-style Plugin Package** — uploads to a Plugin Package record; Dataverse derives the
       plugin types from the package itself.

     Your choice is remembered for that project, and you'll then be asked whether to add the new
     record to a solution (picked from a list of your Dataverse solutions).

### Add Step

Right-click a `.cs` file that contains a class implementing `IPlugin` and choose **D365: Add
Step...** to register a new step for it without leaving Visual Studio.

- Looks up the plugin type in the connected environment by its fully-qualified name — the project
  needs to have been [published](#publish-to-dataverse) first, since a step can't be registered
  against a type Dataverse doesn't know about yet. If the file has more than one `IPlugin` class, or
  the type name matches more than one published assembly, you'll be asked which one to use.
- Opens a dialog to set the message, target entity (only entities valid for the chosen message are
  offered), stage, execution mode, execution order, and — for `Update` steps — the filtering
  attributes.
- **Solution** defaults to the one solution the plugin's assembly belongs to, if it's only in one;
  otherwise you can pick one from the list, or leave it unset.

### C# Class Generation

Right-click an entity in the Entity Explorer to generate an early-bound class for it, CrmSvcUtil-style.
You'll be prompted to select which fields to include, and the result opens directly in the editor as
an unsaved C# document.

- Fields are typed appropriately (`string`, `int?`, `decimal?`, `bool?`, `DateTime?`, `Guid?`, `Money`)
- Lookup, Customer, and Owner fields use `EntityReference`
- Picklist, State, and Status fields automatically have their option values fetched and are typed with
  a matching enum, generated alongside the class
- The class includes `[EntityLogicalName]` / `[AttributeLogicalName]` attributes and an `Id` override
  wired to the primary key, so it plugs straight into `IOrganizationService` code

**Example output:**

```csharp
public enum LeadStatusCode
{
    New = 1,
    Contacted = 2,
    Qualified = 3,
}

[EntityLogicalName(EntityLogicalName)]
public class Lead : Entity
{
    public const string EntityLogicalName = "lead";
    public const string PrimaryIdAttribute = "leadid";

    public Lead() : base(EntityLogicalName) { }

    public override Guid Id
    {
        get => base.Id;
        set { base.Id = value; SetAttributeValue(PrimaryIdAttribute, value); }
    }

    /// <summary>Primary ID</summary>
    [AttributeLogicalName("leadid")]
    public Guid? LeadId
    {
        get => GetAttributeValue<Guid?>("leadid");
        set => SetAttributeValue("leadid", value);
    }

    [AttributeLogicalName("statuscode")]
    public LeadStatusCode? StatusCode
    {
        get
        {
            var v = GetAttributeValue<OptionSetValue>("statuscode");
            return v == null ? (LeadStatusCode?)null : (LeadStatusCode)v.Value;
        }
        set => SetAttributeValue("statuscode", value.HasValue ? new OptionSetValue((int)value.Value) : null);
    }
}
```

> Generated classes reference `Microsoft.Xrm.Sdk` types (`Entity`, `EntityReference`, `OptionSetValue`,
> `Money`) by name only. Add a reference to `Microsoft.CrmSdk.CoreAssemblies` (or your own SDK
> assemblies) in the project you paste the generated code into.

### Enum Generation

Right-click any Picklist, State, or Status attribute in the Entity Explorer and choose **Generate Enum**
to fetch its option values from Dataverse and open a ready-to-use `enum`.

### IntelliSense Integration

Start typing `d365` anywhere in a `.cs` file and two items appear in the completion list:

- **D365: Generate interface…** — prompts for an entity, then inserts a class with all fields
- **D365: Generate interface (select fields…)** — prompts for an entity, then lets you pick which
  fields to include

Accepting either clears the typed text and inserts the generated class (and any option-set enums)
right at the cursor.

### Connection Management

- **Connect to Environment…** — prompts for an environment URL and authentication method, then
  validates connectivity via `WhoAmI`
- **Disconnect** / **Switch Account…** — from the same menu
- **Recent Environments** — the last five environments you've connected to, for one-click reconnect
- **Per-solution** — the connection is remembered against the currently open solution and restored
  automatically the next time you open it

Authentication options:

| Mode | Description |
|---|---|
| User account | Interactive sign-in with your own Microsoft/Entra credentials, via Microsoft's published multitenant "XRM Tooling" client application — no Azure AD app registration required |
| Client credentials | App-only, using an Azure AD client ID and secret (stored using Windows DPAPI, scoped to your Windows user account) |

### Claude / AI Integration (MCP Server)

The extension ships an MCP (Model Context Protocol) server so Claude can query your live Dataverse
schema while helping you write C# code. When the server is running, Claude can look up entity shapes,
field types, and option set values in real time — no copy-pasting schema details into the chat.

#### Available tools

| Tool | Description |
|---|---|
| `list_entities` | List all entities, optionally filtered to a solution |
| `get_entity_attributes` | Get all fields for an entity with their types |
| `get_option_values` | Get the numeric values and labels for a Picklist, State, or Status field |
| `generate_class` | Generate an early-bound C# class (with auto-generated enums for option set fields) |
| `generate_enum` | Generate a C# `enum` for a single option set field |

#### Setup

No credentials needed — the MCP server uses your existing Visual Studio session. Configuration is
opt-in per solution.

1. **Connect** via **Tools → D365 Developer Tools → D365: Connect / Manage Connection…**
2. **Run D365: Configure MCP Server for this Solution** from the same Tools menu. This writes (or
   merges into) `.mcp.json` next to your `.sln` file, pointing Claude Code at the MCP server bundled
   with the extension. A dialog confirms when this happens.
3. **Restart Claude Code** so it picks up the new `.mcp.json`.
4. **Run `/mcp`** in Claude Code to confirm the `d365` server is listed as connected.

That's it. The extension starts a local token-vending bridge (`%LocalAppData%\D365DeveloperTools\mcp-bridge.json`)
whenever you're connected; the MCP server reads from it so Claude always has a fresh token without
storing any credentials.

The extension also posts a brief "D365: MCP server active/inactive" message to Visual Studio's status
bar whenever the bridge starts or stops. Visual Studio's status bar only has one shared text slot, so
this message is best-effort — other activity (including the "Connected to..." message) can overwrite it
immediately.

> If the `d365` server shows as disconnected in `/mcp`, make sure D365 Developer Tools is connected in
> Visual Studio first.

#### Example usage

Once connected, Claude can answer questions like:

> *"Generate a C# class for the `lead` entity, only including the name, status, owner, and created
> date fields."*

Claude will call `get_entity_attributes` and `generate_class` against your live environment and return
ready-to-use code.

## Extensions Menu

Everything lives under **Tools → D365 Developer Tools**:

| Command | Description |
|---|---|
| D365: Connect / Manage Connection… | Opens the connect/disconnect/switch-account/recent-environments picker |
| D365: Show Entity Explorer | Opens the Entity Explorer tool window |
| D365: Show Plugin Explorer | Opens the Plugin Explorer tool window |
| D365: Configure MCP Server for this Solution | Wires up Claude Code's `.mcp.json` for the open solution |

## Requirements

- Visual Studio 2022 17.9 or later, or a Visual Studio 2026 preview build with the *Visual Studio
  extension development* workload
- A Dataverse / Dynamics 365 environment
- For client credentials auth: an Azure AD app registration with a client secret and appropriate
  Dataverse permissions
- .NET 8 runtime (required for the bundled MCP server)
