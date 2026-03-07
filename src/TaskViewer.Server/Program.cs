using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.FileProviders;
using TaskViewer.Server;

var defaultPort = 3456;
var defaultHost = "127.0.0.1";

var explicitPort = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var envPort) ? envPort : (int?)null;
var explicitHost = Environment.GetEnvironmentVariable("HOST") ?? defaultHost;

var opencodeUrl = Environment.GetEnvironmentVariable("OPENCODE_URL") ?? "http://localhost:4096";
var opencodeUsername = Environment.GetEnvironmentVariable("OPENCODE_USERNAME") ?? "opencode";
var opencodePassword = Environment.GetEnvironmentVariable("OPENCODE_PASSWORD") ?? string.Empty;
var sonarUrl = Environment.GetEnvironmentVariable("SONARQUBE_URL") ?? string.Empty;
var sonarToken = Environment.GetEnvironmentVariable("SONARQUBE_TOKEN") ?? string.Empty;

var orchestratorDbPath = Environment.GetEnvironmentVariable("ORCHESTRATOR_DB_PATH") ??
Path.Combine(
    AppContext.BaseDirectory,
    "..",
    "..",
    "..",
    "data",
    "orchestrator.sqlite");

var orchMaxActive = ParseIntSafe(Environment.GetEnvironmentVariable("ORCH_MAX_ACTIVE"), 3, 1);
var orchPollMs = ParseIntSafe(Environment.GetEnvironmentVariable("ORCH_POLL_MS"), 3000, 1000);
var orchMaxAttempts = ParseIntSafe(Environment.GetEnvironmentVariable("ORCH_MAX_ATTEMPTS"), 3, 1);
var orchMaxWorkingGlobal = ParseIntSafe(Environment.GetEnvironmentVariable("ORCH_MAX_WORKING_GLOBAL"), 5, 0);
var resumeFallback = orchMaxWorkingGlobal > 1 ? orchMaxWorkingGlobal - 1 : orchMaxWorkingGlobal;
var orchWorkingResumeBelow = ParseIntSafe(Environment.GetEnvironmentVariable("ORCH_WORKING_RESUME_BELOW"), resumeFallback, 0);

if (orchMaxWorkingGlobal > 0 &&
    orchWorkingResumeBelow >= orchMaxWorkingGlobal)
    orchWorkingResumeBelow = Math.Max(0, orchMaxWorkingGlobal - 1);

const int SessionsCacheTtlMs = 1500;
const int StatusCacheTtlMs = 1000;
const int TodoCacheTtlMs = 3000;
const int TasksAllCacheTtlMs = 1500;
const int RequestConcurrency = 6;
const int MaxAllSessions = 750;
const int ProjectsCacheTtlMs = 10_000;
const int DirectorySessionsCacheTtlMs = 8_000;
const int MaxSessionsPerProject = 500;
const int MessagePresenceCacheTtlMs = 120_000;
const int SessionRecentWindowMs = 5 * 60 * 1000;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{explicitHost}:{explicitPort ?? defaultPort}");

var app = builder.Build();

var rootDir = FindWorkspaceRoot();
var publicDir = Path.Combine(rootDir, "public");

var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(60)
};

var sseHub = new SseHub();

var sessionsCacheLock = new object();
var projectsCacheLock = new object();
var todoCacheLock = new object();

SessionCache sessionsCache = new();
ProjectsCache projectsCache = new();
var sessionsCacheByDirectory = new ConcurrentDictionary<string, TimestampedValue<List<JsonObject>>>(StringComparer.OrdinalIgnoreCase);
var statusCacheByDirectory = new ConcurrentDictionary<string, TimestampedValue<Dictionary<string, JsonObject>>>(StringComparer.OrdinalIgnoreCase);
var todoCache = new ConcurrentDictionary<string, TimestampedValue<List<JsonObject>>>(StringComparer.OrdinalIgnoreCase);
TimestampedValue<List<object>> tasksAllCache = new(DateTimeOffset.MinValue, []);

var assistantPresenceCache = new ConcurrentDictionary<string, TimestampedValue<bool>>(StringComparer.Ordinal);
var assistantPresenceInFlight = new ConcurrentDictionary<string, Task<bool?>>(StringComparer.Ordinal);
var statusOverride = new ConcurrentDictionary<string, (string Type, DateTimeOffset Ts)>(StringComparer.Ordinal);

void InvalidateAllCaches()
{
    lock (sessionsCacheLock)
        sessionsCache = new SessionCache();

    lock (projectsCacheLock)
        projectsCache = new ProjectsCache();

    sessionsCacheByDirectory.Clear();
    statusCacheByDirectory.Clear();
    todoCache.Clear();
    assistantPresenceCache.Clear();
    assistantPresenceInFlight.Clear();
    statusOverride.Clear();

    lock (todoCacheLock)
        tasksAllCache = new TimestampedValue<List<object>>(DateTimeOffset.MinValue, []);
}

void InvalidateTodos(string? directory, string sessionId)
{
    lock (todoCacheLock)
        tasksAllCache = new TimestampedValue<List<object>>(DateTimeOffset.MinValue, []);

    var key = $"{GetDirectoryCacheKey(directory)}::{sessionId}";
    todoCache.TryRemove(key, out _);
}

void NoteStatusOverride(string? directory, string sessionId, string type)
{
    var key = $"{GetDirectoryCacheKey(directory)}::{sessionId}";
    statusOverride[key] = (type, DateTimeOffset.UtcNow);
}

async Task<JsonNode?> OpenCodeFetch(string endpointPath, OpenCodeRequest req)
{
    var baseUri = new Uri(opencodeUrl);
    var url = new Uri(baseUri, endpointPath);

    var queryParts = new List<string>();

    foreach (var (k, v) in req.Query)
    {
        if (string.IsNullOrWhiteSpace(k) ||
            string.IsNullOrWhiteSpace(v))
            continue;

        queryParts.Add($"{Uri.EscapeDataString(k)}={Uri.EscapeDataString(v!)}");
    }

    if (!string.IsNullOrWhiteSpace(req.Directory))
        queryParts.Add($"directory={Uri.EscapeDataString(req.Directory)}");

    var uriBuilder = new UriBuilder(url);
    var existingQuery = uriBuilder.Query;
    existingQuery = existingQuery.TrimStart('?');

    if (!string.IsNullOrWhiteSpace(existingQuery))
        queryParts.Insert(0, existingQuery);

    uriBuilder.Query = string.Join("&", queryParts);

    using var request = new HttpRequestMessage(new HttpMethod(req.Method), uriBuilder.Uri);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    if (!string.IsNullOrWhiteSpace(opencodePassword))
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opencodeUsername}:{opencodePassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    if (!string.IsNullOrWhiteSpace(req.Directory))
        request.Headers.TryAddWithoutValidation("x-opencode-directory", req.Directory);

    if (req.JsonBody is not null)
        request.Content = new StringContent(req.JsonBody.ToJsonString(), Encoding.UTF8, "application/json");

    using var res = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    var text = await res.Content.ReadAsStringAsync();

    if (!res.IsSuccessStatusCode)
        throw new InvalidOperationException($"OpenCode request failed: {(int)res.StatusCode} {res.ReasonPhrase}; {text}");

    if (res.StatusCode == HttpStatusCode.NoContent)
        return null;

    var contentType = res.Content.Headers.ContentType?.MediaType ?? string.Empty;

    if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return JsonNode.Parse(text);
    }

    return JsonValue.Create(text);
}

