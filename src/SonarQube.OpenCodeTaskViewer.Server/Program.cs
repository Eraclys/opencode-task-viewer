using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SonarQube.OpenCodeTaskViewer.Serialization;
using SonarQube.OpenCodeTaskViewer.Server.Api;
using SonarQube.OpenCodeTaskViewer.Server.DependencyInjection;

var contentRootPath = ResolveContentRoot(AppContext.BaseDirectory);
var webRootPath = ResolveWebRoot(AppContext.BaseDirectory, contentRootPath);

var builder = WebApplication.CreateBuilder(
    new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = contentRootPath,
        WebRootPath = webRootPath
    });

var runtimeSettings = builder.AddTaskViewerRuntimeSettings();
var viewerHost = runtimeSettings.Viewer.Host;
var viewerPort = runtimeSettings.Viewer.Port;
var opencodeUrl = runtimeSettings.OpenCode.Url;
var sonarMode = runtimeSettings.SonarQube.Mode;

builder.WebHost.UseUrls($"http://{viewerHost}:{viewerPort}");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    options.SerializerOptions.AddTaskViewerJsonConverters();
});

builder
    .Services
    .AddTaskViewerServerInfrastructure()
    .AddTaskViewerServerApplication();

var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var server = app.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
    var actual = addresses?.FirstOrDefault() ?? app.Urls.FirstOrDefault() ?? $"http://{viewerHost}:{viewerPort}";
    Console.WriteLine($"SonarQube OpenCode Task Viewer running at {actual}");
    Console.WriteLine($"VIEWER_URL={actual}");
    Console.WriteLine($"Using OpenCode server: {opencodeUrl}");
    Console.WriteLine($"Using SonarQube mode: {sonarMode}");

    if (string.Equals(sonarMode, "fake", StringComparison.Ordinal))
        Console.WriteLine("Using built-in fake SonarQube dataset for local UI exploration");
    else if (!string.IsNullOrWhiteSpace(runtimeSettings.SonarQube.Url))
        Console.WriteLine($"Using SonarQube server: {runtimeSettings.SonarQube.Url}");
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSessionsEndpoints();
app.MapOrchestrationEndpoints();
app.MapViewerEndpoints();
app.MapFallbackToFile("index.html");

await app.RunAsync();

static string ResolveContentRoot(string baseDirectory)
{
    var directory = new DirectoryInfo(baseDirectory);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "appsettings.json")) &&
            (File.Exists(Path.Combine(directory.FullName, "wwwroot", "index.html")) ||
             File.Exists(Path.Combine(directory.FullName, "staticwebassets", "index.html"))))
            return directory.FullName;

        directory = directory.Parent;
    }

    return baseDirectory;
}

static string ResolveWebRoot(string baseDirectory, string contentRootPath)
{
    return FindDirectoryUpwards(contentRootPath, "wwwroot") ?? FindDirectoryUpwards(baseDirectory, "wwwroot") ?? FindDirectoryUpwards(contentRootPath, "staticwebassets") ?? FindDirectoryUpwards(baseDirectory, "staticwebassets") ?? Path.Combine(contentRootPath, "wwwroot");
}

static string? FindDirectoryUpwards(string startPath, string directoryName)
{
    var directory = new DirectoryInfo(startPath);

    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, directoryName);

        if (Directory.Exists(candidate))
            return candidate;

        directory = directory.Parent;
    }

    return null;
}
