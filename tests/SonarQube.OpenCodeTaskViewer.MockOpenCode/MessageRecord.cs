namespace SonarQube.OpenCodeTaskViewer.MockOpenCode;

sealed class MessageRecord
{
    public MessageInfoRecord Info { get; set; } = new();
    public string? Text { get; set; }
    public List<MessageContentRecord>? Content { get; set; }
}
