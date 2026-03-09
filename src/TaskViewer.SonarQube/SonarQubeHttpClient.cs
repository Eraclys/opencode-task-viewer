using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace TaskViewer.SonarQube;

public sealed class SonarQubeHttpClient
{
    readonly HttpClient _httpClient;
    readonly SonarQubeClientOptions _options;

    public SonarQubeHttpClient(HttpClient httpClient, SonarQubeClientOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<SonarIssuesSearchResponse> SearchIssuesAsync(SearchIssuesQuery query, CancellationToken cancellationToken = default)
    {
        var data = await FetchAsync("/api/issues/search", BuildSearchIssuesQueryParams(query), cancellationToken);
        return SonarResponseParsers.ParseIssuesSearchResponse(data, query.PageIndex, query.PageSize);
    }

    public async Task<SonarRuleDetailsResponse> GetRuleAsync(string ruleKey, CancellationToken cancellationToken = default)
    {
        var data = await FetchAsync(
            "/api/rules/show",
            new Dictionary<string, string?>
            {
                ["key"] = ruleKey
            },
            cancellationToken);

        return SonarResponseParsers.ParseRuleDetails(data);
    }

    async Task<JsonNode?> FetchAsync(string endpointPath, Dictionary<string, string?> query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Url) ||
            string.IsNullOrWhiteSpace(_options.Token))
            throw new InvalidOperationException("SonarQube is not configured");

        var url = new Uri(new Uri(_options.Url), endpointPath);
        var uriBuilder = new UriBuilder(url);
        var queryParams = HttpUtility.ParseQueryString(uriBuilder.Query);

        foreach (var (key, value) in query)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            queryParams[key] = value;
        }

        uriBuilder.Query = queryParams.ToString() ?? string.Empty;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Token}:"));

        using var request = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"SonarQube request failed: {(int)response.StatusCode} {response.ReasonPhrase}");

        return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
    }

    static Dictionary<string, string?> BuildSearchIssuesQueryParams(SearchIssuesQuery query)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["componentKeys"] = query.ComponentKey,
            ["p"] = query.PageIndex.ToString(CultureInfo.InvariantCulture),
            ["ps"] = query.PageSize.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(query.Branch))
            parameters["branch"] = query.Branch;

        AddCsv(parameters, "types", query.Types, preserveCase: false);
        AddCsv(parameters, "severities", query.Severities, preserveCase: false);
        AddCsv(parameters, "statuses", query.Statuses, preserveCase: false);
        AddCsv(parameters, "rules", query.RuleKeys, preserveCase: true);
        AddCsv(parameters, "issues", query.IssueKeys, preserveCase: true);
        return parameters;
    }

    static void AddCsv(Dictionary<string, string?> parameters, string key, IReadOnlyList<string> values, bool preserveCase)
    {
        if (values.Count == 0)
            return;

        var normalized = values
            .Select(value => preserveCase ? value.Trim() : value.Trim().ToUpperInvariant())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (normalized.Count > 0)
            parameters[key] = string.Join(',', normalized);
    }
}
