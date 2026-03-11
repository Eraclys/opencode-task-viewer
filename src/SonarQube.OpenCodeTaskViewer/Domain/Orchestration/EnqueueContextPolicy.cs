using SonarQube.Client;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public static class EnqueueContextPolicy
{
    public static string ResolveInstructionText(string? explicitInstructions, string? defaultInstructions)
    {
        if (!string.IsNullOrWhiteSpace(explicitInstructions))
            return explicitInstructions.Trim();

        return (defaultInstructions ?? string.Empty).Trim();
    }

    public static bool ShouldPersistInstructionProfile(SonarIssueType issueType, string instructionText) => issueType.HasValue && !string.IsNullOrWhiteSpace(instructionText);
}
