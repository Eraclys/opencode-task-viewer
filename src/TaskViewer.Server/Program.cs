using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using TaskViewer.OpenCode;
using TaskViewer.Server;
using TaskViewer.Server.Api;
using TaskViewer.Server.Application.Orchestration;
using TaskViewer.Server.Application.Sessions;
using TaskViewer.Server.DependencyInjection;
using TaskViewer.Server.Infrastructure.OpenCode;

var builder = WebApplication.CreateBuilder(args);
var runtimeSettings = builder.AddTaskViewerRuntimeSettings();
var viewerHost = runtimeSettings.Viewer.Host;
var viewerPort = runtimeSettings.Viewer.Port;
var opencodeUrl = runtimeSettings.OpenCode.Url;

builder.WebHost.UseUrls($"http://{viewerHost}:{viewerPort}");

builder.Services
    .AddTaskViewerServerInfrastructure()
    .AddTaskViewerServerApplication();

var app = builder.Build();
var orchestrator = app.Services.GetRequiredService<SonarOrchestrator>();
var openCodeEventHandler = app.Services.GetRequiredService<OpenCodeEventHandler>();
var upstreamSseService = app.Services.GetRequiredService<OpenCodeUpstreamSseService>();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var server = app.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
    var actual = addresses?.FirstOrDefault() ?? app.Urls.FirstOrDefault() ?? $"http://{viewerHost}:{viewerPort}";
    Console.WriteLine($"OpenCode Task Viewer running at {actual}");
    Console.WriteLine($"VIEWER_URL={actual}");
    Console.WriteLine($"Using OpenCode server: {opencodeUrl}");
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    _ = orchestrator.DisposeAsync();
});

orchestrator.Start(app.Lifetime.ApplicationStopping);
_ = Task.Run(() => upstreamSseService.RunAsync(openCodeEventHandler.HandleAsync, app.Lifetime.ApplicationStopping));

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSessionsEndpoints(app.Services.GetRequiredService<ISessionsUseCases>());
app.MapOrchestrationEndpoints(app.Services.GetRequiredService<IOrchestrationUseCases>());
app.MapViewerEndpoints();
app.MapFallbackToFile("index.html");

await app.RunAsync();
