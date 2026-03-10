namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed record WorkingSessionsSample(DateTimeOffset SampledAt, int Count);
