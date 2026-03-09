using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Text.Json.Serialization;
using TaskViewer;
using TaskViewer.Infrastructure.OpenCode;
using TaskViewer.OpenCode;
using TaskViewer.Server;
using TaskViewer.Server.Api;
using TaskViewer.Server.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
var runtimeSettings = builder.AddTaskViewerRuntimeSettings();
var viewerHost = runtimeSettings.Viewer.Host;
var viewerPort = runtimeSettings.Viewer.Port;
var opencodeUrl = runtimeSettings.OpenCode.Url;
var sonarMode = runtimeSettings.SonarQube.Mode;

builder.WebHost.UseUrls($"http://{viewerHost}:{viewerPort}");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString);

builder.Services
    .AddTaskViewerServerInfrastructure()
    .AddTaskViewerServerApplication();

var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var server = app.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
    var actual = addresses?.FirstOrDefault() ?? app.Urls.FirstOrDefault() ?? $"http://{viewerHost}:{viewerPort}";
    Console.WriteLine($"OpenCode Task Viewer running at {actual}");
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
