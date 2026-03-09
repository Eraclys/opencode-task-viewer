namespace TaskViewer.Application.Orchestration;

public interface IOrchestrationInputNormalizer
{
    List<string> NormalizeRuleKeys(string? csv);
    (int PageIndex, int PageSize) ParseIssuePaging(int? page, int? pageSize);
    bool HasSingleSpecificRule(IReadOnlyList<string> ruleKeys);
}