var orchestrator = new SonarOrchestrator(
    new SonarOrchestratorOptions
    {
        SonarUrl = sonarUrl,
        SonarToken = sonarToken,
        DbPath = orchestratorDbPath,
        MaxActive = orchMaxActive,
        PollMs = orchPollMs,
        MaxAttempts = orchMaxAttempts,
        MaxWorkingGlobal = orchMaxWorkingGlobal,
        WorkingResumeBelow = orchWorkingResumeBelow,
        OpenCodeFetch = OpenCodeFetch,
        NormalizeDirectory = NormalizeDirectory,
        BuildOpenCodeSessionUrl = (sid, dir) => BuildOpenCodeSessionUrl(sid, dir),
        OnChange = () =>
        {
            InvalidateAllCaches();

            _ = sseHub.Broadcast(
                new
                {
                    type = "update"
                });
        }
    });

app.Lifetime.ApplicationStarted.Register(() =>
{
    var server = app.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
    var actual = addresses?.FirstOrDefault() ?? app.Urls.FirstOrDefault() ?? $"http://{explicitHost}:{explicitPort ?? defaultPort}";
    Console.WriteLine($"OpenCode Task Viewer running at {actual}");
    Console.WriteLine($"VIEWER_URL={actual}");
    Console.WriteLine($"Using OpenCode server: {opencodeUrl}");
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    _ = orchestrator.DisposeAsync();
});

orchestrator.Start(app.Lifetime.ApplicationStopping);
_ = Task.Run(() => StartUpstreamSse(app.Lifetime.ApplicationStopping));

if (Directory.Exists(publicDir))
{
    var fp = new PhysicalFileProvider(publicDir);

    app.UseDefaultFiles(
        new DefaultFilesOptions
        {
            FileProvider = fp
        });

    app.UseStaticFiles(
        new StaticFileOptions
        {
            FileProvider = fp
        });
}

