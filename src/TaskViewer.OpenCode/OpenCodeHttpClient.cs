using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TaskViewer.OpenCode;

public sealed class OpenCodeHttpClient
{
    readonly HttpClient _httpClient;
    readonly OpenCodeClientOptions _options;

    public OpenCodeHttpClient(HttpClient httpClient, OpenCodeClientOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<Dictionary<string, string>> ReadWorkingStatusMapAsync(string directory, CancellationToken cancellationToken = default)
    {
        var data = await SendAsync(
            "/session/status",
            new OpenCodeRequest
            {
                Directory = directory
            },
            cancellationToken);

        return OpenCodeStatusParsers.ParseWorkingStatusMap(data);
    }

    public async Task<List<OpenCodeTodoTransport>> ReadTodosAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
    {
        var data = await SendAsync(
            $"/session/{Uri.EscapeDataString(sessionId)}/todo",
            new OpenCodeRequest
            {
                Directory = directory
            },
            cancellationToken);

        return ToArrayResponse(data)
            .Select(ParseTodo)
            .ToList();
    }

    public async Task<List<OpenCodeMessageTransport>> ReadMessagesAsync(string sessionId, int? limit = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>();

        if (limit.HasValue)
            query["limit"] = limit.Value.ToString();

        var data = await SendAsync(
            $"/session/{Uri.EscapeDataString(sessionId)}/message",
            new OpenCodeRequest
            {
                Query = query
            },
            cancellationToken);

        return ToArrayResponse(data)
            .Select(ParseMessage)
            .ToList();
    }

    public async Task<List<OpenCodeProjectTransport>> ReadProjectsAsync(CancellationToken cancellationToken = default)
    {
        var data = await SendAsync("/project", new OpenCodeRequest(), cancellationToken);

        return ToArrayResponse(data)
            .Select(ParseProject)
            .OfType<OpenCodeProjectTransport>()
            .ToList();
    }

    public async Task<List<OpenCodeSessionTransport>> ReadSessionsAsync(string directory, int limit, CancellationToken cancellationToken = default)
    {
        var data = await SendAsync(
            "/session",
            new OpenCodeRequest
            {
                Query = new Dictionary<string, string?>
                {
                    ["roots"] = "true",
                    ["limit"] = limit.ToString()
                },
                Directory = directory
            },
            cancellationToken);

        return ToArrayResponse(data)
            .Select(session => ParseSession(session, directory))
            .OfType<OpenCodeSessionTransport>()
            .ToList();
    }

    public async Task<OpenCodeSessionTransport?> ReadSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
    {
        var data = await SendAsync(
            $"/session/{Uri.EscapeDataString(sessionId)}",
            new OpenCodeRequest
            {
                Directory = directory
            },
            cancellationToken);

        return ParseSession(data, directory);
    }

    public async Task<DateTimeOffset?> ArchiveSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
    {
        var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Exception? lastError = null;

        var attempts = new Func<Task>[]
        {
            () => SendAsync(
                $"/session/{Uri.EscapeDataString(sessionId)}",
                new OpenCodeRequest
                {
                    Method = "PATCH",
                    Directory = directory,
                    JsonBody = JsonSerializer.SerializeToNode(new { time = new { archived = nowUnixMs } })
                },
                cancellationToken),
            () => SendAsync(
                $"/session/{Uri.EscapeDataString(sessionId)}",
                new OpenCodeRequest
                {
                    Method = "PATCH",
                    Directory = directory,
                    JsonBody = JsonSerializer.SerializeToNode(new { archived = true })
                },
                cancellationToken),
            () => SendAsync(
                $"/session/{Uri.EscapeDataString(sessionId)}/archive",
                new OpenCodeRequest
                {
                    Method = "POST",
                    Directory = directory
                },
                cancellationToken)
        };

        foreach (var attempt in attempts)
        {
            try
            {
                await attempt();
                var session = await ReadSessionAsync(sessionId, directory, cancellationToken);

                if (session?.ArchivedAt.HasValue == true)
                    return session.ArchivedAt;

                lastError = new InvalidOperationException("Archive request succeeded but session did not report archived time");
            }
            catch (Exception error)
            {
                lastError = error;
            }
        }

        throw lastError ?? new InvalidOperationException("Failed to archive session");
    }

    public async Task<string> CreateSessionAsync(string directory, string title, CancellationToken cancellationToken = default)
    {
        var created = await SendAsync(
            "/session",
            new OpenCodeRequest
            {
                Method = "POST",
                Directory = directory,
                JsonBody = JsonSerializer.SerializeToNode(new { title })
            },
            cancellationToken);

        var sessionId = OpenCodeDispatchParsers.ParseCreatedSessionId(created);

        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("OpenCode did not return a session id");

        return sessionId;
    }

    public async Task SendPromptAsync(string directory, string sessionId, string prompt, CancellationToken cancellationToken = default)
    {
        await SendAsync(
            $"/session/{Uri.EscapeDataString(sessionId)}/prompt_async",
            new OpenCodeRequest
            {
                Method = "POST",
                Directory = directory,
                JsonBody = JsonSerializer.SerializeToNode(
                    new
                    {
                        parts = new[]
                        {
                            new
                            {
                                type = "text",
                                text = prompt
                            }
                        }
                    })
            },
            cancellationToken);
    }

    async Task<JsonNode?> SendAsync(string endpointPath, OpenCodeRequest request, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(_options.Url);
        var url = new Uri(baseUri, endpointPath);
        var queryParts = new List<string>();

        foreach (var (key, value) in request.Query)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(value))
                continue;

            queryParts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        if (!string.IsNullOrWhiteSpace(request.Directory))
            queryParts.Add($"directory={Uri.EscapeDataString(request.Directory)}");

        var uriBuilder = new UriBuilder(url);
        var existingQuery = uriBuilder.Query.TrimStart('?');

        if (!string.IsNullOrWhiteSpace(existingQuery))
            queryParts.Insert(0, existingQuery);

        uriBuilder.Query = string.Join("&", queryParts);

        using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), uriBuilder.Uri);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyAuthentication(httpRequest);

        if (!string.IsNullOrWhiteSpace(request.Directory))
            httpRequest.Headers.TryAddWithoutValidation("x-opencode-directory", request.Directory);

        if (request.JsonBody is not null)
            httpRequest.Content = new StringContent(request.JsonBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenCode request failed: {(int)response.StatusCode} {response.ReasonPhrase}; {text}");

        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return JsonValue.Create(text);

        return string.IsNullOrWhiteSpace(text)
            ? null
            : JsonNode.Parse(text);
    }

    void ApplyAuthentication(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.Password))
            return;

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    static List<JsonNode> ToArrayResponse(JsonNode? value)
    {
        if (value is JsonArray array)
            return array.Where(node => node is not null).Select(node => node!).ToList();

        if (value?["items"] is JsonArray items)
            return items.Where(node => node is not null).Select(node => node!).ToList();

        if (value?["sessions"] is JsonArray sessions)
            return sessions.Where(node => node is not null).Select(node => node!).ToList();

        if (value?["data"] is JsonArray data)
            return data.Where(node => node is not null).Select(node => node!).ToList();

        return [];
    }

    static OpenCodeTodoTransport ParseTodo(JsonNode? todo)
    {
        var content = todo?["content"]?.ToString() ?? todo?["text"]?.ToString() ?? todo?["title"]?.ToString() ?? string.Empty;
        return new OpenCodeTodoTransport(content, NormalizeTodoStatus(todo?["status"]?.ToString() ?? todo?["state"]?.ToString()), NormalizePriority(todo?["priority"]?.ToString()));
    }

    static OpenCodeMessageTransport ParseMessage(JsonNode? message)
    {
        return new OpenCodeMessageTransport(GetMessageRole(message), ExtractMessageText(message), ExtractMessageCreatedAt(message));
    }

    static OpenCodeProjectTransport? ParseProject(JsonNode? project)
    {
        if (project is null)
            return null;

        var worktree = NormalizeDirectory(project["worktree"]?.ToString());
        var sandboxes = new List<string>();
        CollectSandboxDirectories(project["sandboxes"], sandboxes);
        return new OpenCodeProjectTransport(worktree, sandboxes);
    }

    static OpenCodeSessionTransport? ParseSession(JsonNode? session, string? fallbackDirectory)
    {
        var sessionId = session?["id"]?.ToString().Trim();

        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var directory = NormalizeDirectory(session?["directory"]?.ToString()) ?? NormalizeDirectory(fallbackDirectory);
        var project = NormalizeDirectory(session?["project"]?["worktree"]?.ToString());
        var createdAt = ParseTime(session?["time"]?["created"]);
        var updatedAt = ParseTime(session?["time"]?["updated"]);
        var archivedAt = ParseTime(session?["time"]?["archived"]);

        return new OpenCodeSessionTransport(
            sessionId,
            session?["title"]?.ToString() ?? session?["name"]?.ToString(),
            directory,
            project,
            createdAt,
            updatedAt,
            archivedAt);
    }

    static DateTimeOffset? ParseTime(JsonNode? value)
    {
        if (value is null)
            return null;

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<long>(out var unixMs))
                return DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

            if (jsonValue.TryGetValue<double>(out var unixDouble))
                return DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(unixDouble));
        }

        return DateTimeOffset.TryParse(value.ToString(), out var timestamp)
            ? timestamp
            : null;
    }

    static string GetMessageRole(JsonNode? message)
        => (message?["info"]?["role"]?.ToString() ?? message?["role"]?.ToString() ?? message?["author"]?["role"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();

    static string ExtractMessageText(JsonNode? message)
    {
        foreach (var candidate in new[]
                 {
                     message?["content"], message?["text"], message?["message"], message?["body"], message?["output"], message?["response"], message?["parts"], message?["data"], message?["info"]?["content"], message?["info"]?["text"]
                 })
        {
            var text = ExtractTextFragment(candidate);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return string.Empty;
    }

    static DateTimeOffset? ExtractMessageCreatedAt(JsonNode? message)
    {
        foreach (var candidate in new[]
                 {
                     message?["info"]?["time"]?["created"], message?["time"]?["created"], message?["createdAt"], message?["timestamp"]
                 })
        {
            var timestamp = ParseTime(candidate);
            if (timestamp.HasValue)
                return timestamp;
        }

        return null;
    }

    static string ExtractTextFragment(JsonNode? value, int depth = 0)
    {
        if (depth > 5 || value is null)
            return string.Empty;

        return value switch
        {
            JsonValue jsonValue => jsonValue.ToString().Trim(),
            JsonArray jsonArray => string.Join("\n", jsonArray.Select(item => ExtractTextFragment(item, depth + 1)).Where(item => !string.IsNullOrWhiteSpace(item))).Trim(),
            _ => ExtractObjectTextFragment(value, depth)
        };
    }

    static string ExtractObjectTextFragment(JsonNode value, int depth)
    {
        foreach (var key in new[] { "text", "content", "message", "body", "value", "markdown" })
        {
            var text = ExtractTextFragment(value[key], depth + 1);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        if (value["parts"] is JsonArray parts)
        {
            var text = ExtractTextFragment(parts, depth + 1);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        if (string.Equals(value["type"]?.ToString(), "text", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(value["text"]?.ToString()))
            return value["text"]!.ToString().Trim();

        return string.Empty;
    }

    static string NormalizeTodoStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "pending";

        var compact = raw.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");

        return compact switch
        {
            "inprogress" or "in_progress" => "in_progress",
            "done" or "complete" or "completed" => "completed",
            "canceled" or "cancelled" => "cancelled",
            "pending" or "todo" or "idle" => "pending",
            _ => compact
        };
    }

    static string? NormalizePriority(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Trim().ToLowerInvariant();

        return normalized switch
        {
            "p0" or "0" or "urgent" or "p1" or "1" => "high",
            "p2" or "2" => "medium",
            "p3" or "3" => "low",
            "high" or "medium" or "low" => normalized,
            _ => normalized
        };
    }

    static void CollectSandboxDirectories(JsonNode? value, List<string> output)
    {
        if (value is null)
            return;

        switch (value)
        {
            case JsonValue scalar:
                {
                    var directory = NormalizeDirectory(scalar.ToString());
                    if (!string.IsNullOrWhiteSpace(directory))
                        output.Add(directory);
                    break;
                }
            case JsonArray array:
                foreach (var node in array)
                    CollectSandboxDirectories(node, output);
                break;
            default:
                {
                    var maybePath = value["directory"]?.ToString() ?? value["path"]?.ToString() ?? value["worktree"]?.ToString() ?? value["root"]?.ToString();
                    var directory = NormalizeDirectory(maybePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        output.Add(directory);
                    break;
                }
        }
    }

    static string? NormalizeDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();

        if (trimmed is "/" or "\\")
            return trimmed;

        if (trimmed.Length == 3 && char.IsLetter(trimmed[0]) && trimmed[1] == ':' && (trimmed[2] == '\\' || trimmed[2] == '/'))
            return trimmed;

        return trimmed.TrimEnd('/', '\\');
    }
}
