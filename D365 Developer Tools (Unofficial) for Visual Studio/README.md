# D365 Developer Tools (Unofficial) for Visual Studio

A Visual Studio extension for C# development against Dynamics 365 / Dataverse. Browse entities and
attributes, and generate early-bound C# classes and option-set enums — without leaving the editor.

This is a Visual Studio port of the [D365 Developer Tools (Unofficial)](../../../D365%20VSCode%20Extension)
VS Code extension, adapted for C#/early-bound Dataverse development instead of TypeScript.

## Table of Contents

- [Features](#features)
  - [Entity Explorer](#entity-explorer)
  - [C# Class Generation](#c-class-generation)
  - [Enum Generation](#enum-generation)
  - [IntelliSense Integration](#intellisense-integration)
  - [Connection Management](#connection-management)
- [Extensions Menu](#extensions-menu)
- [Requirements](#requirements)
- [Known Limitations](#known-limitations)

## Features

### Entity Explorer

A tool window (**Tools → D365 Developer Tools → D365: Show Entity Explorer**) that connects to your
Dataverse environment and lets you browse its metadata.

- Lists all entities, searchable by name
- Filter the list down to a specific solution
- Expand any entity to see its attributes, types, and whether each field is the primary ID or primary name
- Right-click an entity to generate an early-bound C# class
- Right-click a Picklist, State, or Status field to generate a standalone enum

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

## Extensions Menu

Everything lives under **Tools → D365 Developer Tools**:

| Command | Description |
|---|---|
| D365: Connect / Manage Connection… | Opens the connect/disconnect/switch-account/recent-environments picker |
| D365: Show Entity Explorer | Opens the Entity Explorer tool window |

## Requirements

- Visual Studio 2022 17.9 or later, or a Visual Studio 2026 preview build with the *Visual Studio
  extension development* workload
- A Dataverse / Dynamics 365 environment
- For client credentials auth: an Azure AD app registration with a client secret and appropriate
  Dataverse permissions

## Known Limitations

This is a from-scratch C# port of a companion VS Code extension, focused on the core browsing and
code-generation workflow. Not yet included:

- Web resource publish/compare
- The MCP server / Claude integration
- The `// @d365 <entity>` comment + lightbulb generation trigger (only the "type `d365`" completion
  path is implemented)
- If no solution is open, connections are session-only and aren't remembered between restarts