app.MapGet(
    "/api/sessions",
    async (HttpContext ctx) =>
    {
        SetNoStore(ctx.Response);

        try
        {
            var limitParam = ctx.Request.Query["limit"].ToString();

            if (string.IsNullOrWhiteSpace(limitParam))
                limitParam = "20";

            var globalSessions = await ListGlobalSessions(limitParam);

            var directoriesByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var session in globalSessions)
            {
                var dir = GetSessionDirectory(session);
                var key = GetDirectoryCacheKey(dir);

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (!directoriesByKey.ContainsKey(key))
                    directoriesByKey[key] = dir!;
            }

            var statusByDir = new Dictionary<string, Dictionary<string, JsonObject>>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in directoriesByKey.Values)
            {
                var map = await GetStatusMapForDirectory(dir);
                statusByDir[GetDirectoryCacheKey(dir)] = map;
            }

            var summaries = new List<object>();
            var semaphore = new SemaphoreSlim(RequestConcurrency);

            var tasks = globalSessions.Select(async session =>
            {
                await semaphore.WaitAsync();

                try
                {
                    var sessionId = session["id"]?.ToString();

                    if (string.IsNullOrWhiteSpace(sessionId))
                        return;

                    var directory = GetSessionDirectory(session);
                    var statusMap = statusByDir.GetValueOrDefault(GetDirectoryCacheKey(directory)) ?? new Dictionary<string, JsonObject>(StringComparer.Ordinal);
                    var runtimeStatus = NormalizeRuntimeStatus(directory, sessionId, statusMap);
                    var createdAt = ParseTime(session["time"]?["created"]?.ToString());
                    var modifiedAt = ParseTime(session["time"]?["updated"]?.ToString()) ?? createdAt ?? DateTimeOffset.UtcNow.ToString("O");
                    var hasAssistantResponse = IsRuntimeRunning(runtimeStatus) ? null : await GetHasAssistantResponse(sessionId);
                    var status = DeriveSessionKanbanStatus(runtimeStatus, modifiedAt, hasAssistantResponse);

                    lock (summaries)
                    {
                        summaries.Add(
                            new
                            {
                                id = sessionId,
                                name = session["title"]?.ToString() ?? session["name"]?.ToString(),
                                project = GetProjectDisplayPath(session),
                                description = (string?)null,
                                gitBranch = (string?)null,
                                createdAt,
                                modifiedAt,
                                runtimeStatus = new
                                {
                                    type = runtimeStatus
                                },
                                status,
                                hasAssistantResponse,
                                openCodeUrl = BuildOpenCodeSessionUrl(sessionId, directory)
                            });
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            try
            {
                var queueItems = await orchestrator.ListQueue("queued,dispatching", 1000);
                var queueSummaries = queueItems.Select(MapQueueItemToSessionSummary).Where(x => x is not null).Cast<object>();
                summaries.AddRange(queueSummaries);
            }
            catch (Exception queueError)
            {
                Console.Error.WriteLine($"Error loading orchestrator queue for session list: {queueError}");
            }

            var sorted = summaries
                .OrderByDescending(s => ParseTime((string?)s.GetType().GetProperty("modifiedAt")?.GetValue(s) ?? string.Empty))
                .ToList();

            return Results.Json(sorted);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error listing sessions: {error}");

            return Results.Json(
                new
                {
                    error = "Failed to list sessions from OpenCode"
                },
                statusCode: 502);
        }
    });

app.MapGet(
    "/api/sessions/{sessionId}",
    async (string sessionId) =>
    {
        try
        {
            var info = await FindSessionInfo(sessionId);

            if (info is null)
                return Results.Json(
                    new
                    {
                        error = "Session not found"
                    },
                    statusCode: 404);

            var directory = GetSessionDirectory(info);
            var todos = await GetTodosForSession(sessionId, directory);
            var tasks = MapTodosToViewerTasks(todos);

            return Results.Json(tasks);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error getting session todos: {error}");

            return Results.Json(
                new
                {
                    error = "Failed to load session todos from OpenCode"
                },
                statusCode: 502);
        }
    });

app.MapGet(
    "/api/sessions/{sessionId}/last-assistant-message",
    async (string sessionId) =>
    {
        try
        {
            var info = await FindSessionInfo(sessionId);

            if (info is null)
                return Results.Json(
                    new
                    {
                        error = "Session not found"
                    },
                    statusCode: 404);

            var last = await GetLastAssistantMessage(sessionId);

            return Results.Json(
                new
                {
                    sessionId,
                    message = last?.Message,
                    createdAt = last?.CreatedAt
                });
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error getting last assistant message: {error}");

            return Results.Json(
                new
                {
                    error = "Failed to load session messages from OpenCode"
                },
                statusCode: 502);
        }
    });

app.MapPost(
    "/api/sessions/{sessionId}/archive",
    async (string sessionId) =>
    {
        try
        {
            var info = await FindSessionInfo(sessionId);

            if (info is null)
                return Results.Json(
                    new
                    {
                        error = "Session not found"
                    },
                    statusCode: 404);

            var directory = GetSessionDirectory(info);
            var archivedAt = await ArchiveSessionOnOpenCode(sessionId, directory);
            InvalidateAllCaches();

            await sseHub.Broadcast(
                new
                {
                    type = "update"
                });

            return Results.Json(
                new
                {
                    ok = true,
                    archivedAt
                });
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error archiving session: {error}");

            return Results.Json(
                new
                {
                    error = "Failed to archive session in OpenCode"
                },
                statusCode: 502);
        }
    });

app.MapGet("/api/orch/config", () => Results.Json(orchestrator.GetPublicConfig()));

app.MapGet(
    "/api/health",
    () => Results.Json(
        new
        {
            ok = true
        }));

app.MapGet(
    "/api/orch/mappings",
    async () =>
    {
        try
        {
            return Results.Json(await orchestrator.ListMappings());
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error listing orchestrator mappings: {error}");

            return Results.Json(
                new
                {
                    error = "Failed to list orchestration mappings"
                },
                statusCode: 502);
        }
    });

app.MapPost(
    "/api/orch/mappings",
    async (HttpContext ctx) =>
    {
        try
        {
            var body = await JsonNode.ParseAsync(ctx.Request.Body);
            var mapping = await orchestrator.UpsertMapping(body);

            return Results.Json(mapping);
        }
        catch (Exception error)
        {
            var msg = error.Message;
            var status = msg.Contains("Missing", StringComparison.OrdinalIgnoreCase) || msg.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
            Console.Error.WriteLine($"Error saving orchestrator mapping: {error}");

            return Results.Json(
                new
                {
                    error = msg
                },
                statusCode: status);
        }
    });

app.MapGet(
    "/api/orch/instructions",
    async (HttpContext ctx) =>
    {
        try
        {
            var mappingId = ctx.Request.Query["mappingId"].ToString();
            var issueType = ctx.Request.Query["issueType"].ToString();
            var profile = await orchestrator.GetInstructionProfile(mappingId, issueType);

            return Results.Json(
                new
                {
                    mappingId = int.TryParse(mappingId, out var id) ? id : (int?)null,
                    issueType = string.IsNullOrWhiteSpace(issueType) ? null : issueType.ToUpperInvariant(),
                    instructions = profile?["instructions"]?.ToString()
                });
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error loading instruction profile: {error}");

            return Results.Json(
                new
                {
                    error = "Failed to load instruction profile"
                },
                statusCode: 502);
        }
    });

app.MapPost(
    "/api/orch/instructions",
    async (HttpContext ctx) =>
    {
        try
        {
            var body = await JsonNode.ParseAsync(ctx.Request.Body);
            var profile = await orchestrator.UpsertInstructionProfile(body?["mappingId"]?.ToString(), body?["issueType"]?.ToString(), body?["instructions"]?.ToString());

            return Results.Json(
                new
                {
                    mappingId = profile["mapping_id"]?.GetValue<int>(),
                    issueType = profile["issue_type"]?.ToString(),
                    instructions = profile["instructions"]?.ToString(),
                    updatedAt = profile["updated_at"]?.ToString()
                });
        }
        catch (Exception error)
        {
            var msg = error.Message;
            var status = msg.Contains("Missing", StringComparison.OrdinalIgnoreCase) || msg.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
            Console.Error.WriteLine($"Error saving instruction profile: {error}");

            return Results.Json(
                new
                {
                    error = msg
                },
                statusCode: status);
        }
    });

app.MapGet(
    "/api/orch/issues",
    async (HttpContext ctx) =>
    {
        SetNoStore(ctx.Response);

        try
        {
            var mappingId = ctx.Request.Query["mappingId"].ToString();

            if (string.IsNullOrWhiteSpace(mappingId))
                return Results.Json(
                    new
                    {
                        error = "Missing mappingId"
                    },
                    statusCode: 400);

            var result = await orchestrator.ListIssues(
                mappingId,
                ctx.Request.Query["issueType"].ToString(),
                ctx.Request.Query["severity"].ToString(),
                ctx.Request.Query["issueStatus"].ToString(),
                ctx.Request.Query["page"].ToString(),
                ctx.Request.Query["pageSize"].ToString(),
                ctx.Request.Query["ruleKeys"].ToString() is { Length: > 0 } rk ? rk : ctx.Request.Query["rules"].ToString() is { Length: > 0 } rs ? rs : ctx.Request.Query["rule"].ToString());

            return Results.Json(result);
        }
        catch (Exception error)
        {
            var msg = error.Message;
            var status = msg.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
            Console.Error.WriteLine($"Error loading SonarQube issues: {error}");

            return Results.Json(
                new
                {
                    error = msg
                },
                statusCode: status);
        }
    });

app.MapGet(
    "/api/orch/rules",
    async (HttpContext ctx) =>
    {
        SetNoStore(ctx.Response);

        try
        {
            var mappingId = ctx.Request.Query["mappingId"].ToString();

            if (string.IsNullOrWhiteSpace(mappingId))
                return Results.Json(
                    new
                    {
                        error = "Missing mappingId"
                    },
                    statusCode: 400);

            var result = await orchestrator.ListRules(
                mappingId,
                ctx.Request.Query["issueType"].ToString(),
                ctx.Request.Query["issueStatus"].ToString());

            return Results.Json(result);
        }
        catch (Exception error)
        {
            var msg = error.Message;
            var status = msg.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
            Console.Error.WriteLine($"Error loading SonarQube rules: {error}");

            return Results.Json(
                new
                {
                    error = msg
                },
                statusCode: status);
        }
    });

app.MapPost(
    "/api/orch/enqueue",
    async (HttpContext ctx) =>
    {
        try
        {
            var body = await JsonNode.ParseAsync(ctx.Request.Body);
            var issues = body?["issues"] as JsonArray;

            var result = await orchestrator.EnqueueIssues(
                body?["mappingId"]?.ToString(),
                body?["issueType"]?.ToString(),
                body?["instructions"]?.ToString(),
                issues);

            return Results.Json(result);
        }
        catch (Exception error)
        {
            var msg = error.Message;

            var status = msg.Contains("Missing", StringComparison.OrdinalIgnoreCase) || msg.Contains("No issues", StringComparison.OrdinalIgnoreCase) || msg.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase)
                ? 400
                : 502;

            Console.Error.WriteLine($"Error enqueueing SonarQube issues: {error}");

            return Results.Json(
                new
                {
                    error = msg
                },
                statusCode: status);
        }
    });

app.MapPost(
    "/api/orch/enqueue-all",
    async (HttpContext ctx) =>
    {
        try
        {
            var body = await JsonNode.ParseAsync(ctx.Request.Body);
            var ruleKeys = body?["ruleKeys"] ?? body?["rules"] ?? body?["rule"];

            var result = await orchestrator.EnqueueAllMatching(
                body?["mappingId"]?.ToString(),
                body?["issueType"]?.ToString(),
                ruleKeys,
                body?["issueStatus"]?.ToString(),
                body?["severity"]?.ToString(),
                body?["instructions"]?.ToString());

            return Results.Json(result);
        }
        catch (Exception error)
        {
            var msg = error.Message;

            var status = msg.Contains("required", StringComparison.OrdinalIgnoreCase) || msg.Contains("Missing", StringComparison.OrdinalIgnoreCase) || msg.Contains("Mapping not found", StringComparison.OrdinalIgnoreCase)
                ? 400
                : 502;

            Console.Error.WriteLine($"Error enqueueing all matching SonarQube issues: {error}");

            return Results.Json(
                new
                {
                    error = msg
                },
                statusCode: status);
        }
    });

app.MapGet(
    "/api/orch/queue",
    async (HttpContext ctx) =>
    {
        SetNoStore(ctx.Response);

        try
        {
            var items = await orchestrator.ListQueue(ctx.Request.Query["states"].ToString(), ctx.Request.Query["limit"].ToString());
            var stats = await orchestrator.GetQueueStats();
            var worker = await orchestrator.GetWorkerState();

            return Results.Json(
                new
                {
                    items,
                    stats,
                    worker
                });
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error loading queue items: {error}");

            return Results.Json(
                new
                {
                    error = "Failed to load orchestration queue"
                },
                statusCode: 502);
        }
    });

app.MapPost(
    "/api/orch/queue/{queueId}/cancel",
    async (string queueId) =>
    {
        try
        {
            var ok = await orchestrator.CancelQueueItem(queueId);

            if (!ok)
                return Results.Json(
                    new
                    {
                        error = "Queue item not found or already terminal"
                    },
                    statusCode: 404);

            return Results.Json(
                new
                {
                    ok = true
                });
        }
        catch (Exception error)
        {
            var msg = error.Message;
            var status = msg.Contains("Invalid queue id", StringComparison.OrdinalIgnoreCase) ? 400 : 502;
            Console.Error.WriteLine($"Error cancelling queue item: {error}");

            return Results.Json(
                new
                {
                    error = msg
                },
                statusCode: status);
        }
    });

app.MapPost(
    "/api/orch/queue/retry-failed",
    async () =>
    {
        try
        {
            var retried = await orchestrator.RetryFailed();

            return Results.Json(
                new
                {
                    retried
                });
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error retrying failed queue items: {error}");

            return Results.Json(
                new
                {
                    error = "Failed to retry failed queue items"
                },
                statusCode: 502);
        }
    });

app.MapPost(
    "/api/orch/queue/clear",
    async () =>
    {
        try
        {
            var cleared = await orchestrator.ClearQueued();

            return Results.Json(
                new
                {
                    cleared
                });
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error clearing queued items: {error}");

            return Results.Json(
                new
                {
                    error = "Failed to clear queued items"
                },
                statusCode: 502);
        }
    });

app.MapGet(
    "/api/tasks/all",
    async (HttpContext ctx) =>
    {
        SetNoStore(ctx.Response);

        try
        {
            var now = DateTimeOffset.UtcNow;

            lock (todoCacheLock)
            {
                if (tasksAllCache.Value.Count > 0 &&
                    (now - tasksAllCache.Timestamp).TotalMilliseconds < TasksAllCacheTtlMs)
                    return Results.Json(tasksAllCache.Value);
            }

            List<JsonObject> sessions;

            lock (sessionsCacheLock)
            {
                sessions = sessionsCache.Data.Count > 0
                    ? [.. sessionsCache.Data]
                    : [];
            }

            if (sessions.Count == 0)
                sessions = await ListGlobalSessions("all");

            var directoriesByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var session in sessions)
            {
                var dir = GetSessionDirectory(session);
                var key = GetDirectoryCacheKey(dir);

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (!directoriesByKey.ContainsKey(key))
                    directoriesByKey[key] = dir!;
            }

            var statusByDir = new Dictionary<string, Dictionary<string, JsonObject>>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in directoriesByKey.Values)
            {
                statusByDir[GetDirectoryCacheKey(dir)] = await GetStatusMapForDirectory(dir);
            }

            var allTasks = new List<object>();

            foreach (var session in sessions)
            {
                var sessionId = session["id"]?.ToString();

                if (string.IsNullOrWhiteSpace(sessionId))
                    continue;

                var directory = GetSessionDirectory(session);
                var statusMap = statusByDir.GetValueOrDefault(GetDirectoryCacheKey(directory)) ?? new Dictionary<string, JsonObject>(StringComparer.Ordinal);
                var runtimeStatus = NormalizeRuntimeStatus(directory, sessionId, statusMap);

                List<JsonObject> todos;

                try
                {
                    todos = await GetTodosForSession(sessionId, directory);
                }
                catch
                {
                    todos = [];
                }

                var inferred = InferInProgressTodoFromRuntime(todos, runtimeStatus);

                for (var i = 0; i < inferred.Count; i++)
                {
                    var todo = inferred[i];

                    allTasks.Add(
                        new
                        {
                            id = (i + 1).ToString(CultureInfo.InvariantCulture),
                            subject = todo["content"]?.ToString() ?? string.Empty,
                            status = todo["status"]?.ToString() ?? "pending",
                            priority = todo["priority"]?.ToString(),
                            sessionId,
                            sessionName = session["title"]?.ToString() ?? session["name"]?.ToString(),
                            project = GetProjectDisplayPath(session)
                        });
                }
            }

            lock (todoCacheLock)
                tasksAllCache = new TimestampedValue<List<object>>(now, allTasks);

            return Results.Json(allTasks);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error getting all tasks: {error}");

            return Results.Json(
                new
                {
                    error = "Failed to load tasks from OpenCode"
                },
                statusCode: 502);
        }
    });

