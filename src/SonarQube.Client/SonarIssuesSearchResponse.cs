namespace SonarQube.Client;

public sealed record SonarIssuesSearchResponse(
    int PageIndex,
    int PageSize,
    int? Total,
    IReadOnlyList<SonarIssueTransport> Issues);
