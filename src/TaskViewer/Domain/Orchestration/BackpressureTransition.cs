namespace TaskViewer.Domain.Orchestration;

public sealed record BackpressureTransition(bool NextPaused, bool Changed);
