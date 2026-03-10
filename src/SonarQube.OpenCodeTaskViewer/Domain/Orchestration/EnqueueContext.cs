using SonarQube.Client;
using SonarQube.OpenCodeTaskViewer.Infrastructure.Persistence;

namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed record EnqueueContext(
    MappingRecord Mapping,
    SonarIssueType Type,
    string InstructionText);
