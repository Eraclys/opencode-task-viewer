using System.Text.Json;
using TaskViewer.MockSonarQube;

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
            state = MockSonarState.BuildDefault();

        return Results.Json(
            new
            {
                ok = true
            });
    });

app.MapGet(
    "/api/issues/search",
    (HttpRequest request) =>
    {
        var componentKeys = request.Query["componentKeys"].ToString().Trim();
        var typesRaw = request.Query["types"].ToString().Trim();
        var statusesRaw = request.Query["statuses"].ToString().Trim();
        var rulesRaw = request.Query["rules"].ToString().Trim();

        var pageIndex = ParseBoundedInt(
            request.Query["p"],
            1,
            1,
            int.MaxValue);

        var pageSize = ParseBoundedInt(
            request.Query["ps"],
            100,
            1,
            500);

        var typeSet = ParseSet(typesRaw, true);
        var statusSet = ParseSet(statusesRaw, true);
        var ruleSet = ParseSet(rulesRaw, false);

        List<SonarIssueRecord> issues;

        lock (gate)
            issues = state.Issues.ToList();

        if (!string.IsNullOrWhiteSpace(componentKeys))
        {
            var keySet = ParseSet(componentKeys, false);

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

        return Results.Json(
            new
            {
                total,
                p = pageIndex,
                ps = pageSize,
                paging = new
                {
                    pageIndex,
                    pageSize,
                    total
                },
                issues = paged
            });
    });

app.MapGet(
    "/api/rules/show",
    (HttpRequest request) =>
    {
        var key = request.Query["key"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(key))
            return Results.Json(
                new
                {
                    error = "Missing rule key"
                },
                statusCode: 400);

        lock (gate)
        {
            if (!state.Rules.TryGetValue(key, out var name))
                return Results.Json(
                    new
                    {
                        error = "Rule not found"
                    },
                    statusCode: 404);

            return Results.Json(
                new
                {
                    rule = new
                    {
                        key,
                        name
                    }
                });
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

static int ParseBoundedInt(
    string? value,
    int fallback,
    int min,
    int max)
{
    if (!int.TryParse(value, out var parsed))
        return fallback;

    if (parsed < min)
        return min;

    if (parsed > max)
        return max;

    return parsed;
}

namespace TaskViewer.MockSonarQube
{
}
