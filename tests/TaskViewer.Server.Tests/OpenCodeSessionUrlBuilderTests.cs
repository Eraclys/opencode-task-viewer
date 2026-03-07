using System.Text;
using TaskViewer.Server.Application;

namespace TaskViewer.Server.Tests;

public sealed class OpenCodeSessionUrlBuilderTests
{
    [Fact]
    public void Build_ReturnsSimpleSessionUrl_WhenDirectoryMissing()
    {
        var url = OpenCodeSessionUrlBuilder.Build("http://localhost:4096", "sess-1", null);
        Assert.Equal("http://localhost:4096/session/sess-1", url);
    }

    [Fact]
    public void Build_EncodesDirectorySlug_WhenDirectoryProvided()
    {
        const string directory = "C:/Work/Gamma";
        var slug = Convert.ToBase64String(Encoding.UTF8.GetBytes(directory)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var url = OpenCodeSessionUrlBuilder.Build("http://localhost:4096/", "sess-stale", directory);

        Assert.Equal($"http://localhost:4096/{slug}/session/sess-stale", url);
    }
}