app.MapPost(
    "/api/tasks/{sessionId}/{taskId}/note",
    () => Results.Json(
        new
        {
            error = "Not implemented for OpenCode todos"
        },
        statusCode: 501));

app.MapDelete(
    "/api/tasks/{sessionId}/{taskId}",
    () => Results.Json(
        new
        {
            error = "Not implemented for OpenCode todos"
        },
        statusCode: 501));

app.MapGet(
    "/api/events",
    async ctx =>
    {
        ctx.Response.Headers.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        var client = sseHub.AddClient(ctx.Response, ctx.RequestAborted);

        await client.Send(
            new
            {
                type = "connected"
            });

        await client.Completion;
    });

app.MapFallback(async ctx =>
{
    if (!Directory.Exists(publicDir))
    {
        ctx.Response.StatusCode = 404;

        return;
    }

    var indexPath = Path.Combine(publicDir, "index.html");

    if (!File.Exists(indexPath))
    {
        ctx.Response.StatusCode = 404;

        return;
    }

    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.SendFileAsync(indexPath);
});

await app.RunAsync();

void SetNoStore(HttpResponse response)
{
    response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
    response.Headers.Pragma = "no-cache";
    response.Headers.Expires = "0";
}

int ParseIntSafe(string? value, int fallback, int? min = null)
{
    if (!int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var n))
        return fallback;

    if (min.HasValue &&
        n < min.Value)
        return min.Value;

    return n;
}

string FindWorkspaceRoot()
{
    var candidates = new List<string>
    {
        Directory.GetCurrentDirectory(),
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..")),
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                ".."))
    };

    foreach (var start in candidates)
    {
        var current = start;

        for (var i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(current, "TaskViewer.slnx")))
                return current;

            var parent = Directory.GetParent(current);

            if (parent is null)
                break;

            current = parent.FullName;
        }
    }

    return Directory.GetCurrentDirectory();
}

string? ParseTime(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;

    return DateTimeOffset.TryParse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal,
        out var dt)
        ? dt.ToUniversalTime().ToString("O")
        : null;
}

