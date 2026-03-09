namespace TaskViewer.Application.Orchestration;

public interface IOrchestrationInputNormalizer
{
    List<string> NormalizeRuleKeys(string? csv);
    (int PageIndex, int PageSize) ParseIssuePaging(string? page, string? pageSize);
    bool HasSingleSpecificRule(IReadOnlyList<string> ruleKeys);
}
