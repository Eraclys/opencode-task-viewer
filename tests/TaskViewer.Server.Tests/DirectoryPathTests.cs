using TaskViewer.Server.Domain;

namespace TaskViewer.Server.Tests;

public sealed class DirectoryPathTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("/", "/")]
    [InlineData("\\", "\\")]
    [InlineData("C:/", "C:/")]
    [InlineData("C:\\", "C:\\")]
    [InlineData(" C:/Work/Alpha/ ", "C:/Work/Alpha")]
    [InlineData("C:\\Work\\Alpha\\", "C:\\Work\\Alpha")]
    public void Normalize_HandlesExpectedCases(string? input, string? expected) => Assert.Equal(expected, DirectoryPath.Normalize(input));

    [Fact]
    public void GetCacheKey_UsesForwardSlash() => Assert.Equal("C:/Work/Alpha", DirectoryPath.GetCacheKey("C:\\Work\\Alpha\\"));

    [Fact]
    public void GetVariants_ReturnsOriginalForwardAndBackwardForms()
    {
        var variants = DirectoryPath.GetVariants("C:/Work/Alpha");

        Assert.Contains("C:/Work/Alpha", variants);
        Assert.Contains("C:\\Work\\Alpha", variants);
    }
}
