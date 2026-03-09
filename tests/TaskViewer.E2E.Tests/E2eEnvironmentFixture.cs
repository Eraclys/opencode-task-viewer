using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace TaskViewer.E2E.Tests;

public sealed class E2eEnvironmentFixture : IAsyncLifetime
{
    readonly List<Process> _processes = [];
    string _orchestratorDbPath = string.Empty;
    string _alphaDir = string.Empty;
    string _gammaDir = string.Empty;

    public string RootDir { get; private set; } = string.Empty;
    public string MockUrl { get; private set; } = string.Empty;
    public string SonarUrl { get; private set; } = string.Empty;
    public string ViewerUrl { get; private set; } = string.Empty;

    public HttpClient Http { get; } = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task InitializeAsync()
    {
        RootDir = FindRootDir();
        _orchestratorDbPath = Path.Combine(Path.GetTempPath(), $"taskviewer-e2e-{Guid.NewGuid():N}.sqlite");
        _alphaDir = Path.Combine(Path.GetTempPath(), $"taskviewer-alpha-{Guid.NewGuid():N}");
        _gammaDir = Path.Combine(Path.GetTempPath(), $"taskviewer-gamma-{Guid.NewGuid():N}");
        CleanupOrchestratorDb(_orchestratorDbPath);
        InitializeRepoFixture(_alphaDir, ["src/index.js"]);
        InitializeRepoFixture(_gammaDir, ["src/worker.js", "src/server.js", "src/auth.js", "src/jobs.js", "src/cancel.js", "src/dupe.js", "src/fail.js"]);

        var mockProjectPath = Path.Combine(
            RootDir,
            "tests",
            "TaskViewer.MockOpenCode",
            "TaskViewer.MockOpenCode.csproj");

        var mockSonarProjectPath = Path.Combine(
            RootDir,
            "tests",
            "TaskViewer.MockSonarQube",
            "TaskViewer.MockSonarQube.csproj");

        var viewerProjectPath = Path.Combine(
            RootDir,
            "src",
            "TaskViewer.Server",
            "TaskViewer.Server.csproj");

        MockUrl = await StartProjectAndWaitForMarker(
            mockProjectPath,
            "MOCK_OPENCODE_URL=",
            new Dictionary<string, string>
            {
                ["HOST"] = "127.0.0.1",
                ["PORT"] = "0"
            });

        await WaitForOk($"{MockUrl}/__test__/health", TimeSpan.FromSeconds(10));

        SonarUrl = await StartProjectAndWaitForMarker(
            mockSonarProjectPath,
            "MOCK_SONAR_URL=",
            new Dictionary<string, string>
            {
                ["HOST"] = "127.0.0.1",
                ["PORT"] = "0"
            });

        await WaitForOk($"{SonarUrl}/__test__/health", TimeSpan.FromSeconds(10));

        ViewerUrl = await StartProjectAndWaitForMarker(
            viewerProjectPath,
            "VIEWER_URL=",
            new Dictionary<string, string>
            {
                ["HOST"] = "127.0.0.1",
                ["PORT"] = "0",
                ["OPENCODE_URL"] = MockUrl,
                ["ORCHESTRATOR_DB_PATH"] = _orchestratorDbPath,
                ["SONARQUBE_URL"] = SonarUrl,
                ["SONARQUBE_TOKEN"] = "test-token",
                ["ORCH_POLL_MS"] = "1200",
                ["ORCH_MAX_ACTIVE"] = "1",
                ["ORCH_MAX_ATTEMPTS"] = "1"
            });

        await WaitForOk($"{ViewerUrl}/api/tasks/board?limit=1", TimeSpan.FromSeconds(15));
    }

    public async Task DisposeAsync()
    {
        foreach (var process in _processes)
        {
            if (process.HasExited)
                continue;

            TryKill(process, false);
        }

        await Task.Delay(500);

        foreach (var process in _processes)
        {
            if (process.HasExited)
                continue;

            TryKill(process, true);
        }

        CleanupOrchestratorDb(_orchestratorDbPath);
        CleanupDirectory(_alphaDir);
        CleanupDirectory(_gammaDir);
        Http.Dispose();
    }

