using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCode.Client;

public sealed class OpenCodeHttpClient
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    readonly HttpClient _httpClient;
    readonly OpenCodeClientOptions _options;

    public OpenCodeHttpClient(HttpClient httpClient, OpenCodeClientOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<Dictionary<string, SessionRuntimeStatus>> ReadWorkingStatusMapAsync(string directory, CancellationToken cancellationToken = default)
    {
        var responseText = await SendAsync(
            "/session/status",
            new OpenCodeRequest
            {
                Directory = directory
            },
            cancellationToken);

        return ParseWorkingStatusMap(responseText);
    }

    public async Task<List<OpenCodeTodo>> ReadTodosAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
    {
        var responseText = await SendAsync(
            $"/session/{Uri.EscapeDataString(sessionId)}/todo",
            new OpenCodeRequest
            {
                Directory = directory
            },
            cancellationToken);

        return ToArrayResponse<OpenCodeTodoTransport>(responseText)
            .Select(ParseTodo)
            .ToList();
    }

    public async Task<List<OpenCodeMessage>> ReadMessagesAsync(string sessionId, int? limit = null, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>();

        if (limit.HasValue)
            query["limit"] = limit.Value.ToString();

        var responseText = await SendAsync(
            $"/session/{Uri.EscapeDataString(sessionId)}/message",
            new OpenCodeRequest
            {
                Query = query
            },
            cancellationToken);

        return ToArrayResponse<OpenCodeMessageTransport>(responseText)
            .Select(ParseMessage)
            .ToList();
    }

    public async Task<List<OpenCodeProject>> ReadProjectsAsync(CancellationToken cancellationToken = default)
    {
        var responseText = await SendAsync("/project", new OpenCodeRequest(), cancellationToken);

        return ToArrayResponse<OpenCodeProjectTransport>(responseText)
            .Select(ParseProject)
            .OfType<OpenCodeProject>()
            .ToList();
    }

    public async Task<List<OpenCodeSession>> ReadSessionsAsync(string directory, int limit, CancellationToken cancellationToken = default)
    {
        var responseText = await SendAsync(
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

        return ToArrayResponse<OpenCodeSessionTransport>(responseText)
            .Select(session => ParseSession(session, directory))
            .OfType<OpenCodeSession>()
            .ToList();
    }

    public async Task<OpenCodeSession?> ReadSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken = default)
    {
        var responseText = await SendAsync(
            $"/session/{Uri.EscapeDataString(sessionId)}",
            new OpenCodeRequest
            {
                Directory = directory
            },
            cancellationToken);

        return ParseSession(Deserialize<OpenCodeSessionTransport>(responseText), directory);
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
                    JsonBody = JsonSerializer.Serialize(
                        new
                        {
                            time = new
                            {
                                archived = nowUnixMs
                            }
                        })
                },
                cancellationToken),
            () => SendAsync(
                $"/session/{Uri.EscapeDataString(sessionId)}",
                new OpenCodeRequest
                {
                    Method = "PATCH",
                    Directory = directory,
                    JsonBody = JsonSerializer.Serialize(
                        new
                        {
                            archived = true
                        })
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
        var responseText = await SendAsync(
            "/session",
            new OpenCodeRequest
            {
                Method = "POST",
                Directory = directory,
                JsonBody = JsonSerializer.Serialize(
                    new
                    {
                        title
                    })
            },
            cancellationToken);

        var sessionId = ParseCreatedSessionId(Deserialize<OpenCodeCreatedSessionTransport>(responseText));

        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("OpenCode did not return a session id");

        return sessionId;
    }

    public async Task SendPromptAsync(
        string directory,
        string sessionId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(
            $"/session/{Uri.EscapeDataString(sessionId)}/prompt_async",
            new OpenCodeRequest
            {
                Method = "POST",
                Directory = directory,
                JsonBody = JsonSerializer.Serialize(
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

    async Task<string?> SendAsync(string endpointPath, OpenCodeRequest request, CancellationToken cancellationToken)
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
            httpRequest.Content = new StringContent(request.JsonBody, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenCode request failed: {(int)response.StatusCode} {response.ReasonPhrase}; {text}");

        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    void ApplyAuthentication(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.Password))
            return;

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    static T? Deserialize<T>(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(value, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    static List<T> ToArrayResponse<T>(string? value)
    {
        var direct = Deserialize<List<T>>(value);

        if (direct is not null)
            return direct;

        var envelope = Deserialize<OpenCodeArrayEnvelope<T>>(value);

        return envelope?.Items ?? envelope?.Sessions ?? envelope?.Data ?? [];
    }

    static Dictionary<string, SessionRuntimeStatus> ParseWorkingStatusMap(string? value)
    {
        var map = new Dictionary<string, SessionRuntimeStatus>(StringComparer.Ordinal);
        var parsed = Deserialize<Dictionary<string, OpenCodeStatusPayload>>(value);

        if (parsed is null)
            return map;

        foreach (var kv in parsed)
        {
            var raw = kv.Value?.Type;

            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var status = SessionRuntimeStatus.FromRaw(raw);

            map[kv.Key] = status;
        }

        return map;
    }

    static string? ParseCreatedSessionId(OpenCodeCreatedSessionTransport? created)
    {
        var sessionId = created?.Id?.Trim();

        return string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
    }

    static OpenCodeTodo ParseTodo(OpenCodeTodoTransport? todo)
    {
        var content = todo?.Content ?? todo?.Text ?? todo?.Title ?? string.Empty;

        return new OpenCodeTodo(content, NormalizeTodoStatus(todo?.Status ?? todo?.State), NormalizePriority(todo?.Priority));
    }

    static OpenCodeMessage ParseMessage(OpenCodeMessageTransport? message) => new(GetMessageRole(message), ExtractMessageText(message), ExtractMessageCreatedAt(message));

    static OpenCodeProject? ParseProject(OpenCodeProjectTransport? project)
    {
        if (project is null)
            return null;

        var worktree = NormalizeDirectory(project.Worktree);
        var sandboxes = new List<string>();
        CollectSandboxDirectories(project.Sandboxes, sandboxes);

        return new OpenCodeProject(worktree, sandboxes);
    }

    static OpenCodeSession? ParseSession(OpenCodeSessionTransport? session, string? fallbackDirectory)
    {
        var source = session;

        if (source is null)
            return null;

        var sessionId = source.Id?.Trim();

        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var directory = NormalizeDirectory(source.Directory) ?? NormalizeDirectory(fallbackDirectory);
        var project = NormalizeDirectory(source.Project?.Worktree);
        var createdAt = ParseTime(source.Time?.Created);
        var updatedAt = ParseTime(source.Time?.Updated);
        var archivedAt = ParseTime(source.Time?.Archived);

        return new OpenCodeSession(
            sessionId,
            source.Title ?? source.Name,
            directory,
            project,
            createdAt,
            updatedAt,
            archivedAt);
    }

    static DateTimeOffset? ParseTime(JsonElement? value)
    {
        if (!value.HasValue)
            return null;

        var element = value.Value;

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt64(out var unixMs))
                return DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

            if (element.TryGetDouble(out var unixDouble))
                return DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(unixDouble));
        }

        var raw = element.ToString();

        if (long.TryParse(raw, out var unixMilliseconds))
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);

        return DateTimeOffset.TryParse(raw, out var timestamp)
            ? timestamp
            : null;
    }

    static string GetMessageRole(OpenCodeMessageTransport? message)
        => (message?.Info?.Role ?? message?.Role ?? message?.Author?.Role ?? string.Empty).Trim().ToLowerInvariant();

    static string ExtractMessageText(OpenCodeMessageTransport? message)
    {
        foreach (var candidate in new[]
        {
            message?.Content,
            message?.Text,
            message?.Message,
            message?.Body,
            message?.Output,
            message?.Response,
            message?.Parts,
            message?.Data,
            message?.Info?.Content,
            message?.Info?.Text
        })
        {
            var text = ExtractTextFragment(candidate);

            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return string.Empty;
    }

    static DateTimeOffset? ExtractMessageCreatedAt(OpenCodeMessageTransport? message)
    {
        foreach (var candidate in new[]
        {
            message?.Info?.Time?.Created,
            message?.Time?.Created,
            message?.CreatedAt,
            message?.Timestamp
        })
        {
            var timestamp = ParseTime(candidate);

            if (timestamp.HasValue)
                return timestamp;
        }

        return null;
    }

    static string ExtractTextFragment(JsonElement? value, int depth = 0)
    {
        if (depth > 5 ||
            !value.HasValue)
            return string.Empty;

        var element = value.Value;

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return string.Empty;

        return element.ValueKind switch
        {
            JsonValueKind.Array => string.Join("\n", element.EnumerateArray().Select(item => ExtractTextFragment(item, depth + 1)).Where(item => !string.IsNullOrWhiteSpace(item))).Trim(),
            JsonValueKind.Object => ExtractObjectTextFragment(element, depth),
            _ => element.ToString().Trim()
        };
    }

    static string ExtractObjectTextFragment(JsonElement value, int depth)
    {
        foreach (var key in new[]
        {
            "text",
            "content",
            "message",
            "body",
            "value",
            "markdown"
        })
        {
            if (!value.TryGetProperty(key, out var child))
                continue;

            var text = ExtractTextFragment(child, depth + 1);

            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        if (value.TryGetProperty("parts", out var parts) &&
            parts.ValueKind == JsonValueKind.Array)
        {
            var text = ExtractTextFragment(parts, depth + 1);

            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        if (value.TryGetProperty("type", out var type) &&
            string.Equals(type.ToString(), "text", StringComparison.OrdinalIgnoreCase) &&
            value.TryGetProperty("text", out var textValue) &&
            !string.IsNullOrWhiteSpace(textValue.ToString()))
            return textValue.ToString().Trim();

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

    static void CollectSandboxDirectories(JsonElement? value, List<string> output)
    {
        if (!value.HasValue)
            return;

        var element = value.Value;

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                {
                    var directory = NormalizeDirectory(element.ToString());

                    if (!string.IsNullOrWhiteSpace(directory))
                        output.Add(directory);

                    break;
                }
            case JsonValueKind.Array:
                foreach (var node in element.EnumerateArray())
                {
                    CollectSandboxDirectories(node, output);
                }

                break;
            case JsonValueKind.Object:
                {
                    var maybePath = GetStringProperty(element, "directory") ?? GetStringProperty(element, "path") ?? GetStringProperty(element, "worktree") ?? GetStringProperty(element, "root");
                    var directory = NormalizeDirectory(maybePath);

                    if (!string.IsNullOrWhiteSpace(directory))
                        output.Add(directory);

                    break;
                }
        }
    }

    static string? GetStringProperty(JsonElement value, string propertyName)
        => value.TryGetProperty(propertyName, out var property) ? property.ToString() : null;

    static string? NormalizeDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();

        if (trimmed is "/" or "\\")
            return trimmed;

        if (trimmed.Length == 3 &&
            char.IsLetter(trimmed[0]) &&
            trimmed[1] == ':' &&
            (trimmed[2] == '\\' || trimmed[2] == '/'))
            return trimmed;

        return trimmed.TrimEnd('/', '\\');
    }

    sealed class OpenCodeArrayEnvelope<T>
    {
        [JsonPropertyName("items")] public List<T>? Items { get; init; }

        [JsonPropertyName("sessions")] public List<T>? Sessions { get; init; }

        [JsonPropertyName("data")] public List<T>? Data { get; init; }
    }

    sealed class OpenCodeStatusPayload
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
    }

    sealed class OpenCodeTodoTransport
    {
        [JsonPropertyName("content")] public string? Content { get; init; }

        [JsonPropertyName("text")] public string? Text { get; init; }

        [JsonPropertyName("title")] public string? Title { get; init; }

        [JsonPropertyName("status")] public string? Status { get; init; }

        [JsonPropertyName("state")] public string? State { get; init; }

        [JsonPropertyName("priority")] public string? Priority { get; init; }
    }

    sealed class OpenCodeMessageTransport
    {
        [JsonPropertyName("info")] public OpenCodeMessageInfoTransport? Info { get; init; }

        [JsonPropertyName("role")] public string? Role { get; init; }

        [JsonPropertyName("author")] public OpenCodeMessageAuthorTransport? Author { get; init; }

        [JsonPropertyName("content")] public JsonElement? Content { get; init; }

        [JsonPropertyName("text")] public JsonElement? Text { get; init; }

        [JsonPropertyName("message")] public JsonElement? Message { get; init; }

        [JsonPropertyName("body")] public JsonElement? Body { get; init; }

        [JsonPropertyName("output")] public JsonElement? Output { get; init; }

        [JsonPropertyName("response")] public JsonElement? Response { get; init; }

        [JsonPropertyName("parts")] public JsonElement? Parts { get; init; }

        [JsonPropertyName("data")] public JsonElement? Data { get; init; }

        [JsonPropertyName("time")] public OpenCodeTimeTransport? Time { get; init; }

        [JsonPropertyName("createdAt")] public JsonElement? CreatedAt { get; init; }

        [JsonPropertyName("timestamp")] public JsonElement? Timestamp { get; init; }
    }

    sealed class OpenCodeMessageInfoTransport
    {
        [JsonPropertyName("role")] public string? Role { get; init; }

        [JsonPropertyName("time")] public OpenCodeTimeTransport? Time { get; init; }

        [JsonPropertyName("content")] public JsonElement? Content { get; init; }

        [JsonPropertyName("text")] public JsonElement? Text { get; init; }
    }

    sealed class OpenCodeMessageAuthorTransport
    {
        [JsonPropertyName("role")] public string? Role { get; init; }
    }

    sealed class OpenCodeProjectTransport
    {
        [JsonPropertyName("worktree")] public string? Worktree { get; init; }

        [JsonPropertyName("sandboxes")] public JsonElement? Sandboxes { get; init; }
    }

    sealed class OpenCodeSessionTransport
    {
        [JsonPropertyName("id")] public string? Id { get; init; }

        [JsonPropertyName("title")] public string? Title { get; init; }

        [JsonPropertyName("name")] public string? Name { get; init; }

        [JsonPropertyName("directory")] public string? Directory { get; init; }

        [JsonPropertyName("project")] public OpenCodeProjectTransport? Project { get; init; }

        [JsonPropertyName("time")] public OpenCodeTimeTransport? Time { get; init; }
    }

    sealed class OpenCodeTimeTransport
    {
        [JsonPropertyName("created")] public JsonElement? Created { get; init; }

        [JsonPropertyName("updated")] public JsonElement? Updated { get; init; }

        [JsonPropertyName("archived")] public JsonElement? Archived { get; init; }
    }

    sealed class OpenCodeCreatedSessionTransport
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
    }
}
