using TaskViewer.Infrastructure.Persistence;
using TaskViewer.SonarQube;

namespace TaskViewer.Domain.Orchestration;

public sealed record EnqueueContext(
    MappingRecord Mapping,
    SonarIssueType Type,
    string InstructionText);
