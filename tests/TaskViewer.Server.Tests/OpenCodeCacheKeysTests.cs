using TaskViewer.Infrastructure.OpenCode;

namespace TaskViewer.Server.Tests;

public sealed class OpenCodeCacheKeysTests
{
    [Fact]
    public void Directory_NormalizesBeforeBuildingKey()
    {
        var key = OpenCodeCacheKeys.Directory("C:\\Work\\Repo\\");

        Assert.Equal("C:/Work/Repo", key);
    }

    [Fact]
    public void DirectorySession_UsesNormalizedDirectoryKey()
    {
        var key = OpenCodeCacheKeys.DirectorySession("C:\\Work\\Repo\\", "sess-1");

        Assert.Equal("C:/Work/Repo::sess-1", key);
    }
}