    public string GammaDirectory => _gammaDir.Replace('\\', '/');
    public string AlphaDirectory => _alphaDir.Replace('\\', '/');

    public async Task ResetOpenCodeAsync()
    {
        await PostJsonAsync(
            $"{MockUrl}/__test__/reset",
            new
            {
            });

        await PostJsonAsync(
            $"{MockUrl}/__test__/emit",
            new
            {
                directory = @"C:\Work\Alpha",
                type = "session.updated",
                properties = new
                {
                }
            });
    }

    public async Task ResetMocksAsync()
    {
        await PostJsonAsync(
            $"{ViewerUrl}/api/test/orch/reset",
            new
            {
            });

        await ResetOpenCodeAsync();

        await PostJsonAsync(
            $"{MockUrl}/__test__/setFailures",
            new
            {
                sessionCreateCount = 0,
                promptAsyncCount = 0
            });

        await PostJsonAsync(
            $"{SonarUrl}/__test__/reset",
            new
            {
            });
    }

    static void InitializeRepoFixture(string root, IReadOnlyList<string> files)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, ".git"));

        foreach (var file in files)
        {
            var path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (!File.Exists(path))
                File.WriteAllText(path, "// fixture\n");
        }
    }

    static void CleanupDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            // Best effort cleanup for temp test fixtures.
        }
    }

    public async Task PostJsonAsync(string url, object payload)
    {
        using var response = await Http.PostAsJsonAsync(url, payload);

        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();

        throw new HttpRequestException($"Request failed: {(int)response.StatusCode} {response.ReasonPhrase} for {url}. Body: {body}");
    }

    public async Task<JsonNode> PostJsonAndReadAsync(string url, object payload)
    {
        using var response = await Http.PostAsJsonAsync(url, payload);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Request failed: {(int)response.StatusCode} {response.ReasonPhrase} for {url}. Body: {body}");

        return JsonNode.Parse(body) ?? new JsonObject();
    }

    public async Task<JsonNode> GetJsonAsync(string url)
    {
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        return JsonNode.Parse(body) ?? new JsonObject();
    }

    async Task<string> StartProjectAndWaitForMarker(string projectPath, string marker, IReadOnlyDictionary<string, string> env)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RootDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);

        foreach (var (key, value) in env)
        {
            startInfo.Environment[key] = value;
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new StringBuilder();

        void HandleLine(string? line)
        {
            if (line is null)
                return;

            output.AppendLine(line);

            if (line.StartsWith(marker, StringComparison.Ordinal))
                tcs.TrySetResult(line[marker.Length..].Trim());
        }

        process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data);

        process.Exited += (_, _) =>
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.TrySetException(
                    new InvalidOperationException(
                        $"Process exited while waiting for marker '{marker}'.\n{output}"));
            }
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process for {projectPath}");

        _processes.Add(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var reg = cts.Token.Register(() => tcs.TrySetException(new TimeoutException($"Timed out waiting for marker '{marker}'.\n{output}")));

        return await tcs.Task;
    }

    static async Task WaitForOk(string url, TimeSpan timeout)
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var started = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - started < timeout)
        {
            try
            {
                using var response = await http.GetAsync(url);

                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // Retry.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting for {url}");
    }

    static void CleanupOrchestratorDb(string path)
    {
        foreach (var suffix in new[]
        {
            string.Empty,
            "-shm",
            "-wal"
        })
        {
            try
            {
                File.Delete(path + suffix);
            }
            catch
            {
                // Ignore cleanup races.
            }
        }
    }

    static void TryKill(Process process, bool entireProcessTree)
    {
        try
        {
            process.Kill(entireProcessTree);
        }
        catch
        {
            // Ignore.
        }
    }

    static string FindRootDir()
    {
        var dir = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "TaskViewer.slnx")))
                return dir;

            var parent = Directory.GetParent(dir);

            if (parent is null)
                break;

            dir = parent.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root from test runtime directory.");
    }
}
