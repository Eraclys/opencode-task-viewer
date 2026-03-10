using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using SonarQube.OpenCodeTaskViewer.MockOpenCode;

var host = Environment.GetEnvironmentVariable("HOST") ?? "127.0.0.1";
var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var parsedPort) ? parsedPort : 0;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{host}:{port}");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

var gate = new object();
var state = MockState.BuildDefault();
var sseClients = new ConcurrentDictionary<Guid, HttpResponse>();

app.MapGet(
    "/__test__/health",
    () => Results.Json(
        new
        {
            ok = true
        }));

app.MapPost(
    "/__test__/reset",
    () =>
    {
        lock (gate)
            state = MockState.BuildDefault();

        return Results.Json(
            new
            {
                ok = true
            });
    });

app.MapPost(
    "/__test__/setTodos",
    (SetTodosRequest body) =>
    {
        var sessionId = body.SessionId;
        var todos = body.Todos;

        if (string.IsNullOrWhiteSpace(sessionId) ||
            todos is null)
        {
            return Results.Json(
                new
                {
                    error = "Expected { sessionId, todos: [] }"
                },
                statusCode: 400);
        }

        lock (gate)
        {
            state.TodosBySessionId[sessionId] = todos;
            UpdateSessionTime(state, sessionId);
        }

        return Results.Json(
            new
            {
                ok = true
            });
    });

app.MapPost(
    "/__test__/setStatus",
    (SetStatusRequest body) =>
    {
        var directory = NormalizeDir(body.Directory);
        var sessionId = body.SessionId;
        var type = body.Type;

        if (string.IsNullOrWhiteSpace(directory) ||
            string.IsNullOrWhiteSpace(sessionId) ||
            string.IsNullOrWhiteSpace(type))
        {
            return Results.Json(
                new
                {
                    error = "Expected { directory, sessionId, type }"
                },
                statusCode: 400);
        }

        lock (gate)
        {
            if (!state.StatusByDirectory.TryGetValue(directory, out var statuses))
            {
                statuses = new Dictionary<string, StatusRecord>(StringComparer.OrdinalIgnoreCase);
                state.StatusByDirectory[directory] = statuses;
            }

            statuses[sessionId] = new StatusRecord
            {
                Type = type
            };
        }

        return Results.Json(
            new
            {
                ok = true
            });
    });

app.MapPost(
    "/__test__/emit",
    async (EmitRequest body) =>
    {
        var directory = body.Directory;
        var type = body.Type;
        var properties = CloneJsonElement(body.Properties);

        if (string.IsNullOrWhiteSpace(directory) ||
            string.IsNullOrWhiteSpace(type))
        {
            return Results.Json(
                new
                {
                    error = "Expected { directory, type, properties? }"
                },
                statusCode: 400);
        }

        await Broadcast(
            new
            {
                directory,
                payload = new
                {
                    type,
                    properties
                }
            });

        return Results.Json(
            new
            {
                ok = true
            });
    });

app.MapPost(
    "/__test__/setFailures",
    (SetFailuresRequest body) =>
    {
        lock (gate)
        {
            state.FailSessionCreateCount = ParsePositiveInt(body.SessionCreateCount, 0);
            state.FailPromptAsyncCount = ParsePositiveInt(body.PromptAsyncCount, 0);
            state.PromptDelayMs = ParsePositiveInt(body.PromptDelayMs, 0);
        }

        return Results.Json(
            new
            {
                ok = true,
                failSessionCreateCount = state.FailSessionCreateCount,
                failPromptAsyncCount = state.FailPromptAsyncCount,
                promptDelayMs = state.PromptDelayMs
            });
    });

