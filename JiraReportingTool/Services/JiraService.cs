using System.Net.Http.Headers;
using System.Text;

public class JiraService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public JiraService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;

        var email = _config["Jira:Email"];
        var token = _config["Jira:ApiToken"];

        var authBytes = Encoding.ASCII.GetBytes($"{email}:{token}");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
    }

    public async Task<string> GetIssue(string issueKey)
    {
        var baseUrl = _config["Jira:BaseUrl"];
        var response = await _httpClient.GetAsync($"{baseUrl}/rest/api/3/issue/{issueKey}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
