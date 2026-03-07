namespace TaskViewer.Server.Application.Orchestration;

internal interface IOrchestrationInputNormalizer
{
    List<string> NormalizeRuleKeys(string? csv);
    (int PageIndex, int PageSize) ParseIssuePaging(string? page, string? pageSize);
    bool HasSingleSpecificRule(IReadOnlyList<string> ruleKeys);
}
