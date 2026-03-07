namespace TaskViewer.Server.Configuration;

public static class AppRuntimeSettingsLoader
{
    public static AppRuntimeSettings Load(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var viewerHost = ReadString(configuration, "TaskViewer:Host", "HOST", "127.0.0.1");
        var viewerPort = ReadInt(configuration, "TaskViewer:Port", "PORT", 3456, 1);
        var openCodeUrl = ReadString(configuration, "OpenCode:Url", "OPENCODE_URL", "http://localhost:4096");
        var openCodeUsername = ReadString(configuration, "OpenCode:Username", "OPENCODE_USERNAME", "opencode");
        var openCodePassword = FirstNonEmpty(configuration["OPENCODE_PASSWORD"]);
        var sonarUrl = ReadString(configuration, "SonarQube:Url", "SONARQUBE_URL", string.Empty);
        var sonarToken = FirstNonEmpty(configuration["SONARQUBE_TOKEN"]);
        var dbPathSetting = ReadString(configuration, "Orchestration:DbPath", "ORCHESTRATOR_DB_PATH", "data/orchestrator.sqlite");
        var maxActive = ReadInt(configuration, "Orchestration:MaxActive", "ORCH_MAX_ACTIVE", 3, 1);
        var pollMs = ReadInt(configuration, "Orchestration:PollMs", "ORCH_POLL_MS", 3000, 1000);
        var maxAttempts = ReadInt(configuration, "Orchestration:MaxAttempts", "ORCH_MAX_ATTEMPTS", 3, 1);
        var maxWorkingGlobal = ReadInt(configuration, "Orchestration:MaxWorkingGlobal", "ORCH_MAX_WORKING_GLOBAL", 5, 0);
        var resumeFallback = maxWorkingGlobal > 1 ? maxWorkingGlobal - 1 : maxWorkingGlobal;
        var workingResumeBelow = ReadNullableInt(configuration, "Orchestration:WorkingResumeBelow", "ORCH_WORKING_RESUME_BELOW", 0) ?? resumeFallback;

        if (maxWorkingGlobal > 0 &&
            workingResumeBelow >= maxWorkingGlobal)
            workingResumeBelow = Math.Max(0, maxWorkingGlobal - 1);

        ValidateRequiredAbsoluteUri("OpenCode:Url", openCodeUrl);
        ValidateOptionalAbsoluteUri("SonarQube:Url", sonarUrl);

        return new AppRuntimeSettings(
            new ViewerRuntimeSettings(viewerHost, viewerPort),
            new OpenCodeRuntimeSettings(openCodeUrl, openCodeUsername, openCodePassword),
            new SonarQubeRuntimeSettings(sonarUrl, sonarToken),
            new OrchestrationRuntimeSettings(
                ResolvePath(environment.ContentRootPath, dbPathSetting),
                maxActive,
                pollMs,
                maxAttempts,
                maxWorkingGlobal,
                workingResumeBelow));
    }

    static string ResolvePath(string contentRootPath, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        return Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }

    static void ValidateRequiredAbsoluteUri(string name, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
            throw new InvalidOperationException($"Configuration value `{name}` must be an absolute URI.");
    }

    static void ValidateOptionalAbsoluteUri(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        ValidateRequiredAbsoluteUri(name, value);
    }

    static string ReadString(
        IConfiguration configuration,
        string configurationKey,
        string legacyEnvironmentVariable,
        string fallback)
    {
        return FirstNonEmpty(
            configuration[legacyEnvironmentVariable],
            configuration[configurationKey],
            fallback);
    }

    static int ReadInt(
        IConfiguration configuration,
        string configurationKey,
        string legacyEnvironmentVariable,
        int fallback,
        int min)
    {
        var raw = FirstNonEmpty(
            configuration[legacyEnvironmentVariable],
            configuration[configurationKey]);

        if (!int.TryParse(raw, out var parsed) || parsed < min)
            return fallback;

        return parsed;
    }

    static int? ReadNullableInt(
        IConfiguration configuration,
        string configurationKey,
        string legacyEnvironmentVariable,
        int min)
    {
        var raw = FirstNonEmpty(
            configuration[legacyEnvironmentVariable],
            configuration[configurationKey]);

        if (string.IsNullOrWhiteSpace(raw) ||
            !int.TryParse(raw, out var parsed) ||
            parsed < min)
            return null;

        return parsed;
    }

    static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Trim();
        }

        return string.Empty;
    }
}
