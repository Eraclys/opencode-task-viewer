namespace SonarQube.OpenCodeTaskViewer.Server.Configuration;

public static class AppRuntimeSettingsFactory
{
    public static AppRuntimeSettingsOptions Bind(IConfiguration configuration)
    {
        var options = new AppRuntimeSettingsOptions();
        BindInto(options, configuration);

        return options;
    }

    public static void BindInto(AppRuntimeSettingsOptions options, IConfiguration configuration)
    {
        configuration.Bind(options);
        configuration.GetSection("TaskViewer").Bind(options.TaskViewer);
        configuration.GetSection("SonarQube.OpenCodeTaskViewer").Bind(options.TaskViewer);
        configuration.GetSection("OpenCode").Bind(options.OpenCode);
        configuration.GetSection("SonarQube").Bind(options.SonarQube);
        configuration.GetSection("Orchestration").Bind(options.Orchestration);

        options.TaskViewer.Host = FirstNonEmpty(configuration["HOST"], options.TaskViewer.Host);
        options.TaskViewer.Port = FirstNonEmpty(configuration["PORT"], options.TaskViewer.Port);

        options.OpenCode.Url = FirstNonEmpty(configuration["OPENCODE_URL"], options.OpenCode.Url);
        options.OpenCode.Username = FirstNonEmpty(configuration["OPENCODE_USERNAME"], options.OpenCode.Username);
        options.OpenCode.Password = FirstNonEmpty(configuration["OPENCODE_PASSWORD"], options.OpenCode.Password);

        options.SonarQube.Url = FirstNonEmpty(configuration["SONARQUBE_URL"], options.SonarQube.Url);
        options.SonarQube.Token = FirstNonEmpty(configuration["SONARQUBE_TOKEN"], options.SonarQube.Token);
        options.SonarQube.Mode = FirstNonEmpty(configuration["SONARQUBE_MODE"], options.SonarQube.Mode);

        options.Orchestration.DbPath = FirstNonEmpty(configuration["ORCHESTRATOR_DB_PATH"], options.Orchestration.DbPath);
        options.Orchestration.MaxActive = FirstNonEmpty(configuration["ORCH_MAX_ACTIVE"], options.Orchestration.MaxActive);
        options.Orchestration.PerProjectMaxActive = FirstNonEmpty(configuration["ORCH_PER_PROJECT_MAX_ACTIVE"], options.Orchestration.PerProjectMaxActive);
        options.Orchestration.PollMs = FirstNonEmpty(configuration["ORCH_POLL_MS"], options.Orchestration.PollMs);
        options.Orchestration.LeaseSeconds = FirstNonEmpty(configuration["ORCH_LEASE_SECONDS"], options.Orchestration.LeaseSeconds);
        options.Orchestration.MaxAttempts = FirstNonEmpty(configuration["ORCH_MAX_ATTEMPTS"], options.Orchestration.MaxAttempts);
        options.Orchestration.MaxWorkingGlobal = FirstNonEmpty(configuration["ORCH_MAX_WORKING_GLOBAL"], options.Orchestration.MaxWorkingGlobal);
        options.Orchestration.WorkingResumeBelow = FirstNonEmpty(configuration["ORCH_WORKING_RESUME_BELOW"], options.Orchestration.WorkingResumeBelow);
    }

    public static AppRuntimeSettings Create(AppRuntimeSettingsOptions options, IHostEnvironment environment)
    {
        var viewerHost = FirstNonEmpty(options.TaskViewer.Host, "127.0.0.1");
        var viewerPort = ReadInt(options.TaskViewer.Port, 3456, 1);
        var openCodeUrl = FirstNonEmpty(options.OpenCode.Url, "http://localhost:4096");
        var openCodeUsername = FirstNonEmpty(options.OpenCode.Username, "opencode");
        var openCodePassword = FirstNonEmpty(options.OpenCode.Password);
        var sonarMode = FirstNonEmpty(options.SonarQube.Mode, SonarQubeMode.Real).ToLowerInvariant();
        var sonarUrl = FirstNonEmpty(options.SonarQube.Url);
        var sonarToken = FirstNonEmpty(options.SonarQube.Token);
        var dbPathSetting = FirstNonEmpty(options.Orchestration.DbPath);
        var maxActive = ReadInt(options.Orchestration.MaxActive, 3, 1);
        var perProjectMaxActive = ReadInt(options.Orchestration.PerProjectMaxActive, 2, 1);
        var pollMs = ReadInt(options.Orchestration.PollMs, 3000, 1000);
        var leaseSeconds = ReadInt(options.Orchestration.LeaseSeconds, 180, 30);
        var maxAttempts = ReadInt(options.Orchestration.MaxAttempts, 3, 1);
        var maxWorkingGlobal = ReadInt(options.Orchestration.MaxWorkingGlobal, 5, 0);
        var resumeFallback = maxWorkingGlobal > 1 ? maxWorkingGlobal - 1 : maxWorkingGlobal;
        var workingResumeBelow = ReadNullableInt(options.Orchestration.WorkingResumeBelow, 0) ?? resumeFallback;

        if (maxWorkingGlobal > 0 &&
            workingResumeBelow >= maxWorkingGlobal)
            workingResumeBelow = Math.Max(0, maxWorkingGlobal - 1);

        ValidateRequiredAbsoluteUri("OpenCode:Url", openCodeUrl);

        if (!string.Equals(sonarMode, SonarQubeMode.Fake, StringComparison.Ordinal))
            ValidateOptionalAbsoluteUri("SonarQube:Url", sonarUrl);

        return new AppRuntimeSettings(
            new ViewerRuntimeSettings(viewerHost, viewerPort),
            new OpenCodeRuntimeSettings(openCodeUrl, openCodeUsername, openCodePassword),
            new SonarQubeRuntimeSettings(sonarUrl, sonarToken, string.Equals(sonarMode, SonarQubeMode.Fake, StringComparison.Ordinal) ? SonarQubeMode.Fake : SonarQubeMode.Real),
            new OrchestrationRuntimeSettings(
                ResolveDbPath(environment.ContentRootPath, dbPathSetting),
                maxActive,
                perProjectMaxActive,
                pollMs,
                leaseSeconds,
                maxAttempts,
                maxWorkingGlobal,
                workingResumeBelow));
    }

    static string ResolveDbPath(string contentRootPath, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return GetDefaultDbPath(contentRootPath);

        return ResolvePath(contentRootPath, configuredPath);
    }

    static string ResolvePath(string contentRootPath, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        return Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }

    static string GetDefaultDbPath(string contentRootPath)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData))
            return Path.GetFullPath(Path.Combine(contentRootPath, "data", "orchestrator.sqlite"));

        return Path.GetFullPath(Path.Combine(localAppData, "SonarQube.OpenCodeTaskViewer", "orchestrator.sqlite"));
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

    static int ReadInt(string? value, int fallback, int min)
    {
        var raw = FirstNonEmpty(value);

        if (!int.TryParse(raw, out var parsed) ||
            parsed < min)
            return fallback;

        return parsed;
    }

    static int? ReadNullableInt(string? value, int min)
    {
        var raw = FirstNonEmpty(value);

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
