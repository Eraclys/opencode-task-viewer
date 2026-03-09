namespace TaskViewer.Application.Orchestration;

public sealed record BackpressureTransition(bool NextPaused, bool Changed);
