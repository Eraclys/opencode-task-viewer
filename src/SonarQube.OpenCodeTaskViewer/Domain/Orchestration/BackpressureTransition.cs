namespace SonarQube.OpenCodeTaskViewer.Domain.Orchestration;

public sealed record BackpressureTransition(bool NextPaused, bool Changed);
