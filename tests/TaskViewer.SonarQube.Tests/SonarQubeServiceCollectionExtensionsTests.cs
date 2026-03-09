using Microsoft.Extensions.DependencyInjection;

namespace TaskViewer.SonarQube.Tests;

public sealed class SonarQubeServiceCollectionExtensionsTests
{
    [Fact]
    public void AddTaskViewerSonarQube_RegistersDirectServiceBoundary()
    {
        var services = new ServiceCollection();

        services.AddTaskViewerSonarQube(_ => new SonarQubeClientOptions("http://sonar.local", "secret"));

        using var provider = services.BuildServiceProvider();

        var apiClient = provider.GetRequiredService<SonarQubeService>();
        var service = provider.GetRequiredService<ISonarQubeService>();

        Assert.Same(apiClient, service);
    }
}