string? NormalizeDirectory(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;

    var s = value.Trim();

    if (s is "/" or "\\")
        return s;

    if (Regex.IsMatch(s, "^[a-zA-Z]:[\\\\/]$"))
        return s;

    return s.TrimEnd('/', '\\');
}

string? ToForwardSlashDirectory(string? value)
{
    var dir = NormalizeDirectory(value);

    return dir?.Replace('\\', '/');
}

string GetDirectoryCacheKey(string? value) => ToForwardSlashDirectory(value) ?? string.Empty;

List<string> GetDirectoryVariants(string? value)
{
    var dir = NormalizeDirectory(value);

    if (string.IsNullOrWhiteSpace(dir))
        return [];

    var variants = new List<string>
    {
        dir
    };

    var forward = dir.Replace('\\', '/');
    var backward = dir.Replace('/', '\\');

    if (!variants.Contains(forward, StringComparer.Ordinal))
        variants.Add(forward);

    if (!variants.Contains(backward, StringComparer.Ordinal))
        variants.Add(backward);

    return variants;
}

string? GetSessionDirectory(JsonObject? session)
{
    return session?["directory"]?.ToString() ?? session?["project"]?["worktree"]?.ToString();
}

string? GetProjectDisplayPath(JsonObject? session)
{
    return session?["project"]?["worktree"]?.ToString() ?? session?["projectWorktree"]?.ToString() ?? session?["directory"]?.ToString();
}

string? BuildOpenCodeSessionUrl(string sessionId, string? directory)
{
    var sid = (sessionId ?? string.Empty).Trim();

    if (string.IsNullOrWhiteSpace(sid))
        return null;

    var baseUrl = opencodeUrl.TrimEnd('/');

    if (string.IsNullOrWhiteSpace(baseUrl))
        return null;

    var dir = NormalizeDirectory(directory);

    if (string.IsNullOrWhiteSpace(dir))
        return $"{baseUrl}/session/{Uri.EscapeDataString(sid)}";

    var bytes = Encoding.UTF8.GetBytes(dir);
    var slug = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    return $"{baseUrl}/{slug}/session/{Uri.EscapeDataString(sid)}";
}

List<JsonObject> ToArrayResponse(JsonNode? value)
{
    if (value is JsonArray arr)
        return arr.OfType<JsonObject>().ToList();

    if (value is JsonObject obj)
    {
        if (obj["items"] is JsonArray items)
            return items.OfType<JsonObject>().ToList();

        if (obj["sessions"] is JsonArray sessions)
            return sessions.OfType<JsonObject>().ToList();

        if (obj["data"] is JsonArray data)
            return data.OfType<JsonObject>().ToList();
    }

    return [];
}

void CollectSandboxDirectoryCandidates(JsonNode? value, List<string> output)
{
    if (value is null)
        return;

    switch (value)
    {
        case JsonValue v:
            {
                var dir = NormalizeDirectory(v.ToString());

                if (!string.IsNullOrWhiteSpace(dir))
                    output.Add(dir);

                break;
            }
        case JsonArray arr:
            foreach (var n in arr)
            {
                CollectSandboxDirectoryCandidates(n, output);
            }

            break;
        case JsonObject obj:
            {
                var maybePath = obj["directory"]?.ToString() ?? obj["path"]?.ToString() ?? obj["worktree"]?.ToString() ?? obj["root"]?.ToString();
                var dir = NormalizeDirectory(maybePath);

                if (!string.IsNullOrWhiteSpace(dir))
                    output.Add(dir);

                break;
            }
    }
}

(string? Worktree, List<string> Directories) GetProjectSearchDirectories(JsonObject? project)
{
    var candidates = new List<string>();
    var worktree = NormalizeDirectory(project?["worktree"]?.ToString());

    if (!string.IsNullOrWhiteSpace(worktree) &&
        worktree is not "/" and not "\\")
        candidates.Add(worktree);

    CollectSandboxDirectoryCandidates(project?["sandboxes"], candidates);

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var deduped = new List<string>();

    foreach (var c in candidates)
    {
        if (string.IsNullOrWhiteSpace(c) ||
            c is "/" or "\\")
            continue;

        var key = GetDirectoryCacheKey(c);

        if (string.IsNullOrWhiteSpace(key) ||
            !seen.Add(key))
            continue;

        deduped.Add(c);
    }

    return (worktree is "/" or "\\" ? null : worktree, deduped);
}

async Task<List<JsonObject>> ListProjects()
{
    var now = DateTimeOffset.UtcNow;

    lock (projectsCacheLock)
    {
        if (projectsCache.Data.Count > 0 &&
            (now - projectsCache.Timestamp).TotalMilliseconds < ProjectsCacheTtlMs)
            return [.. projectsCache.Data];
    }

    var data = await OpenCodeFetch("/project", new OpenCodeRequest());
    var projects = ToArrayResponse(data);

    lock (projectsCacheLock)
    {
        projectsCache = new ProjectsCache
        {
            Timestamp = now,
            Data = projects
        };
    }

    return projects;
}

async Task<List<JsonObject>> ListSessionsForDirectory(string directory, string? projectWorktree, int limit)
{
    var dir = NormalizeDirectory(directory);

    if (string.IsNullOrWhiteSpace(dir))
        return [];

    var dirKey = GetDirectoryCacheKey(dir);

    if (string.IsNullOrWhiteSpace(dirKey))
        return [];

    if (sessionsCacheByDirectory.TryGetValue(dirKey, out var cached) &&
        (DateTimeOffset.UtcNow - cached.Timestamp).TotalMilliseconds < DirectorySessionsCacheTtlMs)
        return [.. cached.Value];

    var perRequestLimit = Math.Clamp(limit, 1, MaxSessionsPerProject);
    var mergedById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
    var hadSuccess = false;
    Exception? lastError = null;

    foreach (var candidateDir in GetDirectoryVariants(dir))
    {
        try
        {
            var data = await OpenCodeFetch(
                "/session",
                new OpenCodeRequest
                {
                    Query = new Dictionary<string, string?>
                    {
                        ["roots"] = "true",
                        ["limit"] = perRequestLimit.ToString(CultureInfo.InvariantCulture)
                    },
                    Directory = candidateDir
                });

            hadSuccess = true;

            var list = ToArrayResponse(data)
                .Where(s => s["id"] is not null)
                .Where(s => s["time"]?["archived"] is null)
                .Select(s =>
                {
                    var obj = (JsonObject)s.DeepClone();
                    obj["directory"] = NormalizeDirectory(s["directory"]?.ToString()) ?? candidateDir;

                    if (!string.IsNullOrWhiteSpace(projectWorktree))
                    {
                        obj["projectWorktree"] = projectWorktree;

                        obj["project"] = new JsonObject
                        {
                            ["worktree"] = projectWorktree
                        };
                    }

                    return obj;
                })
                .ToList();

            foreach (var session in list)
            {
                var sid = session["id"]?.ToString();

                if (string.IsNullOrWhiteSpace(sid))
                    continue;

                if (!mergedById.ContainsKey(sid))
                    mergedById[sid] = session;
            }
        }
        catch (Exception error)
        {
            lastError = error;
        }
    }

    if (!hadSuccess &&
        lastError is not null)
        throw lastError;

    var sessions = mergedById.Values.ToList();
    sessionsCacheByDirectory[dirKey] = new TimestampedValue<List<JsonObject>>(DateTimeOffset.UtcNow, sessions);

    return sessions;
}

