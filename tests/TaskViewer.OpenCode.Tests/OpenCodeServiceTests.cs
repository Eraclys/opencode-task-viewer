namespace TaskViewer.OpenCode.Tests;

public sealed class OpenCodeServiceTests
{
    [Fact]
    public void BuildSessionUrl_EncodesNormalizedDirectorySlug()
    {
        var client = new OpenCodeService(
            () => throw new InvalidOperationException("Factory should not be used in this test"),
            new OpenCodeClientOptions("http://localhost:4096/", "opencode", string.Empty));

        var url = client.BuildSessionUrl("session-123", "C:/Work/Repo/");

        Assert.Equal("http://localhost:4096/QzovV29yay9SZXBv/session/session-123", url);
    }
}
