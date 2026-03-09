namespace TaskViewer.Application.Orchestration;

public static class EnqueueContextPolicy
{
    public static string ResolveInstructionText(string? explicitInstructions, string? defaultInstructions)
    {
        if (!string.IsNullOrWhiteSpace(explicitInstructions))
            return explicitInstructions.Trim();

        return (defaultInstructions ?? string.Empty).Trim();
    }

    public static bool ShouldPersistInstructionProfile(string? normalizedIssueType, string instructionText)
    {
        return !string.IsNullOrWhiteSpace(normalizedIssueType) && !string.IsNullOrWhiteSpace(instructionText);
    }
}
