using System.Text.Json;

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
var state = MockSonarState.BuildDefault();

app.MapGet("/__test__/health", () => Results.Json(new { ok = true }));

app.MapPost("/__test__/reset", () =>
{
    lock (gate)
        state = MockSonarState.BuildDefault();

    return Results.Json(new { ok = true });
});

app.MapGet("/api/issues/search", (HttpRequest request) =>
{
    var componentKeys = request.Query["componentKeys"].ToString().Trim();
    var typesRaw = request.Query["types"].ToString().Trim();
    var statusesRaw = request.Query["statuses"].ToString().Trim();
    var rulesRaw = request.Query["rules"].ToString().Trim();

    var pageIndex = ParseBoundedInt(request.Query["p"], 1, 1, int.MaxValue);
    var pageSize = ParseBoundedInt(request.Query["ps"], 100, 1, 500);

    var typeSet = ParseSet(typesRaw, makeUpper: true);
    var statusSet = ParseSet(statusesRaw, makeUpper: true);
    var ruleSet = ParseSet(rulesRaw, makeUpper: false);

    List<SonarIssueRecord> issues;
    lock (gate)
        issues = state.Issues.ToList();

    if (!string.IsNullOrWhiteSpace(componentKeys))
    {
        var keySet = ParseSet(componentKeys, makeUpper: false);
        issues = issues
            .Where(issue =>
            {
                var raw = issue.Component ?? string.Empty;
                var idx = raw.IndexOf(':');
                var key = idx > -1 ? raw[..idx] : raw;
                return keySet.Contains(key);
            })
            .ToList();
    }

    if (typeSet.Count > 0)
        issues = issues.Where(issue => typeSet.Contains((issue.Type ?? string.Empty).ToUpperInvariant())).ToList();

    if (statusSet.Count > 0)
        issues = issues.Where(issue => statusSet.Contains((issue.Status ?? string.Empty).ToUpperInvariant())).ToList();

    if (ruleSet.Count > 0)
        issues = issues.Where(issue => ruleSet.Contains((issue.Rule ?? string.Empty).Trim())).ToList();

    var total = issues.Count;
    var start = (pageIndex - 1) * pageSize;
    var paged = issues.Skip(start).Take(pageSize).ToList();

    return Results.Json(new
    {
        total,
        p = pageIndex,
        ps = pageSize,
        paging = new { pageIndex, pageSize, total },
        issues = paged
    });
});

app.MapGet("/api/rules/show", (HttpRequest request) =>
{
    var key = request.Query["key"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(key))
        return Results.Json(new { error = "Missing rule key" }, statusCode: 400);

    lock (gate)
    {
        if (!state.Rules.TryGetValue(key, out var name))
            return Results.Json(new { error = "Rule not found" }, statusCode: 404);

        return Results.Json(new
        {
            rule = new { key, name }
        });
    }
});

app.MapFallback(() => Results.Json(new { error = "Not found" }, statusCode: 404));

app.Lifetime.ApplicationStarted.Register(() =>
{
    var actual = app.Urls.FirstOrDefault() ?? $"http://{host}:{port}";
    Console.WriteLine($"Mock SonarQube listening on {actual}");
    Console.WriteLine($"MOCK_SONAR_URL={actual}");
});

await app.RunAsync();

static HashSet<string> ParseSet(string raw, bool makeUpper)
{
    var comparer = makeUpper ? StringComparer.Ordinal : StringComparer.Ordinal;
    var values = raw
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => makeUpper ? value.ToUpperInvariant() : value)
        .ToArray();

    return new HashSet<string>(values, comparer);
}

static int ParseBoundedInt(string? value, int fallback, int min, int max)
{
    if (!int.TryParse(value, out var parsed))
        return fallback;

    if (parsed < min)
        return min;

    if (parsed > max)
        return max;

    return parsed;
}

sealed class MockSonarState
{
    public Dictionary<string, string> Rules { get; set; } = new(StringComparer.Ordinal);
    public List<SonarIssueRecord> Issues { get; set; } = [];

    public static MockSonarState BuildDefault() => new()
    {
        Rules = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["javascript:S1126"] = "Assignments should not be redundant",
            ["javascript:S3776"] = "Cognitive Complexity of functions should not be too high",
            ["javascript:S5144"] = "Constructing URLs from user input is security-sensitive",
            ["javascript:S1481"] = "Unused local variables should be removed"
        },
        Issues =
        [
            new SonarIssueRecord
            {
                Key = "sq-gamma-001",
                Component = "gamma-key:src/worker.js",
                Line = 42,
                Rule = "javascript:S1126",
                Severity = "MAJOR",
                Type = "CODE_SMELL",
                Status = "OPEN",
                Message = "Remove this redundant assignment."
            },
            new SonarIssueRecord
            {
                Key = "sq-gamma-002",
                Component = "gamma-key:src/server.js",
                Line = 17,
                Rule = "javascript:S3776",
                Severity = "CRITICAL",
                Type = "CODE_SMELL",
                Status = "CONFIRMED",
                Message = "Refactor this function to reduce Cognitive Complexity."
            },
            new SonarIssueRecord
            {
                Key = "sq-gamma-003",
                Component = "gamma-key:src/auth.js",
                Line = 10,
                Rule = "javascript:S5144",
                Severity = "BLOCKER",
                Type = "VULNERABILITY",
                Status = "OPEN",
                Message = "Review this URL construction for SSRF risk."
            },
            new SonarIssueRecord
            {
                Key = "sq-gamma-004",
                Component = "gamma-key:src/jobs.js",
                Line = 91,
                Rule = "javascript:S3776",
                Severity = "MAJOR",
                Type = "CODE_SMELL",
                Status = "OPEN",
                Message = "Reduce the Cognitive Complexity of this function."
            },
            new SonarIssueRecord
            {
                Key = "sq-alpha-001",
                Component = "alpha-key:src/index.js",
                Line = 7,
                Rule = "javascript:S1481",
                Severity = "MINOR",
                Type = "CODE_SMELL",
                Status = "OPEN",
                Message = "Remove this unused local variable."
            }
        ]
    };
}

sealed class SonarIssueRecord
{
    public string Key { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Rule { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
