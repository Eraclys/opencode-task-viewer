using Microsoft.Extensions.DependencyInjection;

namespace TaskViewer.OpenCode.Tests;

public sealed class OpenCodeServiceCollectionExtensionsTests
{
    [Fact]
    public void AddTaskViewerOpenCode_RegistersDirectServiceBoundaries()
    {
        var services = new ServiceCollection();

        services.AddTaskViewerOpenCode(_ => new OpenCodeClientOptions("http://localhost:4096", "opencode", "secret"));

        using var provider = services.BuildServiceProvider();

        var apiClient = provider.GetRequiredService<OpenCodeApiClient>();
        var statusReader = provider.GetRequiredService<IOpenCodeStatusReader>();
        var dispatchClient = provider.GetRequiredService<IOpenCodeDispatchClient>();
        var sseService = provider.GetRequiredService<OpenCodeUpstreamSseService>();

        Assert.Same(apiClient, statusReader);
        Assert.Same(apiClient, dispatchClient);
        Assert.NotNull(sseService);
    }
}
