using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SonarQube.OpenCodeTaskViewer.Server.Configuration;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class AppRuntimeSettingsLoaderTests
{
    [Fact]
    public void Load_UsesAppSettingsForNonSensitiveConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SonarQube.OpenCodeTaskViewer:Host"] = "0.0.0.0",
                    ["SonarQube.OpenCodeTaskViewer:Port"] = "4567",
                    ["HOST"] = "ignored-env-host",
                    ["PORT"] = "9999",
                    ["OpenCode:Url"] = "http://config-opencode:4444",
                    ["OpenCode:Username"] = "config-user",
                    ["OPENCODE_URL"] = "http://env-opencode:4096",
                    ["OPENCODE_USERNAME"] = "env-user",
                    ["OpenCode:Password"] = "ignored-from-config",
                    ["OPENCODE_PASSWORD"] = "env-password",
                    ["SonarQube:Url"] = "http://config-sonar:9000",
                    ["SonarQube:Token"] = "ignored-from-config",
                    ["SONARQUBE_URL"] = "http://env-sonar:9001",
                    ["SONARQUBE_TOKEN"] = "env-token",
                    ["Orchestration:DbPath"] = "data/custom.sqlite",
                    ["ORCHESTRATOR_DB_PATH"] = "data/env.sqlite",
                    ["Orchestration:MaxActive"] = "4",
                    ["ORCH_MAX_ACTIVE"] = "9",
                    ["Orchestration:PollMs"] = "5000",
                    ["ORCH_POLL_MS"] = "1200",
                    ["Orchestration:MaxAttempts"] = "6",
                    ["ORCH_MAX_ATTEMPTS"] = "4",
                    ["Orchestration:MaxWorkingGlobal"] = "8",
                    ["ORCH_MAX_WORKING_GLOBAL"] = "6",
                    ["Orchestration:WorkingResumeBelow"] = "7",
                    ["ORCH_WORKING_RESUME_BELOW"] = "5"
                })
            .Build();

        var contentRoot = Path.Combine(Path.GetTempPath(), "task-viewer-config-tests");
        var settings = AppRuntimeSettingsLoader.Load(configuration, new TestHostEnvironment(contentRoot));

        Assert.Equal("ignored-env-host", settings.Viewer.Host);
        Assert.Equal(9999, settings.Viewer.Port);
        Assert.Equal("http://env-opencode:4096", settings.OpenCode.Url);
        Assert.Equal("env-user", settings.OpenCode.Username);
        Assert.Equal("env-password", settings.OpenCode.Password);
        Assert.Equal("http://env-sonar:9001", settings.SonarQube.Url);
        Assert.Equal("env-token", settings.SonarQube.Token);
        Assert.Equal(SonarQubeMode.Real, settings.SonarQube.Mode);
        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "data/env.sqlite")), settings.Orchestration.DbPath);
        Assert.Equal(9, settings.Orchestration.MaxActive);
        Assert.Equal(1200, settings.Orchestration.PollMs);
        Assert.Equal(4, settings.Orchestration.MaxAttempts);
        Assert.Equal(6, settings.Orchestration.MaxWorkingGlobal);
        Assert.Equal(5, settings.Orchestration.WorkingResumeBelow);
    }

    [Fact]
    public void Load_UsesLocalApplicationDataForDefaultDbPath()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["OpenCode:Url"] = "http://config-opencode:4444"
                })
            .Build();

        var contentRoot = Path.Combine(Path.GetTempPath(), "task-viewer-config-tests");
        var settings = AppRuntimeSettingsLoader.Load(configuration, new TestHostEnvironment(contentRoot));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var expected = string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetFullPath(Path.Combine(contentRoot, "data", "orchestrator.sqlite"))
            : Path.GetFullPath(Path.Combine(localAppData, "SonarQube.OpenCodeTaskViewer", "orchestrator.sqlite"));

        Assert.Equal(expected, settings.Orchestration.DbPath);
    }

    [Fact]
    public void Load_UsesStronglyTypedKeysWhenLegacyKeysAreMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SonarQube.OpenCodeTaskViewer:Host"] = "config-host",
                    ["SonarQube.OpenCodeTaskViewer:Port"] = "1111",
                    ["OpenCode:Url"] = "http://config-opencode:4444",
                    ["OpenCode:Username"] = "config-user",
                    ["SonarQube:Url"] = "http://config-sonar:9000",
                    ["Orchestration:DbPath"] = "data/config.sqlite",
                    ["Orchestration:MaxActive"] = "2",
                    ["Orchestration:PollMs"] = "3000",
                    ["Orchestration:MaxAttempts"] = "3",
                    ["Orchestration:MaxWorkingGlobal"] = "5",
                    ["Orchestration:WorkingResumeBelow"] = "4"
                })
            .Build();

        var contentRoot = Path.Combine(Path.GetTempPath(), "task-viewer-config-tests");
        var settings = AppRuntimeSettingsLoader.Load(configuration, new TestHostEnvironment(contentRoot));

        Assert.Equal("config-host", settings.Viewer.Host);
        Assert.Equal(1111, settings.Viewer.Port);
        Assert.Equal("http://config-opencode:4444", settings.OpenCode.Url);
        Assert.Equal("config-user", settings.OpenCode.Username);
        Assert.Equal(string.Empty, settings.OpenCode.Password);
        Assert.Equal("http://config-sonar:9000", settings.SonarQube.Url);
        Assert.Equal(string.Empty, settings.SonarQube.Token);
        Assert.Equal(SonarQubeMode.Real, settings.SonarQube.Mode);
        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "data/config.sqlite")), settings.Orchestration.DbPath);
        Assert.Equal(2, settings.Orchestration.MaxActive);
        Assert.Equal(3000, settings.Orchestration.PollMs);
        Assert.Equal(3, settings.Orchestration.MaxAttempts);
        Assert.Equal(5, settings.Orchestration.MaxWorkingGlobal);
        Assert.Equal(4, settings.Orchestration.WorkingResumeBelow);
    }

    [Fact]
    public void Load_ClampsResumeBelowWhenItWouldMeetOrExceedGlobalLimit()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["OpenCode:Url"] = "http://config-opencode:4444",
                    ["Orchestration:MaxWorkingGlobal"] = "3",
                    ["Orchestration:WorkingResumeBelow"] = "3"
                })
            .Build();

        var settings = AppRuntimeSettingsLoader.Load(
            configuration,
            new TestHostEnvironment(Path.GetTempPath()));

        Assert.Equal(3, settings.Orchestration.MaxWorkingGlobal);
        Assert.Equal(2, settings.Orchestration.WorkingResumeBelow);
    }

    [Fact]
    public void Load_UsesMicrosoftEnvironmentProviderShape_ForHierarchicalOverrides()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SonarQube.OpenCodeTaskViewer:Host"] = "config-host",
                    ["OpenCode:Url"] = "http://config-opencode:4444",
                    ["Orchestration:DbPath"] = "data/config.sqlite"
                })
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SonarQube.OpenCodeTaskViewer:Host"] = "hierarchical-env-host",
                    ["OpenCode:Url"] = "http://hierarchical-env-opencode:4096",
                    ["SONARQUBE_TOKEN"] = "env-token",
                    ["Orchestration:DbPath"] = "data/hierarchical-env.sqlite"
                })
            .Build();

        var contentRoot = Path.Combine(Path.GetTempPath(), "task-viewer-config-tests");
        var settings = AppRuntimeSettingsLoader.Load(configuration, new TestHostEnvironment(contentRoot));

        Assert.Equal("hierarchical-env-host", settings.Viewer.Host);
        Assert.Equal("http://hierarchical-env-opencode:4096", settings.OpenCode.Url);
        Assert.Equal("env-token", settings.SonarQube.Token);
        Assert.Equal(SonarQubeMode.Real, settings.SonarQube.Mode);
        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "data/hierarchical-env.sqlite")), settings.Orchestration.DbPath);
    }

    [Fact]
    public void Load_AllowsFakeSonarModeWithoutRealUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["OpenCode:Url"] = "http://config-opencode:4444",
                    ["SONARQUBE_MODE"] = "fake"
                })
            .Build();

        var settings = AppRuntimeSettingsLoader.Load(
            configuration,
            new TestHostEnvironment(Path.GetTempPath()));

        Assert.Equal(SonarQubeMode.Fake, settings.SonarQube.Mode);
        Assert.Equal(string.Empty, settings.SonarQube.Url);
        Assert.Equal(string.Empty, settings.SonarQube.Token);
    }

    sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "SonarQube.OpenCodeTaskViewer.Server.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
