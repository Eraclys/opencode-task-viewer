namespace TaskViewer.MockOpenCode;

sealed class ProjectRecord
{
    public string Id { get; set; } = string.Empty;
    public string Worktree { get; set; } = string.Empty;
    public List<string> Sandboxes { get; set; } = [];
    public TimeRecord Time { get; set; } = new();
}