async Task<List<JsonObject>> ListGlobalSessions(string limitParam)
{
    var now = DateTimeOffset.UtcNow;

    lock (sessionsCacheLock)
    {
        if (sessionsCache.Data.Count > 0 &&
            (now - sessionsCache.Timestamp).TotalMilliseconds < SessionsCacheTtlMs)
            return [.. sessionsCache.Data];
    }

    var limit = string.Equals(limitParam, "all", StringComparison.OrdinalIgnoreCase)
        ? MaxAllSessions
        : Math.Clamp(ParseIntSafe(limitParam, 20), 1, MaxAllSessions);

    var projects = await ListProjects();
    var projectSearchEntries = new List<(string Directory, string ProjectWorktree)>();
    var seenDirectoryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var project in projects)
    {
        var info = GetProjectSearchDirectories(project);

        foreach (var directory in info.Directories)
        {
            var key = GetDirectoryCacheKey(directory);

            if (string.IsNullOrWhiteSpace(key) ||
                !seenDirectoryKeys.Add(key))
                continue;

            projectSearchEntries.Add((directory, info.Worktree ?? directory));
        }
    }

    var perDirLimit = string.Equals(limitParam, "all", StringComparison.OrdinalIgnoreCase)
        ? MaxSessionsPerProject
        : Math.Clamp(limit * 8, 120, MaxSessionsPerProject);

    var sessions = new List<JsonObject>();

    foreach (var entry in projectSearchEntries)
    {
        var listed = await ListSessionsForDirectory(entry.Directory, entry.ProjectWorktree, perDirLimit);
        sessions.AddRange(listed);
    }

    sessions = sessions
        .OrderByDescending(s => DateTimeOffset.TryParse(s["time"]?["updated"]?.ToString() ?? s["time"]?["created"]?.ToString(), out var dt) ? dt : DateTimeOffset.MinValue)
        .ToList();

    if (!string.Equals(limitParam, "all", StringComparison.OrdinalIgnoreCase))
        sessions = sessions.Take(limit).ToList();

    lock (sessionsCacheLock)
    {
        sessionsCache = new SessionCache
        {
            Timestamp = now,
            Data = sessions,
            ById = sessions.Where(s => s["id"] is not null).ToDictionary(s => s["id"]!.ToString(), s => s, StringComparer.Ordinal)
        };
    }

    return sessions;
}

async Task<Dictionary<string, JsonObject>> GetStatusMapForDirectory(string? directory)
{
    var dir = NormalizeDirectory(directory);

    if (string.IsNullOrWhiteSpace(dir))
        return new Dictionary<string, JsonObject>(StringComparer.Ordinal);

    var dirKey = GetDirectoryCacheKey(dir);

    if (string.IsNullOrWhiteSpace(dirKey))
        return new Dictionary<string, JsonObject>(StringComparer.Ordinal);

    if (statusCacheByDirectory.TryGetValue(dirKey, out var cached) &&
        (DateTimeOffset.UtcNow - cached.Timestamp).TotalMilliseconds < StatusCacheTtlMs)
        return new Dictionary<string, JsonObject>(cached.Value, StringComparer.Ordinal);

    Dictionary<string, JsonObject> statusMap = new(StringComparer.Ordinal);

    foreach (var candidateDir in GetDirectoryVariants(dir))
    {
        try
        {
            var result = await OpenCodeFetch(
                "/session/status",
                new OpenCodeRequest
                {
                    Directory = candidateDir
                });

            var normalized = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

            if (result is JsonObject obj)
            {
                foreach (var kv in obj)
                {
                    if (kv.Value is JsonObject sObj)
                        normalized[kv.Key] = sObj;
                    else
                        normalized[kv.Key] = new JsonObject
                        {
                            ["type"] = kv.Value?.ToString()
                        };
                }
            }

            if (normalized.Count > 0)
            {
                statusMap = normalized;

                break;
            }

            if (statusMap.Count == 0)
                statusMap = normalized;
        }
        catch
        {
        }
    }

    statusCacheByDirectory[dirKey] = new TimestampedValue<Dictionary<string, JsonObject>>(DateTimeOffset.UtcNow, statusMap);

    return statusMap;
}

string NormalizeTodoStatus(string? raw)
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

string? NormalizeTodoPriority(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
        return null;

    var s = raw.Trim().ToLowerInvariant();

    return s switch
    {
        "p0" or "0" or "urgent" or "p1" or "1" => "high",
        "p2" or "2" => "medium",
        "p3" or "3" => "low",
        "high" or "medium" or "low" => s,
        _ => s
    };
}

JsonObject NormalizeTodo(JsonObject? todo)
{
    var t = todo ?? new JsonObject();
    var content = t["content"]?.ToString() ?? t["text"]?.ToString() ?? t["title"]?.ToString() ?? string.Empty;

    return new JsonObject
    {
        ["content"] = content,
        ["status"] = NormalizeTodoStatus(t["status"]?.ToString() ?? t["state"]?.ToString()),
        ["priority"] = NormalizeTodoPriority(t["priority"]?.ToString())
    };
}

async Task<List<JsonObject>> GetTodosForSession(string sessionId, string? directory)
{
    var cacheKey = $"{GetDirectoryCacheKey(directory)}::{sessionId}";

    if (todoCache.TryGetValue(cacheKey, out var cached) &&
        (DateTimeOffset.UtcNow - cached.Timestamp).TotalMilliseconds < TodoCacheTtlMs)
        return [.. cached.Value];

    var data = await OpenCodeFetch(
        $"/session/{Uri.EscapeDataString(sessionId)}/todo",
        new OpenCodeRequest
        {
            Directory = directory
        });

    var rawTodos = data switch
    {
        JsonArray arr => arr.OfType<JsonObject>().ToList(),
        JsonObject obj when obj["todos"] is JsonArray todos => todos.OfType<JsonObject>().ToList(),
        JsonObject obj when obj["items"] is JsonArray items => items.OfType<JsonObject>().ToList(),
        _ => []
    };

    var normalized = rawTodos.Select(NormalizeTodo).ToList();
    todoCache[cacheKey] = new TimestampedValue<List<JsonObject>>(DateTimeOffset.UtcNow, normalized);

    return normalized;
}

string NormalizeRuntimeStatus(string? directory, string sessionId, Dictionary<string, JsonObject> statusMap)
{
    var key = $"{GetDirectoryCacheKey(directory)}::{sessionId}";

    if (statusOverride.TryGetValue(key, out var over) &&
        (DateTimeOffset.UtcNow - over.Ts).TotalMilliseconds < 60_000)
        return string.IsNullOrWhiteSpace(over.Type) ? "idle" : over.Type;

    if (statusMap.TryGetValue(sessionId, out var s))
    {
        var type = s["type"]?.ToString();

        if (!string.IsNullOrWhiteSpace(type))
            return type;
    }

    return "idle";
}

bool IsRuntimeRunning(string? type)
{
    var t = (type ?? string.Empty).Trim().ToLowerInvariant();

    return t is "busy" or "retry" or "running";
}