app.MapPost(
    "/__test__/addSandboxSession",
    (AddSandboxSessionRequest body) =>
    {
        var projectWorktree = (body.ProjectWorktree ?? body.Worktree ?? string.Empty).Trim();
        var sandboxPath = (body.SandboxPath ?? body.Sandbox ?? string.Empty).Trim();
        var sessionId = (body.SessionId ?? string.Empty).Trim();
        var title = (body.Title ?? "Sandbox Session").Trim();
        var directory = NormalizeDir(body.Directory ?? sandboxPath);

        if (string.IsNullOrWhiteSpace(projectWorktree) ||
            string.IsNullOrWhiteSpace(sandboxPath) ||
            string.IsNullOrWhiteSpace(directory))
        {
            return Results.Json(
                new
                {
                    error = "Expected { projectWorktree, sandboxPath, directory? }"
                },
                statusCode: 400);
        }

        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                sessionId = $"sess-sandbox-{state.NextSessionIndex++}";

            var now = NowIso();
            var project = state.Projects.FirstOrDefault(p => NormalizeDir(p.Worktree) == NormalizeDir(projectWorktree));

            if (project is null)
            {
                project = new ProjectRecord
                {
                    Id = $"p-sandbox-{state.NextSessionIndex++}",
                    Worktree = projectWorktree,
                    Time = new TimeRecord
                    {
                        Created = now,
                        Updated = now
                    }
                };

                state.Projects.Add(project);
            }

            if (!project.Sandboxes.Any(x => NormalizeDir(x) == NormalizeDir(sandboxPath)))
                project.Sandboxes.Add(sandboxPath);

            project.Time.Updated = now;

            if (state.Sessions.All(s => s.Id != sessionId))
            {
                state.Sessions.Insert(
                    0,
                    new SessionRecord
                    {
                        Id = sessionId,
                        Title = title,
                        Directory = directory,
                        Project = new SessionProjectRecord
                        {
                            Worktree = projectWorktree
                        },
                        Time = new TimeRecord
                        {
                            Created = now,
                            Updated = now
                        }
                    });
            }

            if (!state.MessagesBySessionId.ContainsKey(sessionId))
            {
                state.MessagesBySessionId[sessionId] =
                [
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = $"m-{sessionId}",
                            Role = "user",
                            Time = new MessageTimeRecord
                            {
                                Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            }
                        },
                        Text = "Sandbox-only session prompt."
                    }
                ];
            }

            state.TodosBySessionId.TryAdd(sessionId, []);
            state.StatusByDirectory.TryAdd(directory, new Dictionary<string, StatusRecord>(StringComparer.OrdinalIgnoreCase));
        }

        return Results.Json(
            new
            {
                ok = true,
                sessionId,
                directory,
                projectWorktree,
                sandboxPath
            });
    });

app.MapGet(
    "/project",
    () =>
    {
        lock (gate)
            return Results.Json(state.Projects);
    });

app.MapGet(
    "/session",
    (HttpRequest request) =>
    {
        var directory = NormalizeDir(request.Query["directory"].ToString());
        var limitRaw = request.Query["limit"].ToString();

        List<SessionRecord> sessions;

        lock (gate)
        {
            sessions = state
                .Sessions
                .Where(s => string.IsNullOrWhiteSpace(directory) || NormalizeDir(s.Directory) == directory)
                .OrderByDescending(s => ParseTime(s.Time.Updated ?? s.Time.Created))
                .ToList();
        }

        if (int.TryParse(limitRaw, out var limit) &&
            limit > 0)
            sessions = sessions.Take(limit).ToList();

        return Results.Json(sessions);
    });

