namespace TaskViewer.OpenCode.Tests;

public sealed class OpenCodeApiClientTests
{
    [Fact]
    public void BuildSessionUrl_EncodesNormalizedDirectorySlug()
    {
        var client = new OpenCodeApiClient(
            () => throw new InvalidOperationException("Factory should not be used in this test"),
            new OpenCodeClientOptions("http://localhost:4096/", "opencode", string.Empty));

        var url = client.BuildSessionUrl("session-123", "C:/Work/Repo/");

        Assert.Equal("http://localhost:4096/QzovV29yay9SZXBv/session/session-123", url);
    }
}
