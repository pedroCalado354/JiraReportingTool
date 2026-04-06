using System.Text;
using System.Text.Json;
using Anthropic;
using JiraReportingTool.Models;

public class ChatService(IHttpClientFactory httpFactory, IJiraService jira, IConfiguration config)
{
    // ── Tool definitions ─────────────────────────────────────────────────────

    private static readonly List<Tool> _tools =
    [
        new Tool
        {
            Name = "get_sprint_summary",
            Description = "Returns a high-level summary of a Jira sprint: sprint name, dates, total/completed/in-progress story points, team members, and issues at risk.",
            InputSchema = new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    ["sprint_id"] = new OpenApiSchema
                    {
                        Type = "integer",
                        Description = "The numeric Jira sprint ID.",
                    },
                },
                Required = ["sprint_id"],
            },
        },
        new Tool
        {
            Name = "get_sprint_issues",
            Description = "Returns the full issue list for a sprint. Each issue includes key, summary, assignee, status, story points, time spent, and whether it is at risk. Optionally filter by assignee.",
            InputSchema = new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    ["sprint_id"] = new OpenApiSchema
                    {
                        Type = "integer",
                        Description = "The numeric Jira sprint ID.",
                    },
                    ["assignee"] = new OpenApiSchema
                    {
                        Type = "string",
                        Description = "Optional. Filter issues to this assignee's display name (case-insensitive partial match).",
                    },
                },
                Required = ["sprint_id"],
            },
        },
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<string> SendAsync(
        List<ChatMessage> history,
        string userMessage,
        Action<string>? onStatus = null)
    {
        var apiKey = config["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return "**Error:** No Anthropic API key configured. Add `Anthropic:ApiKey` to appsettings.json or user secrets.";

        var http = httpFactory.CreateClient("anthropic");
        var api = new AnthropicApi(http);
        api.AuthorizeUsingApiKey(apiKey);

        var messages = BuildMessages(history, userMessage);

        // Agentic loop
        while (true)
        {
            var response = await api.CreateMessageAsync(new CreateMessageRequest
            {
                Model = "claude-opus-4-6",
                MaxTokens = 4096,
                System = SystemPrompt(),
                Tools = _tools,
                Messages = messages,
            });

            // Extract text and tool-use blocks from response content (OneOf<string, IList<Block>>)
            var responseBlocks = response.Content.IsValue2 ? response.Content.Value2!.ToList() : [];
            var textParts     = responseBlocks.Where(b => b.IsText).Select(b => b.Text!.Text ?? "").ToList();
            var toolUseParts  = responseBlocks.Where(b => b.IsToolUse).Select(b => b.ToolUse!).ToList();

            if (response.StopReason != StopReason.ToolUse || toolUseParts.Count == 0)
                return string.Join("\n\n", textParts);

            // Append assistant turn (keep all blocks, including tool-use blocks)
            messages.Add(new Message
            {
                Role    = MessageRole.Assistant,
                Content = new System.OneOf<string, IList<Block>>((IList<Block>)responseBlocks),
            });

            // Execute each tool and collect results
            var resultBlocks = new List<Block>();
            foreach (var tool in toolUseParts)
            {
                onStatus?.Invoke($"Looking up: {tool.Name}…");
                var result = await ExecuteToolAsync(tool);
                resultBlocks.Add(new Block
                {
                    ToolResult = new ToolResultBlock
                    {
                        ToolUseId = tool.Id,
                        Content   = result,
                    },
                });
            }

            messages.Add(new Message
            {
                Role    = MessageRole.User,
                Content = new System.OneOf<string, IList<Block>>((IList<Block>)resultBlocks),
            });
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string SystemPrompt() =>
        """
        You are a Jira assistant for a software development team that uses 2-week sprints.
        You have access to sprint data via tools. Always use the tools to look up current data
        before answering — do not guess or make up information.

        When asked about a person's work, use get_sprint_issues filtered by their name.
        To assess if someone is "on track", compare time_spent to original_estimate and check
        if any of their issues are marked AT RISK.
        Respond concisely in Markdown format with tables or bullet lists where appropriate.
        Always mention the sprint name and dates when providing sprint information.
        """;

    private static List<Message> BuildMessages(List<ChatMessage> history, string newMessage)
    {
        var result = new List<Message>();
        foreach (var msg in history)
        {
            result.Add(new Message
            {
                Role    = msg.IsUser ? MessageRole.User : MessageRole.Assistant,
                Content = new System.OneOf<string, IList<Block>>(
                    (IList<Block>)[new Block { Text = new TextBlock { Text = msg.Text } }]),
            });
        }
        result.Add(new Message
        {
            Role    = MessageRole.User,
            Content = new System.OneOf<string, IList<Block>>(
                (IList<Block>)[new Block { Text = new TextBlock { Text = newMessage } }]),
        });
        return result;
    }


    private async Task<string> ExecuteToolAsync(ToolUseBlock tool)
    {
        try
        {
            return tool.Name switch
            {
                "get_sprint_summary" => await GetSprintSummaryAsync(tool.Input),
                "get_sprint_issues"  => await GetSprintIssuesAsync(tool.Input),
                _                   => $"Unknown tool: {tool.Name}",
            };
        }
        catch (Exception ex)
        {
            return $"Error executing {tool.Name}: {ex.Message}";
        }
    }

    private async Task<string> GetSprintSummaryAsync(object? input)
    {
        var json     = JsonSerializer.SerializeToElement(input);
        var sprintId = json.GetProperty("sprint_id").GetInt32();
        var report   = await jira.GetDeliveryDataAsync(sprintId);

        var sb = new StringBuilder();
        sb.AppendLine($"Sprint: {report.SprintName}");
        sb.AppendLine($"Dates: {report.StartDate:yyyy-MM-dd} to {report.EndDate:yyyy-MM-dd}");
        sb.AppendLine($"Total story points: {report.TotalStoryPoints}");
        sb.AppendLine($"Completed (done): {report.DoneStoryPoints}");
        sb.AppendLine($"Done issues: {report.DoneCount} / {report.TotalIssues}");
        sb.AppendLine($"In progress: {report.InProgressCount}");
        sb.AppendLine($"Not started: {report.ToDoCount}");
        sb.AppendLine($"Issues at risk: {report.AtRiskIssues.Count}");
        sb.AppendLine($"Sprint progress: {report.SprintProgressPct:F0}% of sprint elapsed");
        sb.AppendLine($"Completion: {report.CompletionPct:F0}%");

        var members = report.Issues
            .Where(i => !string.IsNullOrEmpty(i.Assignee) && i.Assignee != "Unassigned")
            .Select(i => i.Assignee)
            .Distinct()
            .OrderBy(a => a)
            .ToList();
        sb.AppendLine($"Team members: {string.Join(", ", members)}");

        if (report.AtRiskIssues.Count > 0)
        {
            sb.AppendLine("\nAt-risk issues:");
            foreach (var issue in report.AtRiskIssues)
                sb.AppendLine($"  - [{issue.Key}] {issue.Summary} (assignee: {issue.Assignee}, status: {issue.Status})");
        }

        return sb.ToString();
    }

    private async Task<string> GetSprintIssuesAsync(object? input)
    {
        var json     = JsonSerializer.SerializeToElement(input);
        var sprintId = json.GetProperty("sprint_id").GetInt32();
        string? assigneeFilter = null;
        if (json.TryGetProperty("assignee", out var aEl) && aEl.ValueKind == JsonValueKind.String)
            assigneeFilter = aEl.GetString();

        var report = await jira.GetDeliveryDataAsync(sprintId);
        var issues = report.Issues.AsEnumerable();

        if (!string.IsNullOrEmpty(assigneeFilter))
            issues = issues.Where(i =>
                i.Assignee?.Contains(assigneeFilter, StringComparison.OrdinalIgnoreCase) == true);

        var list = issues.ToList();
        var atRisk = report.AtRiskIssues.Select(i => i.Key).ToHashSet();

        var sb = new StringBuilder();
        sb.AppendLine($"Sprint: {report.SprintName} ({report.StartDate:yyyy-MM-dd} to {report.EndDate:yyyy-MM-dd})");
        sb.AppendLine($"Issues ({list.Count}):");
        sb.AppendLine();

        foreach (var issue in list)
        {
            var spent    = issue.TimeSpentSeconds > 0 ? $"{issue.TimeSpentSeconds / 3600.0:F1}h" : "0h";
            var estimate = issue.OriginalEstimateSeconds > 0 ? $"{issue.OriginalEstimateSeconds / 3600.0:F1}h" : "no estimate";
            var risk     = atRisk.Contains(issue.Key) ? " ⚠ AT RISK" : "";
            sb.AppendLine($"[{issue.Key}] {issue.Summary}");
            sb.AppendLine($"  Assignee: {issue.Assignee} | Status: {issue.Status} | Points: {issue.StoryPoints?.ToString() ?? "-"}");
            sb.AppendLine($"  Time: {spent} spent / {estimate}{risk}");
        }

        return sb.ToString();
    }
}

public record ChatMessage(string Text, bool IsUser, bool IsMarkdown = false);
