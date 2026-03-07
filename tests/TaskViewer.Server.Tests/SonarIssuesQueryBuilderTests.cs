using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class SonarIssuesQueryBuilderTests
{
    [Fact]
    public void Build_IncludesRequiredAndOptionalFields()
    {
        var mapping = new MappingRecord
        {
            Id = 7,
            SonarProjectKey = "alpha-key",
            Directory = "C:/Work/Alpha",
            Branch = "main",
            Enabled = true,
            CreatedAt = "2026-01-01T00:00:00.0000000+00:00",
            UpdatedAt = "2026-01-01T00:00:00.0000000+00:00"
        };

        var query = SonarIssuesQueryBuilder.Build(
            mapping,
            2,
            100,
            "CODE_SMELL",
            "MAJOR",
            "OPEN",
            ["javascript:S1126"]);

        Assert.Equal("alpha-key", query["componentKeys"]);
        Assert.Equal("2", query["p"]);
        Assert.Equal("100", query["ps"]);
        Assert.Equal("CODE_SMELL", query["types"]);
        Assert.Equal("MAJOR", query["severities"]);
        Assert.Equal("OPEN", query["statuses"]);
        Assert.Equal("javascript:S1126", query["rules"]);
        Assert.Equal("main", query["branch"]);
    }

    [Fact]
    public void Build_OmitsEmptyOptionalFields()
    {
        var mapping = new MappingRecord
        {
            Id = 1,
            SonarProjectKey = "beta-key",
            Directory = "C:/Work/Beta",
            Branch = null,
            Enabled = true,
            CreatedAt = "",
            UpdatedAt = ""
        };

        var query = SonarIssuesQueryBuilder.Build(
            mapping,
            1,
            50,
            null,
            "",
            "",
            []);

        Assert.False(query.ContainsKey("types"));
        Assert.False(query.ContainsKey("severities"));
        Assert.False(query.ContainsKey("statuses"));
        Assert.False(query.ContainsKey("rules"));
        Assert.False(query.ContainsKey("branch"));
    }
}
