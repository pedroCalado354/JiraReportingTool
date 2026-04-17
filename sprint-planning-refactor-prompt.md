# Sprint Planning Page — Refactor Workload View & Add Daily Metrics

I'm working on the `sprint-planning` Razor page. I need you to refactor the workload view and add a new daily metrics view. Please follow these steps.

## Context first — before making any changes

1. Open the `sprint-planning` Razor page and locate the `workload-container` div (this is what currently renders when the "Workload" button in the view-toggle-group is clicked).
2. Identify how the view-toggle-group works so the new button follows the same pattern (same event binding, same show/hide or routing logic, same styling classes).
3. Identify how tasks are currently loaded and where Jira data is fetched — I need to know if there's an existing service/client for Jira or if I need to extend one.
4. Identify how "custom tasks" are distinguished from Jira-sourced tasks in the data model.

**Then show me your plan before coding.** I want to confirm the approach before you touch files.

---

## Change 1 — Replace the workload view with a Report view

- Remove the `workload-container` div and any CSS/JS/code-behind that exists solely to support it (clean removal, no dead code left behind).
- Rename the toggle button from "Workload" to "Report" (or tell me if you think it should stay labeled "Workload" — your call, justify it).
- Build a new report view that displays **all tasks assigned to all team members** in the current sprint. For each task, show:
  - Assignee
  - Task title / key
  - **Status** (from Jira, live)
  - **Original Estimate** (from Jira)
  - **Remaining Estimate** (from Jira)
  - A clear visual indicator for custom tasks (tasks not backed by Jira) — these should still appear in the report, but with Jira-specific columns shown as "—" or "N/A" rather than blank or broken.
- Group or sort the report in a way that's actually useful for sprint planning (e.g. grouped by team member, then by status). Suggest what you think is best.

---

## Change 2 — Add a "Daily" button to the view-toggle-group

- Add a new button labeled **"Daily"** to the view-toggle-group, styled consistently with the existing buttons.
- When clicked, it should render a daily metrics view showing **completion metrics across all sprint tasks**. Include at minimum:
  - % of tasks completed (count-based)
  - % of estimated work completed (hours-based: `(original - remaining) / original`)
  - Count of tasks per status (To Do / In Progress / Done / etc.)
  - Custom tasks should be included in the count-based metric; exclude them from the hours-based metric if they don't have estimates (and note this in the UI).
- If you think other daily-standup-relevant metrics would be valuable (blocked tasks, tasks with no remaining estimate, tasks untouched in 24h, etc.), propose them in your plan and I'll tell you which to include.

---

## Constraints

- Handle the case where Jira is unreachable or returns partial data gracefully — don't let one failed task lookup break the whole view.
- Don't hardcode Jira field IDs if there's already a config/constants location for them.
- Keep the existing view-toggle behavior (what shows, what hides, URL state if any) consistent across all buttons.

---

**Start by exploring the code and showing me the plan. Don't write implementation code until I confirm.**
