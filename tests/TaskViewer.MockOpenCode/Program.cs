using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    async (HttpRequest request) =>
    {
        var body = await ReadBody(request);
        var sessionId = body?["sessionId"]?.GetValue<string>();
        var todos = body?["todos"] as JsonArray;

        if (string.IsNullOrWhiteSpace(sessionId) ||
            todos is null)
            return Results.Json(
                new
                {
                    error = "Expected { sessionId, todos: [] }"
                },
                statusCode: 400);

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
    async (HttpRequest request) =>
    {
        var body = await ReadBody(request);
        var directory = NormalizeDir(body?["directory"]?.GetValue<string>());
        var sessionId = body?["sessionId"]?.GetValue<string>();
        var type = body?["type"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(directory) ||
            string.IsNullOrWhiteSpace(sessionId) ||
            string.IsNullOrWhiteSpace(type))
            return Results.Json(
                new
                {
                    error = "Expected { directory, sessionId, type }"
                },
                statusCode: 400);

        lock (gate)
        {
            if (!state.StatusByDirectory.TryGetValue(directory, out var statuses))
            {
                statuses = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
                state.StatusByDirectory[directory] = statuses;
            }

            statuses[sessionId] = new JsonObject
            {
                ["type"] = type
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
    async (HttpRequest request) =>
    {
        var body = await ReadBody(request);
        var directory = body?["directory"]?.GetValue<string>();
        var type = body?["type"]?.GetValue<string>();
        var properties = body?["properties"] as JsonObject ?? new JsonObject();

        if (string.IsNullOrWhiteSpace(directory) ||
            string.IsNullOrWhiteSpace(type))
            return Results.Json(
                new
                {
                    error = "Expected { directory, type, properties? }"
                },
                statusCode: 400);

        await Broadcast(
            new JsonObject
            {
                ["directory"] = directory,
                ["payload"] = new JsonObject
                {
                    ["type"] = type,
                    ["properties"] = properties.DeepClone()
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
    async (HttpRequest request) =>
    {
        var body = await ReadBody(request);

        lock (gate)
        {
            state.FailSessionCreateCount = ParsePositiveInt(body?["sessionCreateCount"], 0);
            state.FailPromptAsyncCount = ParsePositiveInt(body?["promptAsyncCount"], 0);
            state.PromptDelayMs = ParsePositiveInt(body?["promptDelayMs"], 0);
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
    async (HttpRequest request) =>
    {
        var body = await ReadBody(request);

        var projectWorktree = (body?["projectWorktree"]?.GetValue<string>() ?? body?["worktree"]?.GetValue<string>() ?? string.Empty).Trim();
        var sandboxPath = (body?["sandboxPath"]?.GetValue<string>() ?? body?["sandbox"]?.GetValue<string>() ?? string.Empty).Trim();
        var sessionId = (body?["sessionId"]?.GetValue<string>() ?? string.Empty).Trim();
        var title = (body?["title"]?.GetValue<string>() ?? "Sandbox Session").Trim();
        var directory = NormalizeDir(body?["directory"]?.GetValue<string>() ?? sandboxPath);

        if (string.IsNullOrWhiteSpace(projectWorktree) ||
            string.IsNullOrWhiteSpace(sandboxPath) ||
            string.IsNullOrWhiteSpace(directory))
            return Results.Json(
                new
                {
                    error = "Expected { projectWorktree, sandboxPath, directory? }"
                },
                statusCode: 400);

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

            state.TodosBySessionId.TryAdd(sessionId, new JsonArray());
            state.StatusByDirectory.TryAdd(directory, new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase));
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

        var body = await ReadBody(request);
        var title = (body?["title"]?.GetValue<string>() ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(title))
            title = "Untitled session";

        var directory = NormalizeDir(request.Query["directory"].ToString());

        if (string.IsNullOrWhiteSpace(directory))
            directory = NormalizeDir(request.Headers["x-opencode-directory"].ToString());

        if (string.IsNullOrWhiteSpace(directory))
            return Results.Json(
                new
                {
                    error = "Missing directory"
                },
                statusCode: 400);

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
            state.TodosBySessionId[sessionId] = new JsonArray();

            if (!state.StatusByDirectory.ContainsKey(directory))
                state.StatusByDirectory[directory] = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);

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
            new JsonObject
            {
                ["directory"] = directory,
                ["payload"] = new JsonObject
                {
                    ["type"] = "session.created",
                    ["properties"] = new JsonObject
                    {
                        ["sessionID"] = created.Id
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
            return Results.Json(
                new
                {
                    error = "Missing directory"
                },
                statusCode: 400);

        lock (gate)
        {
            state.StatusByDirectory.TryGetValue(directory, out var statuses);

            return Results.Json(statuses ?? new Dictionary<string, JsonObject>());
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
                return Results.Json(
                    new
                    {
                        error = "Session not found"
                    },
                    statusCode: 404);

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

            return Results.Json(todos ?? new JsonArray());
        }
    });

app.MapPatch(
    "/session/{sessionId}",
    async (string sessionId, HttpRequest request) =>
    {
        var body = await ReadBody(request);

        lock (gate)
        {
            var session = state.Sessions.FirstOrDefault(s => s.Id == sessionId);

            if (session is null)
                return Results.Json(
                    new
                    {
                        error = "Session not found"
                    },
                    statusCode: 404);

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (body?["time"] is JsonObject timeObj &&
                timeObj["archived"] is not null)
            {
                if (long.TryParse(timeObj["archived"]!.ToString(), out var archived))
                    session.Time.Archived = archived;
            }
            else if (body?["archived"]?.GetValue<bool>() == true)
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
                return Results.Json(
                    new
                    {
                        error = "Session not found"
                    },
                    statusCode: 404);

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

        var body = await ReadBody(request);
        var parts = body?["parts"] as JsonArray;
        var text = string.Empty;

        if (parts is not null)
        {
            var lines = parts
                .Select(part => part?["text"]?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value));

            text = string.Join("\n", lines);
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dir = NormalizeDir(session.Directory);

        lock (gate)
        {
            if (!state.StatusByDirectory.TryGetValue(dir, out var statuses))
            {
                statuses = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
                state.StatusByDirectory[dir] = statuses;
            }

            statuses[sessionId] = new JsonObject
            {
                ["type"] = "busy"
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
            new JsonObject
            {
                ["directory"] = dir,
                ["payload"] = new JsonObject
                {
                    ["type"] = "session.status",
                    ["properties"] = new JsonObject
                    {
                        ["sessionID"] = sessionId,
                        ["status"] = new JsonObject
                        {
                            ["type"] = "busy"
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
                new JsonObject
                {
                    ["directory"] = dir,
                    ["payload"] = new JsonObject
                    {
                        ["type"] = "session.status",
                        ["properties"] = new JsonObject
                        {
                            ["sessionID"] = sessionId,
                            ["status"] = new JsonObject
                            {
                                ["type"] = "idle"
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

async Task Broadcast(JsonObject evt)
{
    var payload = $"data: {evt.ToJsonString()}\n\n";
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

static int ParsePositiveInt(JsonNode? node, int fallback)
{
    if (node is null)
        return fallback;

    if (!int.TryParse(node.ToString(), out var parsed) ||
        parsed <= 0)
        return fallback;

    return parsed;
}

static void UpdateSessionTime(MockState state, string sessionId)
{
    var session = state.Sessions.FirstOrDefault(s => s.Id == sessionId);

    if (session is not null)
        session.Time.Updated = NowIso();
}

static async Task<JsonObject?> ReadBody(HttpRequest request)
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
    var text = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(text))
        return null;

    return JsonNode.Parse(text) as JsonObject;
}

sealed class MockState
{
    public int NextSessionIndex { get; set; } = 1;
    public int FailSessionCreateCount { get; set; }
    public int FailPromptAsyncCount { get; set; }
    public int PromptDelayMs { get; set; }
    public List<ProjectRecord> Projects { get; set; } = [];
    public List<SessionRecord> Sessions { get; set; } = [];
    public Dictionary<string, JsonArray> TodosBySessionId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<MessageRecord>> MessagesBySessionId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, JsonObject>> StatusByDirectory { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static MockState BuildDefault()
    {
        var now = DateTimeOffset.UtcNow;
        var baseCreated = now.AddHours(-1).ToString("O");

        var alphaWorktree = @"C:\Work\Alpha";
        var betaWorktree = @"C:\Work\Beta";
        var gammaWorktree = @"C:\Work\Gamma";

        const string alphaDir = "C:/Work/Alpha";
        const string betaDir = "C:/Work/Beta";
        const string gammaDir = "C:/Work/Gamma";

        var state = new MockState
        {
            Projects =
            [
                new ProjectRecord
                {
                    Id = "global",
                    Worktree = "/",
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-5).ToString("O")
                    }
                },
                new ProjectRecord
                {
                    Id = "p-alpha",
                    Worktree = alphaWorktree,
                    Sandboxes = [@"C:\Work\Alpha\SandboxOnly"],
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-5).ToString("O")
                    }
                },
                new ProjectRecord
                {
                    Id = "p-beta",
                    Worktree = betaWorktree,
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-5).ToString("O")
                    }
                },
                new ProjectRecord
                {
                    Id = "p-gamma",
                    Worktree = gammaWorktree,
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-5).ToString("O")
                    }
                }
            ],
            Sessions =
            [
                new SessionRecord
                {
                    Id = "sess-busy",
                    Title = "Busy Session",
                    Directory = betaDir,
                    Project = new SessionProjectRecord
                    {
                        Worktree = betaWorktree
                    },
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-20).ToString("O")
                    }
                },
                new SessionRecord
                {
                    Id = "sess-retry",
                    Title = "Retrying Session",
                    Directory = alphaDir,
                    Project = new SessionProjectRecord
                    {
                        Worktree = alphaWorktree
                    },
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddSeconds(-45).ToString("O")
                    }
                },
                new SessionRecord
                {
                    Id = "sess-recent",
                    Title = "Recently Updated",
                    Directory = gammaDir,
                    Project = new SessionProjectRecord
                    {
                        Worktree = gammaWorktree
                    },
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddMinutes(-2).ToString("O")
                    }
                },
                new SessionRecord
                {
                    Id = "sess-stale",
                    Title = "Stale Session",
                    Directory = gammaDir,
                    Project = new SessionProjectRecord
                    {
                        Worktree = gammaWorktree
                    },
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddMinutes(-10).ToString("O")
                    }
                },
                new SessionRecord
                {
                    Id = "sess-archived",
                    Title = "Archived Session (Should Not Show)",
                    Directory = gammaDir,
                    Project = new SessionProjectRecord
                    {
                        Worktree = gammaWorktree
                    },
                    Time = new TimeRecord
                    {
                        Created = baseCreated,
                        Updated = now.AddMinutes(-30).ToString("O"),
                        Archived = now.AddMinutes(-25).ToUnixTimeMilliseconds()
                    }
                }
            ],
            TodosBySessionId = new Dictionary<string, JsonArray>(StringComparer.OrdinalIgnoreCase)
            {
                ["sess-busy"] = new(),
                ["sess-retry"] = new(),
                ["sess-recent"] = new(),
                ["sess-stale"] = new()
            },
            MessagesBySessionId = new Dictionary<string, List<MessageRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                ["sess-busy"] =
                [
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m1",
                            Role = "user",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddSeconds(-30).ToUnixTimeMilliseconds()
                            }
                        },
                        Content =
                        [
                            new MessageContentRecord
                            {
                                Type = "text",
                                Text = "Run the worker."
                            }
                        ]
                    },
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m2",
                            Role = "assistant",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddSeconds(-29).ToUnixTimeMilliseconds()
                            }
                        },
                        Content =
                        [
                            new MessageContentRecord
                            {
                                Type = "text",
                                Text = "Worker is running now."
                            }
                        ]
                    }
                ],
                ["sess-retry"] =
                [
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m3",
                            Role = "user",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddSeconds(-60).ToUnixTimeMilliseconds()
                            }
                        },
                        Text = "Try the migration again."
                    },
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m4",
                            Role = "assistant",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddSeconds(-59).ToUnixTimeMilliseconds()
                            }
                        },
                        Text = "Retrying migration with backoff."
                    }
                ],
                ["sess-recent"] =
                [
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m5",
                            Role = "user",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddMinutes(-2).ToUnixTimeMilliseconds()
                            }
                        },
                        Text = "Can you inspect this issue?"
                    }
                ],
                ["sess-stale"] =
                [
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m6",
                            Role = "user",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddHours(-1).ToUnixTimeMilliseconds()
                            }
                        },
                        Text = "Please summarize the diagnostics."
                    },
                    new MessageRecord
                    {
                        Info = new MessageInfoRecord
                        {
                            Id = "m7",
                            Role = "assistant",
                            Time = new MessageTimeRecord
                            {
                                Created = now.AddHours(-1).AddSeconds(1).ToUnixTimeMilliseconds()
                            }
                        },
                        Text = "Diagnostics complete; all checks passed."
                    }
                ]
            },
            StatusByDirectory = new Dictionary<string, Dictionary<string, JsonObject>>(StringComparer.OrdinalIgnoreCase)
            {
                [alphaDir] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["sess-retry"] = new JsonObject
                    {
                        ["type"] = "retry"
                    }
                },
                [betaDir] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["sess-busy"] = new JsonObject
                    {
                        ["type"] = "busy"
                    }
                },
                [gammaDir] = new(StringComparer.OrdinalIgnoreCase)
            }
        };

        return state;
    }
}

sealed class ProjectRecord
{
    public string Id { get; set; } = string.Empty;
    public string Worktree { get; set; } = string.Empty;
    public List<string> Sandboxes { get; set; } = [];
    public TimeRecord Time { get; set; } = new();
}

sealed class SessionRecord
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public SessionProjectRecord Project { get; set; } = new();
    public TimeRecord Time { get; set; } = new();
}

sealed class SessionProjectRecord
{
    public string Worktree { get; set; } = string.Empty;
}

sealed class TimeRecord
{
    public string Created { get; set; } = string.Empty;
    public string Updated { get; set; } = string.Empty;
    public long? Archived { get; set; }
}

sealed class MessageRecord
{
    public MessageInfoRecord Info { get; set; } = new();
    public string? Text { get; set; }
    public List<MessageContentRecord>? Content { get; set; }
}

sealed class MessageInfoRecord
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public MessageTimeRecord Time { get; set; } = new();
}

sealed class MessageTimeRecord
{
    public long Created { get; set; }
}

sealed class MessageContentRecord
{
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