app.MapPost(
    "/session",
    async (HttpRequest request) =>
    {
        lock (gate)
        {
            if (state.FailSessionCreateCount > 0)
            {
                state.FailSessionCreateCount -= 1;

                return Results.Json(
                    new
                    {
                        error = "Injected session creation failure"
                    },
                    statusCode: 500);
            }
        }

        var body = await request.ReadFromJsonAsync<CreateSessionRequest>();
        var title = (body?.Title ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(title))
            title = "Untitled session";

        var directory = NormalizeDir(request.Query["directory"].ToString());

        if (string.IsNullOrWhiteSpace(directory))
            directory = NormalizeDir(request.Headers["x-opencode-directory"].ToString());

        if (string.IsNullOrWhiteSpace(directory))
        {
            return Results.Json(
                new
                {
                    error = "Missing directory"
                },
                statusCode: 400);
        }

        SessionRecord created;

        lock (gate)
        {
            var now = NowIso();
            var sessionId = $"sess-auto-{state.NextSessionIndex++}";
            var worktree = directory.Replace('/', '\\');

            created = new SessionRecord
            {
                Id = sessionId,
                Title = title,
                Directory = directory,
                Project = new SessionProjectRecord
                {
                    Worktree = worktree
                },
                Time = new TimeRecord
                {
                    Created = now,
                    Updated = now
                }
            };

            state.Sessions.Insert(0, created);
            state.MessagesBySessionId[sessionId] = [];
            state.TodosBySessionId[sessionId] = [];

            if (!state.StatusByDirectory.ContainsKey(directory))
                state.StatusByDirectory[directory] = new Dictionary<string, StatusRecord>(StringComparer.OrdinalIgnoreCase);

            if (state.Projects.All(p => NormalizeDir(p.Worktree) != directory))
            {
                state.Projects.Add(
                    new ProjectRecord
                    {
                        Id = $"p-{sessionId}",
                        Worktree = worktree,
                        Time = new TimeRecord
                        {
                            Created = now,
                            Updated = now
                        }
                    });
            }
        }

        _ = Broadcast(
            new
            {
                directory,
                payload = new
                {
                    type = "session.created",
                    properties = new
                    {
                        sessionID = created.Id
                    }
                }
            });

        return Results.Json(created);
    });