string DeriveSessionKanbanStatus(string runtimeType, string modifiedAt, bool? hasAssistantResponse)
{
    if (IsRuntimeRunning(runtimeType))
        return "in_progress";

    if (hasAssistantResponse == true)
        return "completed";

    if (hasAssistantResponse == false)
        return "pending";

    if (!DateTimeOffset.TryParse(modifiedAt, out var ts))
        return "pending";

    var age = DateTimeOffset.UtcNow - ts;

    return age.TotalMilliseconds <= SessionRecentWindowMs ? "pending" : "completed";
}

async Task<JsonObject?> FindSessionInfo(string sessionId)
{
    lock (sessionsCacheLock)
        if (sessionsCache.ById.TryGetValue(sessionId, out var info))
            return info;

    try
    {
        await ListGlobalSessions("200");

        lock (sessionsCacheLock)
            if (sessionsCache.ById.TryGetValue(sessionId, out var info))
                return info;
    }
    catch
    {
    }

    lock (sessionsCacheLock)
        return sessionsCache.ById.GetValueOrDefault(sessionId);
}

async Task<string?> ArchiveSessionOnOpenCode(string sessionId, string? directory)
{
    var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    var attempts = new Func<Task>[]
    {
        () => OpenCodeFetch(
            $"/session/{Uri.EscapeDataString(sessionId)}",
            new OpenCodeRequest
            {
                Method = "PATCH",
                Directory = directory,
                JsonBody = new JsonObject
                {
                    ["time"] = new JsonObject
                    {
                        ["archived"] = nowUnixMs
                    }
                }
            }),
        () => OpenCodeFetch(
            $"/session/{Uri.EscapeDataString(sessionId)}",
            new OpenCodeRequest
            {
                Method = "PATCH",
                Directory = directory,
                JsonBody = new JsonObject
                {
                    ["archived"] = true
                }
            }),
        () => OpenCodeFetch(
            $"/session/{Uri.EscapeDataString(sessionId)}/archive",
            new OpenCodeRequest
            {
                Method = "POST",
                Directory = directory
            })
    };

    Exception? lastError = null;

    foreach (var attempt in attempts)
    {
        try
        {
            await attempt();

            var updated = await OpenCodeFetch(
                $"/session/{Uri.EscapeDataString(sessionId)}",
                new OpenCodeRequest
                {
                    Directory = directory
                });

            var archivedAt = updated?["time"]?["archived"]?.ToString();

            if (!string.IsNullOrWhiteSpace(archivedAt))
                return archivedAt;

            lastError = new InvalidOperationException("Archive request succeeded but session did not report archived time");
        }
        catch (Exception ex)
        {
            lastError = ex;
        }
    }

    throw lastError ?? new InvalidOperationException("Failed to archive session");
}

