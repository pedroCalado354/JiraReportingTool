# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build JiraReportingTool/JiraReportingTool.csproj

# Run (dev server at https://localhost:7xxx)
dotnet run --project JiraReportingTool/JiraReportingTool.csproj

# EF Core migrations (run from the JiraReportingTool/ subfolder)
dotnet ef migrations add <MigrationName>
dotnet ef database update

# Set secrets (API tokens are never in appsettings.json)
dotnet user-secrets --project JiraReportingTool set "Jira:ApiToken" "<value>"
dotnet user-secrets --project JiraReportingTool set "Anthropic:ApiKey" "<value>"
```

Migrations run automatically on startup via `db.Database.Migrate()` in `Program.cs`.

## Architecture

**Stack:** Blazor Server (.NET 10), SQL Server LocalDB, Entity Framework Core 10, ClosedXML (Excel export), Anthropic SDK (AI chat).

All pages use `@rendermode InteractiveServer` and are self-contained single-file Razor components (markup + `@code` block + `<style>` — no code-behind `.cs` files).

### Service Layer

Two `IJiraService` implementations are registered in DI:

| Class | Role |
|---|---|
| `JiraService` | Direct Jira Cloud API calls via `POST /rest/api/3/search/jql` with cursor-based pagination (`nextPageToken`, 100 items/page) |
| `JiraCacheService` | Wraps `JiraService` + `JiraDbRepository`; checks DB first, falls back to API on miss/stale, then persists result |

`JiraCacheService` is registered as `IJiraService` — pages always inject `IJiraService`. Cache TTL is controlled by `Database:CacheTtlMinutes` (default 300 min). Methods that return live/aggregated data bypass the cache: `GetEpicBugsAsync`, `GetIssuesByKeysAsync`, `GetDeliveryDataByFilterAsync`, `GetPriorityBugsAsync`.

Cache keys stored in `SprintReport.ReportIdentifier`:
- `sprint:PROJ:42` — sprint report
- `delivery:42` — delivery data
- `epicall:KEY-1` — all issues for an epic

### Core Data Models (`Models/JiraModels.cs`, `Models/SprintPlanModels.cs`)

`SprintIssue` is the central entity used everywhere. Key fields: `Key`, `Summary`, `Status`, `StatusCategoryKey` (`done` / `indeterminate` / `new`), `Assignee`, `OriginalEstimateSeconds`, `TimeSpentSeconds`, `RemainingEstimateSeconds`, `DueDate`, `EpicKey`, `Worklogs: List<WorklogEntry>`, `Labels`.

`SprintIssue.Labels` is stored as JSON (`nvarchar(max)`) — EF value converter is configured in `AppDbContext.OnModelCreating`.

`SprintPlanHeader` owns a sprint plan and cascades to `SprintPlanAllocation` (one row per task+member+day), `SprintPlanCustomTask`, `SprintPlanHoliday`, `SprintPlanTimeOff`. Custom task IDs are negative integers; Jira task IDs are positive.

### Jira API Notes

- Auth: Basic auth (Base64 `email:token`) via `Authorization` header
- Jira custom fields: `customfield_10016` = story points, `customfield_10020` = sprint array, `customfield_10014` = classic epic link
- Epic linking is dual-mode: classic (`customfield_10014`) or next-gen (`parent.key` where parent issuetype = Epic)
- Worklogs: up to 20 returned inline per issue; `JiraService` re-fetches full worklogs via `GET /rest/api/3/issue/{key}/worklog` for any issue where they are truncated or missing
- Worklog comments are in Atlassian Document Format (ADF); parsed by extracting `content[].content[].text`

### Adding a New Page

1. Create `Components/Pages/YourPage.razor` starting with:
   ```razor
   @page "/your-route"
   @rendermode InteractiveServer
   @using JiraReportingTool.Models
   @inject IJiraService JiraService
   @inject IConfiguration Config
   @inject IJSRuntime JS   // only if using Excel export
   ```
2. Add a `<NavLink>` entry in `Components/Layout/NavMenu.razor`
3. If you need model changes, add an EF migration

### Excel Export Pattern

All pages use the same JS interop pattern — **do not use `Response` or file streaming**:
```csharp
using var ms = new MemoryStream();
wb.SaveAs(ms);
var base64 = Convert.ToBase64String(ms.ToArray());
await JS.InvokeVoidAsync("downloadBase64File", base64, fileName,
    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
```
The `downloadBase64File` function is defined in `wwwroot/js/`.

### Jira Issue Links

```razor
<a href="@(Config["Jira:BaseUrl"]?.TrimEnd('/'))/browse/@issue.Key" target="_blank">
```

### CSS Design System (`wwwroot/app.css`)

CSS custom properties (defined on `:root`):

| Token | Value | Use |
|---|---|---|
| `--primary` | `#2563eb` | Blue — actions, active state |
| `--success` | `#16a34a` | Green — done, healthy |
| `--warning` | `#b45309` | Amber — at risk |
| `--danger` | `#b91c1c` | Red — critical, overdue |
| `--surface` | `#ffffff` | Card/panel background |
| `--surface-2` | `#f3f4f6` | Section header background |
| `--border` | `#e5e7eb` | Dividers |
| `--txt-1/2/3/4` | gray scale | Text hierarchy |
| `--sh-xs/sm/md` | box shadows | Card depth |

Each page's styles are scoped in a `<style>` block at the bottom of the `.razor` file. Shared button classes (`.btn`, `.btn.primary`, `.btn.danger`, `.btn.small`), status chips (`.rpt-status-chip`, `.status-in-progress`, `.status-done`, etc.), and risk badges (`.risk-badge`, `.risk-green`, `.risk-amber`, `.risk-red`) come from `app.css` and are available globally.

### Razor Code Block Scoping

When declaring local variables inside an `else { }` block that is attached to `@if`, declare them **before any HTML** (not in a nested `@{ }` block — that causes `RZ1010`):

```razor
else
{
    var myVar = ComputeSomething(row);  // ✓ declare here, before any HTML
    <td class="@(myVar ? "cls-a" : "cls-b")">...</td>
}
```
