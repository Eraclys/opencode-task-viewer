using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Infrastructure.Orchestration;

public sealed class HttpSonarGateway : ISonarGateway
{
    readonly HttpClient _httpClient;
    readonly string _sonarToken;
    readonly string _sonarUrl;

    public HttpSonarGateway(string sonarUrl, string sonarToken, HttpClient? httpClient = null)
    {
        _sonarUrl = sonarUrl;
        _sonarToken = sonarToken;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<JsonNode?> Fetch(string endpointPath, Dictionary<string, string?> query)
    {
        if (string.IsNullOrWhiteSpace(_sonarUrl) ||
            string.IsNullOrWhiteSpace(_sonarToken))
            throw new InvalidOperationException("SonarQube is not configured");

        var url = new Uri(new Uri(_sonarUrl), endpointPath);
        var uriBuilder = new UriBuilder(url);
        var queryParams = HttpUtility.ParseQueryString(uriBuilder.Query);

        foreach (var (key, value) in query)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            queryParams[key] = value;
        }

        uriBuilder.Query = queryParams.ToString() ?? string.Empty;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_sonarToken}:"));

        using var request = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"SonarQube request failed: {(int)response.StatusCode} {response.ReasonPhrase}");

        return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
    }
}
