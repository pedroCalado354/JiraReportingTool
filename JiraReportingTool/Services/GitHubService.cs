using System.Net.Http.Headers;
using System.Text.Json;
using JiraReportingTool.Models;

public class GitHubService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly string _owner;
    private readonly string[] _repos;

    public GitHubService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config     = config;
        _owner      = config["GitHub:Owner"] ?? "";
        _repos      = (config["GitHub:Repos"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _httpClient.BaseAddress = new Uri("https://api.github.com/");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("JiraReportingTool/1.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        var token = config["GitHub:Token"] ?? "";
        if (!string.IsNullOrEmpty(token))
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_config["GitHub:Token"]) && !string.IsNullOrEmpty(_owner) && _repos.Length > 0;

    /// <summary>
    /// Searches all configured repos for PRs whose title or body mention the Jira issue key.
    /// </summary>
    public async Task<List<DevPullRequest>> GetPullRequestsAsync(string issueKey)
    {
        if (!IsConfigured) return [];

        var repoFilters = _repos.Select(r => $"repo:{_owner}/{r}");
        var q = string.Join(" ", new[] { issueKey, "is:pr" }.Concat(repoFilters));
        var url = $"search/issues?q={Uri.EscapeDataString(q)}&per_page=50&sort=updated";

        var resp = await _httpClient.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return [];

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items)) return [];

        var results = new List<DevPullRequest>();
        foreach (var item in items.EnumerateArray())
        {
            var state = item.TryGetProperty("state", out var st) ? st.GetString() ?? "" : "";
            var mergedAt = item.TryGetProperty("pull_request", out var prEl) &&
                           prEl.TryGetProperty("merged_at", out var ma) &&
                           ma.ValueKind != JsonValueKind.Null
                ? ma.GetString() : null;

            var htmlUrl = item.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";

            var pr = new DevPullRequest
            {
                Title        = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                Url          = htmlUrl,
                Status       = state == "closed" ? (mergedAt != null ? "MERGED" : "DECLINED") : "OPEN",
                SourceBranch = ExtractRepoName(htmlUrl),
            };

            if (item.TryGetProperty("updated_at", out var ua) &&
                DateTime.TryParse(ua.GetString(), out var uaDt))
                pr.LastUpdated = uaDt;

            results.Add(pr);
        }
        return results;
    }

    private string ExtractRepoName(string htmlUrl)
    {
        var prefix = $"https://github.com/{_owner}/";
        if (!htmlUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        var rest = htmlUrl[prefix.Length..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest[..slash];
    }
}
