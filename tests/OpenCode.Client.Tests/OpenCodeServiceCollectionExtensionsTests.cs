using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Client.Tests;

public sealed class OpenCodeServiceCollectionExtensionsTests
{
    [Fact]
    public void AddTaskViewerOpenCode_RegistersDirectServiceBoundaries()
    {
        var services = new ServiceCollection();

        services.AddTaskViewerOpenCode(_ => new OpenCodeClientOptions("http://localhost:4096", "opencode", "secret"));

        using var provider = services.BuildServiceProvider();

        var apiClient = provider.GetRequiredService<IOpenCodeService>();
        var sseService = provider.GetRequiredService<OpenCodeUpstreamSseService>();

        Assert.NotNull(sseService);
        Assert.NotNull(apiClient);
    }
}
