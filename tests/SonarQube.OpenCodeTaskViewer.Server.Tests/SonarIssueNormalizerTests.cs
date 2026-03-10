using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Orchestration;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class SonarIssueNormalizerTests
{
    [Fact]
    public void NormalizeForQueue_ReturnsNull_WhenKeyMissing()
    {
        var mapping = CreateMapping();

        var raw = new SonarIssueTransport(
            null,
            null,
            null,
            null,
            null,
            "javascript:S1126",
            null,
            null,
            null,
            null,
            null);

        var result = SonarIssueNormalizer.NormalizeForQueue(raw, mapping);

        Assert.Null(result);
    }

    [Fact]
    public void NormalizeForQueue_UsesIssueKeyAndFileFallbacks()
    {
        var mapping = CreateMapping();

        var raw = new SonarIssueTransport(
            null,
            "sq-2",
            null,
            "bug",
            "critical",
            "csharpsquid:S1118",
            "Add a private constructor",
            null,
            "alpha-key:src/File.cs",
            "18",
            "OPEN");

        var result = SonarIssueNormalizer.NormalizeForQueue(raw, mapping);

        Assert.NotNull(result);
        Assert.Equal("sq-2", result.Key);
        Assert.Equal("BUG", result.Type);
        Assert.Equal(SonarIssueType.Bug, result.IssueType);
        Assert.Equal("CRITICAL", result.Severity);
        Assert.Equal(SonarIssueSeverity.Critical, result.IssueSeverity);
        Assert.Equal("src/File.cs", result.RelativePath);
        Assert.Equal("C:/Work/Alpha/src/File.cs", result.AbsolutePath);
        Assert.Equal(18, result.Line);
    }

    [Fact]
    public void NormalizeForQueue_HandlesComponentWithoutProjectPrefix()
    {
        var mapping = CreateMapping();

        var raw = new SonarIssueTransport(
            "sq-3",
            null,
            null,
            null,
            null,
            null,
            null,
            "other-project:lib/module.js",
            null,
            null,
            null);

        var result = SonarIssueNormalizer.NormalizeForQueue(raw, mapping);

        Assert.NotNull(result);
        Assert.Equal("lib/module.js", result.RelativePath);
        Assert.Equal("C:/Work/Alpha/lib/module.js", result.AbsolutePath);
        Assert.Equal("CODE_SMELL", result.Type);
        Assert.Equal(SonarIssueType.CodeSmell, result.IssueType);
    }

    static MappingRecord CreateMapping()
        => new()
        {
            Id = 1,
            SonarProjectKey = "alpha-key",
            Directory = "C:/Work/Alpha",
            Branch = "main",
            Enabled = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };
}
