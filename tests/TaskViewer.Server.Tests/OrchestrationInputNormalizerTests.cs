using System.Text.Json.Nodes;
using TaskViewer.Server.Application.Orchestration;

namespace TaskViewer.Server.Tests;

public sealed class OrchestrationInputNormalizerTests
{
    [Fact]
    public void NormalizeRuleKeys_DeduplicatesAndTrims()
    {
        var sut = new OrchestrationInputNormalizer();

        var result = sut.NormalizeRuleKeys(" javascript:S1126 , javascript:S1126, csharpsquid:S101 ");

        Assert.Equal(["javascript:S1126", "csharpsquid:S101"], result);
    }

    [Fact]
    public void NormalizeRuleKeys_HandlesJsonArray()
    {
        var sut = new OrchestrationInputNormalizer();
        var result = sut.NormalizeRuleKeys(new JsonArray(" a:r1 ", "", "a:r1", "b:r2"));

        Assert.Equal(["a:r1", "b:r2"], result);
    }

    [Fact]
    public void ParseIssuePaging_ClampsBoundsAndUsesDefaults()
    {
        var sut = new OrchestrationInputNormalizer();

        var defaults = sut.ParseIssuePaging(null, null);
        var clamped = sut.ParseIssuePaging("-1", 9999);

        Assert.Equal(1, defaults.PageIndex);
        Assert.Equal(100, defaults.PageSize);
        Assert.Equal(1, clamped.PageIndex);
        Assert.Equal(500, clamped.PageSize);
    }

    [Fact]
    public void HasSingleSpecificRule_RejectsAllAndMultiple()
    {
        var sut = new OrchestrationInputNormalizer();

        Assert.False(sut.HasSingleSpecificRule([]));
        Assert.False(sut.HasSingleSpecificRule(["all"]));
        Assert.False(sut.HasSingleSpecificRule(["ALL"]));
        Assert.False(sut.HasSingleSpecificRule(["a:r1", "b:r2"]));
        Assert.True(sut.HasSingleSpecificRule(["a:r1"]));
    }
}