string GetMessageRole(JsonObject? message)
{
    return (message?["info"]?["role"]?.ToString() ?? message?["role"]?.ToString() ?? message?["author"]?["role"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
}

string ExtractTextFragment(JsonNode? value, int depth = 0)
{
    if (depth > 5 ||
        value is null)
        return string.Empty;

    switch (value)
    {
        case JsonValue v: return v.ToString().Trim();
        case JsonArray arr: return string.Join("\n", arr.Select(x => ExtractTextFragment(x, depth + 1)).Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        case JsonObject obj:
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
                    var outText = ExtractTextFragment(obj[key], depth + 1);

                    if (!string.IsNullOrWhiteSpace(outText))
                        return outText;
                }

                if (obj["parts"] is JsonArray parts)
                {
                    var outText = ExtractTextFragment(parts, depth + 1);

                    if (!string.IsNullOrWhiteSpace(outText))
                        return outText;
                }

                if (string.Equals(obj["type"]?.ToString(), "text", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(obj["text"]?.ToString()))
                    return obj["text"]!.ToString().Trim();

                return string.Empty;
            }
        default: return string.Empty;
    }
}

string ExtractAssistantMessageText(JsonObject? message)
{
    var candidates = new[]
    {
        message?["content"],
        message?["text"],
        message?["message"],
        message?["body"],
        message?["output"],
        message?["response"],
        message?["parts"],
        message?["data"],
        message?["info"]?["content"],
        message?["info"]?["text"]
    };

    foreach (var c in candidates)
    {
        var txt = ExtractTextFragment(c);

        if (!string.IsNullOrWhiteSpace(txt))
            return txt;
    }

    return string.Empty;
}

string? ExtractMessageCreatedAt(JsonObject? message)
{
    var candidates = new[]
    {
        message?["info"]?["time"]?["created"]?.ToString(),
        message?["time"]?["created"]?.ToString(),
        message?["createdAt"]?.ToString(),
        message?["timestamp"]?.ToString()
    };

    foreach (var c in candidates)
    {
        var t = ParseTime(c);

        if (!string.IsNullOrWhiteSpace(t))
            return t;
    }

    return null;
}

LastAssistantMessage? FindLastAssistantMessage(List<JsonObject> messages)
{
    for (var i = messages.Count - 1; i >= 0; i--)
    {
        var m = messages[i];

        if (GetMessageRole(m) != "assistant")
            continue;

        return new LastAssistantMessage(ExtractAssistantMessageText(m), ExtractMessageCreatedAt(m));
    }

    return null;
}

async Task<LastAssistantMessage?> GetLastAssistantMessage(string sessionId)
{
    var tail = await OpenCodeFetch(
        $"/session/{Uri.EscapeDataString(sessionId)}/message",
        new OpenCodeRequest
        {
            Query = new Dictionary<string, string?>
            {
                ["limit"] = "400"
            }
        });

    var tailArr = (tail as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
    var match = FindLastAssistantMessage(tailArr);

    if (match is not null)
        return match;

    if (tailArr.Count < 400)
        return null;

    var all = await OpenCodeFetch($"/session/{Uri.EscapeDataString(sessionId)}/message", new OpenCodeRequest());
    var allArr = (all as JsonArray)?.OfType<JsonObject>().ToList() ?? [];

    return FindLastAssistantMessage(allArr);
}

async Task<bool?> GetHasAssistantResponse(string sessionId)
{
    if (assistantPresenceCache.TryGetValue(sessionId, out var cached) &&
        (DateTimeOffset.UtcNow - cached.Timestamp).TotalMilliseconds < MessagePresenceCacheTtlMs)
        return cached.Value;

    var task = assistantPresenceInFlight.GetOrAdd(
        sessionId,
        async _ =>
        {
            try
            {
                var tail = await OpenCodeFetch(
                    $"/session/{Uri.EscapeDataString(sessionId)}/message",
                    new OpenCodeRequest
                    {
                        Query = new Dictionary<string, string?>
                        {
                            ["limit"] = "200"
                        }
                    });

                var tailArr = (tail as JsonArray)?.OfType<JsonObject>().ToList() ?? [];

                if (tailArr.Any(m => string.Equals(m["info"]?["role"]?.ToString(), "assistant", StringComparison.OrdinalIgnoreCase)))
                    return true;

                if (tailArr.Count < 200)
                    return false;

                var all = await OpenCodeFetch($"/session/{Uri.EscapeDataString(sessionId)}/message", new OpenCodeRequest());
                var allArr = (all as JsonArray)?.OfType<JsonObject>().ToList() ?? [];

                return allArr.Any(m => string.Equals(m["info"]?["role"]?.ToString(), "assistant", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        });

    var result = await task;
    assistantPresenceInFlight.TryRemove(sessionId, out _);

    if (result.HasValue)
        assistantPresenceCache[sessionId] = new TimestampedValue<bool>(DateTimeOffset.UtcNow, result.Value);

    return result;
}

List<JsonObject> InferInProgressTodoFromRuntime(List<JsonObject> todos, string runtimeType)
{
    if (!IsRuntimeRunning(runtimeType))
        return todos;

    if (todos.Any(t => string.Equals(t["status"]?.ToString(), "in_progress", StringComparison.Ordinal)))
        return todos;

    var idx = todos.FindIndex(t => string.Equals(t["status"]?.ToString(), "pending", StringComparison.Ordinal));

    if (idx < 0)
        return todos;

    var copy = todos.Select(t => (JsonObject)t.DeepClone()).ToList();
    copy[idx]["status"] = "in_progress";

    return copy;
}

List<object> MapTodosToViewerTasks(List<JsonObject> todos)
{
    var tasks = new List<object>();

    for (var i = 0; i < todos.Count; i++)
    {
        var todo = todos[i];

        tasks.Add(
            new
            {
                id = (i + 1).ToString(CultureInfo.InvariantCulture),
                subject = todo["content"]?.ToString() ?? string.Empty,
                status = todo["status"]?.ToString() ?? "pending",
                priority = todo["priority"]?.ToString()
            });
    }

    return tasks;
}

object? MapQueueItemToSessionSummary(QueueItemRecord item)
{
    var queueState = (item.State ?? string.Empty).Trim().ToLowerInvariant();

    if (queueState is not ("queued" or "dispatching"))
        return null;

    var titleParts = new List<string>();

    if (!string.IsNullOrWhiteSpace(item.IssueKey))
        titleParts.Add(item.IssueKey);

    if (!string.IsNullOrWhiteSpace(item.Rule))
        titleParts.Add(item.Rule);

    if (!string.IsNullOrWhiteSpace(item.Message))
        titleParts.Add(item.Message);

    var name = titleParts.Count > 0 ? $"[Queued] {string.Join(" - ", titleParts)}" : $"[Queued] Item #{item.Id}";

    var runtimeType = queueState == "dispatching" ? "busy" : "queued";
    var createdAt = ParseTime(item.CreatedAt) ?? DateTimeOffset.UtcNow.ToString("O");
    var modifiedAt = ParseTime(item.UpdatedAt) ?? createdAt;

    return new
    {
        id = $"queue-{item.Id}",
        name,
        project = item.Directory,
        description = item.Message,
        gitBranch = (string?)null,
        createdAt,
        modifiedAt,
        runtimeStatus = new
        {
            type = runtimeType
        },
        status = "pending",
        hasAssistantResponse = false,
        openCodeUrl = (string?)null,
        isQueueItem = true,
        queueItemId = item.Id,
        queueState,
        issueKey = string.IsNullOrWhiteSpace(item.IssueKey) ? null : item.IssueKey,
        issueType = item.IssueType,
        issueSeverity = item.Severity,
        issueRule = item.Rule,
        issuePath = item.RelativePath ?? item.AbsolutePath,
        issueLine = item.Line,
        lastError = item.LastError
    };
}

async Task StartUpstreamSse(CancellationToken cancellationToken)
{
    var retryDelayMs = 1000;

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            var url = new Uri(new Uri(opencodeUrl), "/global/event");
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

            if (!string.IsNullOrWhiteSpace(opencodePassword))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opencodeUsername}:{opencodePassword}"));
                req.Headers.TryAddWithoutValidation("Authorization", $"Basic {token}");
            }

            using var res = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!res.IsSuccessStatusCode ||
                res.Content is null)
                throw new InvalidOperationException($"Upstream SSE failed: {(int)res.StatusCode} {res.ReasonPhrase}");

            retryDelayMs = 1000;
            await using var stream = await res.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new StringBuilder();

            while (!reader.EndOfStream &&
                   !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;

                if (line.Length == 0)
                {
                    var raw = buffer.ToString();
                    buffer.Clear();

                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    var dataLines = raw.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).ToList();

                    if (dataLines.Count == 0)
                        continue;

                    var dataStr = string.Join("\n", dataLines.Select(l => l[5..].Trim()));

                    if (string.IsNullOrWhiteSpace(dataStr))
                        continue;

                    JsonNode? evt;

                    try
                    {
                        evt = JsonNode.Parse(dataStr);
                    }
                    catch
                    {
                        continue;
                    }

                    if (evt is not null)
                        await HandleUpstreamEvent(evt);

                    continue;
                }

                buffer.Append(line.Replace("\r", string.Empty)).Append('\n');
            }
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch
        {
            try
            {
                await Task.Delay(retryDelayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            retryDelayMs = Math.Min(retryDelayMs * 2, 30000);
        }
    }
}

async Task HandleUpstreamEvent(JsonNode evt)
{
    var directory = NormalizeDirectory(evt["directory"]?.ToString()) ?? evt["directory"]?.ToString();
    var type = evt["payload"]?["type"]?.ToString();
    var props = evt["payload"]?["properties"] as JsonObject ?? new JsonObject();

    if (string.IsNullOrWhiteSpace(type))
        return;

    if (type == "todo.updated")
    {
        var sessionId = props["sessionID"]?.ToString() ?? props["sessionId"]?.ToString();

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            InvalidateTodos(directory, sessionId);

            await sseHub.Broadcast(
                new
                {
                    type = "update",
                    sessionId
                });
        }
        else
        {
            InvalidateAllCaches();

            await sseHub.Broadcast(
                new
                {
                    type = "update"
                });
        }

        return;
    }

    if (type == "session.status")
    {
        var sessionId = props["sessionID"]?.ToString() ?? props["sessionId"]?.ToString();
        var statusType = props["status"]?["type"]?.ToString() ?? props["type"]?.ToString();

        if (!string.IsNullOrWhiteSpace(sessionId) &&
            !string.IsNullOrWhiteSpace(statusType))
        {
            NoteStatusOverride(directory, sessionId, statusType);

            lock (sessionsCacheLock)
                sessionsCache.Timestamp = DateTimeOffset.MinValue;

            lock (todoCacheLock)
                tasksAllCache = new TimestampedValue<List<object>>(DateTimeOffset.MinValue, []);

            await sseHub.Broadcast(
                new
                {
                    type = "update",
                    sessionId
                });
        }
        else
            await sseHub.Broadcast(
                new
                {
                    type = "update"
                });

        return;
    }

    if (type is "session.created" or "session.updated" or "session.deleted")
    {
        InvalidateAllCaches();

        await sseHub.Broadcast(
            new
            {
                type = "update"
            });

        return;
    }

    if (type.StartsWith("message.", StringComparison.Ordinal))
    {
        assistantPresenceCache.Clear();

        lock (sessionsCacheLock)
            sessionsCache.Timestamp = DateTimeOffset.MinValue;

        await sseHub.Broadcast(
            new
            {
                type = "update"
            });
    }
}
