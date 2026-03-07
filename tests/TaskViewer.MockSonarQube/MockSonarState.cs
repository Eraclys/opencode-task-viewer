namespace TaskViewer.MockSonarQube;

sealed class MockSonarState
{
    public Dictionary<string, string> Rules { get; set; } = new(StringComparer.Ordinal);
    public List<SonarIssueRecord> Issues { get; set; } = [];

    public static MockSonarState BuildDefault() => new()
    {
        Rules = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["javascript:S1126"] = "Assignments should not be redundant",
            ["javascript:S3776"] = "Cognitive Complexity of functions should not be too high",
            ["javascript:S5144"] = "Constructing URLs from user input is security-sensitive",
            ["javascript:S1481"] = "Unused local variables should be removed"
        },
        Issues =
        [
            new SonarIssueRecord
            {
                Key = "sq-gamma-001",
                Component = "gamma-key:src/worker.js",
                Line = 42,
                Rule = "javascript:S1126",
                Severity = "MAJOR",
                Type = "CODE_SMELL",
                Status = "OPEN",
                Message = "Remove this redundant assignment."
            },
            new SonarIssueRecord
            {
                Key = "sq-gamma-002",
                Component = "gamma-key:src/server.js",
                Line = 17,
                Rule = "javascript:S3776",
                Severity = "CRITICAL",
                Type = "CODE_SMELL",
                Status = "CONFIRMED",
                Message = "Refactor this function to reduce Cognitive Complexity."
            },
            new SonarIssueRecord
            {
                Key = "sq-gamma-003",
                Component = "gamma-key:src/auth.js",
                Line = 10,
                Rule = "javascript:S5144",
                Severity = "BLOCKER",
                Type = "VULNERABILITY",
                Status = "OPEN",
                Message = "Review this URL construction for SSRF risk."
            },
            new SonarIssueRecord
            {
                Key = "sq-gamma-004",
                Component = "gamma-key:src/jobs.js",
                Line = 91,
                Rule = "javascript:S3776",
                Severity = "MAJOR",
                Type = "CODE_SMELL",
                Status = "OPEN",
                Message = "Reduce the Cognitive Complexity of this function."
            },
            new SonarIssueRecord
            {
                Key = "sq-alpha-001",
                Component = "alpha-key:src/index.js",
                Line = 7,
                Rule = "javascript:S1481",
                Severity = "MINOR",
                Type = "CODE_SMELL",
                Status = "OPEN",
                Message = "Remove this unused local variable."
            }
        ]
    };
}
