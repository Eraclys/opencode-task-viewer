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

    public async Task<SonarIssuesSearchResponse> SearchIssuesAsync(Dictionary<string, string?> query, int fallbackPageIndex, int fallbackPageSize, CancellationToken cancellationToken = default)
    {
        var data = await FetchAsync("/api/issues/search", query, cancellationToken);
        return SonarResponseParsers.ParseIssuesSearchResponse(data, fallbackPageIndex, fallbackPageSize);
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
}
