namespace TaskViewer.MockOpenCode;

sealed class MessageInfoRecord
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public MessageTimeRecord Time { get; set; } = new();
}