app.MapGet(
    "/experimental/session",
    (HttpRequest request) =>
    {
        var search = request.Query["search"].ToString();
        var limitRaw = request.Query["limit"].ToString();

        List<SessionRecord> sessions;

        lock (gate)
        {
            sessions = state
                .Sessions
                .OrderByDescending(s => ParseTime(s.Time.Updated ?? s.Time.Created))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var query = search.ToLowerInvariant();

            sessions = sessions
                .Where(s => s.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (int.TryParse(limitRaw, out var limit) &&
            limit > 0)
            sessions = sessions.Take(limit).ToList();

        return Results.Json(sessions);
    });

app.MapGet(
    "/session/status",
    (HttpRequest request) =>
    {
        var directory = NormalizeDir(request.Query["directory"].ToString());

        if (string.IsNullOrWhiteSpace(directory))
        {
            return Results.Json(
                new
                {
                    error = "Missing directory"
                },
                statusCode: 400);
        }

        lock (gate)
        {
            state.StatusByDirectory.TryGetValue(directory, out var statuses);

            return Results.Json(statuses ?? new Dictionary<string, StatusRecord>());
        }
    });

app.MapGet(
    "/session/{sessionId}",
    (string sessionId) =>
    {
        lock (gate)
        {
            var session = state.Sessions.FirstOrDefault(s => s.Id == sessionId);

            if (session is null)
            {
                return Results.Json(
                    new
                    {
                        error = "Session not found"
                    },
                    statusCode: 404);
            }

            return Results.Json(session);
        }
    });

app.MapGet(
    "/session/{sessionId}/todo",
    (string sessionId) =>
    {
        lock (gate)
        {
            state.TodosBySessionId.TryGetValue(sessionId, out var todos);

            return Results.Json(todos ?? new List<TodoRecord>());
        }
    });

app.MapPatch(
    "/session/{sessionId}",
    async (string sessionId, HttpRequest request) =>
    {
        var body = await request.ReadFromJsonAsync<ArchiveSessionRequest>();

        lock (gate)
        {
            var session = state.Sessions.FirstOrDefault(s => s.Id == sessionId);

            if (session is null)
            {
                return Results.Json(
                    new
                    {
                        error = "Session not found"
                    },
                    statusCode: 404);
            }

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (body?.Time?.Archived is not null)
                session.Time.Archived = body.Time.Archived.Value;
            else if (body?.Archived == true)
                session.Time.Archived = nowMs;

            session.Time.Updated = NowIso();

            return Results.Json(session);
        }
    });

app.MapGet(
    "/session/{sessionId}/message",
    (string sessionId, HttpRequest request) =>
    {
        var limitRaw = request.Query["limit"].ToString();

        lock (gate)
        {
            state.MessagesBySessionId.TryGetValue(sessionId, out var messages);
            messages ??= [];

            if (int.TryParse(limitRaw, out var limit) &&
                limit > 0)
                messages = messages.TakeLast(limit).ToList();

            return Results.Json(messages);
        }
    });

app.MapPost(
    "/session/{sessionId}/prompt_async",
    async (string sessionId, HttpRequest request) =>
    {
        SessionRecord? session;
        int delayMs;

        lock (gate)
        {
            session = state.Sessions.FirstOrDefault(s => s.Id == sessionId);

            if (session is null)
            {
                return Results.Json(
                    new
                    {
                        error = "Session not found"
                    },
                    statusCode: 404);
            }

            if (state.FailPromptAsyncCount > 0)
            {
                state.FailPromptAsyncCount -= 1;

                return Results.Json(
                    new
                    {
                        error = "Injected prompt_async failure"
                    },
                    statusCode: 500);
            }

            delayMs = state.PromptDelayMs;
        }

        if (delayMs > 0)
            await Task.Delay(delayMs);

        var body = await request.ReadFromJsonAsync<PromptAsyncRequest>();
        var text = string.Join("\n", body?.Parts?.Select(part => part.Text).Where(value => !string.IsNullOrWhiteSpace(value)) ?? []);

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dir = NormalizeDir(session.Directory);

        lock (gate)
        {
            if (!state.StatusByDirectory.TryGetValue(dir, out var statuses))
            {
                statuses = new Dictionary<string, StatusRecord>(StringComparer.OrdinalIgnoreCase);
                state.StatusByDirectory[dir] = statuses;
            }

            statuses[sessionId] = new StatusRecord
            {
                Type = "busy"
            };

            if (!state.MessagesBySessionId.TryGetValue(sessionId, out var messages))
            {
                messages = [];
                state.MessagesBySessionId[sessionId] = messages;
            }

            messages.Add(
                new MessageRecord
                {
                    Info = new MessageInfoRecord
                    {
                        Id = $"m-{sessionId}-{nowMs}",
                        Role = "user",
                        Time = new MessageTimeRecord
                        {
                            Created = nowMs
                        }
                    },
                    Text = string.IsNullOrWhiteSpace(text) ? "Queued prompt" : text
                });

            session.Time.Updated = NowIso();
        }

        await Broadcast(
            new
            {
                directory = dir,
                payload = new
                {
                    type = "session.status",
                    properties = new
                    {
                        sessionID = sessionId,
                        status = new
                        {
                            type = "busy"
                        }
                    }
                }
            });

        _ = Task.Run(async () =>
        {
            await Task.Delay(400);

            lock (gate)
            {
                if (state.StatusByDirectory.TryGetValue(dir, out var statuses))
                    statuses.Remove(sessionId);

                session.Time.Updated = NowIso();
            }

            await Broadcast(
                new
                {
                    directory = dir,
                    payload = new
                    {
                        type = "session.status",
                        properties = new
                        {
                            sessionID = sessionId,
                            status = new
                            {
                                type = "idle"
                            }
                        }
                    }
                });
        });

        return Results.NoContent();
    });

app.MapGet(
    "/global/event",
    async context =>
    {
        context.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        await context.Response.WriteAsync(": connected\n\n");
        await context.Response.Body.FlushAsync();

        var id = Guid.NewGuid();
        sseClients[id] = context.Response;

        try
        {
            await Task.Delay(Timeout.Infinite, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // Closed by client.
        }
        finally
        {
            sseClients.TryRemove(id, out _);
        }
    });

app.MapFallback(() => Results.Json(
    new
    {
        error = "Not found"
    },
    statusCode: 404));

app.Lifetime.ApplicationStarted.Register(() =>
{
    var actual = app.Urls.FirstOrDefault() ?? $"http://{host}:{port}";
    Console.WriteLine($"Mock OpenCode listening on {actual}");
    Console.WriteLine($"MOCK_OPENCODE_URL={actual}");
});

await app.RunAsync();

async Task Broadcast<T>(T evt)
{
    var payload = $"data: {JsonSerializer.Serialize(evt)}\n\n";
    var bytes = Encoding.UTF8.GetBytes(payload);
    var dead = new List<Guid>();

    foreach (var (id, response) in sseClients)
    {
        try
        {
            await response.Body.WriteAsync(bytes);
            await response.Body.FlushAsync();
        }
        catch
        {
            dead.Add(id);
        }
    }

    foreach (var id in dead)
    {
        sseClients.TryRemove(id, out _);
    }
}

static long ParseTime(string? value)
{
    if (DateTimeOffset.TryParse(value, out var parsed))
        return parsed.ToUnixTimeMilliseconds();

    return 0;
}

static string NormalizeDir(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return string.Empty;

    return value.Trim().Replace('\\', '/').TrimEnd('/');
}

static string NowIso() => DateTimeOffset.UtcNow.ToString("O");

static int ParsePositiveInt(int? value, int fallback)
{
    if (!value.HasValue)
        return fallback;

    if (value.Value <= 0)
        return fallback;

    return value.Value;
}

static void UpdateSessionTime(MockState state, string sessionId)
{
    var session = state.Sessions.FirstOrDefault(s => s.Id == sessionId);

    if (session is not null)
        session.Time.Updated = NowIso();
}

static JsonElement? CloneJsonElement(JsonElement? value)
{
    if (!value.HasValue)
        return null;

    using var document = JsonDocument.Parse(value.Value.GetRawText());

    return document.RootElement.Clone();
}

namespace SonarQube.OpenCodeTaskViewer.MockOpenCode
{
    sealed class SetTodosRequest
    {
        public string? SessionId { get; init; }
        public List<TodoRecord>? Todos { get; init; }
    }

    sealed class SetStatusRequest
    {
        public string? Directory { get; init; }
        public string? SessionId { get; init; }
        public string? Type { get; init; }
    }

    sealed class EmitRequest
    {
        public string? Directory { get; init; }
        public string? Type { get; init; }
        public JsonElement? Properties { get; init; }
    }

    sealed class SetFailuresRequest
    {
        public int? SessionCreateCount { get; init; }
        public int? PromptAsyncCount { get; init; }
        public int? PromptDelayMs { get; init; }
    }

    sealed class AddSandboxSessionRequest
    {
        public string? ProjectWorktree { get; init; }
        public string? Worktree { get; init; }
        public string? SandboxPath { get; init; }
        public string? Sandbox { get; init; }
        public string? SessionId { get; init; }
        public string? Title { get; init; }
        public string? Directory { get; init; }
    }

    sealed class CreateSessionRequest
    {
        public string? Title { get; init; }
    }

    sealed class ArchiveSessionRequest
    {
        public ArchiveTimeRequest? Time { get; init; }
        public bool? Archived { get; init; }
    }

    sealed class ArchiveTimeRequest
    {
        public long? Archived { get; init; }
    }

    sealed class PromptAsyncRequest
    {
        public List<PromptPartRequest>? Parts { get; init; }
    }

    sealed class PromptPartRequest
    {
        public string? Type { get; init; }
        public string? Text { get; init; }
    }
}
